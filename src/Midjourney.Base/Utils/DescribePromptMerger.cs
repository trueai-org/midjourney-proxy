using System;
using System.Collections.Generic;
using System.Linq;

namespace Midjourney.Base.Util
{
    /// <summary>
    /// 图生文（Describe）提示词合并器
    /// <para>一次图生文任务会并行调用多次上游接口，每次返回一组候选提示词，需要按批次顺序合并、去重并截断。</para>
    /// </summary>
    public static class DescribePromptMerger
    {
        /// <summary>
        /// 合并多批图生文提示词
        /// <para>按 <paramref name="batches"/> 的枚举顺序展开所有字符串；每个元素先 Trim，null 或空白项丢弃；
        /// 按 <see cref="StringComparer.OrdinalIgnoreCase"/> 忽略大小写去重并保留首次出现的文本；最终最多保留 <paramref name="max"/> 条。</para>
        /// </summary>
        /// <param name="batches">各批次上游返回的提示词数组，批次本身及数组元素都可能为 null</param>
        /// <param name="max">最多保留的提示词条数，默认 4；小于等于 0 时视为无效参数</param>
        /// <returns>合并去重后的提示词数组；批次集合为 null、参数非法或无有效内容时返回空数组（不返回 null）</returns>
        public static string[] Merge(IEnumerable<string[]> batches, int max = 4)
        {
            if (batches == null || max <= 0)
            {
                return Array.Empty<string>();
            }

            // 按 OrdinalIgnoreCase 记录已出现的文本，用于忽略大小写去重
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();

            // 依次展开各批次，保持批次顺序与批次内顺序
            foreach (var item in batches.Where(batch => batch != null).SelectMany(batch => batch))
            {
                if (string.IsNullOrWhiteSpace(item))
                {
                    continue;
                }

                var text = item.Trim();

                // 已出现过（忽略大小写）则跳过，保留首次出现的文本
                if (!seen.Add(text))
                {
                    continue;
                }

                result.Add(text);

                // 达到上限后立即停止，避免继续处理剩余批次
                if (result.Count >= max)
                {
                    break;
                }
            }

            return result.ToArray();
        }
    }
}
