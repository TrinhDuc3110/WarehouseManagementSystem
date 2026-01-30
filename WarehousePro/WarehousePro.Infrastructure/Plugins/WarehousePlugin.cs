using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text.Json;
using WarehousePro.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace WarehousePro.Infrastructure.Persistence
{
    public class WarehousePlugin
    {
        private readonly ApplicationDbContext _context;

        public WarehousePlugin(ApplicationDbContext context)
        {
            _context = context;
        }

        // ------------------------------------------------------------------
        // DETAILED STOCK LOOKUP
        // ------------------------------------------------------------------
        [KernelFunction, Description("Check detailed inventory of a product (View exactly which warehouse and shelf it is located).")]
        public string GetStockDetails([Description("Product Name")] string productName)
        {
            // 1. Find the product
            var product = _context.Products.AsNoTracking()
                .FirstOrDefault(p => p.Name.ToLower().Contains(productName.ToLower()));

            if (product == null)
                return JsonSerializer.Serialize(new { status = "error", message = $"No product found with name '{productName}'" });

            // 2. Get detailed inventory (Includes Warehouse and Location info)
            var inventoryDetails = _context.Inventories.AsNoTracking()
                .Include(i => i.Location)
                .ThenInclude(l => l.Warehouse) // Join to Warehouse table
                .Where(i => i.ProductId == product.Id && i.Quantity > 0)
                .Select(i => new
                {
                    Warehouse = i.Location.Warehouse.Name,
                    Location = i.Location.Code,
                    Quantity = i.Quantity,
                    LastUpdated = i.LastUpdated.HasValue ? i.LastUpdated.Value.ToString("dd/MM HH:mm") : ""
                })
                .OrderBy(x => x.Warehouse)
                .ThenBy(x => x.Location)
                .ToList();

            // 3. Calculate total
            int totalStock = inventoryDetails.Sum(x => x.Quantity);

            // 4. Return result
            if (totalStock == 0)
            {
                return JsonSerializer.Serialize(new
                {
                    status = "success",
                    message = $"Product '{product.Name}' is currently out of stock (Quantity: 0)."
                });
            }

            // 🔥 IMPORTANT: Return object containing detailed list for AI to render as a Table
            var result = new
            {
                message = $"Found {totalStock} units of '{product.Name}' in the system.",
                data = inventoryDetails // AI will use this to generate the table
            };

            return JsonSerializer.Serialize(result);
        }

        // ------------------------------------------------------------------
        // REVENUE / HISTORY LOOKUP
        // ------------------------------------------------------------------
        [KernelFunction, Description("View transaction history of a specific product.")]
        public string GetProductHistory([Description("Product Name")] string productName)
        {
            var product = _context.Products.AsNoTracking()
               .FirstOrDefault(p => p.Name.ToLower().Contains(productName.ToLower()));

            if (product == null) return "Product not found.";

            var history = _context.TransactionDetails.AsNoTracking()
                .Include(d => d.Transaction)
                .Include(d => d.Location)
                .Where(d => d.ProductId == product.Id)
                .OrderByDescending(d => d.CreatedDate)
                .Take(10)
                .Select(d => new
                {
                    Date = d.CreatedDate.ToString("dd/MM/yyyy HH:mm"),
                    Type = d.Transaction.Type == "IMPORT" ? "Import" : "Export",
                    Qty = d.Quantity,
                    AtLocation = d.Location.Code
                })
                .ToList();

            return JsonSerializer.Serialize(history);
        }

        // ------------------------------------------------------------------
        // LOW STOCK ALERTS
        // ------------------------------------------------------------------
        [KernelFunction, Description("List products that are running low on stock.")]
        public string GetLowStockProducts(
            [Description("Alert threshold (Default is 10)")] int threshold = 10)
        {
            try
            {
                // USING GROUP JOIN (Safe for EF Core)
                // Meaning: From Products table, join Inventories, then calculate sum
                var lowStockItems = _context.Products.AsNoTracking()
                    .GroupJoin(
                        _context.Inventories, // Table to join
                        p => p.Id,            // Key in Product
                        i => i.ProductId,     // Key in Inventory
                        (p, invs) => new { Product = p, Inventories = invs } // Result selector
                    )
                    .Select(x => new
                    {
                        Product = x.Product.Name,
                        Price = x.Product.Price,
                        // Calculate sum quantity from the grouped inventories
                        // If list is empty (never imported) -> Sum returns 0 -> Correct behavior
                        TotalStock = x.Inventories.Sum(i => i.Quantity)
                    })
                    .Where(x => x.TotalStock <= threshold) // Filter result
                    .OrderBy(x => x.TotalStock)            // Ascending sort (0 first)
                    .Take(10)                              // Take top 10
                    .ToList();

                if (lowStockItems.Count == 0)
                {
                    return $"Currently, no products are below the threshold of {threshold}.";
                }

                return JsonSerializer.Serialize(lowStockItems);
            }
            catch (Exception ex)
            {
                // Log error to Server Console for debugging
                Console.WriteLine($"[Plugin Error] GetLowStockProducts: {ex.Message}");
                return $"System query error: {ex.Message}";
            }
        }
    }
}