using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WarehousePro.Application.Common.Interfaces;
using WarehousePro.Domain.Entities;
using System.Security.Claims;
using WarehousePro.Infrastructure.Services;

namespace WarehousePro.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ChatController : ControllerBase
    {
        private readonly IApplicationDbContext _context;
        private readonly AIService _aiService;

        // 👇 2. Inject AIService vào Constructor
        public ChatController(IApplicationDbContext context, AIService aiService)
        {
            _context = context;
            _aiService = aiService;
        }

        // ==========================================
        // PHẦN 1: CHAT AI (MỚI THÊM VÀO)
        // ==========================================

        [HttpPost("ask")]
        public async Task<IActionResult> AskAI([FromBody] UserQuery query)
        {
            try
            {
                // 1. Xác thực User
                var userIdString = GetCurrentUserId();
                if (string.IsNullOrEmpty(userIdString)) return Unauthorized();

                User user = null;
                if (Guid.TryParse(userIdString, out Guid userIdGuid))
                {
                    user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userIdGuid);
                }
                else
                {
                    user = await _context.Users.FirstOrDefaultAsync(u => u.Username == userIdString);
                }

                if (user == null) return Unauthorized(new { error = "User not found." });

                if (!string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase))
                {
                    return StatusCode(403, new { error = "Chỉ Admin mới có quyền sử dụng AI Assistant." });
                }

                // 2. Validate GroupId
                if (string.IsNullOrEmpty(query.GroupId) || !Guid.TryParse(query.GroupId, out Guid groupId))
                {
                    return BadRequest(new { error = "GroupId không hợp lệ." });
                }

                // 🔥 BƯỚC QUAN TRỌNG: Lấy thông tin Group để có 'RoomName'
                var chatGroup = await _context.ChatGroups.FirstOrDefaultAsync(g => g.Id == groupId);
                if (chatGroup == null)
                {
                    return BadRequest(new { error = "Nhóm chat không tồn tại." });
                }

                // Lấy tên nhóm (ví dụ: "AI Assistant") để lưu vào bảng Message
                string roomName = chatGroup.Name;

                // 3. Lưu tin nhắn User
                string senderName = user.FullName ?? user.Username ?? "Unknown";

                var userMsg = new ChatMessage
                {
                    Id = Guid.NewGuid(),
                    ChatGroupId = groupId,

                    // 🔥 FIX LỖI: Gán RoomName vào đây
                    RoomName = roomName,

                    SenderName = senderName,
                    SenderRole = "User",
                    Content = query.Text,
                    Timestamp = DateTime.UtcNow.AddHours(7)
                };
                _context.ChatMessages.Add(userMsg);
                await _context.SaveChangesAsync(CancellationToken.None);

                // 4. Gọi AI
                var response = await _aiService.ProcessChatAsync(query.Text, userIdString, query.GroupId);

                // 5. Lưu tin nhắn Bot
                string contentToSave = response.type == "text"
                    ? (response.message ?? response.text)
                    : System.Text.Json.JsonSerializer.Serialize(response);

                var botMsg = new ChatMessage
                {
                    Id = Guid.NewGuid(),
                    ChatGroupId = groupId,

                    // 🔥 FIX LỖI: Gán RoomName vào đây
                    RoomName = roomName,

                    SenderName = "AI Assistant",
                    SenderRole = "Bot",
                    Content = contentToSave,
                    Timestamp = DateTime.UtcNow.AddHours(7)
                };
                _context.ChatMessages.Add(botMsg);
                await _context.SaveChangesAsync(CancellationToken.None);

                return Ok(new
                {
                    message = response.message ?? response.text,
                    type = response.type ?? "text",
                    data = response.data,
                    path = response.path,
                    sender = "bot"
                });
            }
            catch (Exception ex)
            {
                // In chi tiết lỗi ra console server
                var innerMsg = ex.InnerException != null ? ex.InnerException.Message : "";
                Console.WriteLine($"[DB ERROR] {ex.Message} | Inner: {innerMsg}");

                return StatusCode(500, new { error = ex.Message, innerError = innerMsg });
            }
        }

        [HttpGet("ai-group")]
        public async Task<IActionResult> GetOrCreateAIGroup()
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            // 1. Tìm nhóm "AI Assistant" trong toàn bộ hệ thống (Bất kể ai tạo)
            var aiGroup = await _context.ChatGroups.FirstOrDefaultAsync(g => g.Name == "AI Assistant");

            // 2. Nếu chưa có trong DB thì tạo mới
            if (aiGroup == null)
            {
                aiGroup = new ChatGroup
                {
                    Id = Guid.NewGuid(),
                    Name = "AI Assistant",
                    IsSystemDefault = false,
                    CreatedAt = DateTime.UtcNow.AddHours(7)
                };
                _context.ChatGroups.Add(aiGroup);
                await _context.SaveChangesAsync(CancellationToken.None);
            }

            // 3. Quan trọng: Kiểm tra xem User hiện tại đã là thành viên chưa?
            var isMember = await _context.ChatGroupMembers
                .AnyAsync(m => m.ChatGroupId == aiGroup.Id && m.UserId == userId);

            // 4. Nếu chưa là thành viên -> Tự động thêm vào ngay lập tức
            if (!isMember)
            {
                _context.ChatGroupMembers.Add(new ChatGroupMember
                {
                    Id = Guid.NewGuid(),
                    ChatGroupId = aiGroup.Id,
                    UserId = userId,
                    Role = "OWNER", // Hoặc MEMBER
                    JoinedAt = DateTime.UtcNow
                });
                await _context.SaveChangesAsync(CancellationToken.None);
            }

            // 5. Trả về ID để Frontend dùng
            return Ok(new { id = aiGroup.Id, name = aiGroup.Name });
        }

        // ==========================================
        // PHẦN 2: CHAT GROUP (GIỮ NGUYÊN CODE CŨ)
        // ==========================================

        // --- HÀM PHỤ TRỢ: Lấy ID từ Token ---
        private string GetCurrentUserId()
        {
            return User.FindFirstValue("sub") ??
                   User.FindFirstValue(ClaimTypes.NameIdentifier);
        }

        [HttpGet("my-groups")]
        public async Task<IActionResult> GetMyGroups()
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var groups = await _context.ChatGroupMembers
                .AsNoTracking()
                .Where(m => m.UserId.ToLower() == userId.ToLower())
                .Include(m => m.ChatGroup)
                .Select(m => new
                {
                    Id = m.ChatGroup.Id,
                    Name = m.ChatGroup.Name,
                    IsSystemDefault = m.ChatGroup.IsSystemDefault
                })
                .ToListAsync();

            return Ok(groups);
        }

        [HttpPost("groups")]
        public async Task<IActionResult> CreateGroup([FromBody] CreateGroupRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest("Tên nhóm trống");
            var userId = GetCurrentUserId();

            var group = new ChatGroup
            {
                Id = Guid.NewGuid(),
                Name = req.Name,
                IsSystemDefault = req.IsDefault,
                CreatedAt = DateTime.UtcNow
            };
            _context.ChatGroups.Add(group);

            _context.ChatGroupMembers.Add(new ChatGroupMember
            {
                Id = Guid.NewGuid(),
                ChatGroupId = group.Id,
                UserId = userId,
                Role = "ADMIN",
                JoinedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync(CancellationToken.None);
            return Ok(new { group.Id, group.Name, group.IsSystemDefault });
        }



        [HttpPost("groups/{groupId}/members")]
        public async Task<IActionResult> AddMember(Guid groupId, [FromBody] AddMemberRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.UserId)) return BadRequest("UserID trống");

            var targetUserId = req.UserId.Trim();

            var exists = await _context.ChatGroupMembers
                .AnyAsync(m => m.ChatGroupId == groupId && m.UserId == targetUserId);

            if (exists) return BadRequest("User already in group");

            var member = new ChatGroupMember
            {
                Id = Guid.NewGuid(),
                ChatGroupId = groupId,
                UserId = targetUserId,
                Role = "MEMBER",
                JoinedAt = DateTime.UtcNow
            };

            _context.ChatGroupMembers.Add(member);
            await _context.SaveChangesAsync(CancellationToken.None);

            return Ok(new { message = "Thêm thành viên thành công" });
        }


        [HttpGet("groups/{groupId}/members")]
        public async Task<IActionResult> GetGroupMembers(Guid groupId)
        {
            var userId = GetCurrentUserId();
            // Kiểm tra user có trong nhóm không (để bảo mật)
            var inGroup = await _context.ChatGroupMembers.AnyAsync(m => m.ChatGroupId == groupId && m.UserId == userId);
            if (!inGroup) return StatusCode(403, "Bạn không phải thành viên nhóm này.");

            var members = await _context.ChatGroupMembers
                .Where(m => m.ChatGroupId == groupId)
                .Select(m => new
                {
                    m.UserId,
                    m.Role,
                    m.JoinedAt,
                    UserName = _context.Users.Where(u => u.Id.ToString() == m.UserId || u.Username == m.UserId)
                                             .Select(u => u.FullName ?? u.Username).FirstOrDefault() ?? "Unknown"
                })
                .ToListAsync();

            return Ok(members);
        }


        [HttpDelete("groups/{groupId}/members/{targetUserId}")]
        public async Task<IActionResult> RemoveMember(Guid groupId, string targetUserId)
        {
            var currentUserId = GetCurrentUserId();
            if (string.IsNullOrEmpty(currentUserId)) return Unauthorized();

            // Lấy thông tin người đang thực hiện hành động
            var requester = await _context.ChatGroupMembers
                .FirstOrDefaultAsync(m => m.ChatGroupId == groupId && m.UserId == currentUserId);

            if (requester == null) return BadRequest("Bạn không ở trong nhóm này.");

            // Logic phân quyền: Chỉ Admin/Owner mới được xóa người khác
            // Hoặc user tự xóa chính mình (Rời nhóm)
            bool isSelfLeave = currentUserId.Equals(targetUserId, StringComparison.OrdinalIgnoreCase);
            bool isAdmin = requester.Role == "ADMIN" || requester.Role == "OWNER";

            if (!isSelfLeave && !isAdmin)
            {
                return StatusCode(403, "Bạn không có quyền xóa thành viên.");
            }

            var memberToRemove = await _context.ChatGroupMembers
                .FirstOrDefaultAsync(m => m.ChatGroupId == groupId && m.UserId == targetUserId);

            if (memberToRemove == null) return NotFound("Thành viên không tồn tại trong nhóm.");

            _context.ChatGroupMembers.Remove(memberToRemove);
            await _context.SaveChangesAsync(CancellationToken.None);

            return Ok(new { message = "Đã xóa thành viên thành công." });
        }



        [HttpPost("init-default-group")]
        public async Task<IActionResult> InitDefaultGroup()
        {
            if (await _context.ChatGroups.AnyAsync(g => g.IsSystemDefault))
                return Ok("Đã có nhóm chung");

            var group = new ChatGroup
            {
                Id = Guid.NewGuid(),
                Name = "Sảnh Chung (Thông báo)",
                IsSystemDefault = true,
                CreatedAt = DateTime.UtcNow
            };
            _context.ChatGroups.Add(group);

            var userId = GetCurrentUserId();
            if (!string.IsNullOrEmpty(userId))
            {
                _context.ChatGroupMembers.Add(new ChatGroupMember
                {
                    Id = Guid.NewGuid(),
                    ChatGroupId = group.Id,
                    UserId = userId,
                    Role = "ADMIN",
                    JoinedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync(CancellationToken.None);
            return Ok("Đã tạo nhóm chung");
        }
    }

    // --- CÁC CLASS DTO ---

    public class UserQuery
    {
        public string Text { get; set; }
        public string GroupId { get; set; }
    }

    public class CreateGroupRequest
    {
        public string Name { get; set; }
        public bool IsDefault { get; set; }
    }

    public class AddMemberRequest
    {
        public string UserId { get; set; }
    }
}