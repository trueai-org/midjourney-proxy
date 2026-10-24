using Midjourney.Base;
using Midjourney.Base.Data;
using Midjourney.Base.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Midjourney.Tests
{
    public class VideoInputPreparerTests
    {
        private static byte[] CreatePng()
        {
            using var image = new Image<Rgba32>(8, 8, Color.CornflowerBlue);
            using var stream = new MemoryStream();
            image.Save(stream, new PngEncoder());
            return stream.ToArray();
        }

        private static TaskInfo NewInfo(string id = "video-1")
        {
            var startToken = ImagineInputRef.BuildVideoInputToken(id, 0);
            var endToken = ImagineInputRef.BuildVideoInputToken(id, 1);
            var prompt = $"{startToken} cat --video 1 --end {endToken}";

            return new TaskInfo
            {
                Id = id,
                Action = TaskAction.VIDEO,
                Prompt = prompt,
                PromptEn = prompt,
                Description = "/video " + prompt,
            };
        }

        private static ImagineInputRef NewRef(string key, int index)
        {
            return new ImagineInputRef
            {
                Key = key,
                Index = index,
                MimeType = "image/png",
                Size = 8,
            };
        }

        [Fact]
        public async Task PrepareAsync_UploadsBase64AndReplacesTokens()
        {
            var info = NewInfo();
            var refs = new[] { NewRef("k0", 0), NewRef("k1", 1) };
            var uploads = 0;
            var deleted = new List<ImagineInputRef>();

            var result = await VideoInputPreparer.PrepareAsync(
                info,
                refs,
                new Setting(),
                _ => CreatePng(),
                (dataUrl, fileName) =>
                {
                    uploads++;
                    return Task.FromResult(Message.Success($"https://cdn/video-{uploads}.png"));
                },
                url => Task.FromResult(url),
                _ => { },
                toDelete => deleted = toDelete.ToList());

            Assert.Equal(ReturnCode.SUCCESS, result.Code);
            Assert.Equal(2, uploads);
            Assert.Equal("https://cdn/video-1.png", info.BaseImageUrl);
            Assert.DoesNotContain("__mj_video_input_", info.Prompt);
            Assert.Contains("https://cdn/video-1.png", info.Prompt);
            Assert.Contains("https://cdn/video-2.png", info.Prompt);
            Assert.Equal(info.Prompt, info.PromptEn);
            Assert.Equal(2, deleted.Count);
            Assert.Equal(true, info.GetProperty(Constants.TASK_PROPERTY_IMAGINE_PREPARED));
            Assert.Null(info.GetProperty(Constants.TASK_PROPERTY_IMAGINE_INPUTS));
        }

        [Fact]
        public async Task PrepareAsync_UrlRef_ResolvesWithoutUpload()
        {
            var info = NewInfo();
            var token = ImagineInputRef.BuildVideoInputToken(info.Id, 0);
            info.Prompt = $"{token} cat --video 1";
            info.PromptEn = info.Prompt;
            info.Description = "/video " + info.Prompt;
            var refs = new[] { new ImagineInputRef { Url = "https://origin/start.png", Index = 0 } };
            var uploadCalled = false;

            var result = await VideoInputPreparer.PrepareAsync(
                info,
                refs,
                new Setting(),
                _ => CreatePng(),
                (dataUrl, fileName) =>
                {
                    uploadCalled = true;
                    return Task.FromResult(Message.Success("https://cdn/unexpected.png"));
                },
                url => Task.FromResult("https://youchuan/start.png"),
                _ => { },
                _ => { });

            Assert.Equal(ReturnCode.SUCCESS, result.Code);
            Assert.False(uploadCalled);
            Assert.Equal("https://youchuan/start.png", info.BaseImageUrl);
            Assert.Contains("https://youchuan/start.png", info.Prompt);
            Assert.DoesNotContain(token, info.Prompt);
        }

        [Fact]
        public async Task PrepareAsync_CancelledToken_FailsAndDeletesWithoutUpload()
        {
            var info = NewInfo();
            var refs = new[]
            {
                new ImagineInputRef { Key = "k0", Url = "https://cdn/start.png", Index = 0, MimeType = "image/png", Size = 8 },
            };
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var uploadCalled = false;
            List<ImagineInputRef> deleted = [];

            var result = await VideoInputPreparer.PrepareAsync(
                info,
                refs,
                new Setting(),
                _ => throw new InvalidOperationException("getBytes should not be called"),
                (_, _) =>
                {
                    uploadCalled = true;
                    return Task.FromResult(Message.Success("https://cdn/start.png"));
                },
                _ => throw new InvalidOperationException("resolveUrl should not be called"),
                _ => { },
                toDelete => { deleted = toDelete.ToList(); },
                cancellationToken: cts.Token);

            Assert.Equal(ReturnCode.FAILURE, result.Code);
            Assert.Equal("操作已取消", result.Description);
            Assert.False(uploadCalled);
            Assert.Single(deleted);
            Assert.Contains("__mj_video_input_", info.Prompt);
        }

        [Fact]
        public async Task PrepareAsync_EmptyRefsWithPendingToken_FailsWithoutUpload()
        {
            var info = NewInfo();
            var uploadCalled = false;

            var result = await VideoInputPreparer.PrepareAsync(
                info,
                [],
                new Setting(),
                _ => throw new InvalidOperationException("getBytes should not be called"),
                (_, _) =>
                {
                    uploadCalled = true;
                    return Task.FromResult(Message.Success("https://cdn/input.png"));
                },
                _ => throw new InvalidOperationException("resolveUrl should not be called"),
                _ => { },
                _ => { });

            Assert.Equal(ReturnCode.FAILURE, result.Code);
            Assert.Contains("过期", result.Description);
            Assert.False(uploadCalled);
            Assert.Contains("__mj_video_input_", info.Prompt);
        }

        [Fact]
        public async Task PrepareAsync_EmptyRefsWithoutPendingInputs_SucceedsWithoutUpload()
        {
            var info = new TaskInfo
            {
                Id = "video-2",
                Action = TaskAction.VIDEO,
                Prompt = "cat --video 1",
                PromptEn = "cat --video 1",
                Description = "/video cat --video 1",
            };
            var uploadCalled = false;

            var result = await VideoInputPreparer.PrepareAsync(
                info,
                [],
                new Setting(),
                _ => throw new InvalidOperationException("getBytes should not be called"),
                (_, _) =>
                {
                    uploadCalled = true;
                    return Task.FromResult(Message.Success("https://cdn/input.png"));
                },
                _ => throw new InvalidOperationException("resolveUrl should not be called"),
                _ => { },
                _ => { });

            Assert.Equal(ReturnCode.SUCCESS, result.Code);
            Assert.False(uploadCalled);
        }

        [Fact]
        public void HasPendingInputs_DetectsNeedPrepareAndPromptToken()
        {
            var info = NewInfo();
            Assert.True(VideoInputPreparer.HasPendingInputs(info));
            Assert.True(VideoInputPreparer.ContainsPendingToken(info));

            var plain = new TaskInfo
            {
                Id = "video-3",
                Action = TaskAction.VIDEO,
                Prompt = "cat --video 1",
                PromptEn = "cat --video 1",
            };
            Assert.False(VideoInputPreparer.HasPendingInputs(plain));

            plain.SetProperty(Constants.TASK_PROPERTY_IMAGINE_NEED_PREPARE, true);
            Assert.True(VideoInputPreparer.HasPendingInputs(plain));
        }

        [Fact]
        public async Task PrepareAsync_MissingKey_FailsAndDeletes()
        {
            var info = NewInfo();
            var refs = new[] { NewRef("missing", 0) };
            var deleted = new List<ImagineInputRef>();

            var result = await VideoInputPreparer.PrepareAsync(
                info,
                refs,
                new Setting(),
                _ => null,
                (dataUrl, fileName) => Task.FromResult(Message.Success("https://cdn/x.png")),
                url => Task.FromResult(url),
                _ => { },
                toDelete => deleted = toDelete.ToList());

            Assert.Equal(ReturnCode.FAILURE, result.Code);
            Assert.Contains("过期", result.Description);
            Assert.Single(deleted);
            Assert.Contains("__mj_video_input_", info.Prompt);
        }

        [Fact]
        public async Task StageAndPrepare_MixedUrlAndBase64_PreservesStartEndRoles()
        {
            var info = NewInfo();
            var start = new DataUrl { Url = "https://origin/start.png" };
            var end = new DataUrl { MimeType = "image/png", Data = CreatePng() };
            var store = new Dictionary<string, byte[]>();

            var stage = ImagineInputStager.Stage(
                info,
                [start, end],
                3600,
                (key, bytes, ttl) => { store[key] = bytes; return true; },
                urlOnlyAsPrompt: false);

            Assert.True(stage.Success);
            var refs = ImagineInputRef.Parse(info.GetProperty(Constants.TASK_PROPERTY_IMAGINE_INPUTS));
            Assert.Equal(2, refs.Count);
            Assert.Equal("https://origin/start.png", refs.Single(x => x.Index == 0).Url);
            Assert.NotNull(refs.Single(x => x.Index == 1).Key);

            var uploads = 0;
            var result = await VideoInputPreparer.PrepareAsync(
                info,
                refs,
                new Setting(),
                key => store.TryGetValue(key, out var bytes) ? bytes : null,
                (dataUrl, fileName) =>
                {
                    uploads++;
                    return Task.FromResult(Message.Success("https://cdn/end.png"));
                },
                url => Task.FromResult("https://youchuan/start.png"),
                _ => { },
                toDelete =>
                {
                    foreach (var item in toDelete)
                    {
                        if (!string.IsNullOrWhiteSpace(item.Key))
                        {
                            store.Remove(item.Key);
                        }
                    }
                });

            Assert.Equal(ReturnCode.SUCCESS, result.Code);
            Assert.Equal(1, uploads);
            Assert.Equal("https://youchuan/start.png", info.BaseImageUrl);
            Assert.Contains("https://youchuan/start.png", info.Prompt);
            Assert.Contains("https://cdn/end.png", info.Prompt);
            Assert.DoesNotContain("__mj_video_input_", info.Prompt);
            Assert.Empty(store);
        }

        [Fact]
        public async Task PrepareAsync_NonImageMime_FailsAndDeletes()
        {
            var info = NewInfo();
            var refs = new[] { new ImagineInputRef { Key = "k0", Index = 0, MimeType = "video/mp4" } };
            var deleted = new List<ImagineInputRef>();

            var result = await VideoInputPreparer.PrepareAsync(
                info,
                refs,
                new Setting(),
                _ => CreatePng(),
                (dataUrl, fileName) => Task.FromResult(Message.Success("https://cdn/x.png")),
                url => Task.FromResult(url),
                _ => { },
                toDelete => deleted = toDelete.ToList());

            Assert.Equal(ReturnCode.FAILURE, result.Code);
            Assert.Contains("格式", result.Description);
            Assert.Single(deleted);
        }

        [Fact]
        public async Task PrepareAsync_ReplacesVideoExtendPrompt()
        {
            var info = NewInfo();
            var token = ImagineInputRef.BuildVideoInputToken(info.Id, 0);
            info.Prompt = $"{token} cat --video 1";
            info.PromptEn = info.Prompt;
            info.Description = "/video " + info.Prompt;
            info.SetProperty(Constants.TASK_PROPERTY_VIDEO_EXTEND_PROMPT, $"{token} cat --video 1");
            var refs = new[] { NewRef("k0", 0) };

            var result = await VideoInputPreparer.PrepareAsync(
                info,
                refs,
                new Setting(),
                _ => CreatePng(),
                (dataUrl, fileName) => Task.FromResult(Message.Success("https://cdn/start.png")),
                url => Task.FromResult(url),
                _ => { },
                _ => { });

            Assert.Equal(ReturnCode.SUCCESS, result.Code);
            Assert.Equal("https://cdn/start.png cat --video 1",
                info.GetProperty(Constants.TASK_PROPERTY_VIDEO_EXTEND_PROMPT)?.ToString());
        }
    }
}
