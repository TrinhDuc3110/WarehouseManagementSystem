using Hangfire;
using Hangfire.SqlServer;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OfficeOpenXml;
using Scalar.AspNetCore;
using System.Text;
using WarehousePro.API.Hubs;
using WarehousePro.API.Services;
using WarehousePro.Application.Common.Interfaces;
using WarehousePro.Infrastructure.Persistence;
using WarehousePro.Infrastructure.Services;

namespace WarehousePro.API
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // ====================================================
            // 1. CẤU HÌNH LICENSE EXCEL
            // ====================================================
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            // ====================================================
            // 2. DATABASE & CONNECTION STRING
            // ====================================================
            var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

            if (string.IsNullOrEmpty(connectionString))
            {
                // Fallback nếu không đọc được config (đề phòng lỗi deploy)
                throw new InvalidOperationException("Không tìm thấy chuỗi kết nối 'DefaultConnection'. Vui lòng kiểm tra appsettings.json!");
            }

            builder.Services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlServer(connectionString, sqlOptions =>
                {
                    sqlOptions.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null); // Tự động thử lại nếu rớt mạng
                }));

            // ====================================================
            // 3. AUTHENTICATION (JWT)
            // ====================================================
            var jwtKey = builder.Configuration["Jwt:Key"];
            // Tạo key mặc định nếu config thiếu (để tránh crash app, nhưng nên config trong appsettings)
            if (string.IsNullOrEmpty(jwtKey))
            {
                jwtKey = "DayLaMotCaiKeyRatDaiVaBiMatKhongDuocTietLoChoAiHet123!!!";
            }
            var key = Encoding.ASCII.GetBytes(jwtKey);

            builder.Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.RequireHttpsMetadata = false; // Chấp nhận HTTP cho môi trường test/plesk chưa có SSL
                options.SaveToken = true;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "WarehousePro_Server",
                    ValidAudience = builder.Configuration["Jwt:Audience"] ?? "WarehousePro_Client"
                };
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        // Lấy token từ Query String cho SignalR
                        var accessToken = context.Request.Query["access_token"];
                        var path = context.HttpContext.Request.Path;
                        if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                        {
                            context.Token = accessToken;
                        }
                        return Task.CompletedTask;
                    }
                };
            });

            // ====================================================
            // 4. CORS (CẤU HÌNH CHO REACT/NGROK/PLESK)
            // ====================================================
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowAll", policy =>
                {
                    policy.SetIsOriginAllowed(origin => true) // Chấp nhận mọi domain (Frontend, Localhost, Ngrok)
                          .AllowAnyMethod()
                          .AllowAnyHeader()
                          .AllowCredentials();
                });
            });

            // ====================================================
            // 5. SERVICES ĐĂNG KÝ
            // ====================================================
            builder.Services.AddControllers();
            builder.Services.AddOpenApi(); // Cho Swagger/Scalar
            builder.Services.AddSignalR();
            builder.Services.AddMemoryCache();

            // Inject các Service
            builder.Services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());
            builder.Services.AddScoped<IEmailService, EmailService>();
            builder.Services.AddScoped<InventoryJob>();
            builder.Services.AddScoped<IPasswordHasher, PasswordHasher>();
            builder.Services.AddScoped<AiPredictionService>();
            builder.Services.AddScoped<AIService>();

            // ====================================================
            // 6. HANGFIRE (BACKGROUND JOB)
            // ====================================================
            // Lưu ý: Phải cài package Microsoft.Data.SqlClient để dòng này hoạt động
            builder.Services.AddHangfire(config => config
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UseSqlServerStorage(connectionString, new SqlServerStorageOptions
                {
                    CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
                    SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
                    QueuePollInterval = TimeSpan.Zero, // Polling liên tục để job chạy ngay lập tức
                    UseRecommendedIsolationLevel = true,
                    DisableGlobalLocks = true,
                    PrepareSchemaIfNecessary = true // Tự động tạo bảng Hangfire nếu chưa có
                }));

            builder.Services.AddHangfireServer();

            // ====================================================
            // 7. HEALTH CHECKS
            // ====================================================
            builder.Services.AddHealthChecks()
                .AddSqlServer(
                    connectionString: connectionString,
                    name: "SQL Server Database",
                    failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy);

            builder.Services.AddHealthChecksUI(setup =>
            {
                setup.SetEvaluationTimeInSeconds(10);
                setup.MaximumHistoryEntriesPerEndpoint(60);
                setup.AddHealthCheckEndpoint("Backend API", "/health");
            }).AddInMemoryStorage();

            var app = builder.Build();

            // ====================================================
            // 8. PIPELINE (REQUEST HANDLING)
            // ====================================================

            // Xử lý lỗi toàn cục
            app.UseMiddleware<WarehousePro.API.Middlewares.ExceptionMiddleware>();

            // --- QUAN TRỌNG: Mở Scalar/Swagger ở cả Production (Plesk) ---
            app.MapOpenApi();
            app.MapScalarApiReference(options =>
            {
                options.Title = "WarehousePro API";
                options.Theme = ScalarTheme.Mars;
            });
            // -------------------------------------------------------------

            // File tĩnh (cho ảnh, html...)
            app.UseStaticFiles();

            // CORS phải đứng trước Auth
            app.UseCors("AllowAll");

            app.UseAuthentication();
            app.UseAuthorization();

            // Dashboard Hangfire
            app.UseHangfireDashboard("/hangfire");

            // Đăng ký Job định kỳ
            TimeZoneInfo vnTimeZone;
            try { vnTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time"); }
            catch { vnTimeZone = TimeZoneInfo.Local; }

            // Job sáng
            RecurringJob.AddOrUpdate<InventoryJob>(
                "stock-check-morning",
                job => job.CheckLowStockAndNotify(),
                "0 8 * * *",
                new RecurringJobOptions { TimeZone = vnTimeZone }
            );

            // Job chiều
            RecurringJob.AddOrUpdate<InventoryJob>(
                "stock-check-afternoon",
                job => job.CheckLowStockAndNotify(),
                "0 14 * * *",
                new RecurringJobOptions { TimeZone = vnTimeZone }
            );

            // Endpoint Health Check (trả về JSON)
            app.MapHealthChecks("/health", new HealthCheckOptions
            {
                Predicate = _ => true,
                ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
            });

            // Giao diện Health Check đẹp
            app.MapHealthChecksUI(config =>
            {
                config.UIPath = "/health-ui";
                config.PageTitle = "SYSTEM HEALTH MONITOR";
            });

            // SignalR & Controllers
            app.MapHub<InventoryHub>("/hubs/inventory");
            app.MapHub<ChatHub>("/hubs/chat");
            app.MapControllers();

            app.Run();
        }
    }
}