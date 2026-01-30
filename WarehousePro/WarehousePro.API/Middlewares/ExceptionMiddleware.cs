using System.Net;
using System.Text.Json;
using WarehousePro.Domain.Exceptions;

namespace WarehousePro.API.Middlewares;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;
    private readonly IHostEnvironment _env;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger, IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, ex.Message);
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        var statusCode = (int)HttpStatusCode.InternalServerError;
        var message = "Lỗi hệ thống! Vui lòng liên hệ Admin.";
        var details = exception.StackTrace?.ToString();

        // Xử lý các loại lỗi cụ thể
        switch (exception)
        {
            case ApiException apiEx: // Lỗi nghiệp vụ (do mình ném ra)
                statusCode = (int)HttpStatusCode.BadRequest;
                message = apiEx.Message;
                details = null; // Không cần stack trace cho lỗi nghiệp vụ
                break;
            case KeyNotFoundException: // Lỗi không tìm thấy
                statusCode = (int)HttpStatusCode.NotFound;
                message = "Không tìm thấy dữ liệu yêu cầu.";
                details = null;
                break;
            case UnauthorizedAccessException: // Lỗi quyền
                statusCode = (int)HttpStatusCode.Unauthorized;
                message = "Bạn không có quyền thực hiện thao tác này.";
                details = null;
                break;
                // Thêm các case khác nếu cần
        }

        context.Response.StatusCode = statusCode;

        var response = new
        {
            StatusCode = statusCode,
            Message = message,
            // Chỉ hiện chi tiết lỗi (Stack Trace) khi ở môi trường Dev để debug
            Details = _env.IsDevelopment() ? details : null
        };

        var json = JsonSerializer.Serialize(response);
        await context.Response.WriteAsync(json);
    }
}