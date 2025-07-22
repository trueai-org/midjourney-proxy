using System.Collections.Concurrent;
using Midjourney.Base.Data;
using MongoDB.Driver;

namespace Midjourney.Base
{
    /// <summary>
    /// DrawCounter 类用于计数绘图请求。
    /// </summary>
    public class DrawCounter
    {
        /// <summary>
        /// 账号今日绘图统计
        /// </summary>
        public static ConcurrentDictionary<string, Dictionary<GenerationSpeedMode, Dictionary<TaskAction, int>>> AccountTodayCounter = new();

        /// <summary>
        /// 用户今日绘图统计
        /// </summary>
        public static ConcurrentDictionary<string, Dictionary<GenerationSpeedMode, Dictionary<TaskAction, int>>> UserTodayCounter = new();

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

            var now = new DateTimeOffset(DateTime.Now.Date).ToUnixTimeMilliseconds();
            var nowKey = DateTime.Now.Date.ToString("yyyyMMdd");
            var key = $"{nowKey}_{instanceId}";

            if (AccountTodayCounter.ContainsKey(key))
            {
                return true;
            }

            lock (_lock)
            {
                if (AccountTodayCounter.ContainsKey(key))
                {
                    return true;
                }

                AccountTodayCounter.AddOrUpdate(key, [], (key, oldValue) => []);

                var setting = GlobalConfiguration.Setting;
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
                            AccountTodayCounter[key].TryAdd(mode, []);
                            AccountTodayCounter[key][mode].TryAdd(item.Action.Value, 0);
                            AccountTodayCounter[key][mode][item.Action.Value] += item.Count;
                        }
                    }
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
                            AccountTodayCounter[key].TryAdd(mode, []);
                            AccountTodayCounter[key][mode].TryAdd(item.Action.Value, 0);
                            AccountTodayCounter[key][mode][item.Action.Value] += item.Count;
                        }
                    }
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
                                AccountTodayCounter[key].TryAdd(mode, []);
                                AccountTodayCounter[key][mode].TryAdd(item.Action.Value, 0);
                                AccountTodayCounter[key][mode][item.Action.Value] += item.Count;
                            }
                        }
                    }
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

            var now = new DateTimeOffset(DateTime.Now.Date).ToUnixTimeMilliseconds();
            var nowKey = DateTime.Now.Date.ToString("yyyyMMdd");
            var key = $"{nowKey}_{userId}";
            if (UserTodayCounter.ContainsKey(key))
            {
                return true;
            }

            lock (_lock)
            {
                if (UserTodayCounter.ContainsKey(key))
                {
                    return true;
                }

                UserTodayCounter.AddOrUpdate(key, [], (key, oldValue) => []);

                var setting = GlobalConfiguration.Setting;
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
                            UserTodayCounter[key].TryAdd(mode, []);
                            UserTodayCounter[key][mode].TryAdd(item.Action.Value, 0);
                            UserTodayCounter[key][mode][item.Action.Value] += item.Count;
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
                            UserTodayCounter[key].TryAdd(mode, []);
                            UserTodayCounter[key][mode].TryAdd(item.Action.Value, 0);
                            UserTodayCounter[key][mode][item.Action.Value] += item.Count;
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
                                UserTodayCounter[key].TryAdd(mode, []);
                                UserTodayCounter[key][mode].TryAdd(item.Action.Value, 0);
                                UserTodayCounter[key][mode][item.Action.Value] += item.Count;
                            }
                        }
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// 任务成功时，计数器 +1
        /// </summary>
        /// <param name="item"></param>
        public static void Success(TaskInfo item)
        {
            if (item == null || item.Status != TaskStatus.SUCCESS || item.Action == null)
            {
                return;
            }

            lock (_lock)
            {
                var mode = item.Mode ?? GenerationSpeedMode.FAST;

                if (InitUserTodayCounter(item.UserId))
                {
                    var key = $"{DateTime.Now.Date:yyyyMMdd}_{item.UserId}";
                    if (!UserTodayCounter.ContainsKey(key))
                    {
                        UserTodayCounter.TryAdd(key, []);
                    }

                    if (!UserTodayCounter[key].ContainsKey(mode))
                    {
                        UserTodayCounter[key].TryAdd(mode, []);
                    }

                    if (!UserTodayCounter[key][mode].ContainsKey(item.Action.Value))
                    {
                        UserTodayCounter[key][mode].TryAdd(item.Action.Value, 0);
                    }

                    var count = UserTodayCounter[key][mode][item.Action.Value];
                    UserTodayCounter[key][mode][item.Action.Value] += 1;
                }

                if (InitAccountTodayCounter(item.InstanceId))
                {
                    var key = $"{DateTime.Now.Date:yyyyMMdd}_{item.InstanceId}";

                    if (!AccountTodayCounter.ContainsKey(key))
                    {
                        AccountTodayCounter.TryAdd(key, []);
                    }

                    if (!AccountTodayCounter[key].ContainsKey(mode))
                    {
                        AccountTodayCounter[key].TryAdd(mode, []);
                    }

                    if (!AccountTodayCounter[key][mode].ContainsKey(item.Action.Value))
                    {
                        AccountTodayCounter[key][mode].TryAdd(item.Action.Value, 0);
                    }

                    var count = AccountTodayCounter[key][mode][item.Action.Value];
                    AccountTodayCounter[key][mode][item.Action.Value] += 1;
                }
            }
        }
    }
}