using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Org.BouncyCastle.Ocsp;
using WarehousePro.Application.Common.Interfaces;
using WarehousePro.Domain.Entities;

namespace WarehousePro.API.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles ="Admin")]
public class UsersController : ControllerBase
{
    private readonly IApplicationDbContext _context;

    public UsersController(IApplicationDbContext context)
    {
        _context = context;
    }

    // 1. Lấy danh sách
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var users = await _context.Users
            .OrderByDescending(u => u.CreatedDate)
            .Select(u => new { u.Id, u.Username, u.FullName, u.Role, u.Email, u.ReceiveStockAlert })
            .ToListAsync();
        return Ok(users);
    }

    // 2. Lấy chi tiết 1 user
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null) return NotFound();
        return Ok(user);
    }

    // 3. Tạo người dùng mới
    [HttpPost]
    public async Task<IActionResult> Create(CreateUserRequest request)
    {
        // Kiểm tra trùng username
        if (await _context.Users.AnyAsync(u => u.Username == request.Username))
        {
            return BadRequest("Tên tài khoản đã tồn tại!");
        }

        var user = new User
        {
            Username = request.Username,
            Password = BCrypt.Net.BCrypt.HashPassword(request.Password),
            FullName = request.FullName,
            Role = request.Role,
            Email = request.Email,
            ReceiveStockAlert = request.ReceiveStockAlert
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync(CancellationToken.None);

        return Ok(new { message = "Tạo nhân viên thành công!" });
    }

    // 4. Cập nhật thông tin
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, UpdateUserRequest request)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null) return NotFound("Không tìm thấy nhân viên!");

        user.FullName = request.FullName;
        user.Role = request.Role;
        user.Email = request.Email;
        user.ReceiveStockAlert = request.ReceiveStockAlert;

        // Nếu có nhập password mới thì đổi, không thì giữ cũ
        if (!string.IsNullOrEmpty(request.Password))
        {
            user.Password = request.Password;
        }

        await _context.SaveChangesAsync(CancellationToken.None);
        return Ok(new { message = "Cập nhật thành công!" });
    }

    // 5. Xóa người dùng
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null) return NotFound();

        // Chặn không cho xóa chính mình (nếu cần thiết, check logic token ở đây)
        // Ở đây mình chặn xóa user admin gốc cho an toàn
        if (user.Username == "admin")
            return BadRequest("Không thể xóa tài khoản Admin gốc!");

        _context.Users.Remove(user);
        await _context.SaveChangesAsync(CancellationToken.None);
        return Ok(new { message = "Đã xóa nhân viên!" });
    }

    // 6. Bật/Tắt nhận thông báo nhanh (API phụ trợ)
    [HttpPut("{id}/toggle-alert")]
    public async Task<IActionResult> ToggleAlert(Guid id)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null) return NotFound();
        user.ReceiveStockAlert = !user.ReceiveStockAlert;
        await _context.SaveChangesAsync(CancellationToken.None);
        return Ok(new { message = "Đã cập nhật!", newState = user.ReceiveStockAlert });
    }
}

// DTOs
public class CreateUserRequest
{
    public string Username { get; set; }
    public string Password { get; set; }
    public string FullName { get; set; }
    public string Role { get; set; } = "Staff";
    public string? Email { get; set; }
    public bool ReceiveStockAlert { get; set; } = false;
}

public class UpdateUserRequest
{
    public string FullName { get; set; }
    public string Role { get; set; }
    public string? Email { get; set; }
    public string? Password { get; set; } // Optional
    public bool ReceiveStockAlert { get; set; }
}