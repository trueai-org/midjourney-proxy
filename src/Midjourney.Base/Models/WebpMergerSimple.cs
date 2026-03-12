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
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace Midjourney.Base.Models
{
    public class WebpMergerSimple
    {
        //private static readonly HttpClient _httpClient = new()
        //{
        //    Timeout = TimeSpan.FromMinutes(10)
        //};

        private static readonly Lazy<HttpClient> _lazyClient = new(() =>
        {
            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
                MaxConnectionsPerServer = 200,
                ConnectTimeout = TimeSpan.FromSeconds(30),
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMinutes(10)
            };
            return client;
        });

        private static HttpClient _httpClient => _lazyClient.Value;

        /// <summary>
        /// 从四个URL下载WebP图片并合并为一张2x2网格的图片，然后保存到云存储 - 简单存储
        /// 排列顺序:
        /// 1 | 2
        /// -----
        /// 3 | 4
        /// </summary>
        /// <param name="url1">左上角图片URL</param>
        /// <param name="url2">右上角图片URL</param>
        /// <param name="url3">左下角图片URL</param>
        /// <param name="url4">右下角图片URL</param>
        /// <param name="filename">保存的文件名，不包含路径</param>
        /// <returns>存储后的URL</returns>
        /// <param name="httpClient"></param>
        public static async Task<string> MergeWebpImagesFromUrlsSimpleAsync(YouChuanTask ycInfo,
            ImageStorageType storageType,
            AliyunOssOptions aliyunOss,
            TencentCosOptions tencentCos,
            CloudflareR2Options cloudflareR2,
            S3StorageOptions s3Storage,
            LocalStorageOptions localStorage,
            bool youChuanInternalDownload)
        {
            if (storageType == ImageStorageType.NONE)
            {
                return null;
            }

            var url1 = ycInfo.ImgUrls[0].Webp;
            var url2 = ycInfo.ImgUrls[1].Webp;
            var url3 = ycInfo.ImgUrls[2].Webp;
            var url4 = ycInfo.ImgUrls[3].Webp;

            var suffix = "webp";
            var filename = $"merged_{Guid.NewGuid():N}.{suffix}";

            // 并行下载所有图片
            var downloadTasks = new Task<byte[]>[]
            {
                DownloadImageAsync(_httpClient, url1, youChuanInternalDownload),
                DownloadImageAsync(_httpClient, url2, youChuanInternalDownload),
                DownloadImageAsync(_httpClient, url3, youChuanInternalDownload),
                DownloadImageAsync(_httpClient, url4, youChuanInternalDownload)
            };

            await Task.WhenAll(downloadTasks);

            // 使用MemoryStream加载图片
            var images = new Image[4];
            int maxWidth = 512;
            int maxHeight = 512;

            for (int i = 0; i < 4; i++)
            {
                using var memoryStream = new MemoryStream(await downloadTasks[i]);
                if (memoryStream.Length == 0)
                {
                    continue;
                }

                images[i] = Image.Load(memoryStream);
                maxWidth = Math.Max(maxWidth, images[i].Width);
                maxHeight = Math.Max(maxHeight, images[i].Height);
            }

            // 创建2x2网格输出图片
            int outputWidth = maxWidth * 2;
            int outputHeight = maxHeight * 2;

            using var outputImage = new Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(outputWidth, outputHeight);

            // 按照指定的排列顺序放置图片
            // 左上 (1)
            outputImage.Mutate(ctx =>
            {
                if (images[0] == null)
                {
                    return;
                }

                ctx.DrawImage(images[0], new Point(0, 0), 1.0f);
            });

            // 右上 (2)
            outputImage.Mutate(ctx =>
            {
                if (images[1] == null)
                {
                    return;
                }

                ctx.DrawImage(images[1], new Point(maxWidth, 0), 1.0f);
            });

            // 左下 (3)
            outputImage.Mutate(ctx =>
            {
                if (images[2] == null)
                {
                    return;
                }

                ctx.DrawImage(images[2], new Point(0, maxHeight), 1.0f);
            });

            // 右下 (4)
            outputImage.Mutate(ctx =>
            {
                if (images[3] == null)
                {
                    return;
                }

                ctx.DrawImage(images[3], new Point(maxWidth, maxHeight), 1.0f);
            });

            // 创建内存流来存储合并后的图片
            using var resultStream = new MemoryStream();

            if (suffix == "webp")
            {
                // 保存为WebP
                var encoder = new WebpEncoder
                {
                    Quality = 90 // 75 // 可以根据需要调整WebP质量
                };

                outputImage.Save(resultStream, encoder);
            }
            else
            {
                // png
                outputImage.SaveAsPng(resultStream);
            }

            // 清理资源
            for (int i = 0; i < 4; i++)
            {
                images[i]?.Dispose();
            }

            // 上传到存储服务
            resultStream.Position = 0; // 重置流位置

            var path = $"attachments/merges/{DateTime.UtcNow:yyyy/MM/dd}/{filename}";

            var imageUrl = StorageHelper.SaveFileV2Async(resultStream, filename, $"image/{suffix}", path, storageType, aliyunOss, tencentCos, cloudflareR2, s3Storage, localStorage);

            return imageUrl;
        }

        /// <summary>
        /// 从URL下载WebP图片并保存到云存储 - 简单存储
        /// </summary>
        /// <param name="url"></param>
        /// <param name="storageType"></param>
        /// <param name="aliyunOss"></param>
        /// <param name="tencentCos"></param>
        /// <param name="cloudflareR2"></param>
        /// <param name="s3Storage"></param>
        /// <param name="localStorage"></param>
        /// <param name="youChuanInternalDownload"></param>
        /// <returns></returns>
        public static async Task<string> DownloadWebpImagesFromUrlSimpleAsync(string url,
            ImageStorageType storageType,
            AliyunOssOptions aliyunOss,
            TencentCosOptions tencentCos,
            CloudflareR2Options cloudflareR2,
            S3StorageOptions s3Storage,
            LocalStorageOptions localStorage,
            bool youChuanInternalDownload)
        {
            if (storageType == ImageStorageType.NONE)
            {
                return url;
            }

            var suffix = Path.GetFileName(url)?.Split('.').LastOrDefault()?.ToLower() ?? "webp";

            var bytes = await DownloadImageAsync(_httpClient, url, youChuanInternalDownload);

            //using var resultStream = new MemoryStream();
            //if (suffix == "webp")
            //{
            //    // 保存为WebP
            //    var encoder = new WebpEncoder
            //    {
            //        Quality = 90 // 75 // 可以根据需要调整WebP质量
            //    };
            //    resultStream.Write(bytes, 0, bytes.Length);
            //}
            //else
            //{
            //    // png
            //    using var image = Image.Load(bytes);
            //    image.SaveAsPng(resultStream);
            //}
            //// 上传到存储服务
            //resultStream.Position = 0; // 重置流位置

            //var path = $"attachments/merges/{DateTime.UtcNow:yyyy/MM/dd}/{filename}";
            // 保持原路径和文件名
            var uri = new Uri(url);
            var path = uri.AbsolutePath.TrimStart('/'); // 去掉开头的斜杠
            // 如果开头不是 attachments 则添加
            if (!path.StartsWith("attachments/", StringComparison.OrdinalIgnoreCase))
            {
                path = $"attachments/{path}";
            }
            var filename = Path.GetFileName(path);

            using var resultStream = new MemoryStream(bytes);
            var imageUrl = StorageHelper.SaveFileV2Async(resultStream, filename, $"image/{suffix}",
                path, storageType, aliyunOss, tencentCos, cloudflareR2, s3Storage, localStorage);

            return imageUrl;
        }

        /// <summary>
        /// 从URL下载图片
        /// </summary>
        public static async Task<byte[]> DownloadImageAsync(HttpClient client, string url, bool youChuanInternalDownload)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return [];
            }

            // 允许重试
            var retryCount = 3;
            var retryDelay = TimeSpan.FromSeconds(2);
            do
            {
                try
                {
                    url = ReplaceInternalUrl(url, youChuanInternalDownload);

                    return await client.GetByteArrayAsync(url) ?? [];
                }
                catch (Exception) when (retryCount > 0)
                {
                    //WebProxy webProxy = null;
                    //var proxy = GlobalConfiguration.Setting.Proxy;
                    //if (!string.IsNullOrEmpty(proxy?.Host))
                    //{
                    //    webProxy = new WebProxy(proxy.Host, proxy.Port ?? 80);
                    //}
                    //var hch = new HttpClientHandler
                    //{
                    //    UseProxy = webProxy != null,
                    //    Proxy = webProxy
                    //};

                    client = new HttpClient
                    {
                        Timeout = TimeSpan.FromMinutes(10)
                    };

                    retryCount--;
                    await Task.Delay(retryDelay);
                }
            } while (retryCount > 0);

            return [];
        }

        /// <summary>
        /// 替换外网下载的URL为内网URL
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        public static string ReplaceInternalUrl(string url, bool youChuanInternalDownload)
        {
            if (!string.IsNullOrWhiteSpace(url) && youChuanInternalDownload)
            {
                if (url.Contains(TaskInfo.YOUCHUAN_CDN, StringComparison.OrdinalIgnoreCase))
                {
                    return url.Replace(TaskInfo.YOUCHUAN_CDN, TaskInfo.YOUCHUAN_CDN_INTERNAL);
                }
            }
            return url;
        }
    }
}