using System.Diagnostics;
using Midjourney.Base.Data;
using Midjourney.Base.Util;

namespace Midjourney.Base.Models
{
    /// <summary>
    /// 异步视频输入准备核心。
    /// 工作线程负责 base64 图片上传、URL 解析，并把首帧/尾帧占位符替换成最终链接。
    /// 通过委托隔离 Redis/上传/URL 解析/数据库，便于单元测试。
    /// </summary>
    public static class VideoInputPreparer
    {
        /// <summary>
        /// 依次解析视频输入，成功后替换提示词占位符并持久化状态。
        /// 数据缺失、上传失败或异常时恢复任务状态、删除暂存并返回失败。
        /// </summary>
        public static async Task<Message> PrepareAsync(
            TaskInfo info,
            IReadOnlyList<ImagineInputRef> refs,
            Setting prepareSetting,
            Func<string, byte[]> getBytes,
            Func<DataUrl, string, Task<Message>> upload,
            Func<string, Task<string>> resolveUrl,
            Action<TaskInfo> save,
            Action<IEnumerable<ImagineInputRef>> delete,
            Action<ImagineInputRef, DataUrl, bool, long> onPrepared = null,
            Action<Exception> onError = null,
            SemaphoreSlim cpuSemaphore = null,
            CancellationToken cancellationToken = default)
        {
            if (info == null)
            {
                return Message.Success();
            }

            var validRefs = refs?.Where(x => x != null).OrderBy(x => x.Index).ToList() ?? [];
            if (validRefs.Count == 0)
            {
                return HasPendingInputs(info)
                    ? Message.Of(ReturnCode.FAILURE, "视频图片数据已过期，请重新提交")
                    : Message.Success();
            }

            var originalPrompt = info.Prompt;
            var originalPromptEn = info.PromptEn;
            var originalDescription = info.Description;
            var originalBaseImageUrl = info.BaseImageUrl;
            var originalPrepared = info.GetProperty(Constants.TASK_PROPERTY_IMAGINE_PREPARED);
            var originalPreparedUrls = info.GetProperty(Constants.TASK_PROPERTY_IMAGINE_PREPARED_URLS);
            var originalNeedPrepare = info.GetProperty(Constants.TASK_PROPERTY_IMAGINE_NEED_PREPARE);
            var originalInputs = info.GetProperty(Constants.TASK_PROPERTY_IMAGINE_INPUTS);
            var originalExtendPrompt = info.GetProperty(Constants.TASK_PROPERTY_VIDEO_EXTEND_PROMPT);

            void RestoreProperty(string name, object value)
            {
                if (value == null)
                {
                    info.RemoveProperty(name);
                }
                else
                {
                    info.SetProperty(name, value);
                }
            }

            void Restore()
            {
                info.Prompt = originalPrompt;
                info.PromptEn = originalPromptEn;
                info.Description = originalDescription;
                info.BaseImageUrl = originalBaseImageUrl;
                RestoreProperty(Constants.TASK_PROPERTY_IMAGINE_PREPARED, originalPrepared);
                RestoreProperty(Constants.TASK_PROPERTY_IMAGINE_PREPARED_URLS, originalPreparedUrls);
                RestoreProperty(Constants.TASK_PROPERTY_IMAGINE_NEED_PREPARE, originalNeedPrepare);
                RestoreProperty(Constants.TASK_PROPERTY_IMAGINE_INPUTS, originalInputs);
                RestoreProperty(Constants.TASK_PROPERTY_VIDEO_EXTEND_PROMPT, originalExtendPrompt);
            }

            void SafeDelete()
            {
                try
                {
                    delete?.Invoke(validRefs);
                }
                catch
                {
                    // TTL 会最终清理暂存数据
                }
            }

            void SafeError(Exception ex)
            {
                try
                {
                    onError?.Invoke(ex);
                }
                catch
                {
                    // 日志回调异常不应掩盖准备失败
                }
            }

            var webpSetting = prepareSetting ?? new Setting();
            var resolved = new Dictionary<int, string>();
            var preparedUrls = new List<string>();

            try
            {
                foreach (var inputRef in validRefs)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var inputSw = Stopwatch.StartNew();

                    if (!string.IsNullOrWhiteSpace(inputRef.Key))
                    {
                        if (!string.IsNullOrWhiteSpace(inputRef.MimeType)
                            && !inputRef.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                        {
                            Restore();
                            SafeDelete();
                            return Message.Of(ReturnCode.FAILURE, "视频图片格式不支持，请上传图片");
                        }

                        var bytes = getBytes(inputRef.Key);
                        if (ImagineInputRef.IsMissing(bytes))
                        {
                            Restore();
                            SafeDelete();
                            return Message.Of(ReturnCode.FAILURE, "视频图片数据已过期，请重新提交");
                        }

                        var dataUrl = new DataUrl(inputRef.MimeType ?? "image/png", bytes);
                        var webp = false;
                        string fileName;
                        if (cpuSemaphore != null)
                        {
                            await cpuSemaphore.WaitAsync(cancellationToken);
                            try
                            {
                                webp = PromptImageWebpConverter.TryConvertToWebP(dataUrl, webpSetting);
                                fileName = await dataUrl.GenerateFileName();
                            }
                            finally
                            {
                                cpuSemaphore.Release();
                            }
                        }
                        else
                        {
                            webp = PromptImageWebpConverter.TryConvertToWebP(dataUrl, webpSetting);
                            fileName = await dataUrl.GenerateFileName();
                        }

                        var uploadResult = await upload(dataUrl, fileName);

                        if (uploadResult == null
                            || uploadResult.Code != ReturnCode.SUCCESS
                            || string.IsNullOrWhiteSpace(uploadResult.Description))
                        {
                            Restore();
                            SafeDelete();
                            return Message.Of(ReturnCode.FAILURE, "视频图片处理失败，请稍后重试");
                        }

                        resolved[inputRef.Index] = uploadResult.Description;
                        preparedUrls.Add(uploadResult.Description);

                        try
                        {
                            onPrepared?.Invoke(inputRef, dataUrl, webp, inputSw.ElapsedMilliseconds);
                        }
                        catch
                        {
                            // 观测回调异常不应阻断任务
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(inputRef.Url))
                    {
                        var url = await resolveUrl(inputRef.Url);
                        if (string.IsNullOrWhiteSpace(url))
                        {
                            Restore();
                            SafeDelete();
                            return Message.Of(ReturnCode.FAILURE, "视频图片处理失败，请稍后重试");
                        }

                        resolved[inputRef.Index] = url;
                    }
                    else
                    {
                        Restore();
                        SafeDelete();
                        return Message.Of(ReturnCode.FAILURE, "视频图片数据已过期，请重新提交");
                    }
                }

                foreach (var item in resolved)
                {
                    ReplaceToken(info, item.Key, item.Value);
                }

                if (ContainsPendingToken(info))
                {
                    Restore();
                    SafeDelete();
                    return Message.Of(ReturnCode.FAILURE, "视频图片处理失败，请稍后重试");
                }

                if (resolved.TryGetValue(0, out var startUrl) && !string.IsNullOrWhiteSpace(startUrl))
                {
                    info.BaseImageUrl = startUrl;
                }

                info.SetProperty(Constants.TASK_PROPERTY_IMAGINE_PREPARED, true);
                info.SetProperty(Constants.TASK_PROPERTY_IMAGINE_PREPARED_URLS, preparedUrls.ToJson());
                info.SetProperty(Constants.TASK_PROPERTY_IMAGINE_NEED_PREPARE, false);
                info.RemoveProperty(Constants.TASK_PROPERTY_IMAGINE_INPUTS);

                save?.Invoke(info);
                SafeDelete();

                return Message.Success();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Restore();
                SafeDelete();
                return Message.Of(ReturnCode.FAILURE, "操作已取消");
            }
            catch (Exception ex)
            {
                Restore();
                SafeError(ex);
                SafeDelete();
                return Message.Of(ReturnCode.FAILURE, "视频图片处理失败，请稍后重试");
            }
        }

        /// <summary>
        /// 是否仍有未完成的视频输入（需要准备或提示词中残留占位符）。
        /// </summary>
        public static bool HasPendingInputs(TaskInfo info)
        {
            return info != null && (ImagineInputRef.NeedsPrepare(info) || ContainsPendingToken(info));
        }

        public static bool ContainsPendingToken(TaskInfo info)
        {
            var extendPrompt = ImagineInputRef.GetString(info.GetProperty(Constants.TASK_PROPERTY_VIDEO_EXTEND_PROMPT));
            for (var index = 0; index <= 1; index++)
            {
                var token = ImagineInputRef.BuildVideoInputToken(info.Id, index);
                if (info.Prompt?.Contains(token, StringComparison.Ordinal) == true
                    || info.PromptEn?.Contains(token, StringComparison.Ordinal) == true
                    || info.Description?.Contains(token, StringComparison.Ordinal) == true
                    || extendPrompt?.Contains(token, StringComparison.Ordinal) == true)
                {
                    return true;
                }
            }

            return false;
        }

        private static void ReplaceToken(TaskInfo info, int index, string url)
        {
            if (info == null || string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            var token = ImagineInputRef.BuildVideoInputToken(info.Id, index);
            info.Prompt = info.Prompt?.Replace(token, url);
            info.PromptEn = info.PromptEn?.Replace(token, url);
            info.Description = info.Description?.Replace(token, url);

            var extendPrompt = ImagineInputRef.GetString(info.GetProperty(Constants.TASK_PROPERTY_VIDEO_EXTEND_PROMPT));
            if (!string.IsNullOrWhiteSpace(extendPrompt))
            {
                info.SetProperty(Constants.TASK_PROPERTY_VIDEO_EXTEND_PROMPT, extendPrompt.Replace(token, url));
            }
        }
    }
}
