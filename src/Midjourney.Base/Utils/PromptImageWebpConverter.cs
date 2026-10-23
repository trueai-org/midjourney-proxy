using Midjourney.Base.Models;
using Serilog;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace Midjourney.Base.Util
{
    /// <summary>
    /// 提示词/垫图图片 WebP 转换器
    /// </summary>
    public static class PromptImageWebpConverter
    {
        public const string WebpMimeType = "image/webp";

        public const string WebpExtension = ".webp";

        private const int WebpQuality = 90;

        /// <summary>
        /// 未配置忽略格式时的默认值，避免动图丢失动画
        /// </summary>
        private static readonly List<string> DefaultIgnoreFormats = [".gif"];

        /// <summary>
        /// 将 DataUrl 中的图片转换为 WebP。
        /// 未开启配置、已忽略格式、已是 WebP、未知或无法解码的图片保持原样。
        /// </summary>
        /// <param name="dataUrl"></param>
        /// <param name="setting"></param>
        /// <returns>是否发生转换</returns>
        public static bool TryConvertToWebP(DataUrl dataUrl, Setting setting = null)
        {
            if (dataUrl?.Data == null || dataUrl.Data.Length == 0)
            {
                return false;
            }

            setting ??= GlobalConfiguration.Setting;
            if (setting?.EnablePromptImageWebp != true)
            {
                return false;
            }

            if (!TryConvertBytes(dataUrl.Data, dataUrl.MimeType, null, setting, out var bytes, out var mimeType, out _))
            {
                return false;
            }

            dataUrl.Data = bytes;
            dataUrl.MimeType = mimeType;

            return true;
        }

        /// <summary>
        /// 将图片字节数组转换为 WebP。
        /// 未开启配置、已忽略格式、已是 WebP、未知或无法解码的图片保持原样。
        /// </summary>
        public static bool TryConvertToWebP(
            byte[] source,
            string mimeType,
            string fileName,
            Setting setting,
            out byte[] data,
            out string convertedMimeType,
            out string convertedFileName)
        {
            data = source;
            convertedMimeType = mimeType;
            convertedFileName = fileName;

            setting ??= GlobalConfiguration.Setting;
            if (setting?.EnablePromptImageWebp != true || source == null || source.Length == 0)
            {
                return false;
            }

            if (!TryConvertBytes(source, mimeType, fileName, setting, out var bytes, out var mime, out var name))
            {
                return false;
            }

            data = bytes;
            convertedMimeType = mime;
            convertedFileName = name;

            return true;
        }

        private static bool TryConvertBytes(
            byte[] source,
            string mimeType,
            string fileName,
            Setting setting,
            out byte[] data,
            out string convertedMimeType,
            out string convertedFileName)
        {
            data = source;
            convertedMimeType = mimeType;
            convertedFileName = fileName;

            IImageFormat format;
            try
            {
                format = Image.DetectFormat(source);
            }
            catch (UnknownImageFormatException)
            {
                Log.Debug("无法识别图片格式，保留原图，不转换为 WebP");
                return false;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "检测图片格式失败，保留原图，不转换为 WebP");
                return false;
            }

            if (format == null)
            {
                Log.Debug("无法识别图片格式，保留原图，不转换为 WebP");
                return false;
            }

            // 已经是 WebP，无需重复编码
            if (string.Equals(format.Name, "WEBP", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (IsIgnoredFormat(format, setting.PromptImageWebpIgnoreFormats ?? DefaultIgnoreFormats))
            {
                return false;
            }

            try
            {
                using var image = Image.Load(source);

                // 多帧图片（GIF/APNG 等）转换后可能丢失动画，保持原图
                if (image.Frames.Count > 1)
                {
                    Log.Debug("多帧图片不转换为 WebP，避免丢失动画");
                    return false;
                }

                // 按 EXIF 方向摆正像素，避免转换后引用图方向错误
                image.Mutate(x => x.AutoOrient());

                using var stream = new MemoryStream();
                image.Save(stream, new WebpEncoder
                {
                    Quality = WebpQuality,
                    FileFormat = WebpFileFormatType.Lossy,
                });

                var bytes = stream.ToArray();
                if (bytes.Length == 0)
                {
                    return false;
                }

                data = bytes;
                convertedMimeType = WebpMimeType;
                convertedFileName = string.IsNullOrWhiteSpace(fileName)
                    ? fileName
                    : Path.ChangeExtension(fileName, WebpExtension);

                return true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "图片转换为 WebP 失败，保留原格式");
                return false;
            }
        }

        private static bool IsIgnoredFormat(IImageFormat format, IEnumerable<string> ignoreFormats)
        {
            if (ignoreFormats == null)
            {
                return false;
            }

            var formats = ignoreFormats
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(NormalizeFormat)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (formats.Count == 0)
            {
                return false;
            }

            if (formats.Contains(NormalizeFormat(format.Name)))
            {
                return true;
            }

            if (format.FileExtensions?.Any(x => formats.Contains(NormalizeFormat(x))) == true)
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(format.DefaultMimeType)
                && formats.Contains(format.DefaultMimeType.Trim().ToLowerInvariant());
        }

        private static string NormalizeFormat(string value)
        {
            value = value.Trim().ToLowerInvariant();
            if (value.Contains('/') || value.StartsWith('.'))
            {
                return value;
            }

            return "." + value;
        }
    }
}
