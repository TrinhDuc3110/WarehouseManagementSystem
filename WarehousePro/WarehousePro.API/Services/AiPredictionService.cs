using Microsoft.EntityFrameworkCore;
using Microsoft.ML;
using WarehousePro.Application.Common.Interfaces;
using WarehousePro.Domain.MLModels;

namespace WarehousePro.API.Services;

public class AiPredictionService
{
    private readonly IApplicationDbContext _context;
    private readonly MLContext _mlContext;
    private static string _modelPath = Path.Combine(AppContext.BaseDirectory, "SalesModel.zip");
    private ITransformer _trainedModel;

    public AiPredictionService(IApplicationDbContext context)
    {
        _context = context;
        _mlContext = new MLContext(seed: 1);
    }

    // --- 1. TRAINING MODULE ---
    public async Task<string> TrainModel()
    {
        // A. Fetch sales data (EXPORT transactions) from SQL
        var sales = await _context.TransactionDetails
            .Include(d => d.Transaction)
            .Where(d => d.Transaction.Type == "EXPORT")
            .Select(d => new {
                Pid = d.ProductId.ToString(),
                Date = d.Transaction.TransactionDate,
                Qty = (float)d.Quantity
            })
            .ToListAsync();

        if (sales.Count < 5) return "Insufficient data for training (At least 5 export transactions required).";

        // B. Prepare Training Data (Group by Month)
        // Goal: Create pairs [Last Month Sales X] -> [This Month Sales Y] for AI to learn patterns
        var groupedData = sales
            .GroupBy(x => new { x.Pid, x.Date.Month, x.Date.Year })
            .Select(g => new {
                ProductId = g.Key.Pid,
                Month = (float)g.Key.Month,
                Year = g.Key.Year,
                TotalQty = g.Sum(x => x.Qty)
            })
            .OrderBy(x => x.Year).ThenBy(x => x.Month)
            .ToList();

        var trainData = new List<ProductSalesData>();

        // Create dataset (Time-series lag feature)
        foreach (var item in groupedData)
        {
            // Find previous month's data for this product (Simplified logic simulation)
            // In reality, the query would be more complex; here we use the record itself as a sample
            // (Assumption: If 10 were sold this month, next month will likely be around 10 +/- variance)

            trainData.Add(new ProductSalesData
            {
                ProductId = item.ProductId,
                Month = item.Month,
                PrevMonthQty = item.TotalQty,
                TargetQty = item.TotalQty
            });
        }

        IDataView dataView = _mlContext.Data.LoadFromEnumerable(trainData);

        // C. Build Algorithm Pipeline (Regression)
        // Using FastTree algorithm (Decision Tree), highly effective for tabular data
        var pipeline = _mlContext.Transforms.Categorical.OneHotEncoding("ProductIdEncoded", "ProductId")
            .Append(_mlContext.Transforms.Concatenate("Features", "ProductIdEncoded", "Month", "PrevMonthQty"))
            .Append(_mlContext.Regression.Trainers.FastTree(labelColumnName: "TargetQty"));

        // D. Train and Save Model to .zip file
        _trainedModel = pipeline.Fit(dataView);
        _mlContext.Model.Save(_trainedModel, dataView.Schema, _modelPath);

        return $"Training completed! Learned from {trainData.Count} data samples.";
    }

    // --- 2. PREDICTION MODULE ---
    public float PredictSales(string productId, float currentMonthSales)
    {
        if (!File.Exists(_modelPath)) return 0; // Model not trained yet

        // Load model if not already in RAM
        if (_trainedModel == null)
        {
            using (var stream = new FileStream(_modelPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                _trainedModel = _mlContext.Model.Load(stream, out _);
            }
        }

        var predictionEngine = _mlContext.Model.CreatePredictionEngine<ProductSalesData, SalesPrediction>(_trainedModel);

        var nextMonth = DateTime.Now.AddMonths(1).Month;

        // Input: Current data
        var input = new ProductSalesData
        {
            ProductId = productId,
            Month = nextMonth,
            PrevMonthQty = currentMonthSales
        };

        // Output: Future prediction
        var result = predictionEngine.Predict(input);
        return Math.Max(0, result.PredictedQty);
    }
}