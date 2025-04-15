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

using Microsoft.Extensions.Caching.Memory;
using Midjourney.Infrastructure.Dto;
using Midjourney.Infrastructure.Services;
using Midjourney.Infrastructure.Util;
using System.Diagnostics;

namespace Midjourney.Infrastructure.LoadBalancer
{
    /// <summary>
    /// 换脸服务
    /// </summary>
    public class FaceSwapInstance : BaseFaceSwapInstance
    {
        private readonly Task _longTask;
        private readonly AsyncParallelLock _semaphoreSlimLock;
        private readonly CancellationTokenSource _longToken;
        private readonly ManualResetEvent _mre;

        public FaceSwapInstance(ITaskStoreService taskStoreService, INotifyService notifyService, IMemoryCache memoryCache, DiscordHelper discordHelper)
            : base(taskStoreService, notifyService, memoryCache, discordHelper)
        {
            var config = GlobalConfiguration.Setting;

            // 最小 1, 最大 120
            _semaphoreSlimLock = new AsyncParallelLock(Math.Max(1, Math.Min(config.Replicate.FaceSwapCoreSize, 120)));

            // 初始化信号器
            _mre = new ManualResetEvent(false);

            _longToken = new CancellationTokenSource();
            _longTask = new Task(Running, _longToken.Token, TaskCreationOptions.LongRunning);
            _longTask.Start();
        }

        /// <summary>
        /// 判断实例是否存活
        /// </summary>
        /// <returns>是否存活</returns>
        public bool IsAlive => GlobalConfiguration.Setting.Replicate?.EnableFaceSwap == true && !string.IsNullOrWhiteSpace(GlobalConfiguration.Setting.Replicate?.Token);

        /// <summary>
        /// 提交任务
        /// </summary>
        /// <param name="dto"></param>
        /// <param name="info"></param>
        /// <returns></returns>
        public async Task<SubmitResultVO> SubmitTask(InsightFaceSwapDto dto, TaskInfo info)
        {
            // 在任务提交时，前面的的任务数量
            var currentWaitNumbers = _queueTasks.Count;
            var repl = GlobalConfiguration.Setting.Replicate;

            if (repl.FaceSwapQueueSize > 0 && currentWaitNumbers >= repl.FaceSwapQueueSize)
            {
                return SubmitResultVO.Fail(ReturnCode.FAILURE, "提交失败，队列已满，请稍后重试");
            }

            try
            {
                var ff = new FileFetchHelper();

                DataUrl source = null;
                try
                {
                    if (!string.IsNullOrWhiteSpace(dto.SourceBase64))
                    {
                        source = DataUrl.Parse(dto.SourceBase64);
                        dto.SourceUrl = null;

                        var len = source.Data.Length;
                        if (len <= 0 || len > repl.MaxFileSize)
                        {
                            return SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "人脸图片大小超过最大限制");
                        }
                    }
                    else
                    {
                        // 验证 url
                        if (!Uri.TryCreate(dto.SourceUrl, UriKind.Absolute, out var uri))
                        {
                            return SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "人脸图片URL格式错误");
                        }

                        var len = await ff.GetFileSizeAsync(dto.SourceUrl);
                        if (len <= 0 || len > repl.MaxFileSize)
                        {
                            return SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "人脸图片大小超过最大限制");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "人脸图片格式转换异常");
                    return SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "人脸图片格式错误");
                }

                DataUrl target = null;
                try
                {
                    if (!string.IsNullOrWhiteSpace(dto.TargetBase64))
                    {
                        target = DataUrl.Parse(dto.TargetBase64);
                        dto.TargetUrl = null;

                        var len = target.Data.Length;
                        if (len <= 0 || len > repl.MaxFileSize)
                        {
                            return SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "目标图片大小超过最大限制");
                        }
                    }
                    else
                    {
                        // 验证 url
                        if (!Uri.TryCreate(dto.TargetUrl, UriKind.Absolute, out var uri))
                        {
                            return SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "目标图片URL格式错误");
                        }

                        var len = await ff.GetFileSizeAsync(dto.TargetUrl);
                        if (len <= 0 || len > repl.MaxFileSize)
                        {
                            return SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "目标图片大小超过最大限制");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "目标图片格式转换异常");
                    return SubmitResultVO.Fail(ReturnCode.VALIDATION_ERROR, "目标图片格式错误");
                }

                if (!string.IsNullOrWhiteSpace(dto.SourceUrl))
                {
                    info.ReplicateSource = dto.SourceUrl;
                }
                else
                {
                    // 如果是 base64 则到本地
                    var ext = ff.DetermineFileExtension(source.MimeType, source.Data, "");
                    if (string.IsNullOrWhiteSpace(ext))
                    {
                        ext = ".jpg";
                    }

                    // 保存 bytes 到本地
                    var directoryPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "temps");
                    var fileName = $"{Guid.NewGuid().ToString("N")}{ext}";
                    var fullPath = Path.Combine(directoryPath, fileName);

                    lock (_lock)
                    {
                        Directory.CreateDirectory(directoryPath);
                        File.WriteAllBytes(fullPath, source.Data);
                    }

                    info.ReplicateSource = fullPath;
                }

                if (!string.IsNullOrWhiteSpace(dto.TargetUrl))
                {
                    info.ReplicateTarget = dto.TargetUrl;
                }
                else
                {
                    // 保存 base64 到本地
                    var ext = ff.DetermineFileExtension(target.MimeType, target.Data, "");
                    if (string.IsNullOrWhiteSpace(ext))
                    {
                        ext = ".jpg";
                    }

                    // 保存 bytes 到本地
                    var directoryPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "temps");
                    var fileName = $"{Guid.NewGuid().ToString("N")}{ext}";
                    var fullPath = Path.Combine(directoryPath, fileName);

                    lock (_lock)
                    {
                        Directory.CreateDirectory(directoryPath);
                        File.WriteAllBytes(fullPath, target.Data);
                    }

                    info.ReplicateTarget = fullPath;
                }

                info.IsReplicate = true;
                _taskStoreService.Save(info);

                _queueTasks.Enqueue(info);

                // 通知后台服务有新的任务
                _mre.Set();

                if (currentWaitNumbers == 0)
                {
                    return SubmitResultVO.Of(ReturnCode.SUCCESS, "提交成功", info.Id);
                }
                else
                {
                    return SubmitResultVO.Of(ReturnCode.IN_QUEUE, $"排队中，前面还有{currentWaitNumbers}个任务", info.Id)
                        .SetProperty("numberOfQueues", currentWaitNumbers);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "submit task error");

                _taskStoreService.Delete(info.Id);

                if (File.Exists(info.ReplicateTarget))
                {
                    File.Delete(info.ReplicateTarget);
                }

                if (File.Exists(info.ReplicateSource))
                {
                    File.Delete(info.ReplicateSource);
                }

                return SubmitResultVO.Fail(ReturnCode.FAILURE, "提交失败，系统异常");
            }
        }

        /// <summary>
        /// 后台服务执行任务
        /// </summary>
        private void Running()
        {
            while (true)
            {
                try
                {
                    if (_longToken.Token.IsCancellationRequested)
                    {
                        // 清理资源（如果需要）
                        break;
                    }
                }
                catch
                {

                }

                try
                {
                    //if (_longToken.Token.IsCancellationRequested)
                    //{
                    //    // 清理资源（如果需要）
                    //    break;
                    //}

                    // 等待信号通知
                    _mre.WaitOne();

                    // 判断是否还有资源可用
                    while (!_semaphoreSlimLock.IsLockAvailable())
                    {
                        //if (_longToken.Token.IsCancellationRequested)
                        //{
                        //    // 清理资源（如果需要）
                        //    break;
                        //}

                        // 等待
                        Thread.Sleep(100);
                    }

                    // 允许同时执行 N 个信号量的任务
                    //while (true)
                    //{
                    //if (_longToken.Token.IsCancellationRequested)
                    //{
                    //    // 清理资源（如果需要）
                    //    break;
                    //}

                    while (_queueTasks.TryPeek(out var info))
                    {
                        // 判断是否还有资源可用
                        if (_semaphoreSlimLock.IsLockAvailable())
                        {
                            // 从队列中移除任务，并开始执行
                            if (_queueTasks.TryDequeue(out info))
                            {
                                _taskFutureMap[info.Id] = ExecuteTaskAsync(info);
                            }
                        }
                        else
                        {
                            // 如果没有可用资源，等待
                            Thread.Sleep(100);
                        }
                    }
                    //else
                    //{
                    //    // 队列为空，退出循环
                    //    break;
                    //}
                    //}

                    //if (_longToken.Token.IsCancellationRequested)
                    //{
                    //    // 清理资源（如果需要）
                    //    break;
                    //}

                    // 重新设置信号
                    _mre.Reset();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"图片换脸后台作业执行异常");

                    // 停止 1min
                    Thread.Sleep(1000 * 60);
                }
            }
        }

        /// <summary>
        /// 异步执行任务
        /// </summary>
        /// <param name="info"></param>
        /// <returns></returns>
        private async Task ExecuteTaskAsync(TaskInfo info)
        {
            try
            {
                var repl = GlobalConfiguration.Setting.Replicate;

                await _semaphoreSlimLock.LockAsync();

                _runningTasks.TryAdd(info, 0);

                // 判断当前实例是否可用
                if (!IsAlive)
                {
                    info.Fail("实例不可用");
                    SaveAndNotify(info);
                    return;
                }

                info.Status = TaskStatus.SUBMITTED;
                info.Progress = "0%";
                await AsyncSaveAndNotify(info);

                var source = info.ReplicateSource;
                if (!source.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    // 非网络链接，说明是本地文件
                    if (File.Exists(source))
                    {
                        var res = await UploadFile(source, repl.Token);
                        if (!string.IsNullOrWhiteSpace(res?.Urls?.Get))
                        {
                            source = res.Urls.Get;
                        }
                        else
                        {
                            info.Fail("文件上传失败");
                            SaveAndNotify(info);
                            return;
                        }
                    }
                    else
                    {
                        info.Fail("文件不存在");
                        SaveAndNotify(info);
                        return;
                    }
                }

                var target = info.ReplicateTarget;
                if (!target.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    // 非网络链接，说明是本地文件
                    if (File.Exists(target))
                    {
                        var res = await UploadFile(target, repl.Token);
                        if (!string.IsNullOrWhiteSpace(res?.Urls?.Get))
                        {
                            target = res.Urls.Get;
                        }
                        else
                        {
                            info.Fail("文件上传失败");
                            SaveAndNotify(info);
                            return;
                        }
                    }
                    else
                    {
                        info.Fail("文件不存在");
                        SaveAndNotify(info);
                        return;
                    }
                }

                // 判断当前实例是否可用
                if (!IsAlive)
                {
                    info.Fail("实例不可用");
                    SaveAndNotify(info);
                    return;
                }

                // 提交
                var sumitRes = await FaceSwapSubmit(new ReplicateFaceSwapRequest()
                {
                    Input = new ReplicateFaceSwapRequestInput()
                    {
                        InputImage = target,
                        SwapImage = source,
                    },
                    Version = repl.FaceSwapVersion
                }, repl.Token);
                if (!string.IsNullOrWhiteSpace(sumitRes?.Urls?.Get))
                {
                    info.Status = TaskStatus.SUBMITTED;
                    info.Progress = "0%";
                    await AsyncSaveAndNotify(info);
                }
                else
                {
                    info.Fail("执行失败" + sumitRes?.Logs);
                    SaveAndNotify(info);
                    return;
                }

                info.StartTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                info.Status = TaskStatus.SUBMITTED;
                info.Progress = "0%";
                await AsyncSaveAndNotify(info);

                // 超时处理
                var timeoutMin = repl.FaceSwapTimeoutMinutes;
                var sw = new Stopwatch();
                sw.Start();

                while (info.Status == TaskStatus.SUBMITTED || info.Status == TaskStatus.IN_PROGRESS)
                {
                    try
                    {
                        // 每 n 秒获取结果
                        var res = await FaceSwapGet(sumitRes.Urls.Get, repl.Token);
                        if (res?.Status == "succeeded")
                        {
                            // 成功
                            if (res.Output is string output && !string.IsNullOrEmpty(output))
                            {
                                info.ImageUrl = output;
                                info.Success();
                                SaveAndNotify(info);
                                return;
                            }
                            else
                            {
                                info.Fail($"执行失败返回内容为空 {res?.Urls?.Get}");
                                SaveAndNotify(info);
                                return;
                            }
                        }
                        else if (res?.Status == "processing")
                        {
                            info.Status = TaskStatus.IN_PROGRESS;
                            SaveAndNotify(info);
                        }
                        else if (res?.Status == "failed" || res?.Status == "canceled")
                        {
                            _logger.Warning("换脸执行失败 {@0}, {@1}", info.Id, res);

                            // 失败
                            info.Fail("执行失败" + res.Status + ", " + res?.Logs);
                            SaveAndNotify(info);
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "get task result error {@0}", info.Id);
                    }

                    await AsyncSaveAndNotify(info);
                    await Task.Delay(5000);

                    if (sw.ElapsedMilliseconds > timeoutMin * 60 * 1000)
                    {
                        try
                        {
                            // 超时，调用取消任务接口，减少成本
                            await FaceCancel(sumitRes?.Urls.Cancel, repl.Token);
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "cancel task error {@0}", info.Id);
                        }

                        info.Fail($"执行超时 {timeoutMin} 分钟");
                        SaveAndNotify(info);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "task execute error, id: {TaskId}", info.Id);

                info.Fail("[Internal Server Error] " + ex.Message);

                SaveAndNotify(info);
            }
            finally
            {
                _runningTasks.TryRemove(info, out _);
                _taskFutureMap.TryRemove(info.Id, out _);

                _semaphoreSlimLock.Unlock();

                SaveAndNotify(info);

                if (File.Exists(info.ReplicateSource))
                {
                    File.Delete(info.ReplicateSource);
                }

                if (File.Exists(info.ReplicateTarget))
                {
                    File.Delete(info.ReplicateTarget);
                }
            }
        }
    }
}