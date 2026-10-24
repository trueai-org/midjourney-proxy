using System.Text.Json;
using Midjourney.Base.Data;
using Newtonsoft.Json.Linq;

namespace Midjourney.Base.Models
{
    /// <summary>
    /// imagine 异步垫图暂存引用。
    /// 只保存元数据，图片原始字节保存在 Redis 的 Key 中。
    /// </summary>
    public class ImagineInputRef
    {
        /// <summary>
        /// Redis 暂存 Key 前缀
        /// </summary>
        public const string RedisKeyPrefix = "mj:imagine:input:";

        /// <summary>
        /// Redis 暂存 Key
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// 图片 URL（URL-only 输入时使用，不占 Redis）
        /// </summary>
        public string Url { get; set; }

        /// <summary>
        /// 图片 MIME 类型
        /// </summary>
        public string MimeType { get; set; }

        /// <summary>
        /// 提交时的图片顺序，从 0 开始
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// 图片字节数
        /// </summary>
        public int Size { get; set; }

        /// <summary>
        /// 构造暂存 Key
        /// </summary>
        public static string BuildKey(string taskId, int index)
        {
            return $"{RedisKeyPrefix}{taskId}:{index}";
        }

        /// <summary>
        /// 视频输入占位符。用于把首帧/尾帧 URL 的解析推迟到工作线程。
        /// </summary>
        public static string BuildVideoInputToken(string taskId, int index)
        {
            return $"__mj_video_input_{index}_{taskId}__";
        }

        /// <summary>
        /// 是否为仅 URL 的图片（没有图片字节内容）
        /// </summary>
        public static bool IsUrlOnly(DataUrl dataUrl)
        {
            return dataUrl != null
                && !string.IsNullOrWhiteSpace(dataUrl.Url)
                && (dataUrl.Data == null || dataUrl.Data.Length == 0);
        }

        /// <summary>
        /// 暂存数据是否缺失或已过期
        /// </summary>
        public static bool IsMissing(byte[] data)
        {
            return data == null || data.Length == 0;
        }

        /// <summary>
        /// 收集需要清理的暂存 Key
        /// </summary>
        public static string[] CollectKeys(IEnumerable<ImagineInputRef> refs)
        {
            return refs?
                .Where(x => !string.IsNullOrWhiteSpace(x?.Key))
                .Select(x => x.Key)
                .ToArray() ?? [];
        }

        /// <summary>
        /// 序列化为 JSON，写入 TaskInfo.Properties
        /// </summary>
        public static string Serialize(IEnumerable<ImagineInputRef> refs)
        {
            return (refs?.ToList() ?? []).ToJson();
        }

        /// <summary>
        /// 从 TaskInfo.Properties 读取引用列表，兼容 JSON 字符串、JArray、强类型列表
        /// </summary>
        public static List<ImagineInputRef> Parse(object value)
        {
            if (value == null)
            {
                return [];
            }

            if (value is List<ImagineInputRef> list)
            {
                return list;
            }

            if (value is IEnumerable<ImagineInputRef> refs)
            {
                return refs.ToList();
            }

            try
            {
                if (value is JValue jValue)
                {
                    value = jValue.Value;
                }

                if (value is string json && !string.IsNullOrWhiteSpace(json))
                {
                    return json.ToObject<List<ImagineInputRef>>() ?? [];
                }

                if (value is JArray array)
                {
                    return array.ToObject<List<ImagineInputRef>>() ?? [];
                }

                if (value is JsonElement element)
                {
                    if (element.ValueKind == JsonValueKind.String)
                    {
                        return Parse(element.GetString());
                    }

                    if (element.ValueKind == JsonValueKind.Array)
                    {
                        return element.Deserialize<List<ImagineInputRef>>() ?? [];
                    }
                }
            }
            catch
            {
                // 元数据损坏时按空列表处理，由调用方决定是否失败
            }

            return [];
        }

        /// <summary>
        /// 读取已准备的图片链接列表，兼容 JSON 字符串、JArray、JsonElement 和集合。
        /// 用于避免工作线程对已上传的垫图再次执行 URL 转换。
        /// </summary>
        public static HashSet<string> ParseUrls(object value)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (value is JValue jValue)
                {
                    value = jValue.Value;
                }

                IEnumerable<string> items = null;
                if (value is string text && !string.IsNullOrWhiteSpace(text))
                {
                    try
                    {
                        items = text.ToObject<List<string>>();
                    }
                    catch
                    {
                        items = [text];
                    }
                }
                else if (value is JArray array)
                {
                    items = array.ToObject<List<string>>();
                }
                else if (value is JsonElement element)
                {
                    if (element.ValueKind == JsonValueKind.String)
                    {
                        items = [element.GetString()];
                    }
                    else if (element.ValueKind == JsonValueKind.Array)
                    {
                        items = element.Deserialize<List<string>>();
                    }
                }
                else if (value is IEnumerable<string> list)
                {
                    items = list;
                }

                if (items != null)
                {
                    foreach (var item in items)
                    {
                        if (!string.IsNullOrWhiteSpace(item))
                        {
                            result.Add(item);
                        }
                    }
                }
            }
            catch
            {
                // 损坏的元数据按空集合处理
            }

            return result;
        }

        /// <summary>
        /// 解析任务属性中的布尔值，兼容 bool、字符串、JValue、JsonElement 和数字。
        /// </summary>
        public static bool GetBool(object value)
        {
            try
            {
                if (value is bool boolean)
                {
                    return boolean;
                }
                if (value is JValue jValue)
                {
                    return GetBool(jValue.Value);
                }
                if (value is JsonElement element)
                {
                    if (element.ValueKind == JsonValueKind.True) return true;
                    if (element.ValueKind == JsonValueKind.False) return false;
                    if (element.ValueKind == JsonValueKind.String && bool.TryParse(element.GetString(), out var parsed))
                    {
                        return parsed;
                    }
                    if (element.ValueKind == JsonValueKind.Number)
                    {
                        if (element.TryGetInt64(out var number)) return number != 0;
                    }
                    return false;
                }
                if (value is string text && bool.TryParse(text, out var parsedText))
                {
                    return parsedText;
                }
                if (value is int intValue) return intValue != 0;
                if (value is long longValue) return longValue != 0;
                return value != null && Convert.ToBoolean(value);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 解析任务属性中的字符串，兼容 string、JValue 和 JsonElement。
        /// </summary>
        public static string GetString(object value)
        {
            try
            {
                if (value == null) return null;
                if (value is string text) return text;
                if (value is JValue jValue) return jValue.Value?.ToString();
                if (value is JsonElement element)
                {
                    return element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString();
                }
                return value.ToString();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 任务是否已经完成垫图准备。
        /// </summary>
        public static bool IsPrepared(TaskInfo info)
        {
            return info != null
                && (GetBool(info.GetProperty(Constants.TASK_PROPERTY_IMAGINE_PREPARED))
                    || GetString(info.GetProperty(Constants.TASK_PROPERTY_IMAGINE_PREPARED_URLS)) != null);
        }

        /// <summary>
        /// 任务是否需要执行垫图准备。
        /// </summary>
        public static bool NeedsPrepare(TaskInfo info)
        {
            return info != null
                && (GetBool(info.GetProperty(Constants.TASK_PROPERTY_IMAGINE_NEED_PREPARE))
                    || info.GetProperty(Constants.TASK_PROPERTY_IMAGINE_INPUTS) != null);
        }

        /// <summary>
        /// URL 是否应跳过后续转换：空值、已命中替换规则、已准备或已转换的链接都跳过。
        /// </summary>
        public static bool ShouldSkipConversion(
            string url,
            ISet<string> replacedUrls,
            ISet<string> preparedUrls,
            bool alreadyConverted = false)
        {
            return string.IsNullOrWhiteSpace(url)
                || alreadyConverted
                || replacedUrls?.Contains(url) == true
                || preparedUrls?.Contains(url) == true;
        }

        /// <summary>
        /// 按提交时快照构造 WebP 转换配置
        /// </summary>
        public static Setting CreatePrepareSetting(bool enableWebp, IEnumerable<string> ignoreFormats)
        {
            return new Setting
            {
                EnablePromptImageWebp = enableWebp,
                PromptImageWebpIgnoreFormats = ignoreFormats?.ToList(),
            };
        }

    }
}