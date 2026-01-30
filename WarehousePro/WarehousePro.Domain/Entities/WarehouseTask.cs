using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WarehousePro.Domain.Entities
{
    public class WarehouseTask
    {
        public Guid Id { get; set; }
        public string Type { get; set; } 

        public int Quantity { get; set; }
        public string Status { get; set; } = "PENDING";


        public Guid? TransactionId { get; set; }
        public Guid ProductId { get; set; }
        [ForeignKey("ProductId")]
        public Product Product { get; set; }


        public Guid LocationId { get; set; }
        [ForeignKey("LocationId")]
        public Location Location { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
    }
}
