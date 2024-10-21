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
using Midjourney.Infrastructure.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PuppeteerSharp;
using RestSharp;
using Serilog;

namespace Midjourney.Captcha.API
{
    /// <summary>
    /// CF 验证器
    /// </summary>
    public class CloudflareHelper
    {
        // 定义支持异步锁
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        /// <summary>
        /// 下载浏览器
        /// </summary>
        /// <returns></returns>
        public static async Task DownloadBrowser()
        {
            try
            {
                await _semaphore.WaitAsync();

                // 下载并设置浏览器
                await new BrowserFetcher().DownloadAsync();
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// 验证 URL 模拟人机验证
        /// </summary>
        /// <param name="captchaOption"></param>
        /// <param name="hash"></param>
        /// <param name="url"></param>
        /// <returns></returns>
        public static async Task<bool> Validate(CaptchaOption captchaOption, string hash, string url)
        {
            IBrowser browser = null;
            IPage page = null;

            try
            {
                await DownloadBrowser();

                // 启动浏览器
                browser = await Puppeteer.LaunchAsync(new LaunchOptions
                {
                    Headless = captchaOption.Headless // 设置无头模式
                });

                //// 创建无痕浏览器上下文
                //var context = await browser.CreateBrowserContextAsync();

                //// 创建一个新的页面
                //page = await context.NewPageAsync();

                page = await browser.NewPageAsync();

                // 设置用户代理和添加初始化脚本以移除设备指纹信息
                await page.SetUserAgentAsync("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
                await page.EvaluateExpressionOnNewDocumentAsync(@"() => {
                    Object.defineProperty(navigator, 'webdriver', { get: () => undefined });
                    Object.defineProperty(navigator, 'plugins', { get: () => [1, 2, 3] });
                    Object.defineProperty(navigator, 'languages', { get: () => ['en-US', 'en'] });
                    Object.defineProperty(navigator, 'platform', { get: () => 'Win32' });
                    Object.defineProperty(navigator, 'userAgent', { get: () => 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36' });
                }");

                // 收集所有请求的URL
                var urls = new List<string>();
                await page.SetRequestInterceptionAsync(true);
                page.Request += (sender, e) =>
                {
                    urls.Add(e.Request.Url);
                    e.Request.ContinueAsync();
                };

                // 等待
                await Task.Delay(500);

                // 打开 url
                await page.GoToAsync(url);

                // 等待
                await Task.Delay(6000);

                // 日志
                Log.Information("CF 开始验证 URL: {@0}", url);

                var siteKeyCount = 0;
                var siteKey = string.Empty;
                do
                {
                    if (siteKeyCount > 20)
                    {
                        // 超时没有获取到 sitekey
                        return false;
                    }

                    // 获取 Cloudflare 验证页面的 src
                    var src = urls.FirstOrDefault(c => c.StartsWith("https://challenges.cloudflare.com/cdn-cgi/challenge-platform"));
                    siteKey = src?.Split("/").Where(c => c.StartsWith("0x") && c.Length > 20).FirstOrDefault();

                    if (!string.IsNullOrWhiteSpace(siteKey))
                    {
                        break;
                    }

                    siteKeyCount++;
                    await Task.Delay(1000);
                } while (true);

                // 日志
                Log.Information("CF 验证 SiteKey: {@0}", siteKey);
                if (string.IsNullOrWhiteSpace(siteKey))
                {
                    return false;
                }

                var token = string.Empty;
                var tokenCount = 0;

                if (!string.IsNullOrWhiteSpace(captchaOption.TwoCaptchaKey))
                {
                    var taskId = string.Empty;
                    var taskCount = 0;
                    do
                    {
                        if (taskCount > 20)
                        {
                            // 超时没有获取到 taskId
                            return false;
                        }

                        // 使用 RestSharp 调用 2Captcha API 解决验证码
                        var client = new RestClient();
                        var request = new RestRequest("https://api.2captcha.com/createTask", Method.Post);
                        request.AddHeader("Content-Type", "application/json");
                        var body = new
                        {
                            clientKey = captchaOption.TwoCaptchaKey,
                            task = new
                            {
                                type = "TurnstileTaskProxyless",
                                websiteURL = url,
                                websiteKey = siteKey
                            }
                        };

                        var json = JsonConvert.SerializeObject(body);
                        request.AddStringBody(json, DataFormat.Json);
                        var response = await client.ExecuteAsync(request);
                        if (response.StatusCode == System.Net.HttpStatusCode.OK)
                        {
                            var obj = JsonConvert.DeserializeObject<JObject>(response.Content);
                            if (obj.ContainsKey("taskId"))
                            {
                                taskId = obj["taskId"].ToString();

                                if (!string.IsNullOrWhiteSpace(taskId))
                                {
                                    break;
                                }
                            }
                        }

                        taskCount++;
                        await Task.Delay(1000);
                    } while (true);

                    // 日志
                    Log.Information("CF 验证 TaskId: {@0}", taskId);
                    if (string.IsNullOrWhiteSpace(taskId))
                    {
                        return false;
                    }

                    // 等待
                    await Task.Delay(6000);

                    do
                    {
                        if (tokenCount > 60)
                        {
                            // 超时没有获取到 token
                            return false;
                        }

                        var client = new RestClient();
                        var request = new RestRequest("https://api.2captcha.com/getTaskResult", Method.Post);
                        request.AddHeader("Content-Type", "application/json");
                        var body = new
                        {
                            clientKey = captchaOption.TwoCaptchaKey,
                            taskId = taskId
                        };
                        var json = JsonConvert.SerializeObject(body);
                        request.AddStringBody(json, DataFormat.Json);
                        var response = await client.ExecuteAsync(request);
                        if (response.StatusCode == System.Net.HttpStatusCode.OK)
                        {
                            var obj = JsonConvert.DeserializeObject<JObject>(response.Content);
                            if (obj.ContainsKey("solution"))
                            {
                                token = obj["solution"]["token"].ToString();
                                if (!string.IsNullOrWhiteSpace(token))
                                {
                                    break;
                                }
                            }
                        }

                        tokenCount++;
                        await Task.Delay(1000);
                    } while (true);
                }
                else if (!string.IsNullOrWhiteSpace(captchaOption.YesCaptchaKey))
                {
                    // yescaptcha
                    var taskId = string.Empty;
                    var taskCount = 0;
                    do
                    {
                        if (taskCount > 20)
                        {
                            // 超时没有获取到 taskId
                            return false;
                        }

                        // 使用 RestSharp 调用 yescaptcha 解决验证码
                        var client = new RestClient();
                        var request = new RestRequest("https://api.yescaptcha.com/createTask", Method.Post);
                        request.AddHeader("Content-Type", "application/json");
                        var body = new
                        {
                            clientKey = captchaOption.YesCaptchaKey,
                            task = new
                            {
                                type = "TurnstileTaskProxyless",
                                websiteURL = url,
                                websiteKey = siteKey
                            }
                        };

                        var json = JsonConvert.SerializeObject(body);
                        request.AddStringBody(json, DataFormat.Json);
                        var response = await client.ExecuteAsync(request);
                        if (response.StatusCode == System.Net.HttpStatusCode.OK)
                        {
                            var obj = JsonConvert.DeserializeObject<JObject>(response.Content);
                            if (obj.ContainsKey("taskId"))
                            {
                                taskId = obj["taskId"].ToString();

                                if (!string.IsNullOrWhiteSpace(taskId))
                                {
                                    break;
                                }
                            }
                        }

                        taskCount++;
                        await Task.Delay(1000);
                    } while (true);

                    // 日志
                    Log.Information("CF 验证 TaskId: {@0}", taskId);
                    if (string.IsNullOrWhiteSpace(taskId))
                    {
                        return false;
                    }

                    // 等待
                    await Task.Delay(6000);

                    do
                    {
                        if (tokenCount > 60)
                        {
                            // 超时没有获取到 token
                            return false;
                        }

                        var client = new RestClient();
                        var request = new RestRequest("https://api.yescaptcha.com/getTaskResult", Method.Post);
                        request.AddHeader("Content-Type", "application/json");
                        var body = new
                        {
                            clientKey = captchaOption.YesCaptchaKey,
                            taskId = taskId
                        };
                        var json = JsonConvert.SerializeObject(body);
                        request.AddStringBody(json, DataFormat.Json);
                        var response = await client.ExecuteAsync(request);
                        if (response.StatusCode == System.Net.HttpStatusCode.OK)
                        {
                            var obj = JsonConvert.DeserializeObject<JObject>(response.Content);
                            if (obj.ContainsKey("solution"))
                            {
                                token = obj["solution"]["token"].ToString();
                                if (!string.IsNullOrWhiteSpace(token))
                                {
                                    break;
                                }
                            }
                        }

                        tokenCount++;
                        await Task.Delay(1000);
                    } while (true);
                }
                else
                {
                    return false;
                }


                // 日志
                Log.Information("CF 验证 Token: {@0}", token);
                if (string.IsNullOrWhiteSpace(token))
                {
                    return false;
                }

                var submitUrl = $"https://editor.midjourney.com/captcha/api/c/{hash}/submit";
                // https://editor.midjourney.com/captcha/api/c/fopSUuR6brCzgJuFJVmV6Tzn5QaW4Z6yCTbktDzNoZai-3iIlWTsG3E8lywCV4xpxsjS50qVcFDlK6sb/submit

                try
                {
                    // 只提交一次
                    // 提交到 mj 服务器
                    //var retry = 0;
                    //do
                    //{
                    //    if (retry > 3)
                    //    {
                    //        break;
                    //    }
                    //    retry++;
                    //    Log.Information("CF 验证提交 第 {@1} 次, {@0}", retry, submitUrl);
                    //} while (true);

                    var options = new RestClientOptions()
                    {
                        Timeout = TimeSpan.FromMinutes(5),
                        UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/127.0.0.0 Safari/537.36",
                    };
                    var client = new RestClient(options);
                    var request = new RestRequest(submitUrl, Method.Post);

                    request.AlwaysMultipartFormData = true;
                    request.AddParameter("captcha_token", token);

                    var response = await client.ExecuteAsync(request);

                    // 如果是 200 或 404 则认为提交成功
                    if (response.StatusCode == System.Net.HttpStatusCode.OK || response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        Log.Information("CF 验证提交成功, {@0}, {@1}, {@2}", submitUrl, response.StatusCode, response.Content);
                        return true;
                    }
                    else
                    {
                        // 记录错误
                        Log.Error("CF 验证提交失败 {@0}, {@1}", submitUrl, response.Content);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "CF 验证提交异常 {@0}", url);
                }

                //// 使用 httpclient 提交
                //try
                //{
                //    var maxRetries = 3;
                //    int retry = 0;
                //    do
                //    {
                //        try
                //        {
                //            Log.Information("第 {@0} 次提交 CF 验证, URL: {@1}", retry + 1, submitUrl);

                //            var requestContent = new MultipartFormDataContent
                //            {
                //                { new StringContent(token), "captcha_token" }
                //            };

                //            var request = new HttpRequestMessage(HttpMethod.Post, submitUrl)
                //            {
                //                Content = requestContent
                //            };

                //            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/127.0.0.0 Safari/537.36");

                //            var client = new HttpClient();
                //            var response = await client.SendAsync(request);
                //            if (response.IsSuccessStatusCode)
                //            {
                //                Log.Information("CF 验证提交成功, 方法1, {@0}", submitUrl);

                //                return true;
                //            }
                //            else
                //            {
                //                var content = await response.Content.ReadAsStringAsync();
                //                Log.Error("CF 验证提交失败, URL: {SubmitUrl}, 响应内容: {ResponseContent}", submitUrl, content);
                //            }
                //        }
                //        catch (Exception ex)
                //        {
                //            Log.Error(ex, "CF 验证提交异常, URL: {SubmitUrl}", submitUrl);
                //        }

                //        retry++;
                //    } while (retry < maxRetries);
                //}
                //catch (Exception ex)
                //{
                //    Log.Error(ex, "CF 验证提交异常 {@0}", url);
                //}

                //// 使用 httpclient 提交
                //try
                //{
                //    var maxRetries = 3;
                //    int retry = 0;
                //    do
                //    {
                //        try
                //        {
                //            Log.Information("第 {@0} 次提交 CF 验证, URL: {@1}", retry + 1, submitUrl);

                //            using (var client = new HttpClient())
                //            {
                //                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/127.0.0.0 Safari/537.36");

                //                // 创建 MultipartFormDataContent 对象
                //                var form = new MultipartFormDataContent
                //                {
                //                    // 添加表单字段
                //                    { new StringContent(token), "captcha_token" },
                //                };

                //                // 发送 POST 请求
                //                var response = await client.PostAsync(submitUrl, form);

                //                // 处理响应
                //                if (response.IsSuccessStatusCode)
                //                {
                //                    Log.Information("CF 验证提交成功, 方法2, {@0}", submitUrl);

                //                    return true;
                //                }
                //            }
                //        }
                //        catch (Exception ex)
                //        {
                //            Log.Error(ex, "CF 验证提交异常, URL: {SubmitUrl}", submitUrl);
                //        }

                //        retry++;
                //    } while (retry < maxRetries);
                //}
                //catch (Exception ex)
                //{
                //    Log.Error(ex, "CF 验证提交异常 {@0}", url);
                //}

                await Task.Delay(1000);

                // 日志
                Log.Information("CF 验证失败 {@0}", url);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "CF 验证 URL 异常 {@0}", url);
            }
            finally
            {
                // 关闭浏览器
                await page?.CloseAsync();
                await browser?.CloseAsync();
            }

            return false;
        }
    }
}