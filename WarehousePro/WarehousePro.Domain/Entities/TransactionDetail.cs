using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using WarehousePro.Domain.Common;

namespace WarehousePro.Domain.Entities
{
    public class TransactionDetail : BaseEntity
    {
        public Guid TransactionId { get; set; }

        [JsonIgnore]
        public Transaction? Transaction { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public Product? Product { get; set; }

        public Guid ProductId { get; set; }
        public Guid? LocationId { get; set; }
        [ForeignKey("LocationId")]
        public Location? Location { get; set; }

    }
}
