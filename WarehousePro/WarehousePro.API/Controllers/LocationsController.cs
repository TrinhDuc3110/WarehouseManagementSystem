using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WarehousePro.Application.Common.Interfaces;
using WarehousePro.Domain.Entities;

namespace WarehousePro.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class LocationsController : ControllerBase
    {
        private readonly IApplicationDbContext _context;

        public LocationsController(IApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet("code/{code}")]
        public async Task<IActionResult> GetLocationByCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return BadRequest("Code is required");

            var location = await _context.Locations
                .Include(l => l.Warehouse)
                .Include(l => l.inventories)
                .FirstOrDefaultAsync(l => l.Code == code);

            if (location == null)
            {
                return NotFound(new { message = $"Location {code} not found" });
            }

            var status = location.inventories.Any(i => i.Quantity > 0) ? "Occupied" : "Empty";

            return Ok(new
            {
                id = location.Id,
                code = location.Code,
                zone = location.Zone,
                shelf = location.Shelf,
                level = location.Level,
                status = status,
                warehouseName = location.Warehouse?.Name
            });
        }

        [HttpGet("{id}/inventory")]
        public async Task<IActionResult> GetLocationInventory(Guid id)
        {
            var inventory = await _context.Inventories
                .Include(i => i.Product)
                .Where(i => i.LocationId == id)
                .Select(i => new
                {
                    i.ProductId,
                    ProductName = i.Product.Name,
                    ProductSku = i.Product.SKU,
                    ProductImage = i.Product.ImageUrl,
                    Quantity = i.Quantity,
                    LastUpdated = i.LastUpdated
                })
                .ToListAsync();

            return Ok(inventory);
        }
    }
}