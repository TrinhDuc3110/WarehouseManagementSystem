using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WarehousePro.Application.Common.Interfaces;

namespace WarehousePro.API.Controllers;

[Route("api/[controller]")]
[ApiController]
public class DashboardController : ControllerBase
{
    private readonly IApplicationDbContext _context;

    public DashboardController(IApplicationDbContext context)
    {
        _context = context;
    }

    // 1. API Tổng quan NÂNG CAO (Có so sánh tăng giảm)
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary()
    {
        var now = DateTime.UtcNow;
        var startOfThisMonth = new DateTime(now.Year, now.Month, 1);
        var startOfLastMonth = startOfThisMonth.AddMonths(-1);

        // 1. Tổng số sản phẩm
        var totalProducts = await _context.Products.CountAsync();

        // 2. Tổng giá trị tồn kho
        var totalInventoryValue = await _context.Products.SumAsync(p => p.StockQuantity * p.Price);

        // 3. Số sản phẩm sắp hết
        var lowStockProducts = await _context.Products.CountAsync(p => p.StockQuantity <= p.MinStockLevel);

        // 4. Doanh thu tháng này (Chỉ tính phiếu Xuất)
        var revenueThisMonth = await _context.Transactions
            .Where(t => t.Type == "EXPORT" && t.TransactionDate >= startOfThisMonth)
            .SumAsync(t => t.TotalAmount);

        // 5. Doanh thu tháng trước (Để tính % tăng trưởng)
        var revenueLastMonth = await _context.Transactions
            .Where(t => t.Type == "EXPORT" && t.TransactionDate >= startOfLastMonth && t.TransactionDate < startOfThisMonth)
            .SumAsync(t => t.TotalAmount);

        // Tính % tăng trưởng (FIX LỖI: Ép kiểu double)
        double growthRate = 0;
        if (revenueLastMonth > 0)
        {
            growthRate = (double)(revenueThisMonth - revenueLastMonth) / (double)revenueLastMonth * 100;
        }
        else if (revenueThisMonth > 0)
        {
            growthRate = 100; // Tăng trưởng 100% nếu tháng trước = 0
        }

        // 6. Lấy 5 giao dịch gần nhất
        var recentTransactions = await _context.Transactions
            .OrderByDescending(t => t.TransactionDate)
            .Take(6)
            .Select(t => new
            {
                t.Id,
                Type = t.Type,
                Date = t.TransactionDate,
                Note = t.Note,
                TotalAmount = t.TotalAmount,
                Status = t.Status ?? "COMPLETED"
            })
            .ToListAsync();

        // 7. Lấy Top 5 sản phẩm tồn kho nhiều nhất
        var topStockProducts = await _context.Products
            .OrderByDescending(p => p.StockQuantity * p.Price)
            .Take(5)
            .Select(p => new { Name = p.Name, Value = p.StockQuantity * p.Price })
            .ToListAsync();

        return Ok(new
        {
            TotalProducts = totalProducts,
            TotalInventoryValue = totalInventoryValue,
            LowStockProducts = lowStockProducts,
            RevenueThisMonth = revenueThisMonth,
            GrowthRate = Math.Round(growthRate, 1),
            RecentActivities = recentTransactions,
            TopStockProducts = topStockProducts
        });
    }

    // 2. API Dữ liệu biểu đồ (Hỗ trợ Custom Range)
    // GET: api/dashboard/chart-data?period=7days
    // GET: api/dashboard/chart-data?from=2023-10-29&to=2023-11-17
    [HttpGet("chart-data")]
    public async Task<IActionResult> GetChartData(
        [FromQuery] string period = "7days",
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var now = DateTime.UtcNow.Date;
        DateTime startDate;
        DateTime endDate = now;
        string dateFormat = "dd/MM"; // Mặc định format ngày/tháng

        // 1. XÁC ĐỊNH KHOẢNG THỜI GIAN
        if (from.HasValue && to.HasValue)
        {
            // Trường hợp User chọn ngày
            startDate = from.Value.Date;
            endDate = to.Value.Date;
        }
        else
        {
            // Trường hợp chọn Preset (7 ngày, Tháng, Năm)
            switch (period.ToLower())
            {
                case "month": // 30 ngày qua
                    startDate = now.AddDays(-29);
                    break;
                case "year": // 12 tháng qua (Gom nhóm theo tháng)
                    // Gọi hàm riêng xử lý theo tháng để biểu đồ đỡ dày đặc
                    return await GetChartDataByMonth(now.AddMonths(-11));
                case "7days":
                default:
                    startDate = now.AddDays(-6);
                    break;
            }
        }

        // 2. TRUY VẤN DỮ LIỆU
        // Chỉnh endDate về cuối ngày để lấy trọn vẹn giao dịch trong ngày đó
        var queryEndDate = endDate.AddDays(1).AddTicks(-1);

        var rawData = await _context.Transactions
            .Where(t => t.TransactionDate >= startDate && t.TransactionDate <= queryEndDate)
            .GroupBy(t => new { t.TransactionDate.Date, t.Type })
            .Select(g => new
            {
                Date = g.Key.Date,
                Type = g.Key.Type,
                Total = g.Sum(t => t.TotalAmount)
            })
            .ToListAsync();

        // 3. CHUẨN HÓA DỮ LIỆU TRẢ VỀ (Lấp đầy các ngày trống)
        var chartData = new List<object>();
        var totalDays = (endDate - startDate).Days + 1; // Tính tổng số ngày cần vẽ

        // Giới hạn: Nếu chọn khoảng quá rộng (> 60 ngày) thì API này vẫn trả về từng ngày
        // Frontend nên dùng RangePicker giới hạn lại hoặc Backend tự group theo tuần (Nâng cao sau)

        for (int i = 0; i < totalDays; i++)
        {
            var date = startDate.AddDays(i);
            var dateLabel = date.ToString(dateFormat);

            var importAmount = rawData.FirstOrDefault(x => x.Date == date && x.Type == "IMPORT")?.Total ?? 0;
            var exportAmount = rawData.FirstOrDefault(x => x.Date == date && x.Type == "EXPORT")?.Total ?? 0;

            chartData.Add(new
            {
                Date = dateLabel,
                Import = importAmount,
                Export = exportAmount
            });
        }

        return Ok(chartData);
    }

    // (Hàm GetChartDataByMonth giữ nguyên như cũ)
    private async Task<IActionResult> GetChartDataByMonth(DateTime startDate)
    {
        // ... (Code cũ không đổi) ...
        // Copy lại hàm GetChartDataByMonth từ code trước
        var rawData = await _context.Transactions
           .Where(t => t.TransactionDate >= startDate)
           .GroupBy(t => new { t.TransactionDate.Year, t.TransactionDate.Month, t.Type })
           .Select(g => new
           {
               Year = g.Key.Year,
               Month = g.Key.Month,
               Type = g.Key.Type,
               Total = g.Sum(t => t.TotalAmount)
           })
           .ToListAsync();

        var chartData = new List<object>();
        for (int i = 0; i < 12; i++)
        {
            var d = startDate.AddMonths(i);
            var dateLabel = d.ToString("MM/yyyy");
            var importAmount = rawData.FirstOrDefault(x => x.Year == d.Year && x.Month == d.Month && x.Type == "IMPORT")?.Total ?? 0;
            var exportAmount = rawData.FirstOrDefault(x => x.Year == d.Year && x.Month == d.Month && x.Type == "EXPORT")?.Total ?? 0;
            chartData.Add(new { Date = dateLabel, Import = importAmount, Export = exportAmount });
        }
        return Ok(chartData);
    }

    // 3. API Top 5 Sản phẩm bán chạy nhất
    [HttpGet("top-selling")]
    public async Task<IActionResult> GetTopSelling()
    {
        var data = await _context.TransactionDetails
            .Include(d => d.Transaction) // Include để check Type
            .Where(d => d.Transaction.Type == "EXPORT")
            .GroupBy(d => d.Product.Name)
            .Select(g => new
            {
                Name = g.Key,
                Quantity = g.Sum(x => x.Quantity),
                Revenue = g.Sum(x => x.Quantity * x.UnitPrice)
            })
            .OrderByDescending(x => x.Quantity)
            .Take(5)
            .ToListAsync();

        return Ok(data);
    }

    // 4. API Thống kê Tài chính (7 ngày qua - Dùng cho ReportPage)
    [HttpGet("finance-chart")]
    public async Task<IActionResult> GetFinanceChart()
    {
        var sevenDaysAgo = DateTime.UtcNow.AddDays(-6).Date;

        var rawData = await _context.Transactions
            .Where(t => t.TransactionDate >= sevenDaysAgo)
            .GroupBy(t => new { t.TransactionDate.Date, t.Type })
            .Select(g => new
            {
                Date = g.Key.Date,
                Type = g.Key.Type,
                Total = g.Sum(t => t.TotalAmount)
            })
            .ToListAsync();

        var chartData = new List<object>();

        for (int i = 0; i < 7; i++)
        {
            var date = sevenDaysAgo.AddDays(i);
            var dateLabel = date.ToString("dd/MM");

            var revenue = rawData.FirstOrDefault(x => x.Date == date && x.Type == "EXPORT")?.Total ?? 0;
            var cost = rawData.FirstOrDefault(x => x.Date == date && x.Type == "IMPORT")?.Total ?? 0;
            var profit = revenue - cost;

            // Trả về key viết thường (date, revenue...) cho khớp với Recharts ở ReportPage
            chartData.Add(new
            {
                date = dateLabel,
                revenue = revenue,
                cost = cost,
                profit = profit
            });
        }

        return Ok(chartData);
    }

    // 5. API AI Phân tích & Gợi ý (Cho Dashboard AI Panel)
    [HttpGet("ai-insights")]
    public async Task<IActionResult> GetAIInsights()
    {
        var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);

        var salesData = await _context.TransactionDetails
            .Include(d => d.Transaction)
            .Where(d => d.Transaction.Type == "EXPORT" && d.Transaction.TransactionDate >= thirtyDaysAgo)
            .GroupBy(d => d.ProductId)
            .Select(g => new { ProductId = g.Key, TotalSold = g.Sum(x => x.Quantity) })
            .ToListAsync();

        var products = await _context.Products.ToListAsync();
        var insights = new List<object>();

        // Logic: Hàng bán chạy sắp hết
        foreach (var s in salesData)
        {
            var product = products.FirstOrDefault(p => p.Id == s.ProductId);
            if (product == null) continue;

            double avgDailySales = (double)s.TotalSold / 30.0;
            double daysLeft = avgDailySales > 0 ? (product.StockQuantity / avgDailySales) : 999;

            if (daysLeft < 7 && product.StockQuantity > 0)
            {
                insights.Add(new
                {
                    Type = "WARNING",
                    Icon = "fire",
                    Color = "red",
                    Title = "Sản phẩm bán chạy báo động",
                    Message = $"{product.Name} bán rất nhanh ({s.TotalSold}/tháng). Dự kiến cháy hàng trong {Math.Round(daysLeft, 1)} ngày."
                });
            }
        }

        // Logic: Gợi ý nhập hàng
        var lowStock = products.Where(p => p.StockQuantity <= p.MinStockLevel).ToList();
        if (lowStock.Any())
        {
            insights.Add(new
            {
                Type = "SUGGESTION",
                Icon = "bulb",
                Color = "gold",
                Title = "Gợi ý nhập hàng",
                Message = $"Có {lowStock.Count} sản phẩm dưới định mức. Hãy tạo phiếu nhập ngay."
            });
        }

        if (!insights.Any())
        {
            insights.Add(new { Type = "SUCCESS", Icon = "check_circle", Color = "green", Title = "Hệ thống ổn định", Message = "Mọi chỉ số đều tốt." });
        }

        return Ok(insights);
    }
}