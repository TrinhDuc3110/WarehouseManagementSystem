using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.RegularExpressions;
using WarehousePro.Infrastructure.Persistence;
using Microsoft.AspNetCore.SignalR;
using WarehousePro.Infrastructure.Hubs;
using Microsoft.Extensions.Configuration;

namespace WarehousePro.Infrastructure.Services
{
    public class AIService
    {
        private readonly Kernel _kernel;
        private readonly IChatCompletionService _chat;

        public AIService(ApplicationDbContext dbContext, IHubContext<ChatHub> chatHub, IConfiguration configuration)
        {
            try
            {
                // 1. Lấy thông tin từ appsettings.json
                var aiSection = configuration.GetSection("AIConfig");
                
                string baseUrl = aiSection["Endpoint"];
                
                // QUAN TRỌNG: Semantic Kernel dùng chuẩn OpenAI, nên phải trỏ vào /v1
                // Nếu baseUrl chưa có /v1, ta tự cộng thêm vào.
                string openAiEndpoint = baseUrl.TrimEnd('/') + "/v1";

                string modelId = "llama3.1"; // Tên model trong Ollama
                string apiKey = "ollama";    // Ollama không cần key, điền gì cũng được

                var builder = Kernel.CreateBuilder();

                // 2. Configure HttpClient
                // QUAN TRỌNG: Phải thêm Header để vượt qua Ngrok
                var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromMinutes(10);
                httpClient.DefaultRequestHeaders.Add("ngrok-skip-browser-warning", "true");

                // 3. Cấu hình Semantic Kernel dùng OpenAI Connector để gọi Ollama
                builder.AddOpenAIChatCompletion(
                    modelId: modelId,
                    apiKey: apiKey,
                    endpoint: new Uri(openAiEndpoint), // Trỏ về .../v1
                    httpClient: httpClient             // Inject Client đã có Header Ngrok
                );

                // 4. Register Plugins
                builder.Plugins.AddFromObject(new WarehousePlugin(dbContext), "Warehouse");
                builder.Plugins.AddFromObject(new AdminPlugin(dbContext, chatHub), "AdminActions");

                // 5. Build Kernel
                _kernel = builder.Build();
                _chat = _kernel.GetRequiredService<IChatCompletionService>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CRITICAL ERROR IN CONSTRUCTOR]: {ex.Message}");
                throw;
            }
        }

        public async Task<AIResponse> ProcessChatAsync(string userMessage, string userId, string groupId)
        {
            var history = new ChatHistory();
            string today = DateTime.Now.ToString("dd/MM/yyyy HH:mm");

            // Prompt giữ nguyên
            var prompt = $@"
You are a Warehouse Assistant. You communicate in English. Today is {today}.

CRITICAL RULES:
1. Response MUST be valid JSON only. NO Markdown.
2. If the Tool returns a list (Array), you MUST use 'rich_table'.
3. If the Tool returns a text message (String), use 'text'.

--- RESPONSE FORMATS ---
1. DATA LIST (Use when Tool returns JSON Array [...] ): 
   {{ ""type"": ""rich_table"", ""text"": ""Here is the product list:"", ""data"": <THE_JSON_ARRAY_FROM_TOOL> }}

2. SINGLE NUMBER/SUMMARY: 
   {{ ""type"": ""rich_stats"", ""text"": ""Overview:"", ""data"": {{ ""title"": ""..."", ""value"": ""..."" }} }}

3. TEXT MESSAGE: 
   {{ ""type"": ""text"", ""message"": ""..."" }}
";

            history.AddSystemMessage(prompt);
            history.AddUserMessage(userMessage);

            // Giảm nhiệt độ để trả về JSON chuẩn xác hơn
            OpenAIPromptExecutionSettings settings = new()
            {
                ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
                Temperature = 0.1, 
                TopP = 0.9
            };

            try
            {
                var result = await _chat.GetChatMessageContentAsync(history, settings, _kernel);
                return CleanAndParseAIOutput(result.Content);
            }
            catch (Exception ex)
            {
                return new AIResponse { type = "text", message = "System is busy: " + ex.Message };
            }
        }

        private AIResponse CleanAndParseAIOutput(string rawContent)
        {
            if (string.IsNullOrWhiteSpace(rawContent))
                return new AIResponse { type = "text", message = "" };

            try
            {
                string cleanJson = rawContent;
                // Remove Markdown code blocks if AI adds them
                if (cleanJson.Contains("```"))
                {
                    cleanJson = Regex.Replace(cleanJson, @"```json|```", "", RegexOptions.IgnoreCase).Trim();
                }

                // Extract JSON substring
                int firstBrace = cleanJson.IndexOf('{');
                int lastBrace = cleanJson.LastIndexOf('}');

                if (firstBrace >= 0 && lastBrace > firstBrace)
                {
                    cleanJson = cleanJson.Substring(firstBrace, lastBrace - firstBrace + 1);
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        ReadCommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    };

                    var aiResponse = JsonSerializer.Deserialize<AIResponse>(cleanJson, options);
                    if (aiResponse != null)
                    {
                        if (!string.IsNullOrEmpty(aiResponse.type))
                            aiResponse.type = aiResponse.type.ToLower();
                        return aiResponse;
                    }
                }
            }
            catch { }

            // Fallback: return raw content as text
            return new AIResponse { type = "text", message = rawContent, text = rawContent };
        }
    }

    public class AIResponse
    {
        public string message { get; set; }
        public string text { get; set; }
        public string type { get; set; }
        public object data { get; set; }
        public string path { get; set; }
    }
}