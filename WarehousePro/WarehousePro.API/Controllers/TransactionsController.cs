using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Drawing;
using System.IO.Compression;
using WarehousePro.API.Hubs;
using WarehousePro.API.Services;
using WarehousePro.Application.Common.Interfaces;
using WarehousePro.Domain.Entities;

namespace WarehousePro.API.Controllers;

[Route("api/[controller]")]
[ApiController]
public class TransactionsController : ControllerBase
{
    private readonly IApplicationDbContext _context;
    private readonly IHubContext<InventoryHub> _hubContext;
    private readonly IHubContext<ChatHub> _ChatHubContext;
    private readonly IBackgroundJobClient _jobClient;
    private readonly IEmailService _emailService;

    public TransactionsController(
        IApplicationDbContext context,
        IHubContext<InventoryHub> hubContext,
        IBackgroundJobClient jobClient,
        IHubContext<ChatHub> chatHubContext,
        IEmailService emailService)
    {
        _context = context;
        _hubContext = hubContext;
        _jobClient = jobClient;
        _ChatHubContext = chatHubContext;
        _emailService = emailService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var list = await _context.Transactions
            .Include(t => t.Warehouse)
            .Include(t => t.Partner)
            .Include(t => t.Details)
                .ThenInclude(d => d.Product)
            .OrderByDescending(t => t.TransactionDate)
            .Take(100)
            .Select(t => new
            {
                t.Id,
                Code = (t.Type == "IMPORT" ? "IMP-" : "EXP-") + t.Id.ToString().Substring(0, 8).ToUpper(),
                t.Type,
                t.TransactionDate,
                t.TotalAmount,
                t.Note,
                t.Status,
                t.CreatedBy,
                WarehouseName = t.Warehouse.Name,
                WarehouseAddress = t.Warehouse.Address,
                CustomerName = t.Partner != null ? t.Partner.Name : "Walk-in Customer",
                PartnerEmail = t.Partner != null ? t.Partner.Email : "",
                Details = t.Details.Select(d => new
                {
                    d.Product.SKU,
                    ProductName = d.Product.Name,
                    d.Product.Unit,
                    d.Quantity,
                    d.UnitPrice,
                    LocationCode = d.Location != null ? d.Location.Code : "---",
                    LocationZone = d.Location != null ? d.Location.Zone : ""
                })
            })
            .ToListAsync();

        return Ok(list);
    }

    // 2. Create New Transaction (FIXED: Removed 'ct' argument)
    [HttpPost]
    public async Task<IActionResult> Create(CreateTransactionRequest request)
    {
        // Create Strategy to handle SQL Server Retry Logic
        var strategy = _context.Database.CreateExecutionStrategy();

        // FIX: Removed (ct) -> changed to ()
        return await strategy.ExecuteAsync<IActionResult>(async () =>
        {
            using var transactionDB = await _context.Database.BeginTransactionAsync();
            try
            {
                var currentUser = User.FindFirst("FullName")?.Value ?? User.Identity?.Name ?? "System";

                var transaction = new Transaction
                {
                    Type = request.Type,
                    Note = request.Note,
                    PartnerId = request.PartnerId,
                    WarehouseId = request.WarehouseId,
                    TransactionDate = DateTime.Now,
                    CreatedBy = currentUser,
                    Details = new List<TransactionDetail>()
                };

                decimal totalAmount = 0;

                foreach (var item in request.Items)
                {
                    var product = await _context.Products.FindAsync(item.ProductId);
                    if (product == null) throw new Exception($"Product {item.ProductId} not found");

                    var inventory = await _context.Inventories.FirstOrDefaultAsync(i => i.ProductId == item.ProductId && i.LocationId == item.LocationId);

                    if (request.Type == "IMPORT")
                    {
                        if (inventory == null)
                        {
                            inventory = new Inventory { ProductId = item.ProductId, LocationId = item.LocationId, Quantity = 0 };
                            _context.Inventories.Add(inventory);
                        }
                        inventory.Quantity += item.Quantity;
                        product.StockQuantity += item.Quantity;
                    }
                    else
                    {
                        if (inventory == null || inventory.Quantity < item.Quantity) throw new Exception("Insufficient stock");
                        inventory.Quantity -= item.Quantity;
                        product.StockQuantity -= item.Quantity;
                    }

                    transaction.Details.Add(new TransactionDetail
                    {
                        ProductId = item.ProductId,
                        Quantity = item.Quantity,
                        UnitPrice = item.UnitPrice,
                        LocationId = item.LocationId
                    });
                    totalAmount += (item.Quantity * item.UnitPrice);
                }

                transaction.TotalAmount = totalAmount;
                if (request.AmountPaid > 0) transaction.Note += $" | Paid: {request.AmountPaid:N0}";

                _context.Transactions.Add(transaction);

                if (request.PartnerId.HasValue && totalAmount > 0)
                {
                    var partner = await _context.Partners.FindAsync(request.PartnerId.Value);
                    if (partner != null)
                    {
                        decimal remaining = totalAmount - request.AmountPaid;
                        partner.DebtAmount += (remaining > 0 ? remaining : 0);
                        _context.Partners.Update(partner);
                    }
                }

                await _context.SaveChangesAsync(CancellationToken.None);
                await transactionDB.CommitAsync();

                // --- Send Email Background Job ---
                if (request.SendEmail && request.PartnerId.HasValue && request.EmailTemplateId.HasValue)
                {
                    _jobClient.Enqueue(() => SendTransactionEmailBackground(transaction.Id, request.EmailTemplateId.Value));
                }

                // --- Send Chat Notification ---
                if (request.NotifyGroupId.HasValue)
                {
                    try
                    {
                        string typeDisplay = request.Type == "IMPORT" ? "🟢 IMPORT" : "🔴 EXPORT";
                        string msgContent = $"{typeDisplay} #{transaction.Id}\n" +
                                            $"👤 {currentUser}\n" +
                                            $"💰 {totalAmount:N0}\n" +
                                            $"📝 {request.Note ?? "-"}" +
                                            $"[ID:{transaction.Id}]";

                        var sysMsg = new ChatMessage
                        {
                            ChatGroupId = request.NotifyGroupId.Value,
                            RoomName = request.NotifyGroupId.Value.ToString(),
                            SenderName = "SYSTEM",
                            SenderRole = "System",
                            Content = msgContent,
                            Timestamp = DateTime.UtcNow
                        };

                        _context.ChatMessages.Add(sysMsg);
                        await _context.SaveChangesAsync(CancellationToken.None);

                        await _ChatHubContext.Clients.Group(request.NotifyGroupId.Value.ToString())
                            .SendAsync("ReceiveMessage", sysMsg);
                    }
                    catch (Exception ex) { Console.WriteLine("Chat Error: " + ex.Message); }
                }

                await _hubContext.Clients.All.SendAsync("ReceiveUpdate", "New order received");
                return Ok(new { message = "Transaction created successfully" });
            }
            catch (Exception ex)
            {
                await transactionDB.RollbackAsync();
                return BadRequest(ex.Message);
            }
        });
    }

    [NonAction]
    public async Task SendTransactionEmailBackground(Guid transactionId, int templateId)
    {
        try
        {
            var transaction = await _context.Transactions
                .Include(t => t.Partner)
                .Include(t => t.Warehouse)
                .Include(t => t.Details).ThenInclude(d => d.Product)
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == transactionId);

            var template = await _context.EmailTemplates.FindAsync(templateId);

            if (transaction == null || template == null || transaction.Partner == null || string.IsNullOrEmpty(transaction.Partner.Email))
            {
                return;
            }

            string code = (transaction.Type == "IMPORT" ? "IMP-" : "EXP-") + transaction.Id.ToString().Substring(0, 8).ToUpper();
            string customerName = transaction.Partner.Name;

            string subject = template.Subject.Replace("{{MaDon}}", code).Replace("{{TenKhach}}", customerName);
            string body = template.Body.Replace("{{MaDon}}", code).Replace("{{TenKhach}}", customerName);

            var excelBytes = CreateExcelBytes(new List<Transaction> { transaction }, isSingle: true);

            using var stream = new MemoryStream(excelBytes);
            var file = new InMemoryFile(stream, 0, excelBytes.Length, "Attachments", $"Transaction_{code}.xlsx")
            {
                Headers = new HeaderDictionary(),
                ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            };

            await _emailService.SendEmailAsync(
                new List<string> { transaction.Partner.Email },
                null, null,
                subject,
                body,
                new List<IFormFile> { file }
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Background Email Error: {ex.Message}");
        }
    }

    [HttpPost("export")]
    public async Task<IActionResult> ExportExcel([FromBody] ExportRequest request)
    {
        try
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            var query = _context.Transactions.Include(t => t.Details).ThenInclude(d => d.Product).AsQueryable();

            if (request.TransactionIds != null && request.TransactionIds.Any())
                query = query.Where(t => request.TransactionIds.Contains(t.Id));
            else if (request.FromDate.HasValue && request.ToDate.HasValue)
            {
                var toDate = request.ToDate.Value.Date.AddDays(1).AddTicks(-1);
                query = query.Where(t => t.TransactionDate >= request.FromDate && t.TransactionDate <= toDate);
            }

            var list = await query.OrderByDescending(t => t.TransactionDate).ToListAsync();
            if (!list.Any()) return BadRequest("No data found!");

            if (request.Format == "ZIP") return GenerateZipFile(list);
            else return GenerateSingleExcelFile(list);
        }
        catch (Exception ex) { return BadRequest("Export error: " + ex.Message); }
    }

    [HttpPut("{id}/status")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] string newStatus)
    {
        var transaction = await _context.Transactions.Include(t => t.Details).FirstOrDefaultAsync(t => t.Id == id);
        if (transaction == null) return NotFound();
        if (transaction.Status == "CANCELLED") return BadRequest("This transaction is already cancelled!");

        transaction.Status = newStatus;
        if (newStatus == "CANCELLED" && transaction.Type == "EXPORT")
        {
            foreach (var item in transaction.Details)
            {
                var product = await _context.Products.FindAsync(item.ProductId);
                if (product != null) product.StockQuantity += item.Quantity;
            }
        }
        await _context.SaveChangesAsync(CancellationToken.None);
        await _hubContext.Clients.All.SendAsync("ReceiveUpdate", $"Order #{id.ToString().Substring(0, 5).ToUpper()} status changed to {newStatus}");
        return Ok(new { message = "Update successful!" });
    }

    // Internal Transfer (FIXED: Removed 'ct' argument)
    [HttpPost("transfer")]
    public async Task<IActionResult> InternalTransfer([FromBody] TransferRequest req)
    {
        if (req.FromLocationId == req.ToLocationId) return BadRequest("Source and destination locations are identical");

        var strategy = _context.Database.CreateExecutionStrategy();

        // FIX: Removed (ct) -> changed to ()
        return await strategy.ExecuteAsync<IActionResult>(async () =>
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var sourceInv = await _context.Inventories.Include(i => i.Location).FirstOrDefaultAsync(i => i.LocationId == req.FromLocationId && i.ProductId == req.ProductId);
                if (sourceInv == null || sourceInv.Quantity < req.Quantity) return BadRequest("Insufficient stock!");

                var destInv = await _context.Inventories.FirstOrDefaultAsync(i => i.LocationId == req.ToLocationId && i.ProductId == req.ProductId);
                if (destInv == null) { destInv = new Inventory { ProductId = req.ProductId, LocationId = req.ToLocationId, Quantity = 0 }; _context.Inventories.Add(destInv); }

                sourceInv.Quantity -= req.Quantity;
                destInv.Quantity += req.Quantity;

                var destLoc = await _context.Locations.FindAsync(req.ToLocationId);
                var log = new Transaction { Type = "TRANSFER", WarehouseId = destLoc.WarehouseId, TransactionDate = DateTime.UtcNow, Note = req.Note, Details = new List<TransactionDetail> { new TransactionDetail { ProductId = req.ProductId, Quantity = req.Quantity } } };
                _context.Transactions.Add(log);

                await _context.SaveChangesAsync(CancellationToken.None);
                await transaction.CommitAsync();
                return Ok(new { message = "Transfer successful!" });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return BadRequest(ex.Message);
            }
        });
    }

    [HttpGet("product/{sku}")]
    public async Task<IActionResult> GetHistoryByProduct(string sku)
    {
        var product = await _context.Products.AsNoTracking().FirstOrDefaultAsync(p => p.SKU == sku);
        if (product == null) return NotFound("Product not found");
        var history = await _context.TransactionDetails.Include(d => d.Transaction).Where(d => d.ProductId == product.Id).OrderByDescending(d => d.Transaction.TransactionDate).Take(50).Select(d => new { Id = d.Transaction.Id, Type = d.Transaction.Type, Date = d.Transaction.TransactionDate, Quantity = d.Quantity }).ToListAsync();
        return Ok(history);
    }

    [HttpGet("partner/{partnerId}")]
    public async Task<IActionResult> GetByPartner(Guid partnerId)
    {
        var list = await _context.Transactions.Where(t => t.PartnerId == partnerId).OrderByDescending(t => t.TransactionDate).Take(50).Select(t => new { Id = t.Id, TransactionDate = t.TransactionDate, Type = t.Type, TotalAmount = t.TotalAmount, Status = t.Status }).ToListAsync();
        return Ok(list);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var transaction = await _context.Transactions.Include(t => t.Partner).Include(t => t.Warehouse).Include(t => t.Details).ThenInclude(td => td.Product).FirstOrDefaultAsync(t => t.Id == id);
        if (transaction == null) return NotFound();
        return Ok(new
        {
            Id = transaction.Id,
            Code = "TRX-" + transaction.Id.ToString().Substring(0, 8).ToUpper(),
            TransactionDate = transaction.TransactionDate,
            Type = transaction.Type,
            Status = transaction.Status,
            TotalAmount = transaction.TotalAmount,
            Note = transaction.Note,
            PartnerName = transaction.Partner?.Name ?? "Walk-in Customer",
            WarehouseName = transaction.Warehouse?.Name ?? "Default Warehouse",
            Details = transaction.Details.Select(d => new { ProductSku = d.Product?.SKU, ProductName = d.Product?.Name, Quantity = d.Quantity, UnitPrice = d.UnitPrice }).ToList()
        });
    }

    // --- EXCEL GENERATION ---
    private IActionResult GenerateZipFile(List<Transaction> list)
    {
        using (var memoryStream = new MemoryStream())
        {
            using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
            {
                foreach (var transaction in list)
                {
                    var fileName = $"{transaction.TransactionDate:yyyyMMdd}_{transaction.Type}_{transaction.Id.ToString().Substring(0, 5)}.xlsx";
                    var excelBytes = CreateExcelBytes(new List<Transaction> { transaction }, isSingle: true);
                    var entry = archive.CreateEntry(fileName);
                    using (var entryStream = entry.Open()) { entryStream.Write(excelBytes, 0, excelBytes.Length); }
                }
            }
            memoryStream.Position = 0;
            return File(memoryStream.ToArray(), "application/zip", $"Documents_{DateTime.Now:yyyyMMdd_HHmm}.zip");
        }
    }

    private IActionResult GenerateSingleExcelFile(List<Transaction> list)
    {
        var excelBytes = CreateExcelBytes(list, isSingle: false);
        return File(excelBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Report_{DateTime.Now:yyyyMMdd}.xlsx");
    }

    private byte[] CreateExcelBytes(List<Transaction> list, bool isSingle)
    {
        using (var package = new ExcelPackage())
        {
            var worksheet = package.Workbook.Worksheets.Add("Data");
            worksheet.Column(1).Width = 20; worksheet.Column(2).Width = 35; worksheet.Column(5).Width = 20; worksheet.Column(6).Width = 25;
            int row = 1;
            foreach (var t in list)
            {
                var range = worksheet.Cells[row, 1, row, 6]; range.Merge = true; range.Style.Font.Bold = true; range.Style.Font.Color.SetColor(Color.White);
                range.Style.Fill.PatternType = ExcelFillStyle.Solid; range.Style.Fill.BackgroundColor.SetColor(t.Type == "IMPORT" ? Color.SeaGreen : Color.IndianRed);
                range.Value = $"{(t.Type == "IMPORT" ? "IMPORT" : "EXPORT")} | #{t.Id.ToString().Substring(0, 8)} | {t.TransactionDate:dd/MM/yyyy HH:mm}";
                row++;

                worksheet.Cells[row, 1].Value = "SKU"; worksheet.Cells[row, 2].Value = "Product Name"; worksheet.Cells[row, 3].Value = "Unit";
                worksheet.Cells[row, 4].Value = "Qty"; worksheet.Cells[row, 5].Value = "Price"; worksheet.Cells[row, 6].Value = "Total";
                worksheet.Cells[row, 1, row, 6].Style.Font.Italic = true;
                row++;

                foreach (var d in t.Details)
                {
                    worksheet.Cells[row, 1].Value = d.Product?.SKU; worksheet.Cells[row, 2].Value = d.Product?.Name; worksheet.Cells[row, 3].Value = d.Product?.Unit;
                    worksheet.Cells[row, 4].Value = d.Quantity; worksheet.Cells[row, 5].Value = d.UnitPrice; worksheet.Cells[row, 6].Value = d.Quantity * d.UnitPrice;
                    row++;
                }

                if (isSingle)
                {
                    worksheet.Cells[row, 5].Value = "TOTAL:"; worksheet.Cells[row, 6].Value = t.TotalAmount;
                    worksheet.Cells[row, 5, row, 6].Style.Font.Bold = true;
                    row++;
                }
                row++;
            }
            return package.GetAsByteArray();
        }
    }
}

public class InMemoryFile : IFormFile
{
    private readonly Stream _stream;
    private readonly long _length;
    private readonly string _name;
    private readonly string _fileName;

    public InMemoryFile(Stream stream, long baseStreamOffset, long length, string name, string fileName)
    {
        _stream = stream;
        _length = length;
        _name = name;
        _fileName = fileName;
    }

    public string ContentType { get; set; }
    public string ContentDisposition { get; }
    public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
    public long Length => _length;
    public string Name => _name;
    public string FileName => _fileName;

    public void CopyTo(Stream target) => _stream.CopyTo(target);
    public Task CopyToAsync(Stream target, CancellationToken cancellationToken = default) => _stream.CopyToAsync(target, cancellationToken);
    public Stream OpenReadStream() => _stream;
}

public class CreateTransactionRequest
{
    public string Type { get; set; } = "IMPORT";
    public Guid? PartnerId { get; set; }
    public Guid WarehouseId { get; set; }
    public string? Note { get; set; }
    public List<TransactionItemRequest> Items { get; set; } = new();
    public decimal AmountPaid { get; set; } = 0;
    public Guid? NotifyGroupId { get; set; }
    public bool SendEmail { get; set; } = false;
    public int? EmailTemplateId { get; set; }
}

public class TransactionItemRequest
{
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public Guid LocationId { get; set; }
}

public class ExportRequest
{
    public List<Guid>? TransactionIds { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string Format { get; set; } = "EXCEL";
}

public class TransferRequest
{
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
    public Guid FromLocationId { get; set; }
    public Guid ToLocationId { get; set; }
    public string? Note { get; set; }
}