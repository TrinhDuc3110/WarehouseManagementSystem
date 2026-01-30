using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WarehousePro.Domain.Common;

namespace WarehousePro.Domain.Entities
{
    public class Transaction : BaseEntity
    {
        public DateTime TransactionDate { get; set; } = DateTime.UtcNow;
        public string Type { get; set; } = "IMPORT";
        public string?Note { get; set; }
        public decimal TotalAmount { get; set; }
        public string Status { get; set; } = "COMPLETED";


        public Partner? Partner { get; set; } 
        public List<TransactionDetail> Details { get; set; } = new();


       
        public Guid? WarehouseId { get; set; }
        public Guid? PartnerId { get; set; }

        [ForeignKey("WarehouseId")]
        public Warehouse? Warehouse { get; set; }
    }
}
