using Midjourney.Base.Models;
using Midjourney.Base.Util;

namespace Midjourney.Tests
{
    public class ConservativeTaskMatcherTests
    {
        [Fact]
        public void UniqueSeed_ShouldMatchTaskAfterPromptRewrite()
        {
            var target = new TaskInfo
            {
                Id = "target",
                Seed = "81989239",
                PromptEn = "original verbose prompt --p m7478300276063993880"
            };
            var tasks = new[]
            {
                new TaskInfo { Id = "other", Seed = "1776363240" },
                target
            };

            var match = ConservativeTaskMatcher.FindUniqueBySeed(
                tasks, "81989239", out var matchCount);

            Assert.Same(target, match);
            Assert.Equal(1, matchCount);
        }

        [Fact]
        public void DuplicateSeed_ShouldRemainUnmatched()
        {
            var tasks = new[]
            {
                new TaskInfo { Id = "first", Seed = "81989239" },
                new TaskInfo { Id = "second", Seed = "81989239" }
            };

            var match = ConservativeTaskMatcher.FindUniqueBySeed(
                tasks, "81989239", out var matchCount);

            Assert.Null(match);
            Assert.Equal(2, matchCount);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void MissingSeed_ShouldRemainUnmatched(string? seed)
        {
            var tasks = new[]
            {
                new TaskInfo { Id = "task", Seed = "81989239" }
            };

            var match = ConservativeTaskMatcher.FindUniqueBySeed(
                tasks, seed, out var matchCount);

            Assert.Null(match);
            Assert.Equal(0, matchCount);
        }
    }
}
