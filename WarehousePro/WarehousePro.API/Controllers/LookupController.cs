using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WarehousePro.Infrastructure.Persistence;

namespace WarehousePro.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class LookupController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public LookupController(ApplicationDbContext context)
        {
            _context = context;
        }

        // 1. Tìm Sản phẩm
        [HttpGet("products")]
        public async Task<IActionResult> SearchProducts([FromQuery] string search = "")
        {
            var query = _context.Products.AsNoTracking().AsQueryable();
            if (!string.IsNullOrEmpty(search)) query = query.Where(p => p.Name.ToLower().Contains(search.ToLower()));

            var result = await query.Take(20)
                .Select(p => new { label = $"{p.Name} (Tổng: {p.StockQuantity})", value = p.Name })
                .ToListAsync();
            return Ok(result);
        }

        // 2. Tìm Kho (Có lọc theo Sản phẩm cho phép Xuất)
        [HttpGet("warehouses")]
        public async Task<IActionResult> SearchWarehouses([FromQuery] string search = "", [FromQuery] string productName = "")
        {
            // Case A: Nhập kho (Lấy tất cả kho để nhập vào)
            if (string.IsNullOrEmpty(productName))
            {
                var query = _context.Warehouses.AsNoTracking().AsQueryable();
                if (!string.IsNullOrEmpty(search)) query = query.Where(w => w.Name.Contains(search));
                return Ok(await query.Take(20).Select(w => new { label = w.Name, value = w.Id.ToString() }).ToListAsync());
            }

            // Case B: Xuất kho (Chỉ hiện kho nào ĐANG CÓ hàng này)
            var targetProduct = await _context.Products.FirstOrDefaultAsync(p => p.Name == productName);
            if (targetProduct == null) return Ok(new List<object>());

            // Tìm trong Inventory những chỗ có hàng > 0
            var queryInv = _context.Inventories
                .Include(i => i.Location)
                .Where(i => i.ProductId == targetProduct.Id && i.Quantity > 0);

            // Group theo Kho để tính tổng tồn tại kho đó
            var warehouseStock = await queryInv
                .GroupBy(i => i.Location.WarehouseId)
                .Select(g => new {
                    WarehouseId = g.Key,
                    TotalQty = g.Sum(x => x.Quantity)
                })
                .ToListAsync();

            var result = new List<object>();
            foreach (var item in warehouseStock)
            {
                var loc = await _context.Locations.Include(l => l.Warehouse).FirstOrDefaultAsync(l => l.WarehouseId == item.WarehouseId);
                string warehouseName = loc?.Warehouse?.Name ?? "Kho #" + item.WarehouseId;

                result.Add(new
                {
                    label = $"{warehouseName} (Có sẵn: {item.TotalQty})",
                    value = item.WarehouseId.ToString()
                });
            }

            return Ok(result);
        }

        // 3. Tìm Vị trí (Logic quan trọng nhất)
        [HttpGet("locations")]
        public async Task<IActionResult> SearchLocations([FromQuery] string search = "", [FromQuery] string warehouseId = "", [FromQuery] string productName = "")
        {
            // Case A: Nhập kho (Chỉ cần lọc theo Kho, hiện mọi vị trí trống/có hàng)
            if (string.IsNullOrEmpty(productName))
            {
                var query = _context.Locations.AsNoTracking().AsQueryable();

                if (!string.IsNullOrEmpty(warehouseId) && Guid.TryParse(warehouseId, out Guid wId))
                    query = query.Where(l => l.WarehouseId == wId);

                if (!string.IsNullOrEmpty(search))
                    query = query.Where(l => l.Code.Contains(search));

                // Trả về tên kệ bình thường
                return Ok(await query.Take(20).Select(l => new { label = l.Code, value = l.Code }).ToListAsync());
            }

            // Case B: Xuất kho (BẮT BUỘC lọc vị trí có chứa Inventory > 0 của SP đó)
            var targetProduct = await _context.Products.FirstOrDefaultAsync(p => p.Name == productName);
            if (targetProduct == null) return Ok(new List<object>());

            var queryInv = _context.Inventories
                .Include(i => i.Location)
                .Where(i => i.ProductId == targetProduct.Id && i.Quantity > 0);

            // Lọc tiếp theo Kho đã chọn
            if (!string.IsNullOrEmpty(warehouseId) && Guid.TryParse(warehouseId, out Guid wId2))
                queryInv = queryInv.Where(i => i.Location.WarehouseId == wId2);

            if (!string.IsNullOrEmpty(search))
                queryInv = queryInv.Where(i => i.Location.Code.Contains(search));

            var result = await queryInv.Take(20)
                .Select(i => new {
                    label = $"{i.Location.Code} (Tồn: {i.Quantity})", // 🔥 Hiện rõ tồn kho tại kệ này
                    value = i.Location.Code
                })
                .ToListAsync();

            return Ok(result);
        }

        // 4. Đối tác
        [HttpGet("partners")]
        public async Task<IActionResult> SearchPartners([FromQuery] string search = "")
        {
            var query = _context.Partners.AsNoTracking().AsQueryable();
            if (!string.IsNullOrEmpty(search)) query = query.Where(p => p.Name.Contains(search));
            return Ok(await query.Take(20).Select(p => new { label = p.Name, value = p.Name }).ToListAsync());
        }
    }
}