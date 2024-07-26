using LiteDB;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using System.Text.RegularExpressions;

namespace Midjourney.Infrastructure
{
    public static class Extensions
    {
        private static readonly char[] PathSeparator = ['/'];

        /// <summary>
        /// 移除路径首尾 ' ', '/', '\'
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static string TrimPath(this string path)
        {
            return path?.Trim().Trim('/').Trim('\\').Trim('/').Trim();
        }

        /// <summary>
        /// 移除空白字符、url 等，只保留参数的 prompt 用于比较
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static string FormatPrompt(this string str)
        {
            if (str == null)
                throw new ArgumentNullException(nameof(str));

            // 移除 <url> , 例如: <https://www.baidu.com> a cute girl -> acutegirl
            // 移除 url, 例如: https://www.baiud.com a cute girl -> acutegirl
            // 移除空白字符, 例如: a cute girl -> acutegirl

            // 修复 -> v6.0 问题
            // Interactiveinstallations,textlayout,interestingshapes,children.--ar1:1--v6.0--iw2
            // Interactiveinstallations,textlayout,interestingshapes,children.--ar1: 1--v6--iw2

            str = GetPrimaryPrompt(str);

            return Regex.Replace(str, @"<[^>]*>|https?://\S+|\s+", "").ToLower();
        }

        /// <summary>
        /// 获取格式化之后的 prompt 用于比较
        /// </summary>
        /// <param name="prompt"></param>
        /// <returns></returns>
        private static string GetPrimaryPrompt(string prompt)
        {
            // 去除 -- 开头的参数
            prompt = Regex.Replace(prompt, @"\x20+--[a-z]+.*$", string.Empty, RegexOptions.IgnoreCase);

            // 匹配并替换 URL
            string regex = @"https?://[-a-zA-Z0-9+&@#/%?=~_|!:,.;]*[-a-zA-Z0-9+&@#/%=~_|]";
            prompt = Regex.Replace(prompt, regex, "<link>");

            // 替换多余的 <<link>> 为 <link>
            // 针对 " -- " discord 会返回为空
            return prompt.Replace("<<link>>", "<link>")
                .Replace(" -- ", " ")
                .Replace("  ", " ");
        }

        /// <summary>
        /// 转为 url 路径
        /// 例如：由 E:\_backups\p00\3e4 -> _backups/p00/3e4
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static string ToUrlPath(this string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            // 替换所有的反斜杠为斜杠
            // 分割路径，移除空字符串，然后重新连接
            return string.Join("/", path.Replace("\\", "/").Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries)).TrimPath();
        }

        /// <summary>
        /// 将完整路径分解为子路径列表
        /// 例如：/a/b/c -> [a, b, c]
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static string[] ToSubPaths(this string path)
        {
            return path?.ToUrlPath().Split(PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        }

        /// <summary>
        /// 转为 url 路径
        /// 例如：由 E:\_backups\p00\3e4 -> _backups/p00/3e4
        /// </summary>
        /// <param name="path"></param>
        /// <param name="removePrefix">移除的前缀</param>
        /// <returns></returns>
        public static string TrimPrefix(this string path, string removePrefix = "")
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            if (!string.IsNullOrWhiteSpace(removePrefix))
            {
                if (path.StartsWith(removePrefix))
                {
                    path = path.Substring(removePrefix.Length);
                }
            }

            // 替换所有的反斜杠为斜杠
            // 分割路径，移除空字符串，然后重新连接
            return string.Join("/", path.Replace("\\", "/").Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries)).TrimPath();
        }

        /// <summary>
        /// 移除指路径的后缀
        /// </summary>
        /// <param name="path"></param>
        /// <param name="removeSuffix"></param>
        /// <returns></returns>
        public static string TrimSuffix(this string path, string removeSuffix = "")
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            if (!string.IsNullOrWhiteSpace(removeSuffix))
            {
                if (path.EndsWith(removeSuffix))
                {
                    path = path.Substring(0, path.Length - removeSuffix.Length);
                }
            }

            return path;
        }

        /// <summary>
        /// 获取枚举描述或名称
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static string GetDescription(this Enum value)
        {
            if (value == null)
            {
                return null;
            }
            var type = value.GetType();
            var displayName = Enum.GetName(type, value);
            var fieldInfo = type.GetField(displayName);
            var attributes = (DisplayAttribute[])fieldInfo?.GetCustomAttributes(typeof(DisplayAttribute), false);
            if (attributes?.Length > 0)
            {
                displayName = attributes[0].Description ?? attributes[0].Name;
            }
            else
            {
                var desAttributes = (DescriptionAttribute[])fieldInfo?.GetCustomAttributes(typeof(DescriptionAttribute), false);
                if (desAttributes?.Length > 0)
                    displayName = desAttributes[0].Description;
            }
            return displayName;
        }

        /// <summary>
        /// 计算数据流的哈希值并返回十六进制字符串
        /// </summary>
        /// <param name="hash"></param>
        /// <returns></returns>
        public static string ToHex(this byte[] hash)
        {
            return BitConverter.ToString(hash).Replace("-", "").ToLower();
        }

        /// <summary>
        /// 格式化文件大小
        /// </summary>
        /// <param name="size"></param>
        /// <returns></returns>
        public static string ToFileSizeString(this double size)
        {
            //size switch
            //{
            //    var s when s >= 1024 * 1024 * 1024 => $"{s / 1024 / 1024 / 1024:F2} GB/s",
            //    var s when s >= 1024 * 1024 => $"{s / 1024 / 1024:F2} MB/s",
            //    var s when s >= 1024 => $"{s / 1024:F2} KB/s",
            //    var s => $"{s:F2} B/s"
            //};

            if (size >= 1024 * 1024 * 1024)
            {
                return $"{size / 1024 / 1024 / 1024:F2} GB";
            }
            else if (size >= 1024 * 1024)
            {
                return $"{size / 1024 / 1024:F2} MB";
            }
            else if (size >= 1024)
            {
                return $"{size / 1024:F2} KB";
            }
            else
            {
                return $"{size:F2} B";
            }
        }


        /// <summary>
        /// 查询条件扩展
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="query"></param>
        /// <param name="condition"></param>
        /// <param name="predicate"></param>
        /// <returns></returns>
        public static IEnumerable<T> WhereIf<T>(this IEnumerable<T> query, bool condition, Func<T, bool> predicate)
        {
            return condition ? query.Where(predicate) : query;
        }

        /// <summary>
        /// Lite DB 查询条件扩展
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="query"></param>
        /// <param name="condition"></param>
        /// <param name="predicate"></param>
        /// <returns></returns>
        public static ILiteQueryable<T> WhereIf<T>(this ILiteQueryable<T> query, bool condition, Expression<Func<T, bool>> predicate)
        {
            return condition ? query.Where(predicate) : query;
        }

        /// <summary>
        /// 查询条件扩展
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="query"></param>
        /// <param name="condition"></param>
        /// <param name="predicate"></param>
        /// <returns></returns>
        public static IEnumerable<T> WhereIf<T>(this IEnumerable<T> query, bool condition, Func<T, int, bool> predicate)
        {
            return condition ? query.Where(predicate) : query;
        }

        /// <summary>
        /// 转为可视化时间
        /// </summary>
        /// <param name="timestamp"></param>
        /// <returns></returns>
        public static string ToDateTimeString(this long timestamp)
        {
            return timestamp > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(timestamp).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "";
        }

        /// <summary>
        /// String to long time unix
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static long ToLong(this string value)
        {
            return long.TryParse(value, out long result) ? result : 0;
        }
    }
}