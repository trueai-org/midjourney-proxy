using Midjourney.Base.Util;
using Xunit;

namespace Midjourney.Tests
{
    /// <summary>
    /// <see cref="DescribePromptMerger"/> 的单元测试
    /// </summary>
    public class DescribePromptMergerTests
    {
        /// <summary>
        /// 4 组合并结果应保持批次顺序和批次内顺序
        /// </summary>
        [Fact]
        public void Merge_NormalBatches_ShouldKeepOrder()
        {
            var batches = new[]
            {
                new[] { "a cat", "a dog" },
                new[] { "a bird", "a fish" }
            };

            var result = DescribePromptMerger.Merge(batches);

            Assert.Equal(new[] { "a cat", "a dog", "a bird", "a fish" }, result);
        }

        /// <summary>
        /// 大小写不同、首尾空格不同的重复文本只保留首次出现的一条
        /// </summary>
        [Fact]
        public void Merge_DuplicatesIgnoringCaseAndSpaces_ShouldKeepFirst()
        {
            var batches = new[]
            {
                new[] { "A cat", "a dog" },
                new[] { "  a CAT  ", "A Dog", "a bird" }
            };

            var result = DescribePromptMerger.Merge(batches);

            // 保留首次出现的原始文本（已 Trim），重复项丢弃
            Assert.Equal(new[] { "A cat", "a dog", "a bird" }, result);
        }

        /// <summary>
        /// 空数组、null 批次和空白字符串都应被丢弃，且不影响有效项
        /// </summary>
        [Fact]
        public void Merge_EmptyAndWhitespaceItems_ShouldBeDropped()
        {
            var batches = new[]
            {
                Array.Empty<string>(),
                null!,
                new[] { "", "   ", null!, "a cat" },
                new[] { "\t", "a dog" }
            };

            var result = DescribePromptMerger.Merge(batches);

            Assert.Equal(new[] { "a cat", "a dog" }, result);
        }

        /// <summary>
        /// 有效条目超过 max 时应按顺序截断，只保留前 max 条
        /// </summary>
        [Fact]
        public void Merge_MoreThanMax_ShouldTruncate()
        {
            var batches = new[]
            {
                new[] { "p1", "p2", "p3" },
                new[] { "p4", "p5" }
            };

            var result = DescribePromptMerger.Merge(batches, 3);

            Assert.Equal(new[] { "p1", "p2", "p3" }, result);
        }

        /// <summary>
        /// batches 为 null 时应返回空数组
        /// </summary>
        [Fact]
        public void Merge_NullBatches_ShouldReturnEmpty()
        {
            var result = DescribePromptMerger.Merge(null!);

            Assert.Empty(result);
            Assert.NotNull(result);
        }

        /// <summary>
        /// max 小于等于 0 时应返回空数组
        /// </summary>
        /// <param name="max">非法的条数上限</param>
        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void Merge_NonPositiveMax_ShouldReturnEmpty(int max)
        {
            var batches = new[] { new[] { "a cat" } };

            var result = DescribePromptMerger.Merge(batches, max);

            Assert.Empty(result);
        }
    }
}
