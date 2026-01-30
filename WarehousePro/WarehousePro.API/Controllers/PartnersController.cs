using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WarehousePro.Application.Common.Interfaces;
using WarehousePro.Domain.Entities;

namespace WarehousePro.API.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PartnersController : ControllerBase
{
    private readonly IApplicationDbContext _context;

    public PartnersController(IApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        return Ok(await _context.Partners.OrderByDescending(p => p.CreatedDate).ToListAsync());
    }

    [HttpPost]
    public async Task<IActionResult> Create(Partner partner)
    {
        _context.Partners.Add(partner);
        await _context.SaveChangesAsync(CancellationToken.None);
        return Ok(new { message = "Thêm đối tác thành công!" });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, Partner partner)
    {
        var existing = await _context.Partners.FindAsync(id);
        if (existing == null) return NotFound();

        existing.Name = partner.Name;
        existing.Phone = partner.Phone;
        existing.Email = partner.Email;
        existing.Address = partner.Address;
        existing.Type = partner.Type;

        await _context.SaveChangesAsync(CancellationToken.None);
        return Ok(new { message = "Cập nhật thành công!" });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var p = await _context.Partners.FindAsync(id);
        if (p == null) return NotFound();

        _context.Partners.Remove(p);
        await _context.SaveChangesAsync(CancellationToken.None);
        return Ok(new { message = "Đã xóa đối tác!" });
    }
}