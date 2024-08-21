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
            // 匹配基本汉字区、扩展A区和部分扩展B区
            string chinesePattern = @"[\u4e00-\u9fa5\u3400-\u4DBF]";

            return Regex.IsMatch(prompt, chinesePattern);
        }
    }
}