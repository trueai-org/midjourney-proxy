// Midjourney Proxy - Proxy for Midjourney's Discord, enabling AI drawings via API with one-click face swap. A free, non-profit drawing API project.
// Copyright (C) 2024 trueai.org

// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.

// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

// Additional Terms:
// This software shall not be used for any illegal activities. 
// Users must comply with all applicable laws and regulations,
// particularly those related to image and video processing. 
// The use of this software for any form of illegal face swapping,
// invasion of privacy, or any other unlawful purposes is strictly prohibited. 
// Violation of these terms may result in termination of the license and may subject the violator to legal action.

using Serilog;
using System.Net;
using System.Text;
using System.Text.Json;
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
        private readonly HttpClient _httpClient;

        public GPTTranslateService()
        {
            var config = GlobalConfiguration.Setting?.Openai;

            _apiUrl = config?.GptApiUrl ?? TRANSLATE_API;
            _apiKey = config?.GptApiKey;
            _timeout = config?.Timeout ?? TimeSpan.FromSeconds(30);
            _model = config?.Model ?? "gpt-4o-mini";
            _maxTokens = config?.MaxTokens ?? 2048;
            _temperature = config?.Temperature ?? 0;

            WebProxy webProxy = null;
            var proxy = GlobalConfiguration.Setting.Proxy;
            if (!string.IsNullOrEmpty(proxy?.Host))
            {
                webProxy = new WebProxy(proxy.Host, proxy.Port ?? 80);
            }
            var hch = new HttpClientHandler
            {
                UseProxy = webProxy != null,
                Proxy = webProxy
            };
            _httpClient = new HttpClient(hch) { Timeout = _timeout };
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
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
                    new { role = "system", content = "把中文翻译成英文" },
                    new { role = "user", content = prompt }
                },
                max_tokens = _maxTokens,
                temperature = _temperature
            };

            try
            {
                var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
                var response = _httpClient.PostAsync(_apiUrl, content).Result;

                if (!response.IsSuccessStatusCode || string.IsNullOrWhiteSpace(response.Content.ReadAsStringAsync().Result))
                {
                    throw new InvalidOperationException($"{response.StatusCode} - {response.Content.ReadAsStringAsync().Result}");
                }

                var result = JsonDocument.Parse(response.Content.ReadAsStringAsync().Result);
                var choices = result.RootElement.GetProperty("choices").EnumerateArray();
                var translatedText = choices.First().GetProperty("message").GetProperty("content").GetString();

                return translatedText?.Trim() ?? prompt;
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
            // 匹配基本汉字区、扩展A区和部分扩展B区
            string chinesePattern = @"[\u4e00-\u9fa5\u3400-\u4DBF]";

            return Regex.IsMatch(prompt, chinesePattern);
        }
    }
}