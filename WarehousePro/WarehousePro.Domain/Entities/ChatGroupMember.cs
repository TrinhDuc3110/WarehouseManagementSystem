using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WarehousePro.Domain.Entities
{
    public class ChatGroupMember
    {
        public Guid Id { get; set; }
        public Guid ChatGroupId { get; set; }
        [ForeignKey("ChatGroupId")]
        public ChatGroup ChatGroup { get; set; }

        public string UserId { get; set; }
        public string Role { get; set; } = "MEMBER";
        public DateTime JoinedAt { get; set; } = DateTime.Now;
    }
}
