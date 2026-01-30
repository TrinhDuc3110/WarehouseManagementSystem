using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WarehousePro.Domain.Common;

namespace WarehousePro.Domain.Entities
{
    public class Warehouse : BaseEntity
    {

        public string? Province { get; set; } 
        public string? District { get; set; } 
        public string? Ward { get; set; }
        public string? Street { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? Address { get; set; }
        public string? ManagerName { get; set; }

        public ICollection<Location> locations { get; set; } = new List<Location>();
    }
}
