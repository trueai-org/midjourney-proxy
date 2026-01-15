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

using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using JsonException = System.Text.Json.JsonException;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Midjourney.Services
{
    /// <summary>
    /// 通知服务实现类。
    /// </summary>
    public class NotifyService : INotifyService
    {
        private static readonly JsonSerializerOptions _jsonSerializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() } // 添加枚举序列化为字符串的转换器
        };

        private readonly ILogger<NotifyService> _logger;
        private readonly ConcurrentDictionary<string, string> _taskStatusMap = new();
        private readonly HttpClient _httpClient;
        private readonly SemaphoreSlim _semaphoreSlim;
        private readonly int _notifyPoolSize;
        private readonly IFreeSql _freeSql = FreeSqlHelper.FreeSql;

        public NotifyService(ILogger<NotifyService> logger, HttpClient httpClient)
        {
            _logger = logger;
            _httpClient = httpClient;
            _notifyPoolSize = GlobalConfiguration.Setting.NotifyPoolSize;

            var max = Math.Max(1, Math.Min(_notifyPoolSize, 64));

            _semaphoreSlim = new SemaphoreSlim(max, max);
        }

        public async Task NotifyTaskChange(TaskInfo task)
        {
            string notifyHook = task.GetProperty<string>(Constants.TASK_PROPERTY_NOTIFY_HOOK, default);
            if (string.IsNullOrWhiteSpace(notifyHook))
            {
                return;
            }

            string taskId = task.Id;
            string statusStr = $"{task.Status}:{task.Progress}";

            _logger.LogTrace("Wait notify task change, task: {0}({1}), hook: {2}", taskId, statusStr, notifyHook);

            try
            {
                string paramsStr = JsonSerializer.Serialize(task, _jsonSerializerOptions);
                await Task.Run(async () =>
                {
                    try
                    {
                        await ExecuteNotify(taskId, statusStr, notifyHook, paramsStr);
                    }
                    catch (Exception e)
                    {
                        _logger.LogWarning("Notify task change error, task: {0}({1}), hook: {2}, msg: {3}", taskId, statusStr, notifyHook, e.Message);
                    }
                });
            }
            catch (JsonException e)
            {
                _logger.LogError(e, e.Message);
            }
            finally
            {
                // 如果任务已完成，或失败，则移除任务状态
                if (task.Status == TaskStatus.SUCCESS || task.Status == TaskStatus.FAILURE)
                {
                    _taskStatusMap.TryRemove(taskId, out _);
                }
            }
        }

        private async Task ExecuteNotify(string taskId, string currentStatusStr, string notifyHook, string paramsStr)
        {
            await _semaphoreSlim.WaitAsync();

            try
            {
                if (_taskStatusMap.TryGetValue(taskId, out var existStatusStr))
                {
                    int compare = CompareStatusStr(currentStatusStr, existStatusStr);
                    if (compare <= 0)
                    {
                        // 忽略消息
                        _logger.LogDebug("Ignore this change, task: {0}({1})", taskId, currentStatusStr);
                        return;
                    }
                }

                _taskStatusMap[taskId] = currentStatusStr;
            }
            finally
            {
                _semaphoreSlim.Release();
            }

            _logger.LogDebug("Push task change, task: {0}({1}), hook: {2}", taskId, currentStatusStr, notifyHook);

            HttpResponseMessage response = await PostJson(notifyHook, paramsStr);
            if (!response.IsSuccessStatusCode)
            {
                var content = response.Content.ReadAsStringAsync();
                _logger.LogWarning("Notify task change fail, task: {0}({1}), hook: {2}, code: {3}, msg: {4}", taskId, currentStatusStr, notifyHook, (int)response.StatusCode, content);
            }
        }

        private async Task<HttpResponseMessage> PostJson(string notifyHook, string paramsJson)
        {
            var content = new StringContent(paramsJson, Encoding.UTF8, "application/json");
            return await _httpClient.PostAsync(notifyHook, content);
        }

        private int CompareStatusStr(string statusStr1, string statusStr2)
        {
            if (statusStr1 == statusStr2)
            {
                return 0;
            }

            float o1 = ConvertOrder(statusStr1);
            float o2 = ConvertOrder(statusStr2);
            return o1.CompareTo(o2);
        }

        private float ConvertOrder(string statusStr)
        {
            var split = statusStr.Split(':');
            TaskStatus status = Enum.Parse<TaskStatus>(split[0]);
            if (status != TaskStatus.IN_PROGRESS || split.Length == 1)
            {
                return status.GetOrder();
            }

            string progress = split[1];
            if (progress.EndsWith("%"))
            {
                return status.GetOrder() + float.Parse(progress.TrimEnd('%')) / 100;
            }

            return status.GetOrder();
        }

        /// <summary>
        /// 获取今日账号剩余额度
        /// </summary>
        /// <returns></returns>
        public (int fastTotal, int notYcRelaxTotal, int ycRelaxTotal) GetTodayAccountCount()
        {
            // 实时计算快速剩余额度
            var accounts = _freeSql.Select<DiscordAccount>().Where(c => c.Enable == true).ToList();

            // 今日总绘图
            var todayDict = CounterHelper.GetAllAccountTodayTotalCountDict();

            // 快速剩余额度
            var fastDict = CounterHelper.GetAllFastTaskAvailableCountDict();
            var fastTotal = 0;
            foreach (var account in accounts)
            {
                if (fastDict.TryGetValue(account.ChannelId, out var count))
                {
                    // 判断快速日绘图限制
                    if (account.DayDrawLimit > 0)
                    {
                        // 判断快速今日剩余额度
                        var todayFastCount = 0;
                        if (todayDict.TryGetValue(account.ChannelId, out var dic))
                        {
                            dic.TryGetValue(GenerationSpeedMode.FAST, out var fastCount);
                            dic.TryGetValue(GenerationSpeedMode.TURBO, out var turboCount);

                            todayFastCount += fastCount;
                            todayFastCount += turboCount;
                        }

                        fastTotal += Math.Min(count, Math.Max(0, account.DayDrawLimit - todayFastCount));
                    }
                    else
                    {
                        fastTotal += count;
                    }
                }
            }

            // 慢速剩余额度，只有悠船账号才有慢速额度
            var ycAcounts = accounts.Where(c => c.IsYouChuan).ToList();
            var ycRelaxDict = CounterHelper.GetAllYouchuanRelaxCountDict();
            var ycRelaxTotal = 0;
            foreach (var account in ycAcounts)
            {
                // 判断今日是否达到上限
                if (account.YouChuanPicreadReset > DateTime.Now.Date)
                {
                    continue;
                }

                // 悠船日绘图限制
                if (account.YouChuanRelaxDailyLimit > 0)
                {
                    // 判断慢速日绘图限制
                    if (account.DayRelaxDrawLimit > 0)
                    {
                        var relaxTodayCount = Math.Min(account.YouChuanRelaxDailyLimit, account.DayRelaxDrawLimit);

                        // 获取今日已绘图
                        if (ycRelaxDict.TryGetValue(account.ChannelId, out var relaxCount))
                        {
                            relaxTodayCount -= relaxCount;
                            if (relaxTodayCount <= 0)
                            {
                                continue;
                            }
                        }

                        ycRelaxTotal += relaxTodayCount;
                    }
                    else
                    {
                        var relaxTodayCount = account.YouChuanRelaxDailyLimit;

                        // 获取今日已绘图
                        if (ycRelaxDict.TryGetValue(account.ChannelId, out var relaxCount))
                        {
                            relaxTodayCount -= relaxCount;
                            if (relaxTodayCount <= 0)
                            {
                                continue;
                            }
                        }

                        ycRelaxTotal += relaxTodayCount;
                    }
                }
            }

            // 计算非悠船慢速可用额度
            var notYcAccounts = accounts.Where(c => !c.IsYouChuan).ToList();
            var notYcRelaxTotal = notYcAccounts.Count != 0 ? -1 : 0;
            foreach (var account in notYcAccounts)
            {
                // 判断慢速日绘图限制
                if (account.DayRelaxDrawLimit > 0)
                {
                    // 判断今日是否达到上限
                    var todayRelaxCount = 0;
                    if (todayDict.TryGetValue(account.ChannelId, out var dic))
                    {
                        dic.TryGetValue(GenerationSpeedMode.RELAX, out var relaxCount);
                        todayRelaxCount += relaxCount;
                    }

                    // 重置非悠船慢速总额度
                    if (notYcRelaxTotal == -1)
                    {
                        notYcRelaxTotal = 0;
                    }

                    notYcRelaxTotal += Math.Max(0, account.DayRelaxDrawLimit - todayRelaxCount);
                }
            }

            return (fastTotal, notYcRelaxTotal, ycRelaxTotal);
        }
    }
}