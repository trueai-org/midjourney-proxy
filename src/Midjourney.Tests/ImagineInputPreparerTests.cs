using Midjourney.Base;
using Midjourney.Base.Data;
using Midjourney.Base.Models;
using Midjourney.Base.Util;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Midjourney.Tests
{
    public class ImagineInputPreparerTests
    {
        private static byte[] CreatePng()
        {
            using var image = new Image<Rgba32>(8, 8, Color.CornflowerBlue);
            using var stream = new MemoryStream();
            image.Save(stream, new PngEncoder());
            return stream.ToArray();
        }

        private static TaskInfo NewInfo()
        {
            return new TaskInfo
            {
                Id = "task-1",
                Action = TaskAction.IMAGINE,
                Prompt = "cat",
                PromptEn = "cat",
            };
        }

        private static ImagineInputRef NewRef(string key, int index, string mimeType = "image/png")
        {
            return new ImagineInputRef
            {
                Key = key,
                Index = index,
                MimeType = mimeType,
                Size = 8,
            };
        }

        [Fact]
        public async Task PrepareAsync_UploadsInOrderAndPersistsState()
        {
            var info = NewInfo();
            var refs = new[]
            {
                NewRef("k1", 1),
                NewRef("k0", 0),
            };
            var bytes = CreatePng();
            var uploaded = new List<DataUrl>();
            var saved = false;
            var calls = new List<string>();
            List<ImagineInputRef> deleted = [];

            var result = await ImagineInputPreparer.PrepareAsync(
                info,
                refs,
                new Setting(),
                key => bytes,
                (dataUrl, fileName) =>
                {
                    uploaded.Add(dataUrl);
                    return Task.FromResult(Message.Success($"https://cdn/{uploaded.Count}.png"));
                },
                _ => { saved = true; calls.Add("save"); },
                toDelete => { deleted = toDelete.ToList(); calls.Add("delete"); });

            Assert.Equal(ReturnCode.SUCCESS, result.Code);
            Assert.Equal(2, uploaded.Count);
            Assert.Equal("https://cdn/1.png https://cdn/2.png cat", info.Prompt);
            Assert.Equal("https://cdn/1.png https://cdn/2.png cat", info.PromptEn);
            Assert.StartsWith("/imagine https://cdn/1.png", info.Description);
            Assert.Equal(true, info.GetProperty(Constants.TASK_PROPERTY_IMAGINE_PREPARED));
            Assert.Equal(false, info.GetProperty(Constants.TASK_PROPERTY_IMAGINE_NEED_PREPARE));
            Assert.Contains("https://cdn/1.png", info.GetProperty(Constants.TASK_PROPERTY_IMAGINE_PREPARED_URLS)?.ToString() ?? "");
            Assert.Null(info.GetProperty(Constants.TASK_PROPERTY_IMAGINE_INPUTS));
            Assert.True(saved);
            Assert.Equal(2, deleted.Count);
            Assert.Equal(["save", "delete"], calls);
        }

        [Fact]
        public async Task PrepareAsync_CancelledToken_FailsAndDeletesWithoutUpload()
        {
            var info = NewInfo();
            var refs = new[] { NewRef("k0", 0) };
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var uploadCalled = false;
            List<ImagineInputRef> deleted = [];

            var result = await ImagineInputPreparer.PrepareAsync(
                info,
                refs,
                new Setting(),
                _ => throw new InvalidOperationException("getBytes should not be called"),
                (_, _) =>
                {
                    uploadCalled = true;
                    return Task.FromResult(Message.Success("https://cdn/1.png"));
                },
                _ => { },
                toDelete => { deleted = toDelete.ToList(); },
                cancellationToken: cts.Token);

            Assert.Equal(ReturnCode.FAILURE, result.Code);
            Assert.Equal("操作已取消", result.Description);
            Assert.False(uploadCalled);
            Assert.Single(deleted);
            Assert.Equal("cat", info.Prompt);
        }

        [Fact]
        public async Task PrepareAsync_MissingKey_FailsAndDeletesWithoutUpload()
        {
            var info = NewInfo();
            var refs = new[] { NewRef("missing", 0) };
            var uploadCalled = false;
            List<ImagineInputRef> deleted = [];

            var result = await ImagineInputPreparer.PrepareAsync(
                info,
                refs,
                new Setting(),
                _ => null,
                (dataUrl, fileName) =>
                {
                    uploadCalled = true;
                    return Task.FromResult(Message.Success("https://cdn/x.png"));
                },
                _ => { },
                toDelete => deleted = toDelete.ToList());

            Assert.Equal(ReturnCode.FAILURE, result.Code);
            Assert.Contains("过期", result.Description);
            Assert.False(uploadCalled);
            Assert.Single(deleted);
        }

        [Fact]
        public async Task PrepareAsync_UploadFailure_FailsAndDeletes()
        {
            var info = NewInfo();
            var refs = new[] { NewRef("k0", 0) };
            List<ImagineInputRef> deleted = [];

            var result = await ImagineInputPreparer.PrepareAsync(
                info,
                refs,
                new Setting(),
                _ => CreatePng(),
                (dataUrl, fileName) => Task.FromResult(Message.Of(ReturnCode.FAILURE, "upload failed")),
                _ => { },
                toDelete => deleted = toDelete.ToList());

            Assert.Equal(ReturnCode.FAILURE, result.Code);
            Assert.Single(deleted);
            Assert.Null(info.GetProperty(Constants.TASK_PROPERTY_IMAGINE_PREPARED));
        }

        [Fact]
        public async Task PrepareAsync_Exception_FailsAndDeletes()
        {
            var info = NewInfo();
            var refs = new[] { NewRef("k0", 0) };
            List<ImagineInputRef> deleted = [];
            Exception observed = null!;

            var result = await ImagineInputPreparer.PrepareAsync(
                info,
                refs,
                new Setting(),
                _ => throw new InvalidOperationException("boom"),
                (dataUrl, fileName) => Task.FromResult(Message.Success("https://cdn/x.png")),
                _ => { },
                toDelete => deleted = toDelete.ToList(),
                onError: ex => observed = ex);

            Assert.Equal(ReturnCode.FAILURE, result.Code);
            Assert.NotNull(observed);
            Assert.Single(deleted);
        }

        [Fact]
        public async Task PrepareAsync_SaveFailure_RestoresTaskState()
        {
            var info = NewInfo();
            var refs = new[] { NewRef("k0", 0) };
            List<ImagineInputRef> deleted = [];

            var result = await ImagineInputPreparer.PrepareAsync(
                info,
                refs,
                new Setting(),
                _ => CreatePng(),
                (dataUrl, fileName) => Task.FromResult(Message.Success("https://cdn/x.png")),
                _ => throw new InvalidOperationException("save failed"),
                toDelete => deleted = toDelete.ToList());

            Assert.Equal(ReturnCode.FAILURE, result.Code);
            Assert.Equal("cat", info.Prompt);
            Assert.Equal("cat", info.PromptEn);
            Assert.Null(info.GetProperty(Constants.TASK_PROPERTY_IMAGINE_PREPARED));
            Assert.Null(info.GetProperty(Constants.TASK_PROPERTY_IMAGINE_PREPARED_URLS));
            Assert.Single(deleted);
        }

        [Fact]
        public async Task PrepareAsync_NonImageMime_FailsWithoutUpload()
        {
            var info = NewInfo();
            var refs = new[] { NewRef("k0", 0, "text/html") };
            var uploadCalled = false;
            List<ImagineInputRef> deleted = [];

            var result = await ImagineInputPreparer.PrepareAsync(
                info,
                refs,
                new Setting(),
                _ => CreatePng(),
                (dataUrl, fileName) =>
                {
                    uploadCalled = true;
                    return Task.FromResult(Message.Success("https://cdn/x.png"));
                },
                _ => { },
                toDelete => deleted = toDelete.ToList());

            Assert.Equal(ReturnCode.FAILURE, result.Code);
            Assert.Contains("格式", result.Description);
            Assert.False(uploadCalled);
            Assert.Single(deleted);
        }

        [Fact]
        public async Task PrepareAsync_NullOnlyRefs_FailsWithoutUpload()
        {
            var info = NewInfo();
            var uploadCalled = false;

            var result = await ImagineInputPreparer.PrepareAsync(
                info,
                new ImagineInputRef[] { null! },
                new Setting(),
                _ => CreatePng(),
                (dataUrl, fileName) =>
                {
                    uploadCalled = true;
                    return Task.FromResult(Message.Success("https://cdn/x.png"));
                },
                _ => { },
                _ => { });

            Assert.Equal(ReturnCode.FAILURE, result.Code);
            Assert.False(uploadCalled);
            Assert.Equal("cat", info.Prompt);
        }

        [Fact]
        public async Task PrepareAsync_Blend_KeepsDescriptionAndPrependsUrls()
        {
            var info = NewInfo();
            info.Action = TaskAction.BLEND;
            info.Description = "/blend task-1 2";
            var refs = new[] { NewRef("k0", 0) };

            var result = await ImagineInputPreparer.PrepareAsync(
                info,
                refs,
                new Setting(),
                _ => CreatePng(),
                (dataUrl, fileName) => Task.FromResult(Message.Success("https://cdn/blend.png")),
                _ => { },
                _ => { });

            Assert.Equal(ReturnCode.SUCCESS, result.Code);
            Assert.StartsWith("https://cdn/blend.png", info.Prompt);
            Assert.Equal("/blend task-1 2", info.Description);
        }

        [Fact]
        public async Task PrepareAsync_Describe_SetsImageUrlWithoutPrompt()
        {
            var info = NewInfo();
            info.Action = TaskAction.DESCRIBE;
            info.Description = "/describe";
            var originalPrompt = info.Prompt;
            var refs = new[] { NewRef("k0", 0) };

            var result = await ImagineInputPreparer.PrepareAsync(
                info,
                refs,
                new Setting(),
                _ => CreatePng(),
                (dataUrl, fileName) => Task.FromResult(Message.Success("https://cdn/describe.png")),
                _ => { },
                _ => { });

            Assert.Equal(ReturnCode.SUCCESS, result.Code);
            Assert.Equal("https://cdn/describe.png", info.ImageUrl);
            Assert.Equal(originalPrompt, info.Prompt);
            Assert.Equal("/describe", info.Description);
        }

        [Fact]
        public async Task PrepareAsync_WebpSnapshot_IsApplied()
        {
            var info = NewInfo();
            var refs = new[] { NewRef("k0", 0) };
            var converted = false;

            var result = await ImagineInputPreparer.PrepareAsync(
                info,
                refs,
                new Setting { EnablePromptImageWebp = true },
                _ => CreatePng(),
                (dataUrl, fileName) =>
                {
                    converted = dataUrl.MimeType == PromptImageWebpConverter.WebpMimeType;
                    return Task.FromResult(Message.Success("https://cdn/x.webp"));
                },
                _ => { },
                _ => { });

            Assert.Equal(ReturnCode.SUCCESS, result.Code);
            Assert.True(converted);
        }

        [Fact]
        public async Task PrepareAsync_Edit_SetsBaseImageUrlWithoutChangingPrompt()
        {
            var info = NewInfo();
            info.Action = TaskAction.EDIT;
            var refs = new[] { NewRef("k0", 0) };

            var result = await ImagineInputPreparer.PrepareAsync(
                info,
                refs,
                new Setting(),
                _ => CreatePng(),
                (dataUrl, fileName) => Task.FromResult(Message.Success("https://cdn/edit.png")),
                _ => { },
                _ => { });

            Assert.Equal(ReturnCode.SUCCESS, result.Code);
            Assert.Equal("https://cdn/edit.png", info.BaseImageUrl);
            Assert.Equal("cat", info.Prompt);
            Assert.Equal("cat", info.PromptEn);
        }

        [Fact]
        public async Task PrepareAsync_Retexture_AppendsDepthReference()
        {
            var info = NewInfo();
            info.Action = TaskAction.RETEXTURE;
            var refs = new[] { NewRef("k0", 0) };

            var result = await ImagineInputPreparer.PrepareAsync(
                info,
                refs,
                new Setting(),
                _ => CreatePng(),
                (dataUrl, fileName) => Task.FromResult(Message.Success("https://cdn/retexture.png")),
                _ => { },
                _ => { });

            Assert.Equal(ReturnCode.SUCCESS, result.Code);
            Assert.Equal("https://cdn/retexture.png", info.BaseImageUrl);
            Assert.Equal("cat --dref https://cdn/retexture.png", info.Prompt);
            Assert.Equal("cat --dref https://cdn/retexture.png", info.PromptEn);
        }

        [Fact]
        public async Task PrepareAsync_Retexture_DoesNotDuplicateDepthReference()
        {
            var info = NewInfo();
            info.Action = TaskAction.RETEXTURE;
            info.Prompt = "cat --dref https://cdn/retexture.png";
            info.PromptEn = "cat --dref https://cdn/retexture.png";
            var refs = new[] { NewRef("k0", 0) };

            var result = await ImagineInputPreparer.PrepareAsync(
                info,
                refs,
                new Setting(),
                _ => CreatePng(),
                (dataUrl, fileName) => Task.FromResult(Message.Success("https://cdn/retexture.png")),
                _ => { },
                _ => { });

            Assert.Equal(ReturnCode.SUCCESS, result.Code);
            Assert.Equal("cat --dref https://cdn/retexture.png", info.Prompt);
            Assert.Equal("cat --dref https://cdn/retexture.png", info.PromptEn);
        }
    }
}
