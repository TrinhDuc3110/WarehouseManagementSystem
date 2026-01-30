using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using WarehousePro.Application.Common.Interfaces;
using WarehousePro.Domain.Entities;
using BCrypt.Net;


namespace WarehousePro.API.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AuthController : ControllerBase
{
    private readonly IApplicationDbContext _context;
    private readonly IConfiguration _configuration;

    public AuthController(IApplicationDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        try
        {
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Username == request.Username);

            if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.Password))
            {
                return Unauthorized("Sai tài khoản hoặc mật khẩu!");
            }

            var token = GenerateJwtToken(user);

            return Ok(new
            {
                token = token,
                fullName = user.FullName,
                role = user.Role,
                username = user.Username
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
            return StatusCode(500, "Lỗi Server: " + ex.Message);
        }
    }

    [HttpPost("init-admin")]
    public async Task<IActionResult> InitAdmin()
    {
        if (await _context.Users.AnyAsync(u => u.Username == "admin"))
            return BadRequest("Tài khoản admin đã tồn tại!");

        var admin = new User
        {
            Username = "admin",
            Password = BCrypt.Net.BCrypt.HashPassword("123"),
            FullName = "Administrator",
            Role = "Admin",
            Email = "admin@warehousepro.com",
            CreatedDate = DateTime.Now
        };

        _context.Users.Add(admin);
        await _context.SaveChangesAsync(CancellationToken.None);

        return Ok("Đã tạo user: admin / 123 (Mật khẩu đã được mã hóa BCrypt)");
    }


    private string GenerateJwtToken(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Username),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim("FullName", user.FullName)
        };

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: DateTime.Now.AddDays(1),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public class LoginRequest
{
    public string Username { get; set; }
    public string Password { get; set; }
}