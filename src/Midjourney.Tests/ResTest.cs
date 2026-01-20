using System.Text.Json;
using Midjourney.License.Official;
using Xunit.Abstractions;

namespace Midjourney.Tests
{
    public class ResTest : BaseTests
    {
        private readonly TestOutputWrapper _output;

        public ResTest(ITestOutputHelper output)
        {
            _output = new TestOutputWrapper(output);
        }

        [Fact]
        public void TestRes()
        {
            var jsResult = """
                {"success":[],"failure":[{"type":"banned_prompt_detected","message":"Sorry! Our AI moderator thinks this prompt is probably against our community standards.\n\nPlease review our current community standards:\n\n**ALLOWED**\n- Any image up to PG-13 rating involving fiction, fantasy, mythology.\n- Real images that may be seen as respectful or light-hearted parodies, satire, caricatures\n- Imaginary or exaggerated real-life scenarios, including absurd or humorous situations.\n\n**NOT ALLOWED**\n- Disrespectful, harmful, misleading public figures/events portrayals or potential to mislead.\n- Hate speech, explicit or real-world violence.\n- Nudity or unconsented overtly sexualized public figures.\n- Imagery that might be considered culturally insensitive\n\nThis AI system isn't perfect. If you find it rejecting something innocent please press the **Notify Developers** button and we will review it and try to further improve our performance. Thank you for your help!","extra":{"banned_word":"hi","reason":null,"custom_error_msg":null,"weight":1,"flags":0,"clean_prompt":"texture, oil painting, elegant, cold, five senses, japanese, cold, straight face, darkness, dark, oil painting, weird and beautiful, extreme detail, dream core, oil painting, japanese, pale, slender, collarbone, despair, upper body, japanese, thin, full body, eyes open, japanese, sick, pale, thin, dark, dream core, contrast of light and dark, broken, injured, girl with black hair, handsome, green pupil, black long dress, beige top, fall, fall to the ground holding her knees, girl with short hair, collarbone, whole body, holding her knees, kneeling on the ground, big scene, face exposed, kneeling on one knee, white clothes","full_prompt":"Texture, oil painting, elegant, cold, five senses, Japanese, cold, straight face, darkness, dark, oil painting, weird and beautiful, extreme detail, dream core, oil painting, Japanese, pale, slender, collarbone, despair, upper body, Japanese, thin, full body, eyes open, Japanese, sick, pale, thin, dark, dream core, contrast of light and dark, broken, injured, girl with black hair, handsome, green pupil, black long dress, beige top, Fall, fall to the ground holding her knees, girl with short hair, collarbone, whole body, holding her knees, kneeling on the ground, big scene, face exposed, kneeling on one knee, white clothes --niji 6 --ar 9:16 --iw 0.2 --fast","allow_appeal":false,"reasoning":null},"optimisticJobIndex":0,"job_id":"e858c14b-51be-4ce2-aab5-4aa61558ecc4"}]}
                """;

            var apiResponse = JsonSerializer.Deserialize<SubmitResponse>(jsResult, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            var f = apiResponse?.Failure?.FirstOrDefault();
            var kw = f?.GetBannedWord();

            Assert.NotNull(apiResponse);
            Assert.Equal("hi", kw);
        }
    }
}