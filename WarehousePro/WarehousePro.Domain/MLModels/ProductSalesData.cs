using Microsoft.ML.Data;

namespace WarehousePro.Domain.MLModels;

// 1. Data to train AI (Input)
public class ProductSalesData
{
    [LoadColumn(0)] public string ProductId; 
    [LoadColumn(1)] public float Month;     
    [LoadColumn(2)] public float PrevMonthQty; 

    [LoadColumn(3)] public float TargetQty;
}

// 2. (Output)
public class SalesPrediction
{
    [ColumnName("Score")]
    public float PredictedQty; 
}