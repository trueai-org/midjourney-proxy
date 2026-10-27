using Midjourney.Base.Models;

namespace Midjourney.Tests
{
    /// <summary>
    /// 输入链接白名单（Setting.ConvertPromptLinkWhitelistDomains / IsPromptLinkWhitelisted）的单元测试。
    /// 只覆盖纯内存的域名匹配逻辑，不发起任何网络请求。
    /// </summary>
    public class SettingPromptLinkWhitelistTests
    {
        /// <summary>
        /// 创建带指定白名单配置的 Setting 实例。
        /// </summary>
        /// <param name="domains">白名单配置项；为 null 时用于验证 null 语义</param>
        /// <returns>用于测试的 Setting 实例</returns>
        private static Setting Create(List<string> domains) => new() { ConvertPromptLinkWhitelistDomains = domains };

        [Fact]
        public void NotConfigured_ReturnsFalse()
        {
            Assert.False(Create(new List<string>()).IsPromptLinkWhitelisted("https://cdn.huanwangai.com/a.webp"));
            Assert.False(Create(null!).IsPromptLinkWhitelisted("https://cdn.huanwangai.com/a.webp"));
            Assert.False(new Setting().IsPromptLinkWhitelisted("https://cdn.huanwangai.com/a.webp"));
        }

        [Fact]
        public void ExactHost_ReturnsTrue()
        {
            var setting = Create(new List<string> { "cdn.huanwangai.com" });

            Assert.True(setting.IsPromptLinkWhitelisted("https://cdn.huanwangai.com/attachments/2026/10/25/xxx.webp"));
        }

        [Theory]
        [InlineData("https://CDN.HuanWangAI.com/attachments")]
        [InlineData("cdn.huanwangai.com:443")]
        [InlineData("cdn.huanwangai.com.")]
        [InlineData("cdn.huanwangai.com")]
        public void ConfiguredForms_AreNormalizedToHost(string configured)
        {
            var setting = Create(new List<string> { configured });

            Assert.True(setting.IsPromptLinkWhitelisted("https://cdn.huanwangai.com/attachments/2026/10/25/xxx.webp"));
        }

        [Fact]
        public void Subdomain_DoesNotMatch()
        {
            var setting = Create(new List<string> { "cdn.huanwangai.com" });

            Assert.False(setting.IsPromptLinkWhitelisted("https://img.cdn.huanwangai.com/a.webp"));
        }

        [Fact]
        public void SuffixForgery_DoesNotMatch()
        {
            var setting = Create(new List<string> { "cdn.huanwangai.com" });

            Assert.False(setting.IsPromptLinkWhitelisted("https://cdn.huanwangai.com.evil.com/a.webp"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not-a-url")]
        [InlineData("/attachments/2026/10/25/xxx.webp")]
        public void InvalidOrEmptyUrl_ReturnsFalse(string url)
        {
            var setting = Create(new List<string> { "cdn.huanwangai.com" });

            Assert.False(setting.IsPromptLinkWhitelisted(url));
            Assert.False(setting.IsPromptLinkWhitelisted(null!));
        }

        [Fact]
        public void BlankAndUnparsableEntries_AreIgnored()
        {
            var setting = Create(new List<string> { "", "   ", "bad domain", "cdn.huanwangai.com" });

            Assert.True(setting.IsPromptLinkWhitelisted("https://cdn.huanwangai.com/a.webp"));
            Assert.False(setting.IsPromptLinkWhitelisted("https://other.huanwangai.com/a.webp"));
        }

        [Fact]
        public void HostMatch_IsCaseInsensitive()
        {
            var setting = Create(new List<string> { "cdn.huanwangai.com" });

            Assert.True(setting.IsPromptLinkWhitelisted("https://CDN.HUANWANGAI.COM/a.webp"));
        }
    }
}
