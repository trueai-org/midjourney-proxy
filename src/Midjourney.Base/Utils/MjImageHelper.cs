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
using SixLabors.ImageSharp;
using SkiaSharp;

namespace Midjourney.Base.Util
{
    /// <summary>
    /// Midjourney 图片辅助类
    /// </summary>
    public static class MjImageHelper
    {
        /// <summary>
        /// 定义 Midjourney 支持的图片格式名单
        /// </summary>
        public static readonly HashSet<string> AllowedImageFormats = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png",
            ".gif",
            ".webp",
            ".jpeg",
            ".jpg",

            // 兼容换脸/视频额外支持 mp4
            ".mp4"
        };

        /// <summary>
        /// 猜测文件后缀，如果不在白名单则返回 null
        /// </summary>
        /// <param name="mimeType"></param>
        /// <returns> .png | allow | null</returns>
        private static string GuessFileSuffixOrNull(string mimeType)
        {
            if (string.IsNullOrWhiteSpace(mimeType))
            {
                return null;
            }

            var exts = MimeTypeHelper.GetAllExtensions(mimeType).ToList();

            // 获取与白名单交集的后缀
            if (exts?.Count > 0)
            {
                var matchedExts = exts.Intersect(AllowedImageFormats, StringComparer.OrdinalIgnoreCase).ToList();
                if (matchedExts.Count == 1)
                {
                    return matchedExts.First();
                }
                else if (matchedExts.Count > 1)
                {
                    // 多个匹配时优先返回 jpg，其次 png，其次其他
                    if (matchedExts.Contains(".jpg", StringComparer.OrdinalIgnoreCase))
                    {
                        return ".jpg";
                    }
                    else if (matchedExts.Contains(".png", StringComparer.OrdinalIgnoreCase))
                    {
                        return ".png";
                    }
                    else
                    {
                        return matchedExts.First();
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 猜测文件后缀并处理图片，如果处理失败则最终返回 .jpg
        /// </summary>
        /// <param name="dataUrl"></param>
        /// <returns> .jpg | allow </returns>
        /// <param name="defaultExt">未识别到时的默认后缀</param>
        public static async Task<string> GuessFileSuffix(DataUrl dataUrl, string defaultExt = ".jpg")
        {
            if (dataUrl == null)
            {
                throw new ArgumentNullException(nameof(dataUrl));
            }

            // 如果有 contentType 则优先使用
            if (!string.IsNullOrWhiteSpace(dataUrl.MimeType))
            {
                var ext = GuessFileSuffixOrNull(dataUrl.MimeType);
                if (!string.IsNullOrWhiteSpace(ext))
                {
                    return ext;
                }
            }

            // 根据图片内容分析
            var inputBytes = dataUrl?.Data ?? [];
            if (inputBytes.Length > 0)
            {
                // 如果图片超过 20MB 则强制转为 <= 2048 x 2048 的 JPEG 90
                var fileLimit = 20 * 1024 * 1024;
                int targetSize = 2024;

                if (inputBytes.Length > fileLimit)
                {
                    using var bitmap = SKBitmap.Decode(inputBytes);
                    if (bitmap != null)
                    {
                        var width = bitmap.Width;
                        var height = bitmap.Height;

                        if (width > targetSize || height > targetSize)
                        {
                            // 计算缩放比例
                            var scale = Math.Min(targetSize / (float)width, targetSize / (float)height);
                            if (scale < 1.0)
                            {
                                width = (int)(width * scale);
                                height = (int)(height * scale);
                            }

                            // 调整大小并压缩
                            var newImageInfo = new SKImageInfo(width, height);
                            var samplingOptions = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);

                            using var resizedBitmap = bitmap.Resize(newImageInfo, samplingOptions);
                            if (resizedBitmap != null)
                            {
                                using var image = SKImage.FromBitmap(resizedBitmap);
                                using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);

                                dataUrl.Data = data.ToArray();
                                dataUrl.MimeType = "image/jpeg";

                                return ".jpg";
                            }
                        }
                        else
                        {
                            // 不需要调整大小，直接压缩
                            using var image = SKImage.FromBitmap(bitmap);
                            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);

                            dataUrl.Data = data.ToArray();
                            dataUrl.MimeType = "image/jpeg";

                            return ".jpg";
                        }
                    }
                }

                var imageInfo = Image.Identify(inputBytes);
                if (imageInfo != null)
                {
                    var ext = GuessFileSuffixOrNull(imageInfo.Metadata.DecodedImageFormat.DefaultMimeType);
                    if (!string.IsNullOrWhiteSpace(ext))
                    {
                        return ext;
                    }

                    // 未命中说明格式不支持，转为 jpeg 90
                    using var bitmap = SKBitmap.Decode(inputBytes);
                    if (bitmap != null)
                    {
                        using var image = SKImage.FromBitmap(bitmap);
                        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);

                        dataUrl.Data = data.ToArray();
                        dataUrl.MimeType = "image/jpeg";

                        return ".jpg";
                    }
                }
            }

            // 根据 MIME 类型分析
            if (!string.IsNullOrWhiteSpace(dataUrl.MimeType))
            {
                var ext = GuessFileSuffixOrNull(dataUrl.MimeType);
                if (!string.IsNullOrWhiteSpace(ext))
                {
                    return ext;
                }
            }

            // 根据 URL 分析
            var url = dataUrl.Url;
            if (!string.IsNullOrWhiteSpace(url))
            {
                // 根据请求头返回的 ContentType 分析扩展名
                var contentType = await MjImageFetchHelper.GetContentTypeAsync(url);
                var ext = GuessFileSuffixOrNull(contentType);
                if (!string.IsNullOrWhiteSpace(ext))
                {
                    return ext;
                }

                // 根据 uri 猜测扩展名
                var uri = new Uri(url);
                if (uri.Segments.Length > 0)
                {
                    var lastSegment = uri.Segments.Last();
                    ext = Path.GetExtension(lastSegment).ToLowerInvariant();
                    if (AllowedImageFormats.Contains(ext))
                    {
                        return ext;
                    }
                }
            }

            Log.Warning($"Failed to guess file suffix for DataUrl, defaulting to {defaultExt}");

            return defaultExt;
        }
    }
}