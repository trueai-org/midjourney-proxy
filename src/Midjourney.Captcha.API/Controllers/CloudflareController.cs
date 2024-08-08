using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Midjourney.Infrastructure.Dto;
using Midjourney.Infrastructure.Options;
using Midjourney.Infrastructure.Util;
using RestSharp;
using Serilog;
using System.Net;
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
        private readonly IMemoryCache _memoryCache;
        private readonly CaptchaOption _captchaOption;

        public CloudflareController(IOptionsMonitor<CaptchaOption> optionsMonitor, IMemoryCache memoryCache)
        {
            _captchaOption = optionsMonitor.CurrentValue;
            _memoryCache = memoryCache;
        }

        /// <summary>
        /// 校验 Cloudflare URL
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [HttpPost("verify")]
        public ActionResult Validate([FromBody] CaptchaVerfyRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.State))
            {
                return BadRequest("State 不能为空");
            }

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
            var lockKey = $"lock_{request.State}";

            var isLock = await AsyncLocalLock.TryLockAsync(lockKey, TimeSpan.FromSeconds(3), async () =>
            {
                Log.Information("CF 开始验证 {@0}", request);

                // 如果 2 分钟内，有验证成功的通知，则不再执行
                var successKey = $"{request.State}";
                if (_memoryCache.TryGetValue<bool>(successKey, out var ok) && ok)
                {
                    Log.Information("CF 2 分钟内，验证已经成功，不再执行 {@0}", request);
                    return;
                }

                // 如果 10 分钟内，超过 3 次验证失败，则不再执行
                var failKey = $"{request.State}_fail";
                if (_memoryCache.TryGetValue<int>(failKey, out var failCount) && failCount >= 3)
                {
                    Log.Information("CF 10 分钟内，验证失败次数超过 3 次，不再执行 {@0}", request);
                    return;
                }

                // 最终结果
                var finSuccess = false;
                try
                {
                    // https://936929561302675456.discordsays.com/captcha/api/c/hIlZOI0ZQI3qQjpXhzS4GTgw_DuRTjYiyyww38dJuTzmqA8pa3OC60yTJbTmK6jd3i6Q0wZNxiuEp2dW/ack?hash=1
                    // 此链接 30分钟内有效
                    var hashUrl = request.Url;
                    var hash = hashUrl?.Split('/').Where(c => !c.Contains("?")).OrderByDescending(c => c.Length).FirstOrDefault();
                    if (string.IsNullOrWhiteSpace(hash))
                    {
                        return;
                    }

                    var retry = 0;
                    do
                    {
                        // 最多执行 3 次
                        retry++;
                        if (finSuccess || retry > 3)
                        {
                            break;
                        }

                        try
                        {
                            //WebProxy webProxy = null;
                            //var proxy = GlobalConfiguration.Setting?.Proxy;
                            //if (!string.IsNullOrEmpty(proxy?.Host))
                            //{
                            //    webProxy = new WebProxy(proxy.Host, proxy.Port ?? 80);
                            //}
                            //var hch = new HttpClientHandler
                            //{
                            //    UseProxy = webProxy != null,
                            //    Proxy = webProxy
                            //};

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
                                            finSuccess = true;
                                            request.Success = true;
                                            request.Message = "CF 自动验证成功";

                                            // 标记验证成功
                                            _memoryCache.Set(successKey, true, TimeSpan.FromMinutes(2));

                                            // 通过验证
                                            // 通知最多重试 3 次
                                            var notifyHook = request.NotifyHook;
                                            if (!string.IsNullOrWhiteSpace(notifyHook))
                                            {
                                                notifyHook = $"{notifyHook.Trim().TrimEnd('/')}/mj/admin/account-cf-notify";

                                                // 使用 reshshrp 通知 post 请求
                                                var notifyCount = 0;
                                                do
                                                {
                                                    if (notifyCount > 3)
                                                    {
                                                        break;
                                                    }

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
                                                        // 通知成功
                                                        Log.Information("系统自动验证通过，通知成功 {@0} - {@1}", request, notifyHook);
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

                            // 验证失败计数 10 分钟内，最多 3 次
                            if (_memoryCache.TryGetValue<int>(failKey, out var fc))
                            {
                                fc++;
                                _memoryCache.Set(failKey, fc, TimeSpan.FromMinutes(10));
                            }
                            else
                            {
                                _memoryCache.Set(failKey, 1, TimeSpan.FromMinutes(10));
                            }

                            // 通知服务器手动验证
                            // 通过验证
                            // 通知最多重试 3 次
                            var notifyHook = request.NotifyHook;
                            if (!string.IsNullOrWhiteSpace(notifyHook))
                            {
                                notifyHook = $"{notifyHook.Trim().TrimEnd('/')}/mj/admin/account-cf-notify";

                                // 使用 reshshrp 通知 post 请求
                                var notifyCount = 0;
                                do
                                {
                                    if (notifyCount > 3)
                                    {
                                        break;
                                    }
                                    notifyCount++;

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
                                        // 通知请手动验证成功
                                        Log.Information("通知手动验证成功 {@0} - {@1}", request, notifyHook);
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
            if (!isLock)
            {
                Log.Warning("CF 验证正在执行中，请稍后再试 {@0}", request);
            }

            await Task.CompletedTask;
        }
    }
}