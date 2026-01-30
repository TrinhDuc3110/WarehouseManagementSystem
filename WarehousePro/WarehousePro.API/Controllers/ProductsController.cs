using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using OfficeOpenXml;
using WarehousePro.API.Hubs;
using WarehousePro.Application.Common.Interfaces;
using WarehousePro.Domain.Entities;
using WarehousePro.Application.Common.Models;

namespace WarehousePro.API.Controllers;

[Route("api/[controller]")]
[ApiController]
public class ProductsController : ControllerBase
{
    private readonly IApplicationDbContext _context;
    private readonly IHubContext<InventoryHub> _hubContext;
    private readonly IMemoryCache _cache;

    public ProductsController(
            IApplicationDbContext context,
            IHubContext<InventoryHub> hubContext,
            IMemoryCache cache)
    {
        _context = context;
        _hubContext = hubContext;
        _cache = cache;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
         [FromQuery] int page = 1,
         [FromQuery] int size = 10,
         [FromQuery] string? search = null)
    {
        // 1. Tạo Query cơ bản
        var query = _context.Products.AsQueryable();

        // 2. Xử lý Tìm kiếm (Search)
        if (!string.IsNullOrEmpty(search))
        {
            search = search.ToLower().Trim();
            query = query.Where(p => p.Name.ToLower().Contains(search) || p.SKU.ToLower().Contains(search));
        }

        // 3. Đếm tổng số dòng (để tính số trang)
        var totalCount = await query.CountAsync();

        // 4. Phân trang (Skip & Take)
        var items = await query
            .OrderByDescending(p => p.CreatedDate)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync();

        // 5. Trả về kết quả chuẩn
        var result = new PaginatedResult<Product>(items, totalCount, page, size);

        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var product = await _context.Products.FindAsync(id);
        if (product == null) return NotFound();
        return Ok(product);
    }

    [HttpPost]
    public async Task<IActionResult> Create(Product product)
    {
        _context.Products.Add(product);
        await _context.SaveChangesAsync(CancellationToken.None);
        _cache.Remove("product_list");
        return Ok(new { id = product.Id, message = "Tạo sản phẩm thành công!" });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, Product product)
    {
        if (id != product.Id) return BadRequest("Mã ID không khớp!");
        var existingProduct = await _context.Products.FindAsync(id);
        if (existingProduct == null) return NotFound("Không tìm thấy sản phẩm!");

        existingProduct.Name = product.Name;
        existingProduct.SKU = product.SKU;
        existingProduct.Category = product.Category;
        existingProduct.Price = product.Price;
        existingProduct.StockQuantity = product.StockQuantity;
        existingProduct.Unit = product.Unit;
        existingProduct.MinStockLevel = product.MinStockLevel;
        existingProduct.LastModifiedDate = DateTime.UtcNow;

        await _context.SaveChangesAsync(CancellationToken.None);
        _cache.Remove("product_list");
        return Ok(new { message = "Cập nhật thành công!" });
    }


    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var product = await _context.Products.FindAsync(id);
        if (product == null) return NotFound("Không tìm thấy sản phẩm!");

        _context.Products.Remove(product);
        await _context.SaveChangesAsync(CancellationToken.None);
        _cache.Remove("product_list");
        return Ok(new { message = "Xóa thành công!" });
    }

    [HttpGet("sku/{sku}")]
    public async Task<IActionResult> GetBySku(string sku)
    {
        var product = await _context.Products.FirstOrDefaultAsync(p => p.SKU == sku);
        if (product == null) return NotFound("Không tìm thấy sản phẩm!");
        return Ok(product);
    }

    // --- API IMPORT EXCEL (FULL - ĐÃ CÓ CHECK HEADER & TẠO PHIẾU) ---
    [HttpPost("import")]
    public async Task<IActionResult> ImportExcel(IFormFile file)
    {
        if (file == null || file.Length == 0) return BadRequest("Vui lòng chọn file Excel!");

        // Sử dụng Transaction SQL để đảm bảo an toàn dữ liệu (nhập sai là rollback hết)
        using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            // Cấu hình License EPPlus (Quan trọng)
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            // Lấy tên người dùng đang đăng nhập (để lưu vào lịch sử)
            var currentUser = User.FindFirst("FullName")?.Value ?? User.Identity?.Name ?? "Unknow User";

            using (var stream = new MemoryStream())
            {
                await file.CopyToAsync(stream);
                using (var package = new ExcelPackage(stream))
                {
                    var worksheet = package.Workbook.Worksheets[0];
                    var rowCount = worksheet.Dimension?.Rows ?? 0;

                    if (rowCount < 2) return BadRequest("File Excel không có dữ liệu!");

                    // KIỂM TRA TIÊU ĐỀ (HEADER) ĐỂ TRÁNH FILE RÁC 
                    var col1 = worksheet.Cells[1, 1].Value?.ToString()?.Trim().ToLower();
                    var col2 = worksheet.Cells[1, 2].Value?.ToString()?.Trim().ToLower();

                    // Chấp nhận các từ khóa phổ biến để linh động
                    bool isHeaderValid = (col1 != null && col1.Contains("tên")) &&
                                         (col2 != null && (col2.Contains("sku") || col2.Contains("mã")));

                    if (!isHeaderValid)
                    {
                        return BadRequest("Cấu trúc file không đúng! Cột A phải là 'Tên sản phẩm', Cột B phải là 'Mã SKU'.");
                    }

                    // 2. Khởi tạo Phiếu Nhập Kho (Transaction)
                    var importTransaction = new Transaction
                    {
                        Id = Guid.NewGuid(),
                        TransactionDate = DateTime.UtcNow,
                        Type = "IMPORT",
                        Note = $"Import Excel: {file.FileName} (Lúc {DateTime.Now:HH:mm dd/MM})",
                        CreatedBy = currentUser,
                        Details = new List<TransactionDetail>()
                    };

                    decimal totalImportValue = 0;
                    var newProducts = new List<Product>();

                    // Load toàn bộ sản phẩm hiện có vào RAM để check trùng cho nhanh
                    var existingProducts = await _context.Products.ToDictionaryAsync(p => p.SKU);

                    // Duyệt từ dòng 2 (Dữ liệu)
                    for (int row = 2; row <= rowCount; row++)
                    {
                        var name = worksheet.Cells[row, 1].Value?.ToString()?.Trim();
                        var sku = worksheet.Cells[row, 2].Value?.ToString()?.Trim();

                        if (string.IsNullOrEmpty(sku) || string.IsNullOrEmpty(name)) continue;

                        // Đọc các cột còn lại
                        var category = worksheet.Cells[row, 3].Value?.ToString()?.Trim() ?? "Khác";

                        // Xử lý giá tiền (xóa chữ 'đ', dấu phẩy, dấu chấm)
                        var priceString = worksheet.Cells[row, 4].Value?.ToString()?
                            .Replace(",", "")?.Replace(".", "")?.Replace("đ", "")?.Trim();
                        decimal.TryParse(priceString, out decimal price);

                        // Xử lý số lượng
                        var stockString = worksheet.Cells[row, 5].Value?.ToString()?
                            .Replace(",", "")?.Replace(".", "")?.Trim();
                        int.TryParse(stockString, out int quantityImport);

                        var unit = worksheet.Cells[row, 6].Value?.ToString()?.Trim() ?? "PCS";

                        Guid productId;

                        // --- LOGIC SMART UPSERT ---
                        if (existingProducts.TryGetValue(sku, out var existingProduct))
                        {
                            // TRƯỜNG HỢP 1: SẢN PHẨM ĐÃ CÓ -> CẬP NHẬT & CỘNG DỒN
                            productId = existingProduct.Id;

                            existingProduct.Name = name;
                            existingProduct.Category = category;
                            existingProduct.Unit = unit;
                            existingProduct.LastModifiedDate = DateTime.UtcNow;

                            // Cộng dồn tồn kho
                            existingProduct.StockQuantity += quantityImport;

                            // Cập nhật giá mới (nếu có giá trị)
                            if (price > 0) existingProduct.Price = price;

                            _context.Products.Update(existingProduct);
                        }
                        else
                        {
                            // TRƯỜNG HỢP 2: SẢN PHẨM MỚI -> TẠO MỚI
                            productId = Guid.NewGuid();
                            var newProduct = new Product
                            {
                                Id = productId,
                                Name = name,
                                SKU = sku,
                                Category = category,
                                Price = price,
                                StockQuantity = quantityImport, // Mới thì tồn = nhập
                                Unit = unit,
                                MinStockLevel = 5,
                                CreatedDate = DateTime.UtcNow,
                                CreatedBy = currentUser
                            };
                            newProducts.Add(newProduct);
                            existingProducts.Add(sku, newProduct); // Thêm vào dict để tránh lỗi nếu file excel có 2 dòng trùng sku
                        }

                        // Tạo chi tiết phiếu nhập (Lưu lịch sử giá nhập thời điểm này)
                        if (quantityImport > 0)
                        {
                            importTransaction.Details.Add(new TransactionDetail
                            {
                                TransactionId = importTransaction.Id,
                                ProductId = productId,
                                Quantity = quantityImport,
                                UnitPrice = price
                            });
                            totalImportValue += (quantityImport * price);
                        }
                    }

                    // Lưu dữ liệu
                    if (newProducts.Any()) await _context.Products.AddRangeAsync(newProducts);

                    // Chỉ lưu phiếu nếu có chi tiết
                    if (importTransaction.Details.Any())
                    {
                        importTransaction.TotalAmount = totalImportValue;
                        _context.Transactions.Add(importTransaction);
                    }

                    await _context.SaveChangesAsync(CancellationToken.None);
                    _cache.Remove("product_list");

                    // Chốt Transaction SQL
                    await transaction.CommitAsync();

                    // Bắn thông báo Real-time
                    await _hubContext.Clients.All.SendAsync("ReceiveUpdate", $"Vừa import {file.FileName} thành công!");
                }
            }

            return Ok(new { message = "Import dữ liệu thành công!" });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(); // Có lỗi thì hoàn tác sạch sẽ
            return BadRequest($"Lỗi Import: {ex.Message}");
        }
    }


    [HttpPost("upload-image")]
    public async Task<IActionResult> UploadImage(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("Vui lòng chọn ảnh!");

        // Kiểm tra đuôi file
        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif" };
        var extension = Path.GetExtension(file.FileName).ToLower();
        if (!allowedExtensions.Contains(extension))
            return BadRequest("Chỉ chấp nhận file ảnh (.jpg, .png, .gif)");

        // Tạo thư mục lưu trữ nếu chưa có
        var folderName = Path.Combine("wwwroot", "images");
        var pathToSave = Path.Combine(Directory.GetCurrentDirectory(), folderName);
        if (!Directory.Exists(pathToSave)) Directory.CreateDirectory(pathToSave);

        // Đặt tên file duy nhất (tránh trùng)
        var fileName = $"{Guid.NewGuid()}{extension}";
        var fullPath = Path.Combine(pathToSave, fileName);
        var dbPath = $"{Request.Scheme}://{Request.Host}/images/{fileName}"; // URL trả về

        // Lưu file
        using (var stream = new FileStream(fullPath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        return Ok(new { url = dbPath });
    }


}