using Bogus;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WarehousePro.Domain.Entities;
using WarehousePro.Infrastructure.Persistence;
using WarehousePro.Application.Common.Interfaces;

namespace WarehousePro.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SeedController : ControllerBase
    {
        private readonly IApplicationDbContext _context;

        public SeedController(IApplicationDbContext context)
        {
            _context = context;
        }

        // Helper lấy giờ VN cho nhanh
        private DateTime NowVN => DateTime.UtcNow.AddHours(7);

        // ============================================================
        // 1. TẠO DỮ LIỆU NỀN (USER, WAREHOUSE, LOCATION, PARTNER)
        // ============================================================
        [HttpPost("init")]
        public async Task<IActionResult> SeedInitData()
        {
            // A. Tạo User Admin nếu chưa có
            if (!await _context.Users.AnyAsync())
            {
                var admin = new User
                {
                    Id = Guid.NewGuid(),
                    Username = "admin",
                    Password = "123", // Lưu ý: Password này chưa hash (demo)
                    FullName = "System Administrator",
                    Role = "Admin",
                    Email = "admin@warehouse.com",
                    ReceiveStockAlert = true,
                    CreatedDate = NowVN // <-- Sửa thành CreatedDate + Giờ VN
                };
                await _context.Users.AddAsync(admin);
            }

            // B. Tạo Warehouses & Locations
            if (!await _context.Warehouses.AnyAsync())
            {
                var warehouses = new List<Warehouse>
                {
                    new Warehouse { Id = Guid.NewGuid(), Name = "Kho Tổng HCM", Address = "Q.7, TP.HCM", ManagerName = "Nguyen Van A", CreatedDate = NowVN },
                    new Warehouse { Id = Guid.NewGuid(), Name = "Kho Chi Nhánh HN", Address = "Q. Cầu Giấy, HN", ManagerName = "Tran Van B", CreatedDate = NowVN }
                };
                await _context.Warehouses.AddRangeAsync(warehouses);
                await _context.SaveChangesAsync(CancellationToken.None);

                // Tạo Location cho từng kho
                var locations = new List<Location>();
                foreach (var w in warehouses)
                {
                    for (int i = 1; i <= 5; i++) // 5 Zone
                    {
                        for (int j = 1; j <= 5; j++) // 5 Kệ
                        {
                            locations.Add(new Location
                            {
                                Id = Guid.NewGuid(),
                                WarehouseId = w.Id,
                                Code = $"{w.Name.Substring(4, 2).ToUpper()}-{i}-{j}", // VD: HCM-1-1
                                Zone = $"Zone {i}",
                                Shelf = $"Shelf {j}",
                                Level = "1",
                                CreatedDate = NowVN // <-- Sửa thành CreatedDate + Giờ VN
                            });
                        }
                    }
                }
                await _context.Locations.AddRangeAsync(locations);
            }

            // C. Tạo Partners (Khách hàng & NCC)
            if (await _context.Partners.CountAsync() < 10)
            {
                var partnerFaker = new Faker<Partner>()
                    .RuleFor(p => p.Id, f => Guid.NewGuid())
                    .RuleFor(p => p.Name, f => f.Company.CompanyName())
                    .RuleFor(p => p.Email, f => f.Internet.Email())
                    .RuleFor(p => p.Phone, f => f.Phone.PhoneNumber())
                    .RuleFor(p => p.Address, f => f.Address.FullAddress())
                    .RuleFor(p => p.Type, f => f.PickRandom(new[] { "SUPPLIER", "CUSTOMER" }))
                    .RuleFor(p => p.IsActive, f => true)
                    .RuleFor(p => p.DebtAmount, f => 0)
                    .RuleFor(p => p.CreatedDate, f => NowVN); // <-- Sửa thành CreatedDate + Giờ VN

                var partners = partnerFaker.Generate(50);
                await _context.Partners.AddRangeAsync(partners);
            }

            await _context.SaveChangesAsync(CancellationToken.None);
            return Ok("Đã khởi tạo User, Kho, Vị trí, Đối tác thành công (Giờ VN)!");
        }

        // ============================================================
        // 2. TẠO SẢN PHẨM (PRODUCTS)
        // ============================================================
        [HttpPost("products")]
        public async Task<IActionResult> SeedProducts(int count = 200)
        {
            if (count > 1000) count = 1000;

            var productFaker = new Faker<Product>()
                .RuleFor(p => p.Id, f => Guid.NewGuid())
                .RuleFor(p => p.Name, f => f.Commerce.ProductName())
                .RuleFor(p => p.SKU, f => f.Commerce.Ean8())
                .RuleFor(p => p.Category, f => f.Commerce.Categories(1)[0])
                .RuleFor(p => p.Price, f => decimal.Parse(f.Commerce.Price(10000, 2000000)))
                .RuleFor(p => p.StockQuantity, f => 0) // Transaction sẽ tự cộng
                .RuleFor(p => p.MinStockLevel, f => f.Random.Int(10, 50))
                .RuleFor(p => p.Unit, f => f.PickRandom(new[] { "PCS", "BOX", "KG", "SET" }))
                .RuleFor(p => p.ImageUrl, f => f.Image.PicsumUrl())
                // Faker tạo ngày quá khứ, ta không cần cộng 7h vì nó là random
                .RuleFor(p => p.CreatedDate, f => f.Date.Past(1));

            var products = productFaker.Generate(count);
            await _context.Products.AddRangeAsync(products);
            await _context.SaveChangesAsync(CancellationToken.None);

            return Ok($"Đã tạo {count} sản phẩm (Tồn kho = 0).");
        }

        // ============================================================
        // 3. TẠO GIAO DỊCH & CẬP NHẬT KHO (TRANSACTIONS + INVENTORY)
        // ============================================================
        [HttpPost("transactions")]
        public async Task<IActionResult> SeedTransactions(int count = 500)
        {
            _context.Database.SetCommandTimeout(300);

            // 1. Load dữ liệu tham chiếu
            var products = await _context.Products.ToListAsync();
            var partners = await _context.Partners.ToListAsync();
            var locations = await _context.Locations.ToListAsync();
            var warehouseIds = locations.Select(l => l.WarehouseId).Distinct().ToList();

            if (!products.Any() || !locations.Any())
                return BadRequest("Thiếu dữ liệu nền (Product/Location). Hãy chạy API /init và /products trước.");

            var faker = new Faker();
            var transactions = new List<Transaction>();

            // Tải Inventory hiện tại từ DB lên RAM
            var existingInventories = await _context.Inventories.ToListAsync();

            for (int i = 0; i < count; i++)
            {
                var whId = faker.PickRandom(warehouseIds);
                var validLocations = locations.Where(l => l.WarehouseId == whId).ToList();
                if (!validLocations.Any()) continue;

                var type = faker.Random.Bool(0.6f) ? "IMPORT" : "EXPORT";

                // Tạo Header
                var trans = new Transaction
                {
                    Id = Guid.NewGuid(),
                    // TransactionDate random quá khứ thì giữ nguyên (hoặc cộng 7 nếu muốn chính xác giờ hiển thị)
                    TransactionDate = faker.Date.Past(1),
                    Type = type,
                    Note = "Auto Seed " + faker.Lorem.Sentence(3),
                    Status = "COMPLETED",
                    CreatedBy = "Seeder Bot",
                    CreatedDate = NowVN, // <-- Sửa thành CreatedDate + Giờ VN (Ngày tạo phiếu)
                    WarehouseId = whId,
                    PartnerId = faker.PickRandom(partners).Id,
                    Details = new List<TransactionDetail>()
                };

                decimal totalAmount = 0;
                int itemsCount = faker.Random.Int(1, 5);

                for (int j = 0; j < itemsCount; j++)
                {
                    var product = faker.PickRandom(products);
                    var loc = faker.PickRandom(validLocations);
                    var qty = faker.Random.Int(1, 50);

                    var inv = existingInventories.FirstOrDefault(x => x.ProductId == product.Id && x.LocationId == loc.Id);

                    if (type == "IMPORT")
                    {
                        if (inv == null)
                        {
                            inv = new Inventory
                            {
                                Id = Guid.NewGuid(),
                                ProductId = product.Id,
                                LocationId = loc.Id,
                                Quantity = 0,
                                CreatedDate = NowVN 
                            };
                            existingInventories.Add(inv);
                            await _context.Inventories.AddAsync(inv);
                        }
                        inv.Quantity += qty;
                        product.StockQuantity += qty;
                    }
                    else // EXPORT
                    {
                        if (inv == null || inv.Quantity < qty)
                        {
                            trans.Type = "IMPORT";
                            type = "IMPORT";

                            if (inv == null)
                            {
                                inv = new Inventory { Id = Guid.NewGuid(), ProductId = product.Id, LocationId = loc.Id, Quantity = 0, CreatedDate = NowVN };
                                existingInventories.Add(inv);
                                await _context.Inventories.AddAsync(inv);
                            }
                            inv.Quantity += qty;
                            product.StockQuantity += qty;
                        }
                        else
                        {
                            inv.Quantity -= qty;
                            product.StockQuantity -= qty;
                        }
                    }
                    inv.LastUpdated = NowVN;

                    // Thêm Detail
                    trans.Details.Add(new TransactionDetail
                    {
                        Id = Guid.NewGuid(),
                        TransactionId = trans.Id,
                        ProductId = product.Id,
                        LocationId = loc.Id,
                        Quantity = qty,
                        UnitPrice = product.Price,
                        CreatedDate = NowVN
                    });

                    totalAmount += (qty * product.Price);
                }

                trans.TotalAmount = totalAmount;
                transactions.Add(trans);

                if (transactions.Count >= 100)
                {
                    await _context.Transactions.AddRangeAsync(transactions);
                    await _context.SaveChangesAsync(CancellationToken.None);
                    transactions.Clear();
                }
            }

            if (transactions.Any())
            {
                await _context.Transactions.AddRangeAsync(transactions);
                await _context.SaveChangesAsync(CancellationToken.None);
            }

            return Ok(new { message = $"Đã tạo xong {count} giao dịch (Giờ VN). Tồn kho đã được cập nhật chuẩn!" });
        }
    }
}