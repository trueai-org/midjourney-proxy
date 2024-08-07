using Serilog;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Midjourney.Infrastructure.Services
{
    /// <summary>
    /// OpenAI GPT翻译服务
    /// </summary>
    public class GPTTranslateService : ITranslateService
    {
        private const string TRANSLATE_API = "https://api.openai.com/v1/chat/completions";
        private readonly string _apiUrl;
        private readonly string _apiKey;
        private readonly TimeSpan _timeout;
        private readonly string _model;
        private readonly int _maxTokens;
        private readonly double _temperature;

        public GPTTranslateService()
        {
            var config = GlobalConfiguration.Setting?.Openai;
            _apiUrl = config?.GptApiUrl ?? TRANSLATE_API;
            _apiKey = config?.GptApiKey;
            _timeout = config?.Timeout ?? TimeSpan.FromSeconds(30);
            _model = config?.Model ?? "gpt-4o-mini";
            _maxTokens = config?.MaxTokens ?? 2048;
            _temperature = config?.Temperature ?? 0;
        }

        public string TranslateToEnglish(string prompt)
        {
            if (string.IsNullOrWhiteSpace(_apiKey) || string.IsNullOrWhiteSpace(_apiUrl))
            {
                return prompt;
            }

            if (!ContainsChinese(prompt))
            {
                return prompt;
            }

            var requestBody = new
            {
                model = _model,
                messages = new[]
                {
                    new { role = "system", content = "You are a helpful assistant." },
                    new { role = "user", content = $"Translate the following Chinese text to English: {prompt}" }
                },
                max_tokens = _maxTokens,
                temperature = _temperature
            };

            try
            {
                using (var client = new HttpClient { Timeout = _timeout })
                {
                    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");

                    var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
                    var response = client.PostAsync(_apiUrl, content).Result;

                    if (!response.IsSuccessStatusCode || string.IsNullOrWhiteSpace(response.Content.ReadAsStringAsync().Result))
                    {
                        throw new InvalidOperationException($"{response.StatusCode} - {response.Content.ReadAsStringAsync().Result}");
                    }

                    var result = JsonDocument.Parse(response.Content.ReadAsStringAsync().Result);
                    var choices = result.RootElement.GetProperty("choices").EnumerateArray();
                    var translatedText = choices.First().GetProperty("message").GetProperty("content").GetString();

                    return translatedText?.Trim() ?? prompt;
                }
            }
            catch (HttpRequestException e)
            {
                Log.Warning(e, "HTTP request failed");
            }
            catch (JsonException e)
            {
                Log.Warning(e, "Failed to parse JSON response");
            }
            catch (Exception e)
            {
                Log.Warning(e, "Failed to call OpenAI Translate");
            }

            return prompt;
        }

        public bool ContainsChinese(string prompt)
        {
            string chinesePattern = @"[\u4e00-\u9fa5\u9fa6-\u9fef\U00020000-\U0002A6DF\U0002A700-\U0002B73F\U0002B740-\U0002B81F\U0002B820-\U0002CEAF\U0002F800-\U0002FA1F]";
            return Regex.IsMatch(prompt, chinesePattern);
        }
    }
}