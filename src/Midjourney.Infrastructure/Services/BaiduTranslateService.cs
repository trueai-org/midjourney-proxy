using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Midjourney.Infrastructure.Services
{
    public class BaiduTranslateService : ITranslateService
    {
        private const string TRANSLATE_API = "https://fanyi-api.baidu.com/api/trans/vip/translate";
        private readonly string appid;
        private readonly string appSecret;
        private readonly ILogger<BaiduTranslateService> logger;

        public BaiduTranslateService(IOptions<ProxyProperties> translateConfig, ILogger<BaiduTranslateService> logger)
        {
            this.appid = translateConfig.Value?.BaiduTranslate?.Appid;
            this.appSecret = translateConfig.Value?.BaiduTranslate?.AppSecret;
            this.logger = logger;
        }

        public string TranslateToEnglish(string prompt)
        {
            if (string.IsNullOrWhiteSpace(this.appid) || string.IsNullOrWhiteSpace(this.appSecret))
            {
                return prompt;
            }

            if (!ContainsChinese(prompt))
            {
                return prompt;
            }

            string salt = new Random().Next(10000, 99999).ToString();
            string sign = ComputeMd5Hash(this.appid + prompt + salt + this.appSecret);

            var body = new Dictionary<string, string>
            {
                { "from", "zh" },
                { "to", "en" },
                { "appid", this.appid },
                { "salt", salt },
                { "q", prompt },
                { "sign", sign }
            };

            try
            {
                using (var client = new HttpClient())
                {
                    var content = new FormUrlEncodedContent(body);
                    var response = client.PostAsync(TRANSLATE_API, content).Result;

                    if (!response.IsSuccessStatusCode || string.IsNullOrWhiteSpace(response.Content.ReadAsStringAsync().Result))
                    {
                        throw new InvalidOperationException($"{response.StatusCode} - {response.Content.ReadAsStringAsync().Result}");
                    }

                    var result = JsonDocument.Parse(response.Content.ReadAsStringAsync().Result);
                    if (result.RootElement.TryGetProperty("error_code", out var errorCode))
                    {
                        throw new InvalidOperationException($"{errorCode.GetString()} - {result.RootElement.GetProperty("error_msg").GetString()}");
                    }

                    var transResult = result.RootElement.GetProperty("trans_result").EnumerateArray();
                    var translatedStrings = new List<string>();
                    foreach (var item in transResult)
                    {
                        translatedStrings.Add(item.GetProperty("dst").GetString());
                    }

                    return string.Join("\n", translatedStrings);
                }
            }
            catch (Exception e)
            {
                logger.LogWarning("Failed to call Baidu Translate: {0}", e.Message);
            }

            return prompt;
        }

        private static string ComputeMd5Hash(string input)
        {
            using (var md5 = MD5.Create())
            {
                var inputBytes = Encoding.UTF8.GetBytes(input);
                var hashBytes = md5.ComputeHash(inputBytes);
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            }
        }

        public bool ContainsChinese(string prompt)
        {
            return Regex.IsMatch(prompt, @"[\u4e00-\u9fa5]");
        }
    }
}