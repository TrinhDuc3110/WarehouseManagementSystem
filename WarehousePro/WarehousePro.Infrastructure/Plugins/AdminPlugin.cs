using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text.Json;
using WarehousePro.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using WarehousePro.Infrastructure.Hubs;

namespace WarehousePro.Infrastructure.Persistence
{
    public class AdminPlugin
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<ChatHub> _chatHub;

        public AdminPlugin(ApplicationDbContext context, IHubContext<ChatHub> chatHub)
        {
            _context = context;
            _chatHub = chatHub;
        }

        [KernelFunction, Description("Get list of warehouse locations.")]
        public string GetLocations()
        {
            var locations = _context.Locations.AsNoTracking()
                .Select(l => new { Name = l.Code, Detail = $"Zone {l.Zone} - Shelf {l.Shelf}", Note = "Location" }).ToList();
            return JsonSerializer.Serialize(locations);
        }

        [KernelFunction, Description("Get list of partners.")]
        public string GetPartners(string type = "")
        {
            var query = _context.Partners.AsNoTracking().AsQueryable();
            if (!string.IsNullOrEmpty(type)) query = query.Where(p => p.Type.ToLower() == type.ToLower());
            return JsonSerializer.Serialize(query.Select(p => new { p.Name, p.Phone, p.Type }).ToList());
        }

        // ------------------------------------------------------------------
        // 2. CREATE TRANSACTION 
        // ------------------------------------------------------------------
        [KernelFunction, Description("Execute stock IMPORT or EXPORT transaction.")]
        public async Task<string> CreateTransaction(
            [Description("Type: 'IMPORT' or 'EXPORT'")] string type,
            [Description("Product Name")] string productName,
            [Description("Quantity")] int quantity,
            [Description("Partner Name")] string partnerName,
            [Description("Location Code (Required for IMPORT, Optional for EXPORT)")] string locationName = "",
            [Description("Warehouse ID (Optional)")] string warehouseId = "",
            [Description("Actual Price")] decimal? price = null,
            [Description("Note")] string note = "AI Executed")
        {
            Console.WriteLine($"[AI ACTION] {type} | Product: {productName} | Qty: {quantity}");

            // ✅ BƯỚC QUAN TRỌNG: Tạo Strategy để xử lý Retry Logic
            var strategy = _context.Database.CreateExecutionStrategy();

            // Bọc toàn bộ logic trong strategy.ExecuteAsync
            return await strategy.ExecuteAsync(async () =>
            {
                using var transactionDB = await _context.Database.BeginTransactionAsync();
                try
                {
                    // 1. Validation
                    string transType = type.Trim().ToUpper();
                    if (quantity <= 0) return ReturnError("Quantity must be > 0.");

                    var product = await _context.Products.FirstOrDefaultAsync(p => p.Name.ToLower().Contains(productName.ToLower()));
                    if (product == null) return ReturnError($"Product '{productName}' not found.");

                    var partner = await _context.Partners.FirstOrDefaultAsync(p => p.Name.ToLower().Contains(partnerName.ToLower()));
                    if (partner == null) return ReturnError($"Partner '{partnerName}' not found.");

                    decimal unitPrice = price ?? product.Price;
                    decimal totalMoney = unitPrice * quantity;

                    // 2. Transaction Header
                    var trans = new Transaction
                    {
                        Id = Guid.NewGuid(),
                        TransactionDate = DateTime.UtcNow.AddHours(7),
                        Type = transType,
                        Status = "COMPLETED",
                        Note = note,
                        TotalAmount = totalMoney,
                        PartnerId = partner.Id,
                        CreatedBy = "AI Assistant",
                        Details = new List<TransactionDetail>()
                    };

                    if (!string.IsNullOrEmpty(warehouseId) && Guid.TryParse(warehouseId, out Guid wId)) trans.WarehouseId = wId;

                    // 3. Logic Branching
                    if (transType == "IMPORT")
                    {
                        if (string.IsNullOrWhiteSpace(locationName)) return ReturnError("Import requires Location Code.");
                        var location = await _context.Locations.FirstOrDefaultAsync(l => l.Code.ToLower().Contains(locationName.ToLower()));
                        if (location == null) return ReturnError($"Location '{locationName}' not found.");

                        if (trans.WarehouseId == null) trans.WarehouseId = location.WarehouseId;

                        var inventory = await _context.Inventories.FirstOrDefaultAsync(i => i.ProductId == product.Id && i.LocationId == location.Id);
                        if (inventory == null)
                        {
                            inventory = new Inventory { ProductId = product.Id, LocationId = location.Id, Quantity = 0 };
                            _context.Inventories.Add(inventory);
                        }
                        inventory.Quantity += quantity;
                        product.StockQuantity += quantity;

                        trans.Details.Add(new TransactionDetail
                        {
                            Id = Guid.NewGuid(),
                            TransactionId = trans.Id,
                            ProductId = product.Id,
                            Quantity = quantity,
                            UnitPrice = unitPrice,
                            LocationId = location.Id
                        });
                    }
                    else // EXPORT
                    {
                        var queryInv = _context.Inventories.Include(i => i.Location).Where(i => i.ProductId == product.Id && i.Quantity > 0).OrderBy(i => i.Location.Code);

                        // Fix nhỏ: Ép kiểu IOrderedQueryable để tránh lỗi biên dịch khi nối Where sau OrderBy
                        var inventoryQuery = trans.WarehouseId != null
                            ? queryInv.Where(i => i.Location.WarehouseId == trans.WarehouseId)
                            : queryInv;

                        var inventoryList = await inventoryQuery.ToListAsync();

                        if (inventoryList.Sum(i => i.Quantity) < quantity) return ReturnError("Insufficient stock.");

                        if (trans.WarehouseId == null && inventoryList.Any()) trans.WarehouseId = inventoryList.First().Location.WarehouseId;

                        int remaining = quantity;
                        foreach (var inv in inventoryList)
                        {
                            if (remaining <= 0) break;
                            int take = Math.Min(inv.Quantity, remaining);
                            inv.Quantity -= take;
                            remaining -= take;
                            trans.Details.Add(new TransactionDetail
                            {
                                Id = Guid.NewGuid(),
                                TransactionId = trans.Id,
                                ProductId = product.Id,
                                Quantity = take,
                                UnitPrice = unitPrice,
                                LocationId = inv.LocationId
                            });
                        }
                        product.StockQuantity -= quantity;
                    }

                    // 4. Save
                    partner.DebtAmount += (transType == "EXPORT" ? 1 : -1) * totalMoney;
                    _context.Transactions.Add(trans);
                    _context.Products.Update(product);

                    await _context.SaveChangesAsync();
                    await transactionDB.CommitAsync();

                    string locDisplay = transType == "IMPORT" ? locationName : "Auto-Picked";

                    // 🔥 5. RETURN UNIFIED RECEIPT JSON
                    var receiptData = new
                    {
                        TransactionId = trans.Id,
                        title = transType == "IMPORT" ? "IMPORT RECEIPT" : "EXPORT RECEIPT",
                        status = "SUCCESS",
                        fields = new object[]
                        {
                    new { label = "Ticket Code", value = $"TRX-{DateTime.Now:HHmm}" },
                    new { label = "Ticket Type", value = transType == "IMPORT" ? "Import" : "Export Sale" },
                    new { label = "Product", value = productName },
                    new { label = "Quantity", value = (transType == "IMPORT" ? "+" : "-") + quantity },
                    new { label = "Location", value = locDisplay },
                    new { label = "Partner", value = partnerName },
                    new { label = "Total Amount", value = totalMoney.ToString("N0") + " đ" }
                        },
                        footer = $"New Stock: {product.StockQuantity} units"
                    };

                    return JsonSerializer.Serialize(receiptData);
                }
                catch (Exception ex)
                {
                    await transactionDB.RollbackAsync();
                    return ReturnError("System Error: " + ex.Message);
                }
            });
        }

        private string ReturnError(string msg) => JsonSerializer.Serialize(new { status = "error", message = msg });
        private async Task SendNotification(string t, string p, int q, decimal m, Guid id, string l) { /* Keep chat logic as is */ }

        [KernelFunction, Description("Create a new product.")]
        public async Task<string> CreateProduct(string name, decimal price, int stock)
        {
            try
            {
                if (await _context.Products.AnyAsync(p => p.Name == name))
                    return JsonSerializer.Serialize(new { status = "error", message = "Product already exists." });

                var product = new Product
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    Price = price,
                    StockQuantity = stock,
                    CreatedDate = DateTime.UtcNow
                };

                _context.Products.Add(product);

                // Automatically create inventory in the first warehouse (if initial stock > 0)
                if (stock > 0)
                {
                    var defaultLoc = await _context.Locations.FirstOrDefaultAsync();
                    if (defaultLoc != null)
                    {
                        _context.Inventories.Add(new Inventory
                        {
                            ProductId = product.Id,
                            LocationId = defaultLoc.Id,
                            Quantity = stock
                        });
                    }
                }

                await _context.SaveChangesAsync();

                return JsonSerializer.Serialize(new
                {
                    status = "success",
                    message = $"Successfully added product '{name}' with price {price:N0}."
                });
            }
            catch (Exception ex) { return JsonSerializer.Serialize(new { status = "error", message = ex.Message }); }
        }

        [KernelFunction, Description("Create a new system user.")]
        public async Task<string> CreateUser(string fullName, string email, string role)
        {
            try
            {
                // 1. Check duplicate
                if (await _context.Users.AnyAsync(u => u.Email == email))
                    return JsonSerializer.Serialize(new { status = "error", message = $"Email '{email}' already exists in the system." });

                // 2. Create New User (Real Logic)
                var newUser = new User
                {
                    Id = Guid.NewGuid(),
                    FullName = fullName,
                    Email = email,
                    Role = role,
                    Password = "123",
                    CreatedDate = DateTime.UtcNow
                };

                _context.Users.Add(newUser);
                await _context.SaveChangesAsync();

                // 3. Return clear Text format for Frontend
                return JsonSerializer.Serialize(new
                {
                    status = "success",
                    message = $"✅ Successfully created employee: **{fullName}**\n- Email: {email}\n- Role: {role}"
                });
            }
            catch (Exception ex) { return JsonSerializer.Serialize(new { status = "error", message = "Error: " + ex.Message }); }
        }
    }
}