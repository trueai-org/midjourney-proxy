namespace Midjourney.Base
{
    /// <summary>
    /// 计数器
    /// </summary>
    public class CounterHelper
    {
        /// <summary>
        /// 增加图生文每日计数
        /// </summary>
        /// <param name="account"></param>
        /// <param name="incrementBy"></param>
        /// <returns></returns>
        public static int DescribeIncrement(DiscordAccount account, int incrementBy = 1)
        {
            var hashKeyPrefix = $"DescribeCount:{DateTime.Now:yyyyMMdd}";
            if (account == null || incrementBy == 0)
            {
                return RedisHelper.HGet<int>(hashKeyPrefix, account.ChannelId);
            }
            var count = (int)RedisHelper.HIncrBy(hashKeyPrefix, account.ChannelId, incrementBy);
            if (count % 10 == 1)
            {
                RedisHelper.ExpireAt(hashKeyPrefix, DateTime.Today.AddDays(7));
            }

            return count;
        }

        /// <summary>
        /// 获取图生文每日计数
        /// </summary>
        /// <param name="channelId"></param>
        /// <returns></returns>
        public static int GetDescribeCount(string channelId)
        {
            if (string.IsNullOrWhiteSpace(channelId))
            {
                return 0;
            }
            var hashKeyPrefix = $"DescribeCount:{DateTime.Now:yyyyMMdd}";
            return RedisHelper.HGet<int>(hashKeyPrefix, channelId);
        }

        /// <summary>
        /// 增加悠船慢速每日计数
        /// </summary>
        /// <param name="account"></param>
        /// <param name="incrementBy"></param>
        /// <returns></returns>
        public static int YouchuanRelaxIncrement(DiscordAccount account, int incrementBy = 1)
        {
            var hashKeyPrefix = $"YouchuanRelaxCount:{DateTime.Now:yyyyMMdd}";
            if (account == null || incrementBy == 0)
            {
                return RedisHelper.HGet<int>(hashKeyPrefix, account.ChannelId);
            }
            var count = (int)RedisHelper.HIncrBy(hashKeyPrefix, account.ChannelId, incrementBy);
            if (count % 10 == 1)
            {
                RedisHelper.ExpireAt(hashKeyPrefix, DateTime.Today.AddDays(7));
            }
            return count;
        }

        /// <summary>
        /// 获取悠船慢速每日计数
        /// </summary>
        /// <param name="channelId"></param>
        /// <returns></returns>
        public static int GetYouchuanRelaxCount(string channelId)
        {
            if (string.IsNullOrWhiteSpace(channelId))
            {
                return 0;
            }
            var hashKeyPrefix = $"YouchuanRelaxCount:{DateTime.Now:yyyyMMdd}";
            return RedisHelper.HGet<int>(hashKeyPrefix, channelId);
        }

        /// <summary>
        /// 获取悠船所有账号慢速每日计数字典
        /// </summary>
        /// <returns></returns>
        public static Dictionary<string, int> GetAllYouchuanRelaxCountDict()
        {
            var result = new Dictionary<string, int>();
            var hashKeyPrefix = $"YouchuanRelaxCount:{DateTime.Now:yyyyMMdd}";
            var hashAll = RedisHelper.HGetAll<int>(hashKeyPrefix);
            if (hashAll?.Count > 0)
            {
                foreach (var kvp in hashAll)
                {
                    result[kvp.Key] = kvp.Value;
                }
            }
            return result;
        }

        /// <summary>
        /// 设置快速任务可用计数
        /// </summary>
        /// <param name="instanceId"></param>
        /// <param name="count"></param>
        public static void SetFastTaskAvailableCount(string instanceId, int count)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                return;
            }

            var hashKeyPrefix = $"FastTaskAvailableCount:{DateTime.Now:yyyyMMdd}";
            RedisHelper.HSet(hashKeyPrefix, instanceId, count);

            RedisHelper.ExpireAt(hashKeyPrefix, DateTime.Today.AddDays(7));
        }

        /// <summary>
        /// 减少快速任务可用计数
        /// </summary>
        /// <param name="instanceId"></param>
        /// <param name="decrementBy">正整数</param>
        /// <returns></returns>
        public static int FastTaskAvailableDecrementCount(string instanceId, int decrementBy = 1)
        {
            if (string.IsNullOrWhiteSpace(instanceId) || decrementBy <= 0)
            {
                return GetFastTaskAvailableCount(instanceId);
            }
            var hashKeyPrefix = $"FastTaskAvailableCount:{DateTime.Now:yyyyMMdd}";
            return (int)RedisHelper.HIncrBy(hashKeyPrefix, instanceId, -decrementBy);
        }

        /// <summary>
        /// 获取快速任务可用计数
        /// </summary>
        /// <param name="instanceId"></param>
        /// <returns></returns>
        public static int GetFastTaskAvailableCount(string instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                return 0;
            }
            var hashKeyPrefix = $"FastTaskAvailableCount:{DateTime.Now:yyyyMMdd}";
            return RedisHelper.HGet<int>(hashKeyPrefix, instanceId);
        }

        /// <summary>
        /// 任务完成时，计数器 +1
        /// </summary>
        /// <param name="info"></param>
        /// <param name="success"></param>
        public static void TaskCompleteIncrement(TaskInfo info, bool success)
        {
            if (info == null || info.Action == null)
            {
                return;
            }

            var mode = info.Mode ?? GenerationSpeedMode.FAST;

            // 统计所有账号完成，不统计放大，包含失败/成功 - 用于日绘图限制业务
            if (info.Action != TaskAction.UPSCALE)
            {
                var allHashPrefix = $"TaskAccountAll:{DateTime.Now:yyyyMMdd}";
                var allTodayAllCount = (int)RedisHelper.HIncrBy(allHashPrefix, $"{mode}_{info.InstanceId}", 1);
                if (allTodayAllCount % 10 == 1)
                {
                    RedisHelper.ExpireAt(allHashPrefix, DateTime.Today.AddDays(7));
                }
            }

            // 统计账号成功 - 用于列表显示
            if (success)
            {
                var successHashPrefix = $"TaskAccountSuccess:{DateTime.Now:yyyyMMdd}:{info.InstanceId}";
                var accountTodaySuccessCount = (int)RedisHelper.HIncrBy(successHashPrefix, $"{mode}_{info.Action.Value}", 1);
                if (accountTodaySuccessCount % 10 == 1)
                {
                    RedisHelper.ExpireAt(successHashPrefix, DateTime.Today.AddDays(7));
                }
            }

            // 统计个人成功 - 用于列表显示
            if (success)
            {
                var userSuccessHashPrefix = $"TaskUserSuccess:{DateTime.Now:yyyyMMdd}:{info.UserId}";
                var userTodaySuccessCount = (int)RedisHelper.HIncrBy(userSuccessHashPrefix, $"{mode}_{info.Action.Value}", 1);
                if (userTodaySuccessCount % 10 == 1)
                {
                    RedisHelper.ExpireAt(userSuccessHashPrefix, DateTime.Today.AddDays(7));
                }
            }
        }

        /// <summary>
        /// 获取账号今日绘图总数（不包含放大、包含失败）
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

            var hashKeyPrefix = $"TaskAccountAll:{DateTime.Now:yyyyMMdd}";
            var key = $"{mode}_{instanceId}";
            if (mode == GenerationSpeedMode.RELAX)
            {
                return RedisHelper.HGet<int>(hashKeyPrefix, key);
            }
            else
            {
                // 快速 + 极速
                var fastCount = RedisHelper.HGet<int>(hashKeyPrefix, $"{GenerationSpeedMode.FAST}_{instanceId}");
                var turboCount = RedisHelper.HGet<int>(hashKeyPrefix, $"{GenerationSpeedMode.TURBO}_{instanceId}");
                return fastCount + turboCount;
            }
        }

        /// <summary>
        /// 获取所有账号今日绘图总数字典（不包含放大，包含失败） - 用于日绘图限制业务
        /// </summary>
        /// <returns></returns>
        public static Dictionary<string, Dictionary<GenerationSpeedMode, int>> GetAllAccountTodayTotalCountDict()
        {
            var result = new Dictionary<string, Dictionary<GenerationSpeedMode, int>>();
            var hashKeyPrefix = $"TaskAccountAll:{DateTime.Now:yyyyMMdd}";
            var hashAll = RedisHelper.HGetAll<int>(hashKeyPrefix);
            if (hashAll?.Count > 0)
            {
                foreach (var kvp in hashAll)
                {
                    var key = kvp.Key;
                    var keyParts = key.Split('_');
                    var value = kvp.Value;
                    if (keyParts.Length == 2 &&
                        Enum.TryParse<GenerationSpeedMode>(keyParts[0], out var mode))
                    {
                        var instanceId = keyParts[1];
                        if (!result.ContainsKey(instanceId))
                        {
                            result[instanceId] = [];
                        }
                        result[instanceId][mode] = value;
                    }
                }
            }
            return result;
        }



        /// <summary>
        /// 获取用户今日绘图总数
        /// </summary>
        /// <param name="userId"></param>
        /// <returns></returns>
        public static int GetUserTodayTotalCount(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return 0;
            }
            var hashKeyPrefix = $"TaskUserSuccess:{DateTime.Now:yyyyMMdd}:{userId}";
            var hashAll = RedisHelper.HGetAll<int>(hashKeyPrefix);
            if (hashAll?.Count > 0)
            {
                return hashAll.Values.Sum();
            }
            return 0;
        }

        /// <summary>
        /// 获取账号今日成功绘图字典
        /// </summary>
        /// <param name="instanceId"></param>
        /// <returns></returns>
        public static Dictionary<GenerationSpeedMode, Dictionary<TaskAction, int>> GetAccountTodaySuccessCountDict(string instanceId)
        {
            var result = new Dictionary<GenerationSpeedMode, Dictionary<TaskAction, int>>();
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                return result;
            }
            var hashKeyPrefix = $"TaskAccountSuccess:{DateTime.Now:yyyyMMdd}:{instanceId}";
            var hashAll = RedisHelper.HGetAll<int>(hashKeyPrefix);
            if (hashAll?.Count > 0)
            {
                foreach (var kvp in hashAll)
                {
                    var keyParts = kvp.Key.Split('_');
                    if (keyParts.Length == 2 &&
                        Enum.TryParse<GenerationSpeedMode>(keyParts[0], out var mode) &&
                        Enum.TryParse<TaskAction>(keyParts[1], out var action))
                    {
                        if (!result.ContainsKey(mode))
                        {
                            result[mode] = [];
                        }

                        result[mode][action] = kvp.Value;
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// 获取账号今日成功绘图总数
        /// </summary>
        /// <param name="instanceId"></param>
        /// <param name="mode"></param>
        /// <returns></returns>
        public static int GetAccountTodaySuccessTotalCount(string instanceId, GenerationSpeedMode mode)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                return 0;
            }
            var hashKeyPrefix = $"TaskAccountSuccess:{DateTime.Now:yyyyMMdd}:{instanceId}";
            var hashAll = RedisHelper.HGetAll<int>(hashKeyPrefix);
            if (hashAll?.Count > 0)
            {
                int total = 0;
                foreach (var kvp in hashAll)
                {
                    var keyParts = kvp.Key.Split('_');
                    if (keyParts.Length == 2 &&
                        Enum.TryParse<GenerationSpeedMode>(keyParts[0], out var keyMode) &&
                        keyMode == mode)
                    {
                        total += kvp.Value;
                    }
                }
                return total;
            }
            return 0;
        }

        /// <summary>
        /// 获取账号今日总绘图
        /// </summary>
        /// <param name="instanceId"></param>
        /// <returns></returns>
        public static int GetAccountTodaySuccessTotalCount(string instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                return 0;
            }
            var hashKeyPrefix = $"TaskAccountSuccess:{DateTime.Now:yyyyMMdd}:{instanceId}";
            var hashAll = RedisHelper.HGetAll<int>(hashKeyPrefix);
            if (hashAll?.Count > 0)
            {
                return hashAll.Values.Sum();
            }
            return 0;
        }

        /// <summary>
        /// 账号同步累计失败次数，1小时超过 10 次禁用账号
        /// </summary>
        /// <param name="instanceId"></param>
        /// <param name="incrementBy"></param>
        /// <returns></returns>
        public static int IncrementAccountSyncFailure(string instanceId, int incrementBy = 1)
        {
            var key = $"AccountSyncFailureCount:{instanceId}-{DateTime.Now:yyyyMMddHH}";
            var count = (int)RedisHelper.IncrBy(key, incrementBy);

            // 设置过期时间为1小时
            RedisHelper.Expire(key, TimeSpan.FromHours(1));

            return count;
        }
    }
}