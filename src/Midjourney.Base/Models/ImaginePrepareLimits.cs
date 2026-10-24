using System;
using System.Threading;

namespace Midjourney.Base.Models
{
    /// <summary>
    /// 异步垫图准备阶段的统一限流与资源上限。
    /// 网络 I/O 与 CPU 转换分开限流，最小 4 个，每增加 1 个核心 +2，最大 32 个。
    /// 可通过环境变量 MJ_IMAGINE_PREPARE_CONCURRENCY 覆盖并发数。
    /// </summary>
    public static class ImaginePrepareLimits
    {
        /// <summary>
        /// 每增加 1 个核心的并发增量
        /// </summary>
        public const int DefaultPerCore = 2;

        /// <summary>
        /// 并发数下限
        /// </summary>
        public const int MinConcurrency = 4;

        /// <summary>
        /// 并发数上限
        /// </summary>
        public const int MaxConcurrency = 32;

        /// <summary>
        /// 并发数覆盖环境变量
        /// </summary>
        public const string ConcurrencyEnvironmentVariable = "MJ_IMAGINE_PREPARE_CONCURRENCY";

        /// <summary>
        /// 准备阶段 URL 抓取的最大超时
        /// </summary>
        public static readonly TimeSpan UrlFetchTimeout = TimeSpan.FromMinutes(10);

        /// <summary>
        /// 准备阶段单文件最大字节数（24MB）
        /// </summary>
        public const long UrlMaxBytes = 24L * 1024 * 1024;

        /// <summary>
        /// 当前进程的准备阶段并发数
        /// </summary>
        public static int Concurrency { get; } = ResolveConcurrency(
            Environment.ProcessorCount,
            Environment.GetEnvironmentVariable(ConcurrencyEnvironmentVariable));

        /// <summary>
        /// 网络 I/O 限流（URL 抓取、上传）
        /// </summary>
        public static SemaphoreSlim Network { get; } = new(Concurrency, Concurrency);

        /// <summary>
        /// CPU 限流（图片解码/WebP 转换）
        /// </summary>
        public static SemaphoreSlim Cpu { get; } = new(Concurrency, Concurrency);

        /// <summary>
        /// 计算准备阶段并发数：最小 4 个，每增加 1 个核心 +2，最多 32 个；环境变量可覆盖。
        /// </summary>
        public static int ResolveConcurrency(int processorCount, string configured)
        {
            if (int.TryParse(configured, out var value) && value > 0)
            {
                return Math.Clamp(value, MinConcurrency, MaxConcurrency);
            }

            var cores = Math.Max(1, processorCount);
            var concurrency = MinConcurrency + (cores - 1) * DefaultPerCore;
            return Math.Clamp(concurrency, MinConcurrency, MaxConcurrency);
        }
    }
}
