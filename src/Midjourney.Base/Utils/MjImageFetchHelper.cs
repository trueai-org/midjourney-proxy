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

using System.Net;
using System.Net.Http.Headers;
using Serilog;

namespace Midjourney.Base.Util
{
    /// <summary>
    /// MJ 文件抓取助手
    /// </summary>
    public class MjImageFetchHelper
    {
        /// <summary>
        /// 跳过的文件主机名
        /// </summary>
        private static readonly string[] WHITE_HOSTS = ["discordapp.com", "cdn.discordapp.com", "mj.run", "midjourney.com", "cdn.midjourney.com"];

        /// <summary>
        /// 默认最大文件大小 128MB
        /// </summary>
        private static readonly long MAX_SIZE = 128 * 1024 * 1024;

        /// <summary>
        /// 使用懒加载的静态 HttpClient 实例，线程安全且只初始化一次
        /// </summary>
        private static readonly Lazy<HttpClient> LazyHttpClient = new(() =>
        {
            var handler = new SocketsHttpHandler
            {
                // 连接生命周期 5 分钟，确保 DNS 刷新
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),

                // 空闲连接 2 分钟后关闭
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),

                // 每个服务器最大 100 个连接
                MaxConnectionsPerServer = 100,

                // 自动解压缩
                AutomaticDecompression = DecompressionMethods.All,
            };

            // 配置代理（如果需要）
            ConfigureProxy(handler);

            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMinutes(15),
            };

            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/58.0.3029.110 Safari/537.36");

            return client;
        }, LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>
        /// 共享的 HttpClient 实例
        /// </summary>
        private static HttpClient SharedHttpClient => LazyHttpClient.Value;

        /// <summary>
        /// 配置代理
        /// </summary>
        private static void ConfigureProxy(SocketsHttpHandler handler)
        {
            // 从配置中读取代理设置
            var proxyHost = GlobalConfiguration.Setting?.Proxy?.Host;
            var proxyPort = GlobalConfiguration.Setting?.Proxy?.Port ?? 80;

            if (!string.IsNullOrEmpty(proxyHost))
            {
                handler.Proxy = new WebProxy(proxyHost, proxyPort)
                {
                    // 本机/回环地址不走代理，避免影响本地服务与测试
                    BypassProxyOnLocal = true,
                    BypassList = ["127\\.0\\.0\\.1", "localhost", "\\[::1\\]"],
                };
                handler.UseProxy = true;
            }
        }

        /// <summary>
        /// 判断 URL 是否为官方白名单域名
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        public static bool IsWhiteHost(string url)
        {
            var host = new Uri(url).Host;
            return WHITE_HOSTS.Any(x => host.Contains(x, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 判断 URL 是否为云存储加速域名/全球加速域名
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        public static bool IsCdnHost(string url)
        {
            // 自定义加速域名
            var cdn = StorageHelper.Instance?.GetCustomCdn();
            if (string.IsNullOrWhiteSpace(cdn))
            {
                return false;
            }
            var host = new Uri(url).Host;
            var cdnHost = new Uri(cdn).Host;
            var isCdn = host.Equals(cdnHost, StringComparison.OrdinalIgnoreCase);

            // 全球加速地址判断
            if (!isCdn)
            {
                var storage = StorageHelper.Instance?.GetBaseStorage();
                if (!string.IsNullOrWhiteSpace(storage?.GlobalCustomCdn))
                {
                    var globalCdnHost = new Uri(storage.GlobalCustomCdn).Host;
                    isCdn = host.Equals(globalCdnHost, StringComparison.OrdinalIgnoreCase);
                }
            }

            return isCdn;
        }

        /// <summary>
        /// 异步获取文件
        /// 如果不在白名单的后缀，则默认为 jpg
        /// </summary>
        /// <param name="url">https://mp-70570b1c-bf6a-40fe-9635-8e5c1901c65d.cdn.bspapp.com/temp/1723592564348_0.png</param>
        /// <returns></returns>
        public static Task<FetchFileResult> FetchFileAsync(string url, int retry = 0, bool isWhite = true, bool isRetry = true)
        {
            return FetchFileAsync(url, retry, isWhite, isRetry, false, null, default);
        }

        /// <summary>
        /// 异步获取文件，并指定是否将提示词/垫图图片转换为 WebP
        /// </summary>
        public static Task<FetchFileResult> FetchFileAsync(string url, bool convertToWebp, int retry = 0, bool isWhite = true, bool isRetry = true)
        {
            return FetchFileAsync(url, retry, isWhite, isRetry, convertToWebp, null, default);
        }

        /// <summary>
        /// 异步获取文件，并指定单次请求超时与取消令牌。
        /// 用于工作线程准备垫图时限制慢 CDN 占用时间。
        /// </summary>
        /// <param name="detectFileName">是否探测/生成文件名；准备阶段不需要，可避免额外 HEAD 与 CPU</param>
        public static Task<FetchFileResult> FetchFileAsync(
            string url,
            bool convertToWebp,
            TimeSpan timeout,
            CancellationToken cancellationToken = default,
            int retry = 0,
            bool isWhite = true,
            bool isRetry = true,
            long? maxBytes = null,
            bool detectFileName = true)
        {
            var deadline = timeout > TimeSpan.Zero ? DateTime.UtcNow + timeout : (DateTime?)null;
            return FetchFileAsync(url, retry, isWhite, isRetry, convertToWebp, timeout, cancellationToken, deadline, maxBytes, detectFileName);
        }

        private static async Task<FetchFileResult> FetchFileAsync(
            string url,
            int retry,
            bool isWhite,
            bool isRetry,
            bool convertToWebp,
            TimeSpan? timeout,
            CancellationToken cancellationToken,
            DateTime? deadline = null,
            long? maxBytes = null,
            bool detectFileName = true)
        {
            if (isRetry && retry > 5)
            {
                return new FetchFileResult { Success = false, Msg = "Fetch retry limit exceeded" };
            }

            if (string.IsNullOrWhiteSpace(url) || !Uri.IsWellFormedUriString(url, UriKind.Absolute))
            {
                return new FetchFileResult { Success = false, Msg = "Invalid URL" };
            }

            if (!deadline.HasValue && timeout.HasValue && timeout.Value > TimeSpan.Zero)
            {
                deadline = DateTime.UtcNow + timeout.Value;
            }

            var remaining = deadline.HasValue ? deadline.Value - DateTime.UtcNow : (TimeSpan?)null;
            if (remaining.HasValue && remaining.Value <= TimeSpan.Zero)
            {
                return new FetchFileResult { Success = false, Msg = "Fetch timeout" };
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (remaining.HasValue)
            {
                cts.CancelAfter(remaining.Value);
            }

            try
            {
                // 如果是白名单 host 则返回当前 url
                // 官方域名不做转换
                if (isWhite && IsWhiteHost(url))
                {
                    return new FetchFileResult { Success = true, Url = url, Msg = "White host" };
                }

                // 输入链接白名单：配置的域名不抓取/不转存，直接复用原链接
                if (GlobalConfiguration.Setting?.IsPromptLinkWhitelisted(url) == true)
                {
                    return new FetchFileResult { Success = true, Url = url, Msg = "Prompt link whitelist" };
                }

                // 创建独立的请求消息，避免污染共享 HttpClient
                using var request = new HttpRequestMessage(HttpMethod.Get, url);

                var maxSize = maxBytes.GetValueOrDefault(MAX_SIZE);
                if (maxSize <= 0)
                {
                    maxSize = MAX_SIZE;
                }
                if (maxSize > 0)
                {
                    request.Headers.Range = new RangeHeaderValue(0, maxSize - 1);
                }

                using var response = await SharedHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == HttpStatusCode.Redirect)
                    {
                        // 如果是重定向，则尝试获取新的 URL
                        if (response.Headers.Location != null)
                        {
                            var newUrl = response.Headers.Location.ToString();
                            if (newUrl != url)
                            {
                                if (isRetry)
                                {
                                    // 重定向复用同一截止时间，避免每一跳重新获得完整超时
                                    response.Dispose();
                                    return await FetchFileAsync(newUrl, ++retry, isWhite, isRetry, convertToWebp, timeout, cancellationToken, deadline, maxBytes, detectFileName);
                                }
                            }
                        }
                    }

                    return new FetchFileResult { Success = false, Msg = $"Failed to fetch file. Status: {response.StatusCode}" };
                }

                // 先根据响应头拒绝超限文件，避免无谓下载；再按块读取兜底服务端忽略 Range 的情况
                var contentLength = response.Content.Headers.ContentLength;
                var contentRangeLength = response.Content.Headers.ContentRange?.Length;
                if ((contentLength.HasValue && contentLength.Value > maxSize)
                    || (contentRangeLength.HasValue && contentRangeLength.Value > maxSize))
                {
                    return new FetchFileResult { Success = false, Msg = "File size exceeds limit." };
                }

                byte[] fileBytes;
                using (var stream = await response.Content.ReadAsStreamAsync(cts.Token))
                using (var memory = new MemoryStream())
                {
                    var buffer = new byte[81920];
                    int read;
                    while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cts.Token)) > 0)
                    {
                        if (memory.Length + read > maxSize)
                        {
                            return new FetchFileResult { Success = false, Msg = "File size exceeds limit." };
                        }

                        memory.Write(buffer, 0, read);
                    }

                    fileBytes = memory.ToArray();
                }

                var contentType = response.Content.Headers.ContentType?.MediaType;
                var dataUrl = new DataUrl
                {
                    Url = url,
                    MimeType = contentType,
                    Data = fileBytes
                };
                var ext = detectFileName ? await MjImageHelper.GuessFileSuffix(dataUrl) : null;

                // 提示词/垫图图片按配置转换为 WebP
                if (convertToWebp && PromptImageWebpConverter.TryConvertToWebP(dataUrl))
                {
                    ext = PromptImageWebpConverter.WebpExtension;
                    contentType = dataUrl.MimeType;
                }

                var fileName = detectFileName ? $"{Guid.NewGuid()}{ext ?? ".jpg"}" : null;

                // 返回处理后的图片或原始图片数据

                return new FetchFileResult
                {
                    Success = true,
                    FileName = fileName,
                    FileBytes = dataUrl?.Data ?? fileBytes,
                    ContentType = contentType,
                    FileExtension = ext
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new FetchFileResult { Success = false, Msg = "Fetch cancelled" };
            }
            catch (OperationCanceledException)
            {
                return new FetchFileResult { Success = false, Msg = "Fetch timeout" };
            }
            catch (Exception ex)
            {
                return new FetchFileResult { Success = false, Msg = $"Error fetching file: {ex.Message}" };
            }
        }

        /// <summary>
        /// 异步获取文件并存储到文件存储
        /// </summary>
        /// <param name="url">https://mp-70570b1c-bf6a-40fe-9635-8e5c1901c65d.cdn.bspapp.com/temp/1723592564348_0.png</param>
        /// <returns></returns>
        public static async Task<string> FetchFileToStorageAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url) || !Uri.IsWellFormedUriString(url, UriKind.Absolute))
            {
                return null;
            }

            try
            {
                // 如果与加速链接相同，则返回
                if (IsCdnHost(url))
                {
                    return url;
                }

                // 创建独立的请求消息，避免污染共享HttpClient
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (MAX_SIZE > 0)
                {
                    request.Headers.Range = new RangeHeaderValue(0, MAX_SIZE - 1);
                }

                using var response = await SharedHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                if (!response.IsSuccessStatusCode)
                {
                    Log.Warning($"{url} Failed to fetch file. Status: {response.StatusCode}");
                    return null;
                }

                var fileBytes = await response.Content.ReadAsByteArrayAsync();
                if (fileBytes.Length >= MAX_SIZE)
                {
                    Log.Warning($"{url} File size exceeds limit.");
                    return null;
                }
                var contentType = response.Content.Headers.ContentType?.MediaType;
                var dataUrl = new DataUrl
                {
                    Url = url,
                    MimeType = contentType,
                    Data = fileBytes
                };
                var ext = await MjImageHelper.GuessFileSuffix(dataUrl);

                fileBytes = dataUrl?.Data ?? fileBytes;

                var saveFileName = $"attachments/fetchs/{DateTime.Now:yyyyMMdd}/{Guid.NewGuid():N}{ext}";

                // 如果无法获取 contentType，则根据扩展名获取
                if (string.IsNullOrWhiteSpace(contentType))
                {
                    MimeTypeHelper.TryGetMimeType(saveFileName, out contentType);
                }

                var res = StorageHelper.Instance.SaveAsync(new MemoryStream(fileBytes), saveFileName, contentType);
                if (!string.IsNullOrWhiteSpace(res?.Url))
                {
                    return res.Url;
                }

                Log.Warning($"{url} 保存到云存储失败");

                return null;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, $"抓取文件异常: {url}");

                return null;
            }
        }

        /// <summary>
        /// 获取 url 链接文件大小
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static async Task<long> GetFileSizeAsync(string url)
        {
            try
            {
                // 发送一个 HEAD 请求
                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                using var response = await SharedHttpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    if (response.Content.Headers.ContentLength.HasValue)
                    {
                        return response.Content.Headers.ContentLength.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "获取图片链接文件大小失败: {@0}", url);
            }
            return 0;
        }

        /// <summary>
        /// 获取 url 链接的 Content-Type
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        public static async Task<string> GetContentTypeAsync(string url)
        {
            try
            {
                // 发送 HEAD 请求；HEAD 只用于猜后缀，不能让提交卡在慢 CDN 上
                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var response = await SharedHttpClient.SendAsync(request, cts.Token);
                if (response.IsSuccessStatusCode)
                {
                    // 获取 Content-Type 头部
                    if (response.Content.Headers.ContentType != null)
                    {
                        return response.Content.Headers.ContentType.MediaType;
                    }
                }
                return null;
            }
            catch (OperationCanceledException)
            {
                Log.Warning("获取图片链接 Content-Type 超时（5s）: Host={Host}", TryGetHost(url));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "获取图片链接 Content-Type 失败: Host={Host}", TryGetHost(url));
            }
            return null;
        }

        private static string TryGetHost(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url;
        }
    }

    public class FetchFileResult
    {
        public bool Success { get; set; }

        public string Msg { get; set; }

        public string FileName { get; set; }

        public byte[] FileBytes { get; set; }

        public string ContentType { get; set; }

        public string FileExtension { get; set; }

        public string Url { get; set; }
    }
}