using Midjourney.Base;
using Midjourney.Base.Models;

namespace Midjourney.Services
{
    /// <summary>
    /// 账号选择判定逻辑（纯内存）。
    /// 所有 Redis 运行态数据由 <see cref="IAccountRuntimeValues"/> 提供，
    /// 逐账号实时查询与批量快照共用同一套判定，避免两套逻辑出现差异。
    /// </summary>
    internal static class AccountSelectionEvaluator
    {
        /// <summary>
        /// 获取队列任务数：快速队列 + 悠船慢速队列 + 图生文队列。
        /// </summary>
        /// <param name="acc">账号。</param>
        /// <param name="values">运行态数据。</param>
        /// <returns>队列任务数。</returns>
        internal static int GetQueueTaskCount(DiscordAccount acc, IAccountRuntimeValues values)
        {
            var count = values.FastQueueCount;

            if (acc.IsYouChuan)
            {
                count += values.RelaxQueueCount;
            }

            count += values.DescribeQueueCount;
            return count;
        }

        /// <summary>
        /// 获取正在运行的并发任务数：快速并发 + 悠船慢速并发 + 图生文并发。
        /// </summary>
        /// <param name="acc">账号。</param>
        /// <param name="values">运行态数据。</param>
        /// <returns>并发任务数。</returns>
        internal static int GetRunningTaskCount(DiscordAccount acc, IAccountRuntimeValues values)
        {
            var count = values.FastConcurrency;

            if (acc.IsYouChuan)
            {
                count += values.RelaxConcurrency;
            }

            count += values.DescribeConcurrency;
            return count;
        }

        /// <summary>
        /// 判断悠船是否允许继续使用慢速。
        /// </summary>
        /// <param name="acc">账号。</param>
        /// <param name="values">运行态数据。</param>
        /// <returns>是否允许慢速。</returns>
        internal static bool IsYouChuanAllowRelax(DiscordAccount acc, IAccountRuntimeValues values)
        {
            if (acc.IsYouChuan)
            {
                if (acc.YouChuanRelaxedReset > DateTime.Now.Date)
                {
                    // 已上限
                    return false;
                }

                // 悠船每日限制慢速
                var relaxCount = values.RelaxCount;
                var limit = acc.YouChuanRelaxDailyLimit;

                if (relaxCount < limit - acc.RelaxCoreSize - acc.RelaxQueueSize)
                {
                    return true;
                }
                else if (relaxCount >= limit)
                {
                    return false;
                }
                else
                {
                    // 精确计算剩余次数
                    var currentCore = values.RelaxConcurrency;
                    var currentQueue = values.RelaxQueueCount;
                    if (relaxCount + currentCore + currentQueue < limit)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// 根据速度模式判断是否允许继续绘图，不判断放大任务。
        /// </summary>
        /// <param name="acc">账号。</param>
        /// <param name="mode">速度模式。</param>
        /// <param name="values">运行态数据。</param>
        /// <returns>是否允许继续。</returns>
        internal static bool IsAllowContinue(DiscordAccount acc, GenerationSpeedMode mode, IAccountRuntimeValues values)
        {
            var isContinue = true;

            // 使用数据库判断指定模式是否允许绘图
            if (isContinue)
            {
                // 首先判断允许速度
                if (acc.AllowModes.Count > 0 && !acc.AllowModes.Contains(mode))
                {
                    isContinue = false;
                }
                else
                {
                    // 如果是固定速度，则强制修改速度为固定速度
                    if (acc.Mode != null)
                    {
                        mode = acc.Mode.Value;
                    }

                    // 判断速度对应的额度是否满足
                    switch (mode)
                    {
                        case GenerationSpeedMode.RELAX:
                            {
                                if (acc.IsYouChuan)
                                {
                                    isContinue = acc.YouChuanRelaxedReset == null || acc.YouChuanRelaxedReset <= DateTime.Now.Date;
                                }
                                else
                                {
                                    isContinue = true;
                                }
                            }
                            break;

                        case GenerationSpeedMode.FAST:
                            {
                                isContinue = values.FastAvailableCount > 6;
                            }
                            break;

                        case GenerationSpeedMode.TURBO:
                            {
                                isContinue = values.FastAvailableCount > 12;
                            }
                            break;

                        default:
                            break;
                    }
                }
            }

            // 判断空闲队列
            if (isContinue)
            {
                // 悠船慢速队列验证
                if (acc.IsYouChuan && mode == GenerationSpeedMode.RELAX)
                {
                    var queueCount = values.RelaxQueueCount;

                    // 判断 RELAX 队列是否有空闲
                    isContinue = acc.RelaxQueueSize <= 0 || queueCount < acc.RelaxQueueSize;
                }
                else
                {
                    // 快速队列验证
                    var defaultOrFastQueueCount = values.FastQueueCount;
                    isContinue = acc.QueueSize <= 0 || defaultOrFastQueueCount < acc.QueueSize;
                }
            }

            // 判断日绘图限制
            if (isContinue)
            {
                // 慢速
                if (mode == GenerationSpeedMode.RELAX)
                {
                    if (acc.DayRelaxDrawLimit <= -1)
                    {
                        isContinue = true;
                    }
                    else
                    {
                        var todayCount = values.TodayCount(mode);
                        isContinue = todayCount < acc.DayRelaxDrawLimit;
                    }
                }
                // 快速 | 极速
                else
                {
                    if (acc.DayDrawLimit <= -1)
                    {
                        isContinue = true;
                    }
                    else
                    {
                        var todayCount = values.TodayCount(mode);
                        isContinue = todayCount < acc.DayDrawLimit;
                    }
                }
            }

            // 根据计数器精确判断
            if (isContinue)
            {
                // 如果慢速模式，只有悠船才判断慢速次数
                if (mode == GenerationSpeedMode.RELAX)
                {
                    if (acc.IsYouChuan)
                    {
                        isContinue = IsYouChuanAllowRelax(acc, values);
                    }
                    else
                    {
                        isContinue = true;
                    }
                }
                else
                {
                    // 如果是快速，则判断快速是否足够次数
                    // 快速至少 > 3 次
                    // 暂不考虑极速的次数、暂不考虑视频的次数

                    if (values.FastAvailableCount > 3 && values.FastAvailableCount > acc.CoreSize + acc.QueueCount)
                    {
                        isContinue = true;
                    }
                    else
                    {
                        // 精确计算
                        var currentCore = values.FastConcurrency;
                        var currentQueue = values.FastQueueCount;
                        if (values.FastAvailableCount > 3 && values.FastAvailableCount > currentCore + currentQueue)
                        {
                            isContinue = true;
                        }
                        else
                        {
                            isContinue = false;
                        }
                    }
                }
            }

            return isContinue;
        }

        /// <summary>
        /// 判断是否允许图生文。
        /// </summary>
        /// <param name="acc">账号。</param>
        /// <param name="values">运行态数据。</param>
        /// <returns>是否允许图生文。</returns>
        internal static bool IsAllowDescribe(DiscordAccount acc, IAccountRuntimeValues values)
        {
            if (acc.IsDescribe)
            {
                // 官方/discord 账号不限制图生文
                if (acc.IsOfficial || acc.IsDiscord)
                {
                    return true;
                }

                // 悠船每日 200 限制
                var limit = 200;
                if (acc.IsYouChuan)
                {
                    if (acc.YouChuanPicreadReset > DateTime.Now.Date)
                    {
                        // 已上限
                        return false;
                    }

                    var describeCount = values.DescribeCount;
                    if (describeCount < limit - acc.CoreSize - acc.QueueSize)
                    {
                        return true;
                    }
                    else if (describeCount >= limit)
                    {
                        return false;
                    }
                    else
                    {
                        // 精确计算剩余次数
                        var currentCore = values.DescribeConcurrency;
                        var currentQueue = values.DescribeQueueCount;
                        if (describeCount + currentCore + currentQueue < limit)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 判断是否允许生成视频。
        /// </summary>
        /// <param name="acc">账号。</param>
        /// <param name="mode">速度模式。</param>
        /// <param name="values">运行态数据。</param>
        /// <returns>是否允许生成视频。</returns>
        internal static bool IsAllowGenerateVideo(DiscordAccount acc, GenerationSpeedMode mode, IAccountRuntimeValues values)
        {
            var videoUseCount = 8;
            if (mode == GenerationSpeedMode.TURBO)
            {
                videoUseCount *= 2;
            }

            // 如果是悠船账号
            if (acc.IsYouChuan)
            {
                if (mode == GenerationSpeedMode.RELAX)
                {
                    return false;
                }

                return values.FastAvailableCount > videoUseCount;
            }

            // 如果是官方账号
            if (acc.IsOfficial)
            {
                if (mode == GenerationSpeedMode.RELAX)
                {
                    if (!acc.IsRelaxVideo)
                    {
                        return false;
                    }
                }

                return values.FastAvailableCount > videoUseCount || acc.IsRelaxVideo;
            }

            // Discord 账号
            if (acc.IsDiscord)
            {
                if (mode == GenerationSpeedMode.RELAX)
                {
                    if (!acc.IsRelaxVideo)
                    {
                        return false;
                    }

                    return acc.IsRelaxVideo;
                }

                return values.FastAvailableCount > videoUseCount || acc.IsRelaxVideo;
            }

            return false;
        }
    }
}
