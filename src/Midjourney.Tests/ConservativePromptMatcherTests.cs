using Midjourney.Base.Util;

namespace Midjourney.Tests
{
    public class ConservativePromptMatcherTests
    {
        [Fact]
        public void LongBotTruncation_ShouldMatch()
        {
            var shared = string.Join(" ", Enumerable.Repeat(
                "cinematic wide shot with layered atmospheric detail", 12));
            var submitted = $"{shared} final subject detail --ar 16:9 --v 8.1";
            var returned = $"{shared}...";

            Assert.True(ConservativePromptMatcher.IsMatch(submitted, returned));
        }

        [Fact]
        public void ShortPrefix_ShouldNotMatch()
        {
            Assert.False(ConservativePromptMatcher.IsMatch(
                "cinematic portrait with soft light",
                "cinematic portrait..."));
        }

        [Fact]
        public void LowCoverageLongPrefix_ShouldNotMatch()
        {
            var returned = new string('a', 300);
            var submitted = returned + new string('b', 400);

            Assert.False(ConservativePromptMatcher.IsMatch(submitted, returned));
        }

        [Fact]
        public void DivergentLongPrompts_ShouldNotMatch()
        {
            var left = string.Join(" ", Enumerable.Repeat(
                "cinematic wide shot with layered atmospheric detail", 8));
            var right = new string('x', left.Length);

            Assert.False(ConservativePromptMatcher.IsMatch(left, right));
        }

        [Fact]
        public void ParametersAndWhitespace_ShouldBeIgnored()
        {
            var shared = string.Join(" ", Enumerable.Repeat(
                "cinematic wide shot with layered atmospheric detail", 12));

            Assert.True(ConservativePromptMatcher.IsMatch(
                $"  {shared}   --ar 16:9 --v 8.1",
                $"{shared}..."));
        }
    }
}
