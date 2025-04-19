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

using Microsoft.IdentityModel.Logging;
using Midjourney.Infrastructure.Storage;
using MimeDetective;
using Serilog;
using System.Net;
using System.Net.Http.Headers;

namespace Midjourney.Infrastructure.Util
{
    /// <summary>
    /// 文件抓取助手
    /// 多种方案
    /// 
    /// 备选：https://github.com/samuelneff/MimeTypeMap
    /// </summary>
    public class FileFetchHelper
    {
        /// <summary>
        /// 可抓取的文件文件后缀
        /// </summary>
        private string[] WHITE_EXTENSIONS = ["jpg", "png", "webp", "bmp", "gif", "pdf", "jpeg", "tiff", "svg", "heif", "heic", "mp4"];

        /// <summary>
        /// 跳过的文件主机名
        /// </summary>
        private string[] WHITE_HOSTS = ["discordapp.com", "cdn.discordapp.com", "mj.run", "midjourney.com", "cdn.midjourney.com"];

        private readonly HttpClient _httpClient;
        private readonly long _maxFileSize;

        /// <summary>
        /// 文件抓取到本地
        /// 默认：超时 15 分钟
        /// </summary>
        /// <param name="maxFileSize">默认最大文件大小为 128 MB</param>
        public FileFetchHelper(long maxFileSize = 128 * 1024 * 1024)
        {
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
            _httpClient = new HttpClient(hch)
            {
                Timeout = TimeSpan.FromMinutes(15),
            };

            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/58.0.3029.110 Safari/537.36");

            _maxFileSize = maxFileSize;
        }

        /// <summary>
        /// 异步获取文件
        /// 如果不在白名单的后缀，则默认为 jpg
        /// </summary>
        /// <param name="url">https://mp-70570b1c-bf6a-40fe-9635-8e5c1901c65d.cdn.bspapp.com/temp/1723592564348_0.png</param>
        /// <returns></returns>
        public async Task<FetchFileResult> FetchFileAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url) || !Uri.IsWellFormedUriString(url, UriKind.Absolute))
            {
                return new FetchFileResult { Success = false, Msg = "Invalid URL" };
            }

            try
            {
                // 如果是白名单 host 则返回当前 url
                var host = new Uri(url).Host;

                // 官方域名不做转换
                if (WHITE_HOSTS.Any(x => host.Contains(x)))
                {
                    return new FetchFileResult { Success = true, Url = url, Msg = "White host" };
                }

                //_httpClient.DefaultRequestHeaders.Host = host;

                if (_maxFileSize > 0)
                {
                    _httpClient.DefaultRequestHeaders.Range = new RangeHeaderValue(0, _maxFileSize - 1);
                }

                var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

                if (!response.IsSuccessStatusCode)
                {
                    return new FetchFileResult { Success = false, Msg = $"Failed to fetch file. Status: {response.StatusCode}" };
                }

                var fileBytes = await response.Content.ReadAsByteArrayAsync();
                if (fileBytes.Length >= _maxFileSize)
                {
                    return new FetchFileResult { Success = false, Msg = "File size exceeds limit." };
                }

                var contentType = response.Content.Headers.ContentType?.MediaType;
                var fileName = GetFileNameFromUrlOrHeaders(url, response.Content.Headers);
                var fileExtension = DetermineFileExtension(contentType, fileBytes, fileName);

                fileName = $"{Guid.NewGuid()}{fileExtension}";

                // 再根据扩展名获取 MIME 类型
                var mm = MimeKit.MimeTypes.GetMimeType(fileName);
                if (!string.IsNullOrWhiteSpace(mm))
                {
                    contentType = mm;
                }

                return new FetchFileResult
                {
                    Success = true,
                    FileName = fileName,
                    FileBytes = fileBytes,
                    ContentType = contentType,
                    FileExtension = fileExtension
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
        public async Task<string> FetchFileToStorageAsync(string url)
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

                if (_maxFileSize > 0)
                {
                    _httpClient.DefaultRequestHeaders.Range = new RangeHeaderValue(0, _maxFileSize - 1);
                }

                var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                if (!response.IsSuccessStatusCode)
                {
                    Log.Warning($"{url} Failed to fetch file. Status: {response.StatusCode}");
                    return null;
                }

                var fileBytes = await response.Content.ReadAsByteArrayAsync();
                if (fileBytes.Length >= _maxFileSize)
                {
                    Log.Warning($"{url} File size exceeds limit.");
                    return null;
                }

                var contentType = response.Content.Headers.ContentType?.MediaType;
                var fileName = GetFileNameFromUrlOrHeaders(url, response.Content.Headers);
                var fileExtension = DetermineFileExtension(contentType, fileBytes, fileName);

                fileName = $"{Guid.NewGuid()}{fileExtension}";

                // 再根据扩展名获取 MIME 类型
                var mm = MimeKit.MimeTypes.GetMimeType(fileName);
                if (!string.IsNullOrWhiteSpace(mm))
                {
                    contentType = mm;
                }

                var saveFileName = $"attachments/fetchs/{DateTime.Now:yyyyMMdd}/{Guid.NewGuid():N}{fileExtension}";
                var res = StorageHelper.Instance?.SaveAsync(new MemoryStream(fileBytes), saveFileName, mm);
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
        /// 从 URL 或响应头获取文件名
        /// </summary>
        /// <param name="url"></param>
        /// <param name="headers"></param>
        /// <returns></returns>
        private string GetFileNameFromUrlOrHeaders(string url, HttpContentHeaders headers)
        {
            var fileName = Path.GetFileName(new Uri(url).LocalPath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = headers.ContentDisposition?.FileName?.Trim('"') ?? "unknown";
            }

            return fileName;
        }

        /// <summary>
        /// 确定文件扩展名
        /// </summary>
        /// <param name="contentType"></param>
        /// <param name="fileBytes"></param>
        /// <param name="fileName"></param>
        /// <returns></returns>
        public string DetermineFileExtension(string contentType, byte[] fileBytes, string fileName)
        {
            contentType = contentType?.ToLowerInvariant();
            fileName = fileName?.ToLowerInvariant();

            var extension = string.Empty;

            // 尝试从 MimeTypes 获取扩展名
            if (!string.IsNullOrWhiteSpace(contentType))
            {
                extension = MimeTypes.GetMimeTypeExtensions(contentType)
                    .Where(c => WHITE_EXTENSIONS.Contains(c))
                    .FirstOrDefault();
            }

            // 使用 MimeKit + MimeDetective 根据文件内容推断 MIME 类型
            if (string.IsNullOrEmpty(extension))
            {
                var inspector = new ContentInspectorBuilder()
                {
                    Definitions = MimeDetective.Definitions.Default.All()
                }.Build();
                var results = inspector.Inspect(fileBytes);
                var mimeType = results.ByMimeType();
                if (mimeType != null && mimeType.Length > 0)
                {
                    foreach (var item in mimeType)
                    {
                        if (MimeKit.MimeTypes.TryGetExtension(item.MimeType, out var ext) && WHITE_EXTENSIONS.Contains(ext?.Trim('.')))
                        {
                            extension = ext.Trim('.');
                            break;
                        }
                    }
                }
            }

            // 使用 MimeKit 扩展名获取
            if (string.IsNullOrWhiteSpace(extension) && !string.IsNullOrWhiteSpace(fileName))
            {
                var mimeType = MimeKit.MimeTypes.GetMimeType(fileName);
                if (!string.IsNullOrWhiteSpace(mimeType))
                {
                    if (MimeKit.MimeTypes.TryGetExtension(mimeType, out var ext) && WHITE_EXTENSIONS.Contains(ext?.Trim('.')))
                    {
                        extension = ext;
                    }
                }
            }

            // 使用魔术数手动解析扩展名
            if (string.IsNullOrEmpty(extension))
            {
                extension = GetFileExtensionFromMagicNumber(fileBytes);
            }

            // 使用文件名获取扩展名
            if (string.IsNullOrEmpty(extension))
            {
                extension = Path.GetExtension(fileName);
            }

            if (!string.IsNullOrWhiteSpace(extension))
            {
                extension = extension.Trim('.');
            }

            return $".{(WHITE_EXTENSIONS.Contains(extension) ? extension : "jpg")}";
        }

        /// <summary>
        /// 获取 MIME 类型
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        public string GetMimeType(string fileName)
        {
            return MimeKit.MimeTypes.GetMimeType(fileName);
        }

        /// <summary>
        /// 通过魔术数获取文件扩展名
        /// </summary>
        /// <param name="bytes"></param>
        /// <returns></returns>
        private string GetFileExtensionFromMagicNumber(byte[] bytes)
        {
            if (bytes.Length < 4)
                return null;

            // 检查文件头的前几个字节（魔术数）
            if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
                return CheckExtension("png");
            if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
                return CheckExtension("jpg");
            if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46)
                return CheckExtension("gif");
            if (bytes[0] == 0x42 && bytes[1] == 0x4D)
                return CheckExtension("bmp");
            if ((bytes[0] == 0x49 && bytes[1] == 0x49 && bytes[2] == 0x2A && bytes[3] == 0x00) || (bytes[0] == 0x4D && bytes[1] == 0x4D && bytes[2] == 0x00 && bytes[3] == 0x2A))
                return CheckExtension("tiff");
            if (bytes.Length >= 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
                return CheckExtension("webp");
            if (bytes.Length >= 6 && bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46 && bytes[4] == 0x2D && bytes[5] == 0x31)
                return CheckExtension("pdf");
            if (bytes.Length >= 4 && bytes[0] == 0x3C && bytes[1] == 0x3F && bytes[2] == 0x78 && bytes[3] == 0x6D)
                return CheckExtension("svg");
            if (bytes.Length >= 12 && bytes[0] == 0x66 && bytes[1] == 0x74 && bytes[2] == 0x79 && bytes[3] == 0x70 && bytes[4] == 0x68 && bytes[5] == 0x65 && bytes[6] == 0x69 && bytes[7] == 0x63)
                return CheckExtension("heic");
            if (bytes.Length >= 12 && bytes[0] == 0x66 && bytes[1] == 0x74 && bytes[2] == 0x79 && bytes[3] == 0x70 && bytes[4] == 0x68 && bytes[5] == 0x65 && bytes[6] == 0x69 && bytes[7] == 0x66)
                return CheckExtension("heif");

            // 扩展其他类型的支持...

            return null;
        }

        /// <summary>
        /// 检查扩展名是否在白名单中
        /// </summary>
        /// <param name="extension"></param>
        /// <returns></returns>
        /// <exception cref="NotSupportedException"></exception>
        private string CheckExtension(string extension)
        {
            if (WHITE_EXTENSIONS.Contains(extension))
            {
                return extension;
            }

            return null;
        }

        /// <summary>
        /// 获取 url 链接文件大小
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public async Task<long> GetFileSizeAsync(string url)
        {
            // 发送一个 HEAD 请求
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Head, url);
            HttpResponseMessage response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                // 获取 Content-Length 头部
                if (response.Content.Headers.ContentLength.HasValue)
                {
                    return response.Content.Headers.ContentLength.Value;
                }
            }

            throw new Exception("Unable to retrieve file size.");
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