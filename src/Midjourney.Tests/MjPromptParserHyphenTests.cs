using Midjourney.Base.Util;
using Xunit;

namespace Midjourney.Tests
{
    public class MjPromptParserHyphenTests
    {
        [Fact]
        public void EditReferenceWithHyphenatedUrlMatchesTheDiscordEcho()
        {
            // Submitted prompt: reference URLs carry hyphens in host and path.
            var submitted = "a cat reading --sref https://hurricane-prod-postgate.tos-cn-shanghai.volces.com/a-b/c-d.png?X-Tos-Date=1 --edit https://images.unsplash.com/photo-1514888286974-6c03e2ca1dba?w=600 --ar 1:1 --v 8.2";
            // Discord echoes shortened links and appends --fast and --seed.
            var echoed = "a cat reading --sref <https://s.mj.run/pUN3o8dAj-A> --edit <https://s.mj.run/rvU3do2TM9A> --ar 1:1 --v 8.2 --fast --seed 1293697064";

            Assert.Equal("a cat reading", MjPromptParser.Parse(submitted).CleanPrompt);
            Assert.Equal(MjPromptParser.Parse(submitted).CleanPrompt, MjPromptParser.Parse(echoed).CleanPrompt);
        }

        [Fact]
        public void HyphenatedWordsInsideAValueStayInThatValue()
        {
            var result = MjPromptParser.Parse("portrait --no red-hat, blue-scarf --v 7");
            Assert.Equal("portrait", result.CleanPrompt);
            Assert.Contains(result.Parameters, p => p.Name == "no" && p.Value == "red-hat, blue-scarf");
        }
    }
}
