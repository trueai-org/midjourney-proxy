using Midjourney.Base.Models;

namespace Midjourney.Tests
{
    public class ImaginePrepareLimitsTests
    {
        [Theory]
        [InlineData(1, null, 4)]
        [InlineData(2, null, 6)]
        [InlineData(3, null, 8)]
        [InlineData(8, null, 18)]
        [InlineData(15, null, 32)]
        [InlineData(16, null, 32)]
        [InlineData(64, null, 32)]
        public void ResolveConcurrency_StartsAtFourAndAddsTwoPerAdditionalCoreCappedAtThirtyTwo(int processorCount, string configured, int expected)
        {
            Assert.Equal(expected, ImaginePrepareLimits.ResolveConcurrency(processorCount, configured));
        }

        [Theory]
        [InlineData("1", 4)]
        [InlineData("2", 4)]
        [InlineData("4", 4)]
        [InlineData("6", 6)]
        [InlineData("32", 32)]
        [InlineData("100", 32)]
        [InlineData("0", 10)]
        [InlineData("-1", 10)]
        [InlineData("abc", 10)]
        [InlineData("", 10)]
        public void ResolveConcurrency_UsesConfiguredValueWithinBounds(string configured, int expected)
        {
            Assert.Equal(expected, ImaginePrepareLimits.ResolveConcurrency(4, configured));
        }

        [Fact]
        public void NetworkAndCpu_UseSeparateSemaphores()
        {
            Assert.NotSame(ImaginePrepareLimits.Network, ImaginePrepareLimits.Cpu);
        }

        [Fact]
        public void UrlFetchTimeout_IsTenMinutes()
        {
            Assert.Equal(TimeSpan.FromMinutes(10), ImaginePrepareLimits.UrlFetchTimeout);
        }

        [Fact]
        public void UrlMaxBytes_IsTwentyFourMegabytes()
        {
            Assert.Equal(24L * 1024 * 1024, ImaginePrepareLimits.UrlMaxBytes);
        }
    }
}
