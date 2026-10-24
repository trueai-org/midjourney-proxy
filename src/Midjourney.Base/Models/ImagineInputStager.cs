using Midjourney.Base.Data;
using Serilog;

namespace Midjourney.Base.Models
{
    /// <summary>
    /// 异步垫图暂存结果。
    /// </summary>
    public sealed class ImagineStageResult
    {
        public bool Success { get; set; }

        public string Error { get; set; }

        public List<ImagineInputRef> Refs { get; set; } = [];

        public List<string> StagedKeys { get; set; } = [];

        public List<string> UrlOnly { get; set; } = [];
    }

    /// <summary>
    /// 请求线程侧的垫图暂存核心：URL-only 直接写入提示词，base64 原始字节写入 Redis。
    /// 通过 set 委托隔离 Redis，便于单元测试。
    /// </summary>
    public static class ImagineInputStager
    {
        /// <summary>
        /// 垫图暂存 TTL，默认 60 分钟，最大 60 分钟。
        /// </summary>
        public static TimeSpan GetTtl()
        {
            return TimeSpan.FromMinutes(60);
        }

        /// <summary>
        /// 暂存垫图。成功时更新提示词和任务属性；失败时不修改提示词/属性，并返回已写入的 key 供调用方清理。
        /// </summary>
        /// <param name="info">任务</param>
        /// <param name="dataUrls">按输入顺序排列的图片，允许 null 占位</param>
        /// <param name="ttlSeconds">Redis TTL 秒数</param>
        /// <param name="set">Redis 写入委托</param>
        /// <param name="urlOnlyAsPrompt">URL-only 图片是否直接写入提示词；为 false 时作为 URL 引用留给工作线程处理</param>
        /// <param name="onError">异常诊断回调；默认写入日志，异常详情不进入 Error</param>
        public static ImagineStageResult Stage(
            TaskInfo info,
            IReadOnlyList<DataUrl> dataUrls,
            int ttlSeconds,
            Func<string, byte[], int, bool> set,
            bool urlOnlyAsPrompt = true,
            Action<Exception> onError = null)
        {
            var result = new ImagineStageResult { Success = true };
            if (info == null)
            {
                return result;
            }

            var urlOnly = new List<string>();
            var refs = new List<ImagineInputRef>();

            try
            {
                var count = dataUrls?.Count ?? 0;
                for (var index = 0; index < count; index++)
                {
                    var dataUrl = dataUrls[index];
                    if (dataUrl == null)
                    {
                        continue;
                    }

                    if (ImagineInputRef.IsUrlOnly(dataUrl))
                    {
                        if (urlOnlyAsPrompt)
                        {
                            urlOnly.Add(dataUrl.Url);
                        }
                        else
                        {
                            refs.Add(new ImagineInputRef
                            {
                                Url = dataUrl.Url,
                                MimeType = dataUrl.MimeType,
                                Index = index,
                                Size = 0,
                            });
                        }
                        continue;
                    }

                    if (dataUrl.Data == null || dataUrl.Data.Length == 0)
                    {
                        continue;
                    }

                    var key = ImagineInputRef.BuildKey(info.Id, index);
                    if (!set(key, dataUrl.Data, ttlSeconds))
                    {
                        result.Success = false;
                        result.Error = "写入垫图缓存失败";
                        result.Refs = refs;
                        result.StagedKeys = refs.Select(x => x.Key).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                        result.UrlOnly = urlOnly;
                        return result;
                    }

                    refs.Add(new ImagineInputRef
                    {
                        Key = key,
                        MimeType = dataUrl.MimeType,
                        Index = index,
                        Size = dataUrl.Data.Length,
                    });
                }

                if (urlOnly.Count > 0)
                {
                    var urls = string.Join(" ", urlOnly);
                    info.Prompt = urls + " " + info.Prompt;
                    info.PromptEn = urls + " " + info.PromptEn;
                    info.Description = "/imagine " + info.Prompt;
                }

                if (refs.Count > 0)
                {
                    info.SetProperty(Constants.TASK_PROPERTY_IMAGINE_INPUTS, ImagineInputRef.Serialize(refs));
                    info.SetProperty(Constants.TASK_PROPERTY_IMAGINE_NEED_PREPARE, true);
                }

                result.Refs = refs;
                result.StagedKeys = refs.Select(x => x.Key).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                result.UrlOnly = urlOnly;
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = "垫图暂存失败，请稍后重试";
                result.Refs = refs;
                result.StagedKeys = refs.Select(x => x.Key).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                result.UrlOnly = urlOnly;

                if (onError != null)
                {
                    try
                    {
                        onError(ex);
                    }
                    catch
                    {
                        // 诊断回调异常不应掩盖暂存失败
                    }
                }
                else
                {
                    Log.Error(ex, "异步垫图暂存异常: TaskId={TaskId}", info?.Id);
                }

                return result;
            }
        }
    }
}
