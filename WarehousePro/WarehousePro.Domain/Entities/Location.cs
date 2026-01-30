using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using WarehousePro.Domain.Common;

namespace WarehousePro.Domain.Entities
{
    public class Location : BaseEntity
    {
        public string Code { get; set; } = string.Empty;
        public string? Zone { get; set; }
        public string? Shelf { get; set; }
        public string? Level { get; set; }

        public Guid WarehouseId { get; set; }
        [JsonIgnore]
        public Warehouse? Warehouse { get; set; }

        public ICollection<Inventory> inventories { get; set; } = new List<Inventory>();
    }
}
