using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using WarehousePro.Infrastructure.Persistence;
using WarehousePro.Domain.Entities;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace WarehousePro.API.Hubs
{
    public class ChatHub : Hub
    {
        private readonly ApplicationDbContext _context;

        public ChatHub(ApplicationDbContext context)
        {
            _context = context;
        }

        // =================================================================
        // 1. AUTOMATICALLY JOIN CHAT ROOMS ON CONNECTION
        // =================================================================
        public override async Task OnConnectedAsync()
        {
            var userId = Context.User?.FindFirst("sub")?.Value
                         ?? Context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            if (!string.IsNullOrEmpty(userId))
            {
                // Find groups this user belongs to in ChatGroupMembers table
                var userGroupIds = await _context.ChatGroupMembers
                    .Where(m => m.UserId == userId)
                    .Select(m => m.ChatGroupId.ToString())
                    .ToListAsync();

                // Join SignalR Group
                foreach (var gid in userGroupIds)
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, gid);
                }
            }

            await base.OnConnectedAsync();
        }

        // =================================================================
        // 2. MANUALLY JOIN CHAT ROOM
        // =================================================================
        public async Task JoinGroup(string groupId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, groupId);
        }

        // =================================================================
        // 3. LOAD MESSAGE HISTORY (Updated per your Entity)
        // =================================================================
        public async Task LoadHistory(string groupIdStr)
        {
            // Cố gắng parse GUID
            bool isGuid = Guid.TryParse(groupIdStr, out Guid groupId);

            try
            {
                var query = _context.ChatMessages.AsQueryable();

                if (isGuid)
                {
                    // ✅ Ưu tiên load theo ID nhóm
                    query = query.Where(m => m.ChatGroupId == groupId);
                }
                else
                {
                    // Fallback cho phòng cũ (nếu có)
                    query = query.Where(m => m.RoomName == groupIdStr);
                }

                // Lấy 50 tin mới nhất
                var historyDesc = await query
                    .OrderByDescending(m => m.Timestamp)
                    .Take(50)
                    .ToListAsync();

                var history = historyDesc.OrderBy(m => m.Timestamp).ToList();

                await Clients.Caller.SendAsync("LoadHistory", history);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Lỗi load history: " + ex.Message);
            }
        }

        // =================================================================
        // 4. SEND MESSAGE (Updated per your Entity)
        // =================================================================
        public async Task SendMessageToGroup(string groupIdStr, string message, string clientName)
        {
            // Parse Guid for new group
            bool isGuid = Guid.TryParse(groupIdStr, out Guid groupId);

            try
            {
                // 1. Handle Sender Name & Role
                var username = Context.User?.FindFirst("FullName")?.Value
                               ?? Context.User?.Identity?.Name;

                // Get Role from Token (if available)
                var role = Context.User?.FindFirst("role")?.Value
                           ?? Context.User?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value
                           ?? "Staff";

                // Fallback: If Guest/Not logged in, use provided clientName
                if (string.IsNullOrEmpty(username) || username == "Guest")
                {
                    username = clientName;
                    role = "Guest";
                }

                // 2. Create Entity matching your ChatMessage.cs
                var chatMsg = new ChatMessage
                {
                    // Id is auto-increment int, no need to set Guid.NewGuid()

                    ChatGroupId = isGuid ? groupId : null, // Set link if it's a proper Group Chat
                    RoomName = groupIdStr,                 // Keep RoomName for backward compatibility

                    SenderName = username,
                    SenderRole = role,                     // Save role for color display (Admin red, Staff green...)
                    Content = message,

                    IsSystemMessage = false,
                    Timestamp = DateTime.Now           
                };

                // 3. Save to DB
                _context.ChatMessages.Add(chatMsg);
                await _context.SaveChangesAsync();

                // 4. Broadcast to group
                await Clients.Group(groupIdStr).SendAsync("ReceiveMessage", chatMsg);
            }
            catch (Exception ex)
            {
                Console.WriteLine("❌ ERROR SENDING MESSAGE: " + ex.ToString());
                throw new HubException("Server error: Cannot save message.");
            }
        }


    }
}