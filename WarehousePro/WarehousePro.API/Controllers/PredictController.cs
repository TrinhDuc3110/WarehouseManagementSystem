using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WarehousePro.API.Services;
using WarehousePro.Application.Common.Interfaces;

namespace WarehousePro.API.Controllers;

[Route("api/[controller]")]
[ApiController]
public class PredictController : ControllerBase
{
    private readonly AiPredictionService _aiService;
    private readonly IApplicationDbContext _context;

    public PredictController(AiPredictionService aiService, IApplicationDbContext context)
    {
        _aiService = aiService;
        _context = context;
    }

    // 1. Nút bấm "Huấn Luyện Ngay"
    [HttpPost("train")]
    public async Task<IActionResult> Train()
    {
        try
        {
            var result = await _aiService.TrainModel();
            return Ok(new { message = result });
        }
        catch (Exception ex)
        {
            return BadRequest("Lỗi training: " + ex.Message);
        }
    }

    // 2. Lấy bảng dự báo
    [HttpGet("forecast")]
    public async Task<IActionResult> GetForecast()
    {
        var products = await _context.Products.ToListAsync();

        // 👇 SỬA: Dùng List<ForecastDto> thay vì List<object>
        var forecasts = new List<ForecastDto>();

        var currentMonth = DateTime.UtcNow.Month;
        var currentYear = DateTime.UtcNow.Year;

        foreach (var p in products)
        {
            var soldThisMonth = await _context.TransactionDetails
                .Where(d => d.ProductId == p.Id && d.Transaction.Type == "EXPORT" &&
                            d.Transaction.TransactionDate.Month == currentMonth &&
                            d.Transaction.TransactionDate.Year == currentYear)
                .SumAsync(d => d.Quantity);

            var predicted = _aiService.PredictSales(p.Id.ToString(), soldThisMonth);

            string status = "Ổn định";
            string color = "green";

            if (predicted > p.StockQuantity)
            {
                status = $"Nguy cơ thiếu hàng (Dự báo bán {predicted:N0}, Tồn {p.StockQuantity})";
                color = "red";
            }
            else if (predicted > 0 && predicted < p.StockQuantity / 5)
            {
                status = "Tồn kho quá nhiều";
                color = "orange";
            }

            // 👇 SỬA: Khởi tạo DTO
            forecasts.Add(new ForecastDto
            {
                ProductName = p.Name,
                Stock = p.StockQuantity,
                SoldMonth = soldThisMonth,
                ForecastNextMonth = (int)predicted,
                Advice = status,
                Color = color
            });
        }

        // 👇 GIỜ THÌ ORDER BY ĐƯỢC RỒI
        return Ok(forecasts.OrderByDescending(x => x.ForecastNextMonth));
    }
}

// 👇 THÊM CLASS DTO Ở CUỐI FILE
public class ForecastDto
{
    public string ProductName { get; set; }
    public int Stock { get; set; }
    public int SoldMonth { get; set; }
    public int ForecastNextMonth { get; set; }
    public string Advice { get; set; }
    public string Color { get; set; }
}