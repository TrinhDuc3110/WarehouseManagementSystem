using MailKit.Net.Smtp;
using MimeKit;
using Microsoft.AspNetCore.Http;
using MailKit.Security; // Cần thiết cho SecureSocketOptions

namespace WarehousePro.API.Services;

public class EmailService : IEmailService
{
    private readonly IConfiguration _config;

    public EmailService(IConfiguration config)
    {
        _config = config;
    }

    public async Task SendEmailAsync(
        List<string> toEmails, 
        List<string>? ccEmails, 
        List<string>? bccEmails, 
        string subject, 
        string htmlMessage, 
        List<IFormFile>? attachments)
    {
        var email = new MimeMessage();

        // 1. Sender (Người gửi)
        email.From.Add(new MailboxAddress(
            _config["EmailSettings:SenderName"], 
            _config["EmailSettings:SenderEmail"]
        ));

        // 2. To (Người nhận chính - Bắt buộc)
        // Vì Controller gửi xuống List<string>, ta cần loop
        foreach (var to in toEmails)
        {
            email.To.Add(MailboxAddress.Parse(to));
        }

        // 3. CC (Carbon Copy - Nếu có)
        if (ccEmails != null && ccEmails.Any())
        {
            foreach (var cc in ccEmails)
            {
                email.Cc.Add(MailboxAddress.Parse(cc));
            }
        }

        // 4. BCC (Blind Carbon Copy - Nếu có)
        if (bccEmails != null && bccEmails.Any())
        {
            foreach (var bcc in bccEmails)
            {
                email.Bcc.Add(MailboxAddress.Parse(bcc));
            }
        }

        // 5. Subject
        email.Subject = subject;

        // 6. Body Builder
        var builder = new BodyBuilder { HtmlBody = htmlMessage };

        // 7. 🔥 XỬ LÝ FILE ĐÍNH KÈM (Giữ nguyên logic an toàn của bạn)
        if (attachments != null && attachments.Count > 0)
        {
            foreach (var file in attachments)
            {
                if (file.Length > 0)
                {
                    using var ms = new MemoryStream();
                    await file.CopyToAsync(ms);
                    var fileBytes = ms.ToArray();

                    // Xử lý ContentType an toàn
                    ContentType contentType;
                    try
                    {
                        contentType = ContentType.Parse(file.ContentType);
                    }
                    catch
                    {
                        contentType = ContentType.Parse("application/octet-stream");
                    }

                    builder.Attachments.Add(file.FileName, fileBytes, contentType);
                }
            }
        }

        email.Body = builder.ToMessageBody();

        // 8. Kết nối SMTP và Gửi
        using var smtp = new SmtpClient();
        try 
        {
            await smtp.ConnectAsync(
                _config["EmailSettings:SmtpServer"],
                int.Parse(_config["EmailSettings:Port"]),
                SecureSocketOptions.StartTls
            );

            await smtp.AuthenticateAsync(
                _config["EmailSettings:SenderEmail"],
                _config["EmailSettings:Password"]
            );

            await smtp.SendAsync(email);
        }
        catch (Exception ex)
        {
            // Tùy chọn: Log lỗi ra console hoặc file log để debug
            Console.WriteLine($"Lỗi gửi mail: {ex.Message}");
            throw; // Ném lỗi ra để Controller bắt được
        }
        finally
        {
            await smtp.DisconnectAsync(true);
        }
    }
}