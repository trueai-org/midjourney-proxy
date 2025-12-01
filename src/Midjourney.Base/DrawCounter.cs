using System.Collections.Concurrent;
using Midjourney.Base.Data;
using MongoDB.Driver;

namespace Midjourney.Base
{
    /// <summary>
    /// DrawCounter 类用于计数绘图请求
    /// </summary>
    public class DrawCounter
    {
        /// <summary>
        /// 账号今日成功绘图统计
        /// key: yyyyMMdd_账号ID, value: (speed, action: count)
        /// </summary>
        public static ConcurrentDictionary<string, Dictionary<GenerationSpeedMode, Dictionary<TaskAction, int>>> AccountTodaySuccessCounter = new();

        /// <summary>
        /// 账号今日所有绘图统计(不包含放大, 包含失败)
        /// key: yyyyMMdd_账号ID, value: (speed, count)
        /// </summary>
        public static ConcurrentDictionary<string, Dictionary<GenerationSpeedMode, int>> AccountTodayAllCounter = new();

        /// <summary>
        /// 用户今日成功绘图统计
        /// key: yyyyMMdd_用户ID, value: (speed, action: count)
        /// </summary>
        public static ConcurrentDictionary<string, Dictionary<GenerationSpeedMode, Dictionary<TaskAction, int>>> UserTodadSuccessCounter = new();

        /// <summary>
        /// 线程安全锁对象，用于确保多线程环境下的同步访问。
        /// </summary>
        private static readonly object _lock = new();

        /// <summary>
        /// 初始化今日账号绘图统计
        /// </summary>
        /// <param name="instanceId">指定账号出事啊</param>
        public static bool InitAccountTodayCounter(string instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                return false;
            }

            var setting = GlobalConfiguration.Setting;

            var now = new DateTimeOffset(DateTime.Now.Date).ToUnixTimeMilliseconds();
            var nowKey = DateTime.Now.Date.ToString("yyyyMMdd");
            var key = $"{nowKey}_{instanceId}";

            if (AccountTodaySuccessCounter.ContainsKey(key))
            {
                return true;
            }

            // 如果是 redis
            if (setting.IsValidRedis)
            {
                lock (_lock)
                {
                    AccountTodayAllCounter.TryAdd(key, []);
                    AccountTodaySuccessCounter.AddOrUpdate(key, [], (key, oldValue) => []);

                    GenerationSpeedMode[] modes = [GenerationSpeedMode.FAST, GenerationSpeedMode.RELAX, GenerationSpeedMode.TURBO];
                    foreach (var mode in modes)
                    {
                        // redis
                        var accountTodayAllKey = $"counter_all_{DateTime.Now:yyyyMMdd}:{instanceId}_{mode}";
                        var accountTodayAllCount = RedisHelper.Get<int?>(accountTodayAllKey);
                        if (accountTodayAllCount == null)
                        {
                            continue;
                        }

                        AccountTodayAllCounter[key].TryAdd(mode, 0);
                        AccountTodayAllCounter[key][mode] = accountTodayAllCount.Value;

                        foreach (TaskAction action in Enum.GetValues(typeof(TaskAction)))
                        {
                            var accountSuccessKey = $"counter_success_{DateTime.Now:yyyyMMdd}:{instanceId}_{mode}_{action}";
                            var accountSuccessCount = RedisHelper.Get<int?>(accountSuccessKey);
                            if (accountSuccessCount != null)
                            {
                                if (!AccountTodaySuccessCounter[key].ContainsKey(mode))
                                {
                                    AccountTodaySuccessCounter[key].TryAdd(mode, []);
                                }
                                if (!AccountTodaySuccessCounter[key][mode].ContainsKey(action))
                                {
                                    AccountTodaySuccessCounter[key][mode].TryAdd(action, 0);
                                }
                                AccountTodaySuccessCounter[key][mode][action] = accountSuccessCount.Value;
                            }
                        }
                    }
                }

                // 昨天的 key 移除
                var yesterdayKey = $"{DateTime.Now.Date.AddDays(-1):yyyyMMdd}_{instanceId}";
                if (AccountTodaySuccessCounter.ContainsKey(yesterdayKey))
                {
                    AccountTodaySuccessCounter.TryRemove(yesterdayKey, out _);
                }

                if (AccountTodayAllCounter.ContainsKey(yesterdayKey))
                {
                    AccountTodayAllCounter.TryRemove(yesterdayKey, out _);
                }

                return true;
            }

            lock (_lock)
            {
                AccountTodaySuccessCounter.AddOrUpdate(key, [], (key, oldValue) => []);

                if (setting.DatabaseType == DatabaseType.MongoDB)
                {
                    var list = MongoHelper.GetCollection<TaskInfo>().AsQueryable()
                        .Where(x => x.SubmitTime >= now && x.Status == TaskStatus.SUCCESS && x.InstanceId == instanceId)
                        .GroupBy(c => new
                        {
                            c.Mode,
                            c.Action
                        })
                        .Select(g => new
                        {
                            g.Key.Mode,
                            g.Key.Action,
                            Count = g.Count()
                        }).ToList();

                    foreach (var item in list)
                    {
                        var mode = item.Mode ?? GenerationSpeedMode.FAST;
                        if (item.Action != null)
                        {
                            AccountTodaySuccessCounter[key].TryAdd(mode, []);
                            AccountTodaySuccessCounter[key][mode].TryAdd(item.Action.Value, 0);
                            AccountTodaySuccessCounter[key][mode][item.Action.Value] += item.Count;
                        }
                    }

                    // 已提交的所有任务（包含取消）
                    // 统计不包含放大的所有任务, 按速度分组
                    var allCounts = MongoHelper.GetCollection<TaskInfo>().AsQueryable()
                        .Where(x => x.SubmitTime >= now && x.InstanceId == instanceId && x.Action != TaskAction.UPSCALE && x.Status != TaskStatus.MODAL && x.Status != TaskStatus.NOT_START)
                        .GroupBy(c => c.Mode)
                        .Select(g => new
                        {
                            Mode = g.Key,
                            Count = g.Count()
                        })
                        .ToList()
                        .Where(c => c.Mode != null)
                        .ToDictionary(c => c.Mode.Value, c => c.Count);

                    AccountTodayAllCounter.AddOrUpdate(key, allCounts, (k, v) => allCounts);
                }
                else if (setting.DatabaseType == DatabaseType.LiteDB)
                {
                    var list = LiteDBHelper.TaskStore.GetCollection().Query()
                        .Where(x => x.SubmitTime >= now && x.Status == TaskStatus.SUCCESS && x.InstanceId == instanceId)
                        .Select(c => new
                        {
                            c.Mode,
                            c.Action
                        })
                        .ToList()
                        .GroupBy(c => new
                        {
                            c.Mode,
                            c.Action
                        })
                        .Select(g => new
                        {
                            g.Key.Mode,
                            g.Key.Action,
                            Count = g.Count()
                        }).ToList();

                    foreach (var item in list)
                    {
                        var mode = item.Mode ?? GenerationSpeedMode.FAST;
                        if (item.Action != null)
                        {
                            AccountTodaySuccessCounter[key].TryAdd(mode, []);
                            AccountTodaySuccessCounter[key][mode].TryAdd(item.Action.Value, 0);
                            AccountTodaySuccessCounter[key][mode][item.Action.Value] += item.Count;
                        }
                    }

                    // 已提交的所有任务（包含取消）
                    // 统计不包含放大的所有任务, 按速度分组
                    var allCounts = LiteDBHelper.TaskStore.GetCollection().Query()
                        .Where(x => x.SubmitTime >= now && x.InstanceId == instanceId && x.Action != TaskAction.UPSCALE && x.Status != TaskStatus.MODAL && x.Status != TaskStatus.NOT_START && x.Mode != null)
                        .ToList()
                        .Select(c => c.Mode)
                        .ToList()
                        .GroupBy(c => c)
                        .Where(g => g.Key != null)
                        .ToDictionary(g => g.Key.Value, g => g.Count());
                    AccountTodayAllCounter.AddOrUpdate(key, allCounts, (k, v) => allCounts);
                }
                else
                {
                    var freeSql = FreeSqlHelper.FreeSql;
                    if (freeSql != null)
                    {
                        var list = freeSql.Select<TaskInfo>()
                            .Where(x => x.SubmitTime >= now && x.Status == TaskStatus.SUCCESS && x.InstanceId == instanceId)
                            .GroupBy(c => new
                            {
                                c.Mode,
                                c.Action
                            })
                            .ToList(g => new
                            {
                                g.Key.Mode,
                                g.Key.Action,
                                Count = g.Count()
                            });

                        foreach (var item in list)
                        {
                            var mode = item.Mode ?? GenerationSpeedMode.FAST;
                            if (item.Action != null)
                            {
                                AccountTodaySuccessCounter[key].TryAdd(mode, []);
                                AccountTodaySuccessCounter[key][mode].TryAdd(item.Action.Value, 0);
                                AccountTodaySuccessCounter[key][mode][item.Action.Value] += item.Count;
                            }
                        }

                        // 已提交的所有任务（包含取消）
                        // 统计不包含放大的所有任务, 按速度分组
                        var allCounts = freeSql.Select<TaskInfo>()
                            .Where(x => x.SubmitTime >= now && x.InstanceId == instanceId && x.Action != TaskAction.UPSCALE && x.Status != TaskStatus.MODAL && x.Status != TaskStatus.NOT_START)
                            .GroupBy(c => c.Mode)
                            .ToList(g => new
                            {
                                Mode = g.Key,
                                Count = g.Count()
                            })
                            .Where(c => c.Mode != null)
                            .ToDictionary(c => c.Mode.Value, c => c.Count);
                        AccountTodayAllCounter.AddOrUpdate(key, allCounts, (k, v) => allCounts);
                    }
                }

                // 昨天的 key 移除
                var yesterdayKey = $"{DateTime.Now.Date.AddDays(-1):yyyyMMdd}_{instanceId}";
                if (AccountTodaySuccessCounter.ContainsKey(yesterdayKey))
                {
                    AccountTodaySuccessCounter.TryRemove(yesterdayKey, out _);
                }

                if (AccountTodayAllCounter.ContainsKey(yesterdayKey))
                {
                    AccountTodayAllCounter.TryRemove(yesterdayKey, out _);
                }
            }

            return true;
        }

        /// <summary>
        /// 初始化用户今日绘图统计
        /// </summary>
        /// <param name="userId"></param>
        public static bool InitUserTodayCounter(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return false;
            }
            var setting = GlobalConfiguration.Setting;

            var now = new DateTimeOffset(DateTime.Now.Date).ToUnixTimeMilliseconds();
            var nowKey = DateTime.Now.Date.ToString("yyyyMMdd");
            var key = $"{nowKey}_{userId}";

            if (UserTodadSuccessCounter.ContainsKey(key))
            {
                return true;
            }

            // 如果是 redis
            if (setting.IsValidRedis)
            {
                lock (_lock)
                {
                    UserTodadSuccessCounter.AddOrUpdate(key, [], (key, oldValue) => []);

                    GenerationSpeedMode[] modes = [GenerationSpeedMode.FAST, GenerationSpeedMode.RELAX, GenerationSpeedMode.TURBO];
                    foreach (var mode in modes)
                    {
                        foreach (TaskAction action in Enum.GetValues(typeof(TaskAction)))
                        {
                            var userTodaySuccessKey = $"counter_user_success_{DateTime.Now.Date:yyyyMMdd}:{userId}_{mode}_{action}";
                            var userTodaySuccessCount = RedisHelper.Get<int?>(userTodaySuccessKey);
                            if (userTodaySuccessCount != null)
                            {
                                if (!UserTodadSuccessCounter.ContainsKey(key))
                                {
                                    UserTodadSuccessCounter.TryAdd(key, []);
                                }
                                if (!UserTodadSuccessCounter[key].ContainsKey(mode))
                                {
                                    UserTodadSuccessCounter[key].TryAdd(mode, []);
                                }
                                if (!UserTodadSuccessCounter[key][mode].ContainsKey(action))
                                {
                                    UserTodadSuccessCounter[key][mode].TryAdd(action, 0);
                                }
                                UserTodadSuccessCounter[key][mode][action] = userTodaySuccessCount.Value;
                            }
                        }
                    }
                }

                // 昨天的 key 移除
                var yesterdayKey = $"{DateTime.Now.Date.AddDays(-1):yyyyMMdd}_{userId}";
                if (UserTodadSuccessCounter.ContainsKey(yesterdayKey))
                {
                    UserTodadSuccessCounter.TryRemove(yesterdayKey, out _);
                }

                return true;
            }

            lock (_lock)
            {
                UserTodadSuccessCounter.AddOrUpdate(key, [], (key, oldValue) => []);

                if (setting.DatabaseType == DatabaseType.MongoDB)
                {
                    var list = MongoHelper.GetCollection<TaskInfo>().AsQueryable()
                        .Where(x => x.SubmitTime >= now && x.Status == TaskStatus.SUCCESS && x.UserId == userId)
                        .GroupBy(c => new
                        {
                            c.Mode,
                            c.Action
                        })
                        .Select(g => new
                        {
                            g.Key.Mode,
                            g.Key.Action,
                            Count = g.Count()
                        }).ToList();

                    foreach (var item in list)
                    {
                        var mode = item.Mode ?? GenerationSpeedMode.FAST;
                        if (item.Action != null)
                        {
                            UserTodadSuccessCounter[key].TryAdd(mode, []);
                            UserTodadSuccessCounter[key][mode].TryAdd(item.Action.Value, 0);
                            UserTodadSuccessCounter[key][mode][item.Action.Value] += item.Count;
                        }
                    }
                }
                else if (setting.DatabaseType == DatabaseType.LiteDB)
                {
                    var list = LiteDBHelper.TaskStore.GetCollection().Query()
                        .Where(x => x.SubmitTime >= now && x.Status == TaskStatus.SUCCESS && x.UserId == userId)
                        .Select(c => new
                        {
                            c.Mode,
                            c.Action
                        })
                        .ToList()
                        .GroupBy(c => new
                        {
                            c.Mode,
                            c.Action
                        })
                        .Select(g => new
                        {
                            g.Key.Mode,
                            g.Key.Action,
                            Count = g.Count()
                        }).ToList();

                    foreach (var item in list)
                    {
                        var mode = item.Mode ?? GenerationSpeedMode.FAST;
                        if (item.Action != null)
                        {
                            UserTodadSuccessCounter[key].TryAdd(mode, []);
                            UserTodadSuccessCounter[key][mode].TryAdd(item.Action.Value, 0);
                            UserTodadSuccessCounter[key][mode][item.Action.Value] += item.Count;
                        }
                    }
                }
                else
                {
                    var freeSql = FreeSqlHelper.FreeSql;
                    if (freeSql != null)
                    {
                        var list = freeSql.Select<TaskInfo>()
                            .Where(x => x.SubmitTime >= now && x.Status == TaskStatus.SUCCESS && x.UserId == userId)
                            .GroupBy(c => new
                            {
                                c.Mode,
                                c.Action
                            })
                            .ToList(g => new
                            {
                                g.Key.Mode,
                                g.Key.Action,
                                Count = g.Count()
                            });

                        foreach (var item in list)
                        {
                            var mode = item.Mode ?? GenerationSpeedMode.FAST;
                            if (item.Action != null)
                            {
                                UserTodadSuccessCounter[key].TryAdd(mode, []);
                                UserTodadSuccessCounter[key][mode].TryAdd(item.Action.Value, 0);
                                UserTodadSuccessCounter[key][mode][item.Action.Value] += item.Count;
                            }
                        }
                    }
                }

                // 昨天的 key 移除
                var yesterdayKey = $"{DateTime.Now.Date.AddDays(-1):yyyyMMdd}_{userId}";
                if (UserTodadSuccessCounter.ContainsKey(yesterdayKey))
                {
                    UserTodadSuccessCounter.TryRemove(yesterdayKey, out _);
                }
            }

            return true;
        }

        /// <summary>
        /// 任务完成时，计数器 +1
        /// </summary>
        /// <param name="info"></param>
        public static void Complete(TaskInfo info, bool success, bool isIncr = true)
        {
            if (info == null || info.Action == null)
            {
                return;
            }

            var mode = info.Mode ?? GenerationSpeedMode.FAST;
            var setting = GlobalConfiguration.Setting;

            // 如果是 redis
            if (setting.IsValidRedis)
            {
                lock (_lock)
                {
                    // 统计账号
                    if (true)
                    {
                        // redis
                        var accountTodayAllKey = $"counter_all_{DateTime.Now:yyyyMMdd}:{info.InstanceId}_{mode}";
                        var accountTodayAllCount = 0;

                        if (isIncr)
                        {
                            accountTodayAllCount = (int)RedisHelper.IncrBy(accountTodayAllKey, 1);
                            RedisHelper.Expire(accountTodayAllKey, TimeSpan.FromDays(2));
                        }
                        else
                        {
                            accountTodayAllCount = RedisHelper.Get<int?>(accountTodayAllKey) ?? 0;
                        }

                        // 统计所有
                        var key = $"{DateTime.Now:yyyyMMdd}_{info.InstanceId}";
                        if (!AccountTodayAllCounter.ContainsKey(key))
                        {
                            AccountTodayAllCounter.TryAdd(key, []);
                        }
                        if (!AccountTodayAllCounter[key].ContainsKey(mode))
                        {
                            AccountTodayAllCounter[key].TryAdd(mode, 0);
                        }
                        AccountTodayAllCounter[key][mode] = accountTodayAllCount;

                        // 统计成功
                        if (success)
                        {
                            // redis
                            var accountSuccessKey = $"counter_success_{DateTime.Now:yyyyMMdd}:{info.InstanceId}_{mode}_{info.Action}";

                            var accountSuccessCount = 0;
                            if (isIncr)
                            {
                                accountSuccessCount = (int)RedisHelper.IncrBy(accountSuccessKey, 1);
                                RedisHelper.Expire(accountSuccessKey, TimeSpan.FromDays(2));
                            }
                            else
                            {
                                accountSuccessCount = RedisHelper.Get<int?>(accountSuccessKey) ?? 0;
                            }

                            if (!AccountTodaySuccessCounter.ContainsKey(key))
                            {
                                AccountTodaySuccessCounter.TryAdd(key, []);
                            }
                            if (!AccountTodaySuccessCounter[key].ContainsKey(mode))
                            {
                                AccountTodaySuccessCounter[key].TryAdd(mode, []);
                            }
                            if (!AccountTodaySuccessCounter[key][mode].ContainsKey(info.Action.Value))
                            {
                                AccountTodaySuccessCounter[key][mode].TryAdd(info.Action.Value, 0);
                            }

                            AccountTodaySuccessCounter[key][mode][info.Action.Value] = accountSuccessCount;
                        }
                    }

                    // 统计个人
                    if (true)
                    {
                        // 统计成功
                        if (success)
                        {
                            // 个人成功统计
                            var userTodaySuccessKey = $"counter_user_success_{DateTime.Now.Date:yyyyMMdd}:{info.UserId}_{mode}_{info.Action.Value}";
                            var userTodaySuccessCount = 0;

                            if (isIncr)
                            {
                                userTodaySuccessCount = (int)RedisHelper.IncrBy(userTodaySuccessKey, 1);
                                RedisHelper.Expire(userTodaySuccessKey, TimeSpan.FromDays(2));
                            }
                            else
                            {
                                userTodaySuccessCount = RedisHelper.Get<int?>(userTodaySuccessKey) ?? 0;
                            }

                            var key = $"{DateTime.Now.Date:yyyyMMdd}_{info.UserId}";
                            if (!UserTodadSuccessCounter.ContainsKey(key))
                            {
                                UserTodadSuccessCounter.TryAdd(key, []);
                            }

                            if (!UserTodadSuccessCounter[key].ContainsKey(mode))
                            {
                                UserTodadSuccessCounter[key].TryAdd(mode, []);
                            }

                            if (!UserTodadSuccessCounter[key][mode].ContainsKey(info.Action.Value))
                            {
                                UserTodadSuccessCounter[key][mode].TryAdd(info.Action.Value, 0);
                            }

                            UserTodadSuccessCounter[key][mode][info.Action.Value] = userTodaySuccessCount;
                        }
                    }
                }

                // 增量时才发送通知
                if (isIncr)
                {
                    var notification = new RedisNotification
                    {
                        Type = ENotificationType.CompleteTaskInfo,
                        IsSuccess = success,
                        TaskInfo = info
                    };
                    RedisHelper.Publish(RedisHelper.Prefix + Constants.REDIS_NOTIFY_CHANNEL, notification.ToJson());
                }

                return;
            }

            lock (_lock)
            {
                // 统计账号
                if (InitAccountTodayCounter(info.InstanceId))
                {
                    // 统计所有
                    var key = $"{DateTime.Now.Date:yyyyMMdd}_{info.InstanceId}";
                    if (!AccountTodayAllCounter.ContainsKey(key))
                    {
                        AccountTodayAllCounter.TryAdd(key, []);
                    }
                    if (!AccountTodayAllCounter[key].ContainsKey(mode))
                    {
                        AccountTodayAllCounter[key].TryAdd(mode, 0);
                    }
                    AccountTodayAllCounter[key][mode] += 1;

                    // 统计成功
                    if (success)
                    {
                        if (!AccountTodaySuccessCounter.ContainsKey(key))
                        {
                            AccountTodaySuccessCounter.TryAdd(key, []);
                        }

                        if (!AccountTodaySuccessCounter[key].ContainsKey(mode))
                        {
                            AccountTodaySuccessCounter[key].TryAdd(mode, []);
                        }

                        if (!AccountTodaySuccessCounter[key][mode].ContainsKey(info.Action.Value))
                        {
                            AccountTodaySuccessCounter[key][mode].TryAdd(info.Action.Value, 0);
                        }

                        AccountTodaySuccessCounter[key][mode][info.Action.Value] += 1;
                    }
                }

                // 统计个人
                if (InitUserTodayCounter(info.UserId))
                {
                    // 统计成功
                    if (success)
                    {
                        var key = $"{DateTime.Now.Date:yyyyMMdd}_{info.UserId}";
                        if (!UserTodadSuccessCounter.ContainsKey(key))
                        {
                            UserTodadSuccessCounter.TryAdd(key, []);
                        }

                        if (!UserTodadSuccessCounter[key].ContainsKey(mode))
                        {
                            UserTodadSuccessCounter[key].TryAdd(mode, []);
                        }

                        if (!UserTodadSuccessCounter[key][mode].ContainsKey(info.Action.Value))
                        {
                            UserTodadSuccessCounter[key][mode].TryAdd(info.Action.Value, 0);
                        }

                        var count = UserTodadSuccessCounter[key][mode][info.Action.Value];
                        UserTodadSuccessCounter[key][mode][info.Action.Value] += 1;
                    }
                }
            }
        }

        /// <summary>
        /// 获取账号今日绘图总数（不包含放大，包含失败）
        /// </summary>
        /// <param name="instanceId"></param>
        /// <param name="mode">慢速时返回快速绘图总数，快速时返回 (快速+极速) 绘图总数</param>
        /// <returns></returns>
        public static int GetAccountTodayTotalCount(string instanceId, GenerationSpeedMode mode)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                return 0;
            }

            var nowKey = DateTime.Now.Date.ToString("yyyyMMdd");
            var key = $"{nowKey}_{instanceId}";
            if (AccountTodayAllCounter.TryGetValue(key, out var speedDict))
            {
                if (mode == GenerationSpeedMode.RELAX)
                {
                    if (speedDict.TryGetValue(mode, out var count))
                    {
                        return count;
                    }
                }
                else
                {
                    // 快速 + 极速
                    speedDict.TryGetValue(GenerationSpeedMode.FAST, out var fastCount);
                    speedDict.TryGetValue(GenerationSpeedMode.TURBO, out var turboCount);

                    return fastCount + turboCount;
                }
            }

            return 0;
        }
    }
}