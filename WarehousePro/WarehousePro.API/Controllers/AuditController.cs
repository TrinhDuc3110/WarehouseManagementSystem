using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WarehousePro.Application.Common.Interfaces;
using WarehousePro.Domain.Entities;

namespace WarehousePro.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuditController : ControllerBase
    {
        private readonly IApplicationDbContext _context;

        public AuditController(IApplicationDbContext context)
        {
            _context = context;
        }

        // GET: api/Audit
        [HttpGet]
        public async Task<IActionResult> GetLogs(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? search = null,
            [FromQuery] string? action = null,
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null,
            [FromQuery] bool onlySuspicious = false)
        {
            try
            {
                // 1. Initialize Query
                var query = _context.AuditLogs.AsNoTracking().AsQueryable();

                // 2. Filters
                if (!string.IsNullOrEmpty(search))
                {
                    string s = search.ToLower().Trim();
                    query = query.Where(x =>
                        (x.UserId != null && x.UserId.ToLower().Contains(s)) ||
                        (x.TableName != null && x.TableName.ToLower().Contains(s)) ||
                        (x.RecordId != null && x.RecordId.ToLower().Contains(s)) ||
                        (x.RiskNote != null && x.RiskNote.ToLower().Contains(s)) ||
                        (x.NewValues != null && x.NewValues.ToLower().Contains(s))
                    );
                }

                if (!string.IsNullOrEmpty(action) && action != "ALL")
                {
                    query = query.Where(x => x.Action == action);
                }

                if (fromDate.HasValue)
                    query = query.Where(x => x.CreatedDate >= fromDate.Value);

                if (toDate.HasValue)
                {
                    // Include the entire end day
                    var endOfDay = toDate.Value.Date.AddDays(1).AddTicks(-1);
                    query = query.Where(x => x.CreatedDate <= endOfDay);
                }

                if (onlySuspicious)
                    query = query.Where(x => x.IsSuspicious);

                // 3. Pagination & Execution
                var totalRecords = await query.CountAsync();

                var logs = await query
                    .OrderByDescending(x => x.CreatedDate)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                return Ok(new
                {
                    total = totalRecords,
                    page = page,
                    pageSize = pageSize,
                    data = logs
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Server error: " + ex.Message });
            }
        }
    }
}