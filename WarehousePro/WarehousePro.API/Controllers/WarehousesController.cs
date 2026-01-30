using Microsoft.AspNetCore.Mvc;
using WarehousePro.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using WarehousePro.Domain.Entities;

namespace WarehousePro.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class WarehousesController : Controller
    {
        private readonly IApplicationDbContext _context;

        public WarehousesController(IApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var warehouses = await _context.Warehouses
                .Include(w => w.locations)
                .ThenInclude(l => l.inventories)
                .ToListAsync();

            var result = warehouses.Select(w => new
            {
                w.Id,
                w.Name,
                w.Address,
                w.ManagerName, 

                locations = w.locations.Select(l => new
                {
                    l.Id,
                    l.Code,
                    l.Zone,
                    l.Shelf,
                    l.Level,
                    l.WarehouseId,

                    status = l.inventories != null && l.inventories.Any(i => i.Quantity > 0)
                        ? "Occupied"
                        : "Empty"
                }).OrderBy(l => l.Code).ToList()
            });

            return Ok(result);
        }

        [HttpPost]
        public async Task<IActionResult> Create(Warehouse warehouses)
        {
            _context.Warehouses.Add(warehouses);
            await _context.SaveChangesAsync(CancellationToken.None);

            return Ok(new
            {
                message = "Tạo kho thành công!",
                id = warehouses.Id,
            });
        }

        [HttpDelete("locations/{id}")]
        public async Task<IActionResult> DeleteLocation(Guid id)
        {
            var location = await _context.Locations.Include(l => l.inventories).FirstOrDefaultAsync(l => l.Id == id);
            if (location == null) return NotFound();

            if (location.inventories.Any(i => i.Quantity > 0))
                return BadRequest("Vị trí này đang có hàng, không thể xóa!");

            _context.Locations.Remove(location);
            await _context.SaveChangesAsync(CancellationToken.None);
            return Ok(new { message = "Đã xóa vị trí" });
        }

        [HttpGet("locations/{id}/inventory")]
        public async Task<IActionResult> GetLocationInventory(Guid id)
        {
            var data = await _context.Inventories
                .Include(i => i.Product)
                .Where(i => i.LocationId == id && i.Quantity > 0)
                .Select(i => new
                {
                    i.ProductId,
                    ProductName = i.Product.Name,
                    ProductSku = i.Product.SKU,
                    ProductImage = i.Product.ImageUrl,
                    i.Quantity
                })
                .ToListAsync();
            return Ok(data);
        }

        [HttpPost("{warehouseId}/locations")]
        public async Task<IActionResult> AddLocation(Guid warehouseId, [FromBody] CreateLocationRequest req)
        {
            var warehouse = await _context.Warehouses.FindAsync(warehouseId);
            if (warehouse == null) return NotFound("Kho không tồn tại");

            var exists = await _context.Locations.AnyAsync(l => l.Code == req.Code && l.WarehouseId == warehouseId);
            if (exists) return BadRequest($"Mã vị trí {req.Code} đã tồn tại trong kho này.");

            var location = new Location
            {
                WarehouseId = warehouseId,
                Code = req.Code,
                Zone = req.Zone,
                Shelf = req.Shelf,
                Level = req.Level,
            };

            _context.Locations.Add(location);
            await _context.SaveChangesAsync(CancellationToken.None);
            return Ok(new { message = "Thêm vị trí thành công!" });
        }

        [HttpGet("check-stock/{productId}")]
        public async Task<IActionResult> CheckStockLocation(Guid productId)
        {
            var data = await _context.Inventories
                .Include(i => i.Location)
                .ThenInclude(l => l.Warehouse)
                .Where(i => i.ProductId == productId && i.Quantity > 0)
                .Select(i => new
                {
                    LocationId = i.LocationId,
                    WarehouseId = i.Location.WarehouseId,
                    Warehouse = i.Location.Warehouse.Name,
                    LocationCode = i.Location.Code,
                    Zone = i.Location.Zone,
                    Quantity = i.Quantity
                })
                .ToListAsync();

            return Ok(data);
        }

        [HttpPost("{warehouseId}/locations/generate")]
        public async Task<IActionResult> GenerateLocations(Guid warehouseId, [FromBody] GenerateLocationsRequest req)
        {
            var warehouse = await _context.Warehouses.FindAsync(warehouseId);
            if (warehouse == null) return NotFound("Kho không tồn tại");

            var newLocations = new List<Location>();
            var existingCodes = await _context.Locations
                .Where(x => x.WarehouseId == warehouseId)
                .Select(x => x.Code)
                .ToListAsync();

            var existingCodesSet = new HashSet<string>(existingCodes);

            for (int s = req.ShelfFrom; s <= req.ShelfTo; s++)
            {
                for (int l = req.LevelFrom; l <= req.LevelTo; l++)
                {
                    var shelfCode = s.ToString("D2");
                    var levelCode = l.ToString("D2");
                    var fullCode = $"{req.Zone}-{shelfCode}-{levelCode}";

                    if (!existingCodesSet.Contains(fullCode))
                    {
                        newLocations.Add(new Location
                        {
                            WarehouseId = warehouseId,
                            Code = fullCode,
                            Zone = req.Zone,
                            Shelf = shelfCode,
                            Level = levelCode
                        });
                    }
                }
            }

            if (newLocations.Any())
            {
                _context.Locations.AddRange(newLocations);
                await _context.SaveChangesAsync(CancellationToken.None);
            }

            return Ok(new { message = $"Đã sinh thành công {newLocations.Count} vị trí mới!", count = newLocations.Count });
        }

        // --- MỚI THÊM: API NHẬP / XUẤT KHO ---
        // Gọi API này từ React để Import/Export hàng
        [HttpPost("inventory/adjust")]
        public async Task<IActionResult> AdjustInventory([FromBody] AdjustInventoryRequest req)
        {
            // 1. Check Vị trí
            var location = await _context.Locations.FindAsync(req.LocationId);
            if (location == null) return NotFound("Không tìm thấy vị trí lưu trữ.");

            // 2. Check Tồn kho hiện tại
            var inventory = await _context.Inventories
                .FirstOrDefaultAsync(i => i.LocationId == req.LocationId && i.ProductId == req.ProductId);

            if (req.Type == "IMPORT") // --- Logic NHẬP ---
            {
                if (inventory == null)
                {
                    inventory = new Inventory
                    {
                        Id = Guid.NewGuid(),
                        LocationId = req.LocationId,
                        ProductId = req.ProductId,
                        Quantity = req.Quantity,
                        LastUpdated = DateTime.UtcNow
                    };
                    _context.Inventories.Add(inventory);
                }
                else
                {
                    inventory.Quantity += req.Quantity;
                    inventory.LastUpdated = DateTime.UtcNow;
                }
            }
            else if (req.Type == "EXPORT")
            {
                if (inventory == null || inventory.Quantity < req.Quantity)
                {
                    return BadRequest($"Không đủ hàng để xuất! Tồn hiện tại: {(inventory?.Quantity ?? 0)}");
                }

                inventory.Quantity -= req.Quantity;
                inventory.LastUpdated = DateTime.UtcNow;

                // Nếu xuất hết sạch thì xóa dòng record cho sạch Database
                if (inventory.Quantity == 0)
                {
                    _context.Inventories.Remove(inventory);
                }
            }

            await _context.SaveChangesAsync(CancellationToken.None);
            return Ok(new { message = "Cập nhật kho thành công!" });
        }
    }

    // --- CÁC CLASS DTO ---
    public class GenerateLocationsRequest
    {
        public string Zone { get; set; } = "A";
        public int ShelfFrom { get; set; } = 1;
        public int ShelfTo { get; set; } = 1;
        public int LevelFrom { get; set; } = 1;
        public int LevelTo { get; set; } = 1;
    }

    public class CreateLocationRequest
    {
        public string Code { get; set; }
        public string? Zone { get; set; }
        public string? Shelf { get; set; }
        public string? Level { get; set; }
    }

    public class AdjustInventoryRequest
    {
        public Guid LocationId { get; set; }
        public Guid ProductId { get; set; }
        public int Quantity { get; set; }
        public string Type { get; set; } 
    }
}