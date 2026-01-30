using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WarehousePro.Infrastructure.Persistence;
using WarehousePro.Domain.Entities;

namespace WarehousePro.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class EmailTemplateController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public EmailTemplateController(ApplicationDbContext context)
        {
            _context = context;
        }

        // 1. GET ALL: Lấy danh sách mẫu
        [HttpGet]
        public async Task<ActionResult<IEnumerable<EmailTemplate>>> GetTemplates()
        {
            return await _context.EmailTemplates.OrderByDescending(t => t.CreatedAt).ToListAsync();
        }

        // 2. GET BY ID: Lấy chi tiết 1 mẫu (Hàm này cần thiết để CreatedAtAction hoạt động)
        [HttpGet("{id}")]
        public async Task<ActionResult<EmailTemplate>> GetTemplate(int id)
        {
            var template = await _context.EmailTemplates.FindAsync(id);

            if (template == null)
            {
                return NotFound();
            }

            return template;
        }

        // 3. POST: Tạo mẫu mới
        [HttpPost]
        public async Task<ActionResult<EmailTemplate>> CreateTemplate(EmailTemplate template)
        {
            // Thiết lập thời gian tạo mặc định nếu chưa có
            template.CreatedAt = DateTime.Now;

            _context.EmailTemplates.Add(template);
            await _context.SaveChangesAsync();

            // 👇 SỬA LỖI TẠI ĐÂY: Trỏ về hàm GetTemplate (số ít) thay vì GetTemplates (số nhiều)
            return CreatedAtAction(nameof(GetTemplate), new { id = template.Id }, template);
        }

        // 4. DELETE: Xóa mẫu
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteTemplate(int id)
        {
            var template = await _context.EmailTemplates.FindAsync(id);
            if (template == null) return NotFound();

            _context.EmailTemplates.Remove(template);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateTemplate(int id, EmailTemplate template)
        {
            if (id != template.Id)
            {
                return BadRequest("IDs don't match.");
            }

            // Kiểm tra xem mẫu có tồn tại không
            var existingTemplate = await _context.EmailTemplates.FindAsync(id);
            if (existingTemplate == null)
            {
                return NotFound();
            }

            // Cập nhật các trường dữ liệu
            existingTemplate.Name = template.Name;
            existingTemplate.Subject = template.Subject;
            existingTemplate.Body = template.Body;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                throw;
            }

            return NoContent();
        }
    }
}