using System.Text.Json;
using Midjourney.Base;
using Midjourney.Base.Data;
using Midjourney.Base.Models;
using Newtonsoft.Json.Linq;

namespace Midjourney.Tests
{
    public class ImagineInputStagerTests
    {
        private static TaskInfo NewInfo()
        {
            return new TaskInfo
            {
                Id = "task-1",
                Prompt = "cat",
                PromptEn = "cat",
                Description = "/imagine cat",
            };
        }

        [Fact]
        public void Stage_UrlOnly_AddsPromptWithoutRedis()
        {
            var info = NewInfo();
            var setCalled = false;

            var result = ImagineInputStager.Stage(
                info,
                [new DataUrl { Url = "https://example.com/a.png" }],
                3600,
                (key, bytes, ttl) => { setCalled = true; return true; });

            Assert.True(result.Success);
            Assert.False(setCalled);
            Assert.Equal("https://example.com/a.png cat", info.Prompt);
            Assert.Single(result.UrlOnly);
            Assert.Empty(result.Refs);
            Assert.Null(info.GetProperty(Constants.TASK_PROPERTY_IMAGINE_INPUTS));
        }

        [Fact]
        public void Stage_UrlOnlyAsRefs_DoesNotModifyPrompt()
        {
            var info = NewInfo();

            var result = ImagineInputStager.Stage(
                info,
                [new DataUrl { Url = "https://example.com/a.png", MimeType = "image/png" }],
                3600,
                (key, bytes, ttl) => true,
                urlOnlyAsPrompt: false);

            Assert.True(result.Success);
            Assert.Empty(result.StagedKeys);
            Assert.Single(result.Refs);
            Assert.Equal("https://example.com/a.png", result.Refs[0].Url);
            Assert.Equal(0, result.Refs[0].Index);
            Assert.Equal("cat", info.Prompt);
            Assert.Equal(true, info.GetProperty(Constants.TASK_PROPERTY_IMAGINE_NEED_PREPARE));
        }

        [Fact]
        public void Stage_NullSlot_PreservesInputIndex()
        {
            var info = NewInfo();
            var written = new List<string>();

            var result = ImagineInputStager.Stage(
                info,
                [null, new DataUrl { MimeType = "image/png", Data = [1, 2, 3] }],
                3600,
                (key, bytes, ttl) => { written.Add(key); return true; },
                urlOnlyAsPrompt: false);

            Assert.True(result.Success);
            Assert.Equal(["mj:imagine:input:task-1:1"], written);
            Assert.Equal(1, result.Refs[0].Index);
        }

        [Fact]
        public void Stage_Base64_WritesBytesAndProperties()
        {
            var info = NewInfo();
            var bytes = new byte[] { 1, 2, 3 };
            var written = new Dictionary<string, byte[]>();

            var result = ImagineInputStager.Stage(
                info,
                [new DataUrl { MimeType = "image/png", Data = bytes }],
                3600,
                (key, value, ttl) => { written[key] = value; return true; });

            Assert.True(result.Success);
            Assert.Single(result.Refs);
            Assert.Single(result.StagedKeys);
            Assert.Equal("mj:imagine:input:task-1:0", result.StagedKeys[0]);
            Assert.Same(bytes, written[result.StagedKeys[0]]);
            Assert.Equal("cat", info.Prompt);
            Assert.Equal(true, info.GetProperty(Constants.TASK_PROPERTY_IMAGINE_NEED_PREPARE));
        }

        [Fact]
        public void Stage_SetFailure_ReturnsStagedKeysForCleanup()
        {
            var info = NewInfo();
            var calls = 0;

            var result = ImagineInputStager.Stage(
                info,
                [
                    new DataUrl { MimeType = "image/png", Data = [1] },
                    new DataUrl { MimeType = "image/png", Data = [2] },
                ],
                3600,
                (key, value, ttl) => ++calls == 1);

            Assert.False(result.Success);
            Assert.Single(result.StagedKeys);
            Assert.Equal("cat", info.Prompt);
            Assert.Null(info.GetProperty(Constants.TASK_PROPERTY_IMAGINE_INPUTS));
        }

        [Fact]
        public void Stage_Exception_ReturnsGenericErrorAndReportsDetailToDiagnostics()
        {
            var info = NewInfo();
            Exception? captured = null;

            var result = ImagineInputStager.Stage(
                info,
                [new DataUrl { MimeType = "image/png", Data = [1] }],
                3600,
                (key, bytes, ttl) => throw new InvalidOperationException("redis://secret-host:6379"),
                onError: ex => captured = ex);

            Assert.False(result.Success);
            Assert.Equal("垫图暂存失败，请稍后重试", result.Error);
            Assert.DoesNotContain("secret-host", result.Error);
            Assert.NotNull(captured);
            Assert.Contains("secret-host", captured!.Message);
            Assert.Empty(result.StagedKeys);
            Assert.Equal("cat", info.Prompt);
        }

        [Fact]
        public void GetTtl_IsFixedAtSixtyMinutes()
        {
            Assert.Equal(TimeSpan.FromMinutes(60), ImagineInputStager.GetTtl());
        }

        [Fact]
        public void PropertyHelpers_ReadCommonJsonShapes()
        {
            using var doc = JsonDocument.Parse("true");

            Assert.True(ImagineInputRef.GetBool(true));
            Assert.True(ImagineInputRef.GetBool("true"));
            Assert.True(ImagineInputRef.GetBool(new JValue(true)));
            Assert.True(ImagineInputRef.GetBool(doc.RootElement));
            Assert.False(ImagineInputRef.GetBool("no"));
            Assert.Equal("x", ImagineInputRef.GetString("x"));
            Assert.Equal("x", ImagineInputRef.GetString(new JValue("x")));

            var prepared = NewInfo();
            prepared.SetProperty(Constants.TASK_PROPERTY_IMAGINE_PREPARED, true);
            Assert.True(ImagineInputRef.IsPrepared(prepared));

            var needPrepare = NewInfo();
            needPrepare.SetProperty(Constants.TASK_PROPERTY_IMAGINE_INPUTS, "[]");
            Assert.True(ImagineInputRef.NeedsPrepare(needPrepare));
        }
    }
}
