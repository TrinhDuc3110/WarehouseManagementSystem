using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WarehousePro.Application.Common.Interfaces;
using WarehousePro.Domain.Entities;

namespace WarehousePro.API.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PaymentsController : ControllerBase
{
    private readonly IApplicationDbContext _context;

    public PaymentsController(IApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var list = await _context.Payments
            .Include(p => p.Partner)
            .OrderByDescending(p => p.PaymentDate)
            .ToListAsync();
        return Ok(list);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreatePaymentRequest request)
    {
        var partner = await _context.Partners.FindAsync(request.PartnerId);
        if (partner == null) return BadRequest("Không tìm thấy đối tác!");

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var payment = new Payment
            {
                PaymentDate = DateTime.UtcNow,
                Type = request.Type,
                Amount = request.Amount,
                Note = request.Note,
                PartnerId = request.PartnerId
            };

            _context.Payments.Add(payment);

            // CẬP NHẬT CÔNG NỢ
            if (request.Type == "RECEIPT") // Thu tiền khách
            {
                partner.DebtAmount -= request.Amount; // Khách trả bớt nợ
            }
            else // Chi trả NCC
            {
                // Giả sử: DebtAmount âm là mình nợ NCC. Trả tiền thì cộng lên (về 0)
                // Hoặc quy ước: DebtAmount dương là công nợ phải thu/phải trả tùy Type đối tác.
                // Quy ước đơn giản:
                // Khách hàng: Debt > 0 là họ nợ mình.
                // NCC: Debt > 0 là mình nợ họ.
                
                partner.DebtAmount -= request.Amount; // Trả bớt nợ
            }

            await _context.SaveChangesAsync(CancellationToken.None);
            await transaction.CommitAsync();

            return Ok(new { message = "Tạo phiếu thành công!" });
        }
        catch
        {
            await transaction.RollbackAsync();
            return BadRequest("Lỗi xử lý tài chính");
        }
    }
}

public class CreatePaymentRequest
{
    public string Type { get; set; } // RECEIPT / PAYMENT
    public decimal Amount { get; set; }
    public string? Note { get; set; }
    public Guid PartnerId { get; set; }
}