using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WarehousePro.Domain.Entities
{
    public class ChatGroup
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public bool IsSystemDefault { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public ICollection<ChatMessage> Messages { get; set; }
        public ICollection<ChatGroupMember> Members { get; set; }
    }
}
