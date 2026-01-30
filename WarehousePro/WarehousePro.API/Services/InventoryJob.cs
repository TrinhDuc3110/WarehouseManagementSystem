using Microsoft.EntityFrameworkCore;
using WarehousePro.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration; // Đảm bảo có using này cho IConfiguration

namespace WarehousePro.API.Services;

public class InventoryJob
{
    private readonly IApplicationDbContext _context;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _config;

    public InventoryJob(IApplicationDbContext context, IEmailService emailService, IConfiguration config)
    {
        _context = context;
        _emailService = emailService;
        _config = config;
    }

    // --- JOB 1: GỬI BÁO CÁO TỔNG HỢP (ĐỊNH KỲ) ---
    public async Task CheckLowStockAndNotify()
    {
        // 1. Tìm TẤT CẢ sản phẩm sắp hết hàng
        var lowStockProducts = await _context.Products
            .Where(p => p.StockQuantity <= p.MinStockLevel)
            .ToListAsync();

        if (!lowStockProducts.Any()) return;

        // 2. Lấy danh sách người nhận (List<string>)
        var recipients = await GetRecipients();
        if (!recipients.Any()) return;

        // 3. Tạo nội dung Email
        var htmlContent = $@"
            <h3>📊 BÁO CÁO TỒN KHO ĐỊNH KỲ ({DateTime.Now:HH:mm dd/MM})</h3>
            <p>Dưới đây là danh sách các mặt hàng đang dưới định mức an toàn:</p>
            <table border='1' cellpadding='5' cellspacing='0' style='border-collapse: collapse; width: 100%;'>
                <tr style='background-color: #f2f2f2; text-align: left;'>
                    <th>Mã SKU</th><th>Tên Sản Phẩm</th><th>Tồn kho</th><th>Định mức</th>
                </tr>";

        foreach (var item in lowStockProducts)
        {
            htmlContent += $"<tr><td>{item.SKU}</td><td>{item.Name}</td><td style='color:red; font-weight:bold'>{item.StockQuantity}</td><td>{item.MinStockLevel}</td></tr>";
        }
        htmlContent += "</table><p>Vui lòng lên kế hoạch nhập hàng.</p>";

        // 4. Gửi mail (FIXED: Gọi 1 lần với danh sách người nhận)
        await _emailService.SendEmailAsync(
            recipients,                                              // 1. To Emails (List)
            null,                                                    // 2. CC
            null,                                                    // 3. BCC
            $"[WarehousePro] Báo cáo tồn kho {DateTime.Now:HH:mm}",  // 4. Subject
            htmlContent,                                             // 5. Body
            null                                                     // 6. Attachments
        );
    }

    // --- JOB 2: CẢNH BÁO TỨC THỜI (KHI VỪA XUẤT KHO) ---
    public async Task SendInstantAlert(Guid productId)
    {
        // 1. Lấy thông tin sản phẩm
        var product = await _context.Products.FindAsync(productId);
        if (product == null || product.StockQuantity > product.MinStockLevel) return;

        // 2. Lấy người nhận
        var recipients = await GetRecipients();
        if (!recipients.Any()) return;

        // 3. Tạo nội dung Email
        var htmlContent = $@"
            <h3 style='color: red;'>🚨 CẢNH BÁO KHẨN CẤP: SẢN PHẨM SẮP HẾT</h3>
            <p>Hệ thống vừa phát hiện giao dịch xuất kho đưa sản phẩm sau xuống dưới mức an toàn:</p>
            <ul>
                <li><strong>Sản phẩm:</strong> {product.Name} ({product.SKU})</li>
                <li><strong>Tồn kho hiện tại:</strong> <span style='color:red; font-size: 18px; font-weight: bold'>{product.StockQuantity}</span> {product.Unit}</li>
                <li><strong>Mức tối thiểu:</strong> {product.MinStockLevel}</li>
            </ul>
            <p>Vui lòng kiểm tra và nhập hàng ngay lập tức!</p>";

        // 4. Gửi mail (FIXED: Gọi 1 lần với danh sách người nhận)
        await _emailService.SendEmailAsync(
            recipients,                                          // 1. To Emails (List)
            null,                                                // 2. CC
            null,                                                // 3. BCC
            $"[URGENT] Cảnh báo hết hàng: {product.Name}",       // 4. Subject
            htmlContent,                                         // 5. Body
            null                                                 // 6. Attachments
        );
    }

    // Hàm phụ trợ lấy danh sách email
    private async Task<List<string>> GetRecipients()
    {
        return await _context.Users
            .Where(u => u.ReceiveStockAlert == true && !string.IsNullOrEmpty(u.Email))
            .Select(u => u.Email)
            .ToListAsync();
    }
}