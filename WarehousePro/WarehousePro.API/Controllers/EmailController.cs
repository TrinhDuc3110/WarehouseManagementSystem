using Microsoft.AspNetCore.Mvc;
using WarehousePro.API.Services;
using System.ComponentModel.DataAnnotations;

namespace WarehousePro.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class EmailController : ControllerBase
    {
        private readonly IEmailService _emailService;

        public EmailController(IEmailService emailService)
        {
            _emailService = emailService;
        }

        [HttpPost("send")]
        [DisableRequestSizeLimit]
        public async Task<IActionResult> SendEmail([FromForm] EmailRequestDto request)
        {
            try
            {
                // 1. Validate cơ bản
                if ((request.ToEmails == null || !request.ToEmails.Any()) &&
                    (request.CcEmails == null || !request.CcEmails.Any()) &&
                    (request.BccEmails == null || !request.BccEmails.Any()))
                {
                    return BadRequest("Phải có ít nhất một người nhận (To, CC hoặc BCC).");
                }

                // 2. Log thông tin file (Debug)
                if (request.Attachments != null && request.Attachments.Count > 0)
                {
                    foreach (var file in request.Attachments)
                    {
                        Console.WriteLine($"📎 Đính kèm: {file.FileName} ({file.Length} bytes)");
                    }
                }

                // 3. Gọi Service gửi mail (Giả sử Service đã update để nhận CC/BCC)
                // Lưu ý: Nếu gửi Bulk (nhiều người), bạn nên loop hoặc dùng tính năng BCC của SMTP
                await _emailService.SendEmailAsync(
                    request.ToEmails,
                    request.CcEmails,
                    request.BccEmails,
                    request.Subject,
                    request.Body,
                    request.Attachments
                );

                // 4. (TODO) Lưu log vào database tại đây:
                // _emailLogRepository.Add(new EmailLog { ... });

                return Ok(new { message = "Email đã được gửi thành công!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Lỗi Controller: {ex.Message}");
                return StatusCode(500, new { message = "Lỗi hệ thống: " + ex.Message });
            }
        }
    }

    // Update DTO để hỗ trợ CC và BCC
    public class EmailRequestDto
    {
        public List<string> ToEmails { get; set; } = new List<string>();
        public List<string>? CcEmails { get; set; } // Mới
        public List<string>? BccEmails { get; set; } // Mới
        [Required]
        public string Subject { get; set; } = "";
        [Required]
        public string Body { get; set; } = "";
        public List<IFormFile>? Attachments { get; set; }
    }
}