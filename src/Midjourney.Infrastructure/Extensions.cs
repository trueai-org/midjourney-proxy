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

using LiteDB;
using Midjourney.Infrastructure.Data;
using MongoDB.Driver.Linq;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using System.Net;
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
        /// string 转 int
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static int ToInt(this string value)
        {
            if (!string.IsNullOrWhiteSpace(value) && int.TryParse(value?.Trim(), out int v))
            {
                return v;
            }
            return default;
        }

        /// <summary>
        /// string 转 long
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static long ToInt64(this string value)
        {
            if (!string.IsNullOrWhiteSpace(value) && long.TryParse(value?.Trim(), out long v))
            {
                return v;
            }
            return default;
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

            return Regex.Replace(str, @"<[^>]*>|https?://\S+|\s+|\p{P}", "").ToLower();
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
        /// 格式化只保留纯文本和链接（移除 -- 参数）
        /// </summary>
        /// <param name="prompt"></param>
        /// <returns></returns>
        public static string FormatPromptParam(this string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return prompt;
            }

            // 移除 <url> , 例如: <https://www.baidu.com> a cute girl -> <https://www.baidu.com>acutegirl
            // 移除 url, 例如: https://www.baiud.com a cute girl ->  https://www.baiud.comacutegirl
            // 移除空白字符, 例如: a cute girl -> acutegirl

            // 修复 -> v6.0 问题
            // Interactiveinstallations,textlayout,interestingshapes,children.--ar1:1--v6.0--iw2
            // Interactiveinstallations,textlayout,interestingshapes,children.--ar1: 1--v6--iw2

            // 去除 -- 开头的参数
            prompt = Regex.Replace(prompt, @"\x20+--[a-z]+.*$", string.Empty, RegexOptions.IgnoreCase);


            // 替换多余的 <<link>> 为 <link>
            // 针对 " -- " discord 会返回为空
            prompt = prompt.Replace(" -- ", " ").Replace("  ", " ");
            return Regex.Replace(prompt, @"\s+|\p{P}", "").ToLower();
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
        /// 根据多个条件动态添加查询条件，并支持排序和限制返回数量。
        /// </summary>
        /// <typeparam name="T">实体类型。</typeparam>
        /// <param name="dataHelper">数据助手接口。</param>
        /// <param name="orderBy">排序字段表达式。</param>
        /// <param name="orderByAsc">是否升序排序。</param>
        /// <param name="limit">返回的最大记录数。</param>
        /// <param name="filters">一组条件表达式及其对应的布尔值。</param>
        /// <returns>满足条件的实体列表。</returns>
        public static List<T> WhereIf<T>(this IDataHelper<T> dataHelper, params (bool condition, Expression<Func<T, bool>> filter)[] filters) where T : IBaseId
        {
            // 获取所有数据的初始查询
            var query = dataHelper.GetAll().AsQueryable();

            // 动态应用条件
            foreach (var (condition, filter) in filters)
            {
                if (condition)
                {
                    query = query.Where(filter);
                }
            }

            //// 应用排序
            //query = orderByAsc ? query.OrderBy(orderBy) : query.OrderByDescending(orderBy);

            //// 应用限制
            //if (limit > 0)
            //{
            //    query = query.Take(limit);
            //}

            return query.ToList();
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
        /// MongoDB 查询条件扩展方法。
        /// 根据条件动态添加查询条件。
        /// </summary>
        /// <typeparam name="T">实体类型。</typeparam>
        /// <param name="query">MongoDB 可查询对象。</param>
        /// <param name="condition">条件布尔值，决定是否添加查询条件。</param>
        /// <param name="predicate">要添加的查询条件表达式。</param>
        /// <returns>带有可选条件的查询对象。</returns>
        public static IMongoQueryable<T> WhereIf<T>(this IMongoQueryable<T> query, bool condition, Expression<Func<T, bool>> predicate)
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
        public static IQueryable<T> WhereIf<T>(this IQueryable<T> query, bool condition, Expression<Func<T, bool>> predicate)
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

        /// <summary>
        /// 时间段输入解析
        /// 格式为 "HH:mm-HH:mm, HH:mm-HH:mm, ..."，例如 "09:00-17:00, 18:00-22:00"
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static List<TimeSlot> ToTimeSlots(this string input)
        {
            var timeSlots = new List<TimeSlot>();
            var slots = input.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var slot in slots)
            {
                var times = slot.Trim().Split('-');
                if (times.Length == 2 && TimeSpan.TryParse(times[0], out var start) && TimeSpan.TryParse(times[1], out var end))
                {
                    timeSlots.Add(new TimeSlot { Start = start, End = end });
                }
            }

            return timeSlots;
        }

        /// <summary>
        /// 判断是否在工作时间内（如果没有值，则默认：true）
        /// </summary>
        /// <param name="dateTime"></param>
        /// <param name="input"></param>
        /// <returns></returns>
        public static bool IsInWorkTime(this DateTime dateTime, string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return true;
            }

            var currentTime = dateTime.TimeOfDay;
            var ts = input.ToTimeSlots();
            foreach (var slot in ts)
            {
                if (slot.Start <= slot.End)
                {
                    // 正常时间段：例如 09:00-17:00
                    if (currentTime >= slot.Start && currentTime <= slot.End)
                    {
                        return true;
                    }
                }
                else
                {
                    // 跨越午夜的时间段：例如 23:00-02:00
                    if (currentTime >= slot.Start || currentTime <= slot.End)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 判断是否处于摸鱼时间（如果没有值，则默认：false）
        /// </summary>
        /// <param name="dateTime"></param>
        /// <param name="input"></param>
        /// <returns></returns>
        public static bool IsInFishTime(this DateTime dateTime, string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            var currentTime = dateTime.TimeOfDay;

            var ts = input.ToTimeSlots();
            foreach (var slot in ts)
            {
                if (slot.Start <= slot.End)
                {
                    // 正常时间段：例如 09:00-17:00
                    if (currentTime >= slot.Start && currentTime <= slot.End)
                    {
                        return true;
                    }
                }
                else
                {
                    // 跨越午夜的时间段：例如 23:00-02:00
                    if (currentTime >= slot.Start || currentTime <= slot.End)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 排序条件扩展
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="query"></param>
        /// <param name="where"></param>
        /// <param name="keySelector"></param>
        /// <param name="desc"></param>
        /// <returns></returns>
        public static ILiteQueryable<T> OrderByIf<T>(this ILiteQueryable<T> query, bool where, Expression<Func<T, object>> keySelector, bool desc = true)
        {
            if (desc)
            {
                return where ? query.OrderByDescending(keySelector) : query;
            }
            else
            {
                return where ? query.OrderBy(keySelector) : query;
            }
        }

        /// <summary>
        /// URL 添加处理样式
        /// </summary>
        /// <param name="url"></param>
        /// <param name="style"></param>
        /// <returns></returns>
        public static string ToStyle(this string url, string style)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return url;
            }

            url = WebUtility.HtmlDecode(url);

            if (string.IsNullOrWhiteSpace(style))
            {
                return url;
            }

            if (url.IndexOf('?') > 0)
            {
                return url + "&" + style;
            }

            return url + "?" + style;
        }
    }

    /// <summary>
    /// 时间段解析
    /// </summary>
    public class TimeSlot
    {
        /// <summary>
        /// 当天开始时间
        /// </summary>
        public TimeSpan Start { get; set; }

        /// <summary>
        /// 当天结束时间
        /// </summary>
        public TimeSpan End { get; set; }
    }
}