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
using Microsoft.IdentityModel.Logging;
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
                handler.Proxy = new WebProxy(proxyHost, proxyPort);
                handler.UseProxy = true;
            }
        }

        /// <summary>
        /// 异步获取文件
        /// 如果不在白名单的后缀，则默认为 jpg
        /// </summary>
        /// <param name="url">https://mp-70570b1c-bf6a-40fe-9635-8e5c1901c65d.cdn.bspapp.com/temp/1723592564348_0.png</param>
        /// <returns></returns>
        public static async Task<FetchFileResult> FetchFileAsync(string url, int retry = 0, bool isWhite = true, bool isRetry = true)
        {
            if (isRetry && retry > 5)
            {
                return new FetchFileResult { Success = false, Msg = "Fetch retry limit exceeded" };
            }

            if (string.IsNullOrWhiteSpace(url) || !Uri.IsWellFormedUriString(url, UriKind.Absolute))
            {
                return new FetchFileResult { Success = false, Msg = "Invalid URL" };
            }

            try
            {
                // 如果是白名单 host 则返回当前 url
                var host = new Uri(url).Host;

                // 官方域名不做转换
                if (isWhite && WHITE_HOSTS.Any(x => host.Contains(x)))
                {
                    return new FetchFileResult { Success = true, Url = url, Msg = "White host" };
                }

                // 创建独立的请求消息，避免污染共享 HttpClient
                using var request = new HttpRequestMessage(HttpMethod.Get, url);

                if (MAX_SIZE > 0)
                {
                    request.Headers.Range = new RangeHeaderValue(0, MAX_SIZE - 1);
                }

                var response = await SharedHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
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
                                    return await FetchFileAsync(newUrl, ++retry, isWhite, isRetry);
                                }
                            }
                        }
                    }

                    return new FetchFileResult { Success = false, Msg = $"Failed to fetch file. Status: {response.StatusCode}" };
                }

                var fileBytes = await response.Content.ReadAsByteArrayAsync();
                if (fileBytes.Length >= MAX_SIZE)
                {
                    return new FetchFileResult { Success = false, Msg = "File size exceeds limit." };
                }

                var contentType = response.Content.Headers.ContentType?.MediaType;
                var dataUrl = new DataUrl
                {
                    Url = url,
                    MimeType = contentType,
                    Data = fileBytes
                };
                var ext = await MjImageHelper.GuessFileSuffix(dataUrl);
                var fileName = $"{Guid.NewGuid()}{ext}";

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
                var host = new Uri(url).Host;
                var cdn = StorageHelper.Instance?.GetCustomCdn();
                if (string.IsNullOrWhiteSpace(cdn))
                {
                    return null;
                }
                var cdnHost = new Uri(cdn).Host;

                // 如果与加速链接相同，则返回
                if (cdnHost.Equals(host, StringComparison.OrdinalIgnoreCase))
                {
                    return url;
                }

                // 创建独立的请求消息，避免污染共享HttpClient
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (MAX_SIZE > 0)
                {
                    request.Headers.Range = new RangeHeaderValue(0, MAX_SIZE - 1);
                }

                var response = await SharedHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
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

                LogHelper.LogWarning($"{url} Failed to save file to storage.");

                return null;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, $"Error fetching file: {url}");

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
                HttpRequestMessage request = new(HttpMethod.Head, url);
                HttpResponseMessage response = await SharedHttpClient.SendAsync(request);
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
                // 发送一个 HEAD 请求
                HttpRequestMessage request = new(HttpMethod.Head, url);
                HttpResponseMessage response = await SharedHttpClient.SendAsync(request);
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
            catch (Exception ex)
            {
                Log.Warning(ex, "获取图片链接 Content-Type 失败: {@0}", url);
            }
            return null;
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