using WarehousePro.Domain.Common;

namespace WarehousePro.Domain.Entities;

public class Payment : BaseEntity
{
    public DateTime PaymentDate { get; set; } = DateTime.UtcNow;
    public string Type { get; set; } = "RECEIPT"; // "RECEIPT" (Thu) hoặc "PAYMENT" (Chi)
    public decimal Amount { get; set; }
    public string? Note { get; set; }

    public Guid PartnerId { get; set; }
    public Partner? Partner { get; set; } // Nối với đối tác
}