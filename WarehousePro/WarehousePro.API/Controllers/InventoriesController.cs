using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WarehousePro.Application.Common.Interfaces;

namespace WarehousePro.API.Controllers;

[Route("api/[controller]")]
[ApiController]
public class InventoriesController : ControllerBase
{
    private readonly IApplicationDbContext _context;

    public InventoriesController(IApplicationDbContext context)
    {
        _context = context;
    }


    [HttpGet("product/{sku}")]
    public async Task<IActionResult> GetByProductSku(string sku)
    {
        // Tìm ID sản phẩm dựa trên SKU trước (để tối ưu query)
        var product = await _context.Products.FirstOrDefaultAsync(p => p.SKU == sku);
        if (product == null) return NotFound("Sản phẩm không tồn tại");

        var inventories = await _context.Inventories
            .Include(i => i.Location)
                .ThenInclude(l => l.Warehouse)
            .Where(i => i.ProductId == product.Id && i.Quantity > 0)
            .Select(i => new
            {
                LocationId = i.LocationId,
                LocationCode = i.Location.Code,
                LocationZone = i.Location.Zone, 
                WarehouseName = i.Location.Warehouse.Name,
                Quantity = i.Quantity,
            })
            .OrderBy(i => i.WarehouseName)
            .ThenBy(i => i.LocationCode)
            .ToListAsync();

        return Ok(inventories);
    }


    [HttpGet]
    public async Task<IActionResult> GetInventoryReport(
        [FromQuery] Guid? warehouseId,
        [FromQuery] string? keyword,
        [FromQuery] int page = 1,
        [FromQuery] int size = 10)
    {
        var query = _context.Inventories
            .Include(i => i.Product)
            .Include(i => i.Location)
            .ThenInclude(l => l.Warehouse)
            .Where(i => i.Quantity > 0)
            .AsQueryable();

        // 1. Lọc theo Kho
        if (warehouseId.HasValue)
        {
            query = query.Where(i => i.Location.WarehouseId == warehouseId);
        }

        // 2. Lọc theo từ khóa (Tên SP hoặc Mã SKU)
        if (!string.IsNullOrEmpty(keyword))
        {
            string lowerKeyword = keyword.ToLower();
            query = query.Where(i => i.Product.Name.ToLower().Contains(lowerKeyword)
                                  || i.Product.SKU.ToLower().Contains(lowerKeyword));
        }

        // 3. Phân trang
        var totalCount = await query.CountAsync();
        var items = await query
            .OrderBy(i => i.Product.Name)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(i => new
            {
                i.Id,
                ProductId = i.ProductId,
                ProductName = i.Product.Name,
                ProductSKU = i.Product.SKU,
                ProductImage = i.Product.ImageUrl,
                Unit = i.Product.Unit,

                WarehouseName = i.Location.Warehouse.Name,
                LocationCode = i.Location.Code,
                Zone = i.Location.Zone,
                Shelf = i.Location.Shelf,
                Level = i.Location.Level,

                Quantity = i.Quantity
            })
            .ToListAsync();

        return Ok(new
        {
            Data = items,
            Total = totalCount,
            Page = page,
            Size = size
        });
    }
}