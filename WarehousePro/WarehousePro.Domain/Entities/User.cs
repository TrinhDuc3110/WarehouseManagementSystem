using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WarehousePro.Domain.Common;

namespace WarehousePro.Domain.Entities
{
    public class User : BaseEntity
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty ;
        public string FullName { get; set; } = string.Empty;
        public string Role { get; set; } = "Admin";

        public string? Email { get; set; }
        public bool ReceiveStockAlert { get; set; } = false;
    }
}
