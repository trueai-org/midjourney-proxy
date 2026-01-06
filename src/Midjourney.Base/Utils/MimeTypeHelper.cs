using Microsoft.AspNetCore.StaticFiles;

namespace Midjourney.Base.Util
{
    /// <summary>
    /// MIME 类型辅助类 - v20260106
    /// </summary>
    public static class MimeTypeHelper
    {
        /// <summary>
        /// 使用懒加载的静态实例，线程安全且只初始化一次
        /// </summary>
        private static readonly Lazy<FileExtensionContentTypeProvider> _lazyProvider =
            new(() => new FileExtensionContentTypeProvider());

        /// <summary>
        /// 获取 FileExtensionContentTypeProvider 的单例实例
        /// </summary>
        private static FileExtensionContentTypeProvider Provider => _lazyProvider.Value;

        /// <summary>
        /// 通过文件名获取 MIME 类型
        /// </summary>
        /// <param name="fileName">文件名（可以包含路径）</param>
        /// <param name="mimeType">输出的 MIME 类型</param>
        /// <returns>是否找到对应的 MIME 类型</returns>
        public static bool TryGetMimeType(string fileName, out string mimeType)
        {
            return Provider.TryGetContentType(fileName, out mimeType);
        }

        /// <summary>
        /// 通过文件名获取 MIME 类型，如果找不到返回默认值
        /// </summary>
        /// <param name="fileName">文件名</param>
        /// <param name="defaultMimeType">默认 MIME 类型，默认为 "application/octet-stream"</param>
        /// <returns>MIME 类型</returns>
        public static string GetMimeType(string fileName, string defaultMimeType = "application/octet-stream")
        {
            // 方法1
            //var mime = MimeKit.MimeTypes.GetMimeType(fileName);
            //if (string.IsNullOrWhiteSpace(mime))
            //{
            //    mime = defaultMimeType;
            //}

            // 方法2
            return Provider.TryGetContentType(fileName, out string mimeType)
                ? mimeType
                : defaultMimeType;
        }

        /// <summary>
        /// 通过 MIME 类型获取文件扩展名（反向查找）
        /// </summary>
        /// <param name="mimeType">MIME 类型</param>
        /// <returns>文件扩展名，如果找不到返回 null</returns>
        public static string GetExtension(string mimeType)
        {
            return Provider.Mappings
                .FirstOrDefault(x => x.Value.Equals(mimeType, StringComparison.OrdinalIgnoreCase))
                .Key;
        }

        /// <summary>
        /// 通过 MIME 类型获取所有可能的文件扩展名
        /// </summary>
        /// <param name="mimeType">MIME 类型</param>
        /// <returns>所有匹配的扩展名列表</returns>
        public static IEnumerable<string> GetAllExtensions(string mimeType)
        {
            return Provider.Mappings
                .Where(x => x.Value.Equals(mimeType, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Key);
        }

        /// <summary>
        /// 判断文件是否为图片类型
        /// </summary>
        public static bool IsImage(string fileName)
        {
            return TryGetMimeType(fileName, out string mimeType)
                && mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 判断文件是否为视频类型
        /// </summary>
        public static bool IsVideo(string fileName)
        {
            return TryGetMimeType(fileName, out string mimeType)
                && mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 判断文件是否为音频类型
        /// </summary>
        public static bool IsAudio(string fileName)
        {
            return TryGetMimeType(fileName, out string mimeType)
                && mimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 判断文件是否为文本类型
        /// </summary>
        public static bool IsText(string fileName)
        {
            return TryGetMimeType(fileName, out string mimeType)
                && mimeType.StartsWith("text/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 获取所有支持的 MIME 类型映射
        /// </summary>
        public static IDictionary<string, string> GetAllMappings()
        {
            return Provider.Mappings;
        }
    }
}