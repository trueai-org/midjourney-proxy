using Midjourney.Base.Data;
using Midjourney.Base.Util;

namespace Midjourney.Base.Models
{
    /// <summary>
    /// 异步 imagine 垫图准备核心逻辑。
    /// 通过委托隔离 Redis/上传/数据库，便于单元测试，并由 DiscordService 注入实际实现。
    /// </summary>
    public static class ImagineInputPreparer
    {
        /// <summary>
        /// 依次读取、转换并上传暂存垫图，成功后把链接写入提示词并持久化状态。
        /// 数据缺失、上传失败或异常时恢复任务状态、删除暂存并返回失败，调用方不得继续提交上游。
        /// </summary>
        public static async Task<Message> PrepareAsync(
            TaskInfo info,
            IReadOnlyList<ImagineInputRef> refs,
            Setting prepareSetting,
            Func<string, byte[]> getBytes,
            Func<DataUrl, string, Task<Message>> upload,
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

            var validRefs = refs?.Where(x => x != null).ToList() ?? [];
            if (validRefs.Count == 0)
            {
                return Message.Of(ReturnCode.FAILURE, "垫图数据已过期，请重新提交");
            }

            var originalPrompt = info.Prompt;
            var originalPromptEn = info.PromptEn;
            var originalDescription = info.Description;
            var originalBaseImageUrl = info.BaseImageUrl;
            var originalPrepared = info.GetProperty(Constants.TASK_PROPERTY_IMAGINE_PREPARED);
            var originalPreparedUrls = info.GetProperty(Constants.TASK_PROPERTY_IMAGINE_PREPARED_URLS);
            var originalNeedPrepare = info.GetProperty(Constants.TASK_PROPERTY_IMAGINE_NEED_PREPARE);
            var originalInputs = info.GetProperty(Constants.TASK_PROPERTY_IMAGINE_INPUTS);

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

            var urls = new List<string>();
            var webpSetting = prepareSetting ?? new Setting();

            try
            {
                foreach (var inputRef in validRefs.OrderBy(x => x.Index))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var inputSw = System.Diagnostics.Stopwatch.StartNew();

                    if (string.IsNullOrWhiteSpace(inputRef.Key))
                    {
                        Restore();
                        SafeDelete();
                        return Message.Of(ReturnCode.FAILURE, "垫图数据已过期，请重新提交");
                    }

                    if (!string.IsNullOrWhiteSpace(inputRef.MimeType)
                        && !inputRef.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    {
                        Restore();
                        SafeDelete();
                        return Message.Of(ReturnCode.FAILURE, "垫图格式不支持，请上传图片");
                    }

                    var bytes = getBytes(inputRef.Key);
                    if (ImagineInputRef.IsMissing(bytes))
                    {
                        Restore();
                        SafeDelete();
                        return Message.Of(ReturnCode.FAILURE, "垫图数据已过期，请重新提交");
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
                        return Message.Of(ReturnCode.FAILURE, "垫图处理失败，请稍后重试");
                    }

                    urls.Add(uploadResult.Description);

                    try
                    {
                        onPrepared?.Invoke(inputRef, dataUrl, webp, inputSw.ElapsedMilliseconds);
                    }
                    catch
                    {
                        // 观测回调异常不应阻断任务
                    }
                }

                if (info.Action == TaskAction.DESCRIBE)
                {
                    if (urls.Count != 1)
                    {
                        SafeDelete();
                        return Message.Of(ReturnCode.FAILURE, "图片参数错误");
                    }

                    info.ImageUrl = urls[0];
                }
                else if (info.Action == TaskAction.EDIT || info.Action == TaskAction.RETEXTURE)
                {
                    if (urls.Count != 1)
                    {
                        Restore();
                        SafeDelete();
                        return Message.Of(ReturnCode.FAILURE, "图片参数错误");
                    }

                    info.BaseImageUrl = urls[0];

                    // 转绘需要把基础图片作为 --dref 写入提示词；重复准备时不能重复追加
                    if (info.Action == TaskAction.RETEXTURE)
                    {
                        AppendDepthReference(info, urls[0]);
                    }
                }
                else if (urls.Count > 0)
                {
                    var imageUrls = string.Join(" ", urls);
                    info.Prompt = imageUrls + " " + info.Prompt;
                    info.PromptEn = imageUrls + " " + info.PromptEn;

                    if (info.Action == TaskAction.IMAGINE)
                    {
                        info.Description = "/imagine " + info.Prompt;
                    }
                }

                info.SetProperty(Constants.TASK_PROPERTY_IMAGINE_PREPARED, true);
                info.SetProperty(Constants.TASK_PROPERTY_IMAGINE_PREPARED_URLS, urls.ToJson());
                info.SetProperty(Constants.TASK_PROPERTY_IMAGINE_NEED_PREPARE, false);
                info.RemoveProperty(Constants.TASK_PROPERTY_IMAGINE_INPUTS);

                // 先保存准备结果，再删除暂存数据：崩溃后可通过已保存的提示词继续提交
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
                return Message.Of(ReturnCode.FAILURE, "垫图处理失败，请稍后重试");
            }
        }

        /// <summary>
        /// 将基础图片追加为转绘的 --dref 参数。已存在相同 --dref 时不重复追加。
        /// </summary>
        public static void AppendDepthReference(TaskInfo info, string url)
        {
            if (info == null || string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            var reference = "--dref " + url;
            var prompt = info.Prompt;
            var promptEn = string.IsNullOrWhiteSpace(info.PromptEn) ? prompt : info.PromptEn;

            info.Prompt = AppendArgument(prompt, reference);
            info.PromptEn = AppendArgument(promptEn, reference);
        }

        private static string AppendArgument(string prompt, string argument)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return argument;
            }

            if (prompt.Contains(argument, StringComparison.OrdinalIgnoreCase))
            {
                return prompt;
            }

            return prompt.TrimEnd() + " " + argument;
        }
    }
}
