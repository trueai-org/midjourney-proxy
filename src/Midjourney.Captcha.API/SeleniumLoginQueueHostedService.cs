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

using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Midjourney.Infrastructure.Dto;
using Midjourney.Infrastructure.Options;
using Midjourney.Infrastructure.Util;
using RestSharp;
using Serilog;

namespace Midjourney.Captcha.API
{
    /// <summary>
    /// Selenium 登录队列服务
    /// </summary>
    public class SeleniumLoginQueueHostedService : BackgroundService
    {
        private static readonly ConcurrentQueue<AutoLoginRequest> _queue = new();

        private readonly ILogger<SeleniumLoginQueueHostedService> _logger;
        private readonly SemaphoreSlim _concurrencySemaphore;
        private readonly IMemoryCache _memoryCache;
        private readonly CaptchaOption _captchaOption;

        public SeleniumLoginQueueHostedService(
            ILogger<SeleniumLoginQueueHostedService> logger,
            IOptionsMonitor<CaptchaOption> optionsMonitor,
            IMemoryCache memoryCache)
        {
            _logger = logger;
            _captchaOption = optionsMonitor.CurrentValue;

            var max = Math.Max(1, optionsMonitor.CurrentValue.Concurrent);

            _concurrencySemaphore = new SemaphoreSlim(max, max);

            _memoryCache = memoryCache;
        }

        /// <summary>
        /// 将请求入队
        /// </summary>
        /// <param name="request"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public static void EnqueueRequest(AutoLoginRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            _queue.Enqueue(request);
        }

        /// <summary>
        /// 尝试出队请求
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public static bool TryDequeueRequest(out AutoLoginRequest request)
        {
            return _queue.TryDequeue(out request);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("自动登录服务运行中...");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (TryDequeueRequest(out var request))
                    {
                        // 等待并发信号量
                        await _concurrencySemaphore.WaitAsync(stoppingToken);

                        // 在后台线程中处理请求
                        _ = ProcessRequestAsync(request, stoppingToken)
                            .ContinueWith(t =>
                            {
                                // 释放并发信号量
                                _concurrencySemaphore.Release();
                            }, TaskContinuationOptions.OnlyOnRanToCompletion);

                        _logger.LogInformation($"自动登录服务运行中... 待执行: {_queue.Count}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "自动登录验证队列异常");
                }

                await Task.Delay(500);
            }
        }

        private async Task ProcessRequestAsync(AutoLoginRequest request, CancellationToken cancellationToken)
        {
            try
            {
                await DoWork(request);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"自动登录异常: {request.State}");
            }
        }

        /// <summary>
        /// 开始执行作业
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        /// <exception cref="LogicException"></exception>
        private async Task DoWork(AutoLoginRequest request)
        {
            var lockKey = $"autologin_lock_{request.State}";

            var isLock = await AsyncLocalLock.TryLockAsync(lockKey, TimeSpan.FromSeconds(3), async () =>
            {
                Log.Information("开始自动登录 {@0}", request);

                var conn = await ValidateNotify(request.NotifyHook);
                if (!conn)
                {
                    Log.Warning("自动登录回调错误 {@0}", request);
                    return;
                }

                // 如果 2 分钟内，有验证成功的通知，则不再执行
                var successKey = $"autologin_{request.State}";
                if (_memoryCache.TryGetValue<bool>(successKey, out var ok) && ok)
                {
                    Log.Information("自动登录 2 分钟内，验证已经成功，不再执行 {@0}", request);
                    return;
                }

                // 如果 10 分钟内，超过 3 次验证失败，则不再执行
                var failKey = $"autologin_{request.State}_fail";
                if (_memoryCache.TryGetValue<int>(failKey, out var failCount) && failCount >= 3)
                {
                    Log.Information("自动登录 10 分钟内，验证失败次数超过 3 次，不再执行 {@0}", request);
                    return;
                }

                var root = GlobalConfiguration.ContentRootPath;

                // 最终结果
                var finSuccess = false;
                try
                {
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
                            var (success, data) = SeleniumLoginHelper.Login(root, _captchaOption.YesCaptchaKey,
                                  request.LoginAccount, request.LoginPassword, request.Login2fa);

                            if (success == true && !string.IsNullOrWhiteSpace(data))
                            {
                                finSuccess = true;

                                request.Token = data;
                                request.Success = true;
                                request.Message = "自动登录成功";

                                // 标记验证成功
                                _memoryCache.Set(successKey, true, TimeSpan.FromMinutes(2));

                                // 通过验证
                                // 通知最多重试 3 次
                                var notifyHook = request.NotifyHook;
                                if (!string.IsNullOrWhiteSpace(notifyHook))
                                {
                                    notifyHook = $"{notifyHook.Trim().TrimEnd('/')}/mj/admin/account-login-notify";

                                    // 使用 reshshrp 通知 post 请求
                                    var notifyCount = 0;
                                    do
                                    {
                                        if (notifyCount > 3)
                                        {
                                            break;
                                        }

                                        var client = new RestClient();
                                        var req = new RestRequest(notifyHook, Method.Post)
                                        {
                                            Timeout = TimeSpan.FromSeconds(30)
                                        };
                                        req.AddHeader("Content-Type", "application/json");
                                        req.AddJsonBody(request, contentType: ContentType.Json);
                                        var res = await client.ExecuteAsync(req);
                                        if (res.StatusCode != System.Net.HttpStatusCode.OK)
                                        {
                                            Log.Error("登录成功通知失败 {@0} - {@1}", request, notifyHook);
                                        }
                                        else
                                        {
                                            // 通知成功
                                            Log.Information("登录成功，通知成功 {@0} - {@1}", request, notifyHook);
                                            break;
                                        }

                                        notifyCount++;
                                    } while (true);
                                }
                            }
                            else
                            {
                                request.Message = data ?? "登录失败";

                                Log.Warning("自动登录失败 {@0}", request);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "自动登录执行异常 {@0}", request);
                        }

                        await Task.Delay(1000);
                    } while (true);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "自动登录执行异常 {@0}", request);
                }
                finally
                {
                    Log.Information("自动登录最终结果 {@0}, {@1}", request, finSuccess);

                    try
                    {
                        if (!finSuccess)
                        {
                            request.Success = false;

                            request.Message ??= "自动登录失败，请手动登录";

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
                                notifyHook = $"{notifyHook.Trim().TrimEnd('/')}/mj/admin/account-login-notify";

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
                                        Log.Error("自动登录通知失败 {@0} - {@1}", request, notifyHook);
                                    }
                                    else
                                    {
                                        Log.Information("自动登录通知手动登录 {@0} - {@1}", request, notifyHook);
                                        break;
                                    }
                                } while (true);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "自动登录通知服务器异常 {@0}", request);
                    }
                }
            });
            if (!isLock)
            {
                Log.Warning("自动登录正在执行中，请稍后再试 {@0}", request);
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// 验证回调通知服务器是否可以连接
        /// </summary>
        /// <param name="notify"></param>
        /// <returns></returns>
        public async Task<bool> ValidateNotify(string notify)
        {
            var request = new AutoLoginRequest()
            {
                NotifyHook = notify
            };

            var notifyHook = request.NotifyHook;
            if (!string.IsNullOrWhiteSpace(notifyHook))
            {
                notifyHook = $"{notifyHook.Trim().TrimEnd('/')}/mj/admin/account-login-notify";
                var client = new RestClient();
                var req = new RestRequest(notifyHook, Method.Post)
                {
                    Timeout = TimeSpan.FromSeconds(60)
                };
                req.AddHeader("Content-Type", "application/json");
                req.AddJsonBody(request, contentType: ContentType.Json);
                var res = await client.ExecuteAsync(req);
                if (res.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    return true;
                }
            }

            return false;
        }
    }
}