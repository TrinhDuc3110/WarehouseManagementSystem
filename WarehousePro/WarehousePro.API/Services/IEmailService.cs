using Microsoft.AspNetCore.Http;

namespace WarehousePro.API.Services;

public interface IEmailService
{
    Task SendEmailAsync(
        List<string> toEmails,
        List<string>? ccEmails,
        List<string>? bccEmails,
        string subject,
        string htmlMessage,
        List<IFormFile>? attachments
    );
}