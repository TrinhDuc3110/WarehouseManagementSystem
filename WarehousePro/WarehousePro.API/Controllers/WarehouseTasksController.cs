using k8s.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using WarehousePro.API.Hubs;
using WarehousePro.Application.Common.Interfaces;
using WarehousePro.Domain.Entities;

namespace WarehousePro.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class WarehouseTasksController : Controller
    {

        private readonly IApplicationDbContext _context;
        private readonly IHubContext<ChatHub> _chatHub;
        private readonly IHubContext<InventoryHub> _inventoryHub;


        public WarehouseTasksController(
                    IApplicationDbContext context,
                    IHubContext<InventoryHub> inventoryHub,
                    IHubContext<ChatHub> chatHub)
        {
            _context = context;
            _inventoryHub = inventoryHub;
            _chatHub = chatHub;
        }


        [HttpGet("by-location/{locationId}")]
        public async Task<IActionResult> GetTaskByLocation(Guid locationId)
        {
            try
            {
                var tasks = await _context.WarehouseTasks
               .Include(t => t.Product)
               .Where(t => t.LocationId == locationId && t.Status == "PENDING")
               .OrderBy(t => t.CreatedAt)
               .Select(t => new
               {
                   t.Id,
                   t.Type,
                   t.Quantity,
                   t.TransactionId,
                   ProductName = t.Product.Name,
                   ProductSku = t.Product.SKU,
                   ProductImage = t.Product.ImageUrl
               })
               .ToListAsync();

                return Ok(tasks);
            }
            catch (Exception ex) {
                Console.WriteLine("LỖI 500: " + ex.Message);
                return StatusCode(500, "Lỗi Server: " + ex.Message);
            }
           
        }


        [HttpPost("execute/{taskId}")]
        public async Task<IActionResult> ExecuteTask(Guid taskId)
        {
            try
            {
                var task = await _context.WarehouseTasks
                    .Include(t => t.Product) // Include Product để lấy tên hiển thị
                    .Include(t => t.Location) // Include Location để lấy tên kệ
                    .FirstOrDefaultAsync(t => t.Id == taskId);

                if (task == null || task.Status != "PENDING")
                    return BadRequest("Nhiệm vụ không tồn tại hoặc đã xong.");

                var inventory = await _context.Inventories
                    .FirstOrDefaultAsync(i => i.LocationId == task.LocationId && i.ProductId == task.ProductId);

                // --- Xử lý tồn kho ---
                if (task.Type == "IMPORT")
                {
                    if (inventory == null)
                    {
                        inventory = new Inventory
                        {
                            Id = Guid.NewGuid(),
                            LocationId = task.LocationId,
                            ProductId = task.ProductId,
                            Quantity = task.Quantity,
                            LastUpdated = DateTime.UtcNow
                        };
                        _context.Inventories.Add(inventory);
                    }
                    else
                    {
                        inventory.Quantity += task.Quantity;
                        inventory.LastUpdated = DateTime.UtcNow;
                    }
                }
                else // EXPORT
                {
                    if (inventory == null || inventory.Quantity < task.Quantity)
                        return BadRequest("Kho không đủ hàng!");

                    inventory.Quantity -= task.Quantity;
                    inventory.LastUpdated = DateTime.UtcNow;

                    if (inventory.Quantity == 0) _context.Inventories.Remove(inventory);
                }

                // Cập nhật trạng thái Task
                task.Status = "COMPLETED";
                task.CompletedAt = DateTime.UtcNow;

                // Tự động đóng Transaction
                if (task.TransactionId != null)
                {
                    var pendingCount = await _context.WarehouseTasks
                        .CountAsync(t => t.TransactionId == task.TransactionId && t.Status == "PENDING" && t.Id != taskId);

                    if (pendingCount == 0)
                    {
                        var transaction = await _context.Transactions.FindAsync(task.TransactionId);
                        if (transaction != null) transaction.Status = "COMPLETED";
                    }
                }

                await _context.SaveChangesAsync(CancellationToken.None);

                // 1. Báo InventoryHub để cập nhật số liệu
                await _inventoryHub.Clients.All.SendAsync("ReceiveUpdate", "UpdateInventory");

                // =========================================================
                // 2. 🔥 BẮN THÔNG BÁO VÀO NHÓM CHAT (TÍNH NĂNG MỚI)
                // =========================================================
                try
                {
                    // Tìm nhóm "Sảnh Chung" (Nhóm mặc định)
                    var generalGroup = await _context.ChatGroups.FirstOrDefaultAsync(g => g.IsSystemDefault);

                    // Nếu chưa có nhóm nào thì tìm đại nhóm đầu tiên, hoặc bỏ qua
                    if (generalGroup == null) generalGroup = await _context.ChatGroups.FirstOrDefaultAsync();

                    if (generalGroup != null)
                    {
                        var actionIcon = task.Type == "IMPORT" ? "🟢 NHẬP" : "🔴 XUẤT";
                        var messageContent = $"{actionIcon}: {task.Quantity}x {task.Product?.Name} tại kệ {task.Location?.Code}. (Tồn kho mới: {inventory?.Quantity ?? 0})";

                        // Lưu tin nhắn hệ thống
                        var sysMsg = new ChatMessage
                        {
                            // Id tự tăng (int) thì bỏ dòng này, nếu Guid thì để Guid.NewGuid()
                            ChatGroupId = generalGroup.Id,
                            RoomName = generalGroup.Id.ToString(),

                            SenderName = "🤖 KHO BOT",
                            SenderRole = "System",
                            Content = messageContent,
                            IsSystemMessage = true,
                            Timestamp = DateTime.UtcNow
                        };

                        _context.ChatMessages.Add(sysMsg);
                        await _context.SaveChangesAsync(CancellationToken.None);

                        // Gửi SignalR cho nhóm chat
                        await _chatHub.Clients.Group(generalGroup.Id.ToString()).SendAsync("ReceiveMessage", sysMsg);
                    }
                }
                catch (Exception chatEx)
                {
                    Console.WriteLine("⚠️ Lỗi gửi chat (không ảnh hưởng nghiệp vụ): " + chatEx.Message);
                }
                // =========================================================

                return Ok(new { message = "Thành công!" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }
    }
}
