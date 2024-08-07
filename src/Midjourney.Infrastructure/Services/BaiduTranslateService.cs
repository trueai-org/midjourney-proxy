using Serilog;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Midjourney.Infrastructure.Services
{
    /// <summary>
    /// 百度翻译服务
    /// </summary>
    public class BaiduTranslateService : ITranslateService
    {
        private const string TRANSLATE_API = "https://fanyi-api.baidu.com/api/trans/vip/translate";
        public BaiduTranslateService()
        {
        }

        public string TranslateToEnglish(string prompt)
        {
            var appid = GlobalConfiguration.Setting?.BaiduTranslate?.Appid;
            var appSecret = GlobalConfiguration.Setting?.BaiduTranslate?.AppSecret;

            if (string.IsNullOrWhiteSpace(appid) || string.IsNullOrWhiteSpace(appSecret))
            {
                return prompt;
            }

            if (!ContainsChinese(prompt))
            {
                return prompt;
            }

            string salt = new Random().Next(10000, 99999).ToString();
            string sign = ComputeMd5Hash(appid + prompt + salt + appSecret);

            var body = new Dictionary<string, string>
            {
                { "from", "zh" },
                { "to", "en" },
                { "appid", appid },
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
                Log.Warning(e, "Failed to call Baidu Translate");
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
            string chinesePattern = @"[\u4e00-\u9fa5\u9fa6-\u9fef\U00020000-\U0002A6DF\U0002A700-\U0002B73F\U0002B740-\U0002B81F\U0002B820-\U0002CEAF\U0002F800-\U0002FA1F]";
            return Regex.IsMatch(prompt, chinesePattern);
        }
    }
}