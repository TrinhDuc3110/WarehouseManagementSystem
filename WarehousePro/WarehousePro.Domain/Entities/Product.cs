using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WarehousePro.Domain.Common;

namespace WarehousePro.Domain.Entities
{
    public class Product : BaseEntity
    {
        public string Name { get; set; } = string.Empty;
        public string SKU { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int StockQuantity { get; set; }
        public int MinStockLevel { get; set; }
        public string? ImageUrl { get; set; }
        public string Unit { get; set; } = "PCS";

        // Stock update method
        public void UpdateStock(int quantity, string type)
        {
            if(type == "Import")
            {
                StockQuantity += quantity;
            }
            else if(type == "Export")
            {
                if(StockQuantity < quantity)
                {
                    throw new Exception($"Insufficient stock! Current:{StockQuantity}");
                }
                StockQuantity -= quantity;
            }
        }
    }

}
