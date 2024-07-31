using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Midjourney.Infrastructure.Dto;
using Midjourney.Infrastructure.Options;
using Midjourney.Infrastructure.Util;
using RestSharp;
using Serilog;
using System.Text.Json;

namespace Midjourney.Captcha.API.Controllers
{
    /// <summary>
    /// Cloudflare 自动验证控制器。
    /// </summary>
    [Route("cf")]
    [ApiController]
    public class CloudflareController : ControllerBase
    {
        private readonly CaptchaOption _captchaOption;

        public CloudflareController(IOptionsMonitor<CaptchaOption> optionsMonitor)
        {
            _captchaOption = optionsMonitor.CurrentValue;
        }

        /// <summary>
        /// 校验 Cloudflare URL
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [HttpPost("verify")]
        public ActionResult Validate([FromBody] CaptchaVerfyRequest request)
        {
            _ = Task.Run(async () =>
            {
                await DoWork(request);
            });
            return Ok();
        }

        /// <summary>
        /// 开始执行作业
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        /// <exception cref="LogicException"></exception>
        private async Task DoWork(CaptchaVerfyRequest request)
        {
            LocalLock.TryLock(request.Url, TimeSpan.FromSeconds(10), async () =>
            {
                // 最终结果
                var finSuccess = false;
                try
                {
                    // https://936929561302675456.discordsays.com/captcha/api/c/hIlZOI0ZQI3qQjpXhzS4GTgw_DuRTjYiyyww38dJuTzmqA8pa3OC60yTJbTmK6jd3i6Q0wZNxiuEp2dW/ack?hash=1
                    var hashUrl = request.Url;
                    var hash = hashUrl?.Split('/').Where(c => !c.Contains("?")).OrderByDescending(c => c.Length).FirstOrDefault();
                    if (string.IsNullOrWhiteSpace(hash))
                    {
                        return;
                    }

                    var retry = 0;
                    do
                    {
                        // 最多 5 次重试
                        if (finSuccess || retry > 5)
                        {
                            break;
                        }

                        try
                        {
                            var httpClient = new HttpClient()
                            {
                                Timeout = Timeout.InfiniteTimeSpan
                            };
                            var response = await httpClient.GetAsync(hashUrl);
                            var con = await response.Content.ReadAsStringAsync();
                            if (!string.IsNullOrWhiteSpace(con))
                            {
                                // 解析
                                var json = JsonSerializer.Deserialize<JsonElement>(con);
                                if (json.TryGetProperty("hash", out var h) && json.TryGetProperty("token", out var to))
                                {
                                    var hashStr = h.GetString();
                                    var token = to.GetString();

                                    if (!string.IsNullOrWhiteSpace(hashStr) && !string.IsNullOrWhiteSpace(token))
                                    {
                                        // 通过 hash 和 token 拼接验证 CF 验证 URL
                                        // https://editor.midjourney.com/captcha/challenge/index.html?hash=OOUxejO94EQNxsCODRVPbg&token=dXDm-gSb4Zlsx-PCkNVyhQ
                                        var url = $"https://editor.midjourney.com/captcha/challenge/index.html?hash={hashStr}&token={token}";
                                        var success = await CloudflareHelper.Validate(_captchaOption, hash, url);
                                        if (success)
                                        {
                                            request.Success = true;
                                            request.Message = "CF 自动验证成功";

                                            // 通过验证
                                            // 通知最多重试 3 次
                                            var notifyHook = request.NotifyHook;
                                            if (!string.IsNullOrWhiteSpace(notifyHook))
                                            {
                                                // 使用 reshshrp 通知 post 请求
                                                var notifyCount = 0;
                                                do
                                                {
                                                    if (notifyCount > 3)
                                                    {
                                                        break;
                                                    }

                                                    notifyHook = $"{notifyHook.Trim().TrimEnd('/')}/account-cf-notify";
                                                    var client = new RestClient();
                                                    var req = new RestRequest(notifyHook, Method.Post);
                                                    req.AddHeader("Content-Type", "application/json");
                                                    req.AddJsonBody(request, contentType: ContentType.Json);
                                                    var res = await client.ExecuteAsync(req);
                                                    if (res.StatusCode != System.Net.HttpStatusCode.OK)
                                                    {
                                                        Log.Error("通知失败 {@0} - {@1}", request, notifyHook);
                                                    }
                                                    else
                                                    {
                                                        finSuccess = true;
                                                        break;
                                                    }

                                                    notifyCount++;
                                                } while (true);
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    throw new LogicException("CF 生成链接失败");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "CF 链接生成执行异常 {@0}", request);
                        }

                        retry++;
                        await Task.Delay(1000);
                    } while (true);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "CF 执行异常 {@0}", request);
                }
                finally
                {
                    Log.Information("CF 验证最终结果 {@0}, {@1}", request, finSuccess);

                    try
                    {
                        if (!finSuccess)
                        {
                            request.Success = false;
                            request.Message = "CF 自动验证失败，请手动验证";

                            // 通知服务器手动验证
                            // 通过验证
                            // 通知最多重试 3 次
                            var notifyHook = request.NotifyHook;
                            if (!string.IsNullOrWhiteSpace(notifyHook))
                            {
                                // 使用 reshshrp 通知 post 请求
                                var notifyCount = 0;
                                do
                                {
                                    if (notifyCount > 3)
                                    {
                                        break;
                                    }
                                    notifyCount++;

                                    notifyHook = $"{notifyHook.Trim().TrimEnd('/')}/account-cf-notify";
                                    var client = new RestClient();
                                    var req = new RestRequest(notifyHook, Method.Post);
                                    req.AddHeader("Content-Type", "application/json");
                                    req.AddJsonBody(request, contentType: ContentType.Json);
                                    var res = await client.ExecuteAsync(req);
                                    if (res.StatusCode != System.Net.HttpStatusCode.OK)
                                    {
                                        Log.Error("通知失败 {@0} - {@1}", request, notifyHook);
                                    }
                                    else
                                    {
                                        break;
                                    }
                                } while (true);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "通知服务器手动验证异常 {@0}", request);
                    }
                }
            });

            await Task.CompletedTask;
        }
    }
}