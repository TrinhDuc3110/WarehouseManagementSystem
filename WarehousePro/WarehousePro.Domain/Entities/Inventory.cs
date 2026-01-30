using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using WarehousePro.Domain.Common;

namespace WarehousePro.Domain.Entities
{
    public class Inventory: BaseEntity
    {
        public Guid ProductId { get; set; }
        [JsonIgnore]
        public Product? Product { get; set; }

        public Guid LocationId { get; set; }
        [JsonIgnore]
        public Location Location { get; set; }

        public int Quantity { get; set; }
        public DateTime? LastUpdated { get; set; }
    }
}
