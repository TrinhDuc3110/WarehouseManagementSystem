using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WarehousePro.Domain.Entities
{
    public class ChatMessage
    {
        [Key]
        public Guid Id { get; set; }
        public string SenderName { get; set; } 
        public string SenderRole { get; set; }
        public string Content { get; set; }
        public string RoomName { get; set; } 
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public bool IsSystemMessage { get; set; } = false;

        public Guid? ChatGroupId { get; set; }
        [ForeignKey("ChatGroupId")]
        public ChatGroup ChatGroup { get; set; }
    }
}
