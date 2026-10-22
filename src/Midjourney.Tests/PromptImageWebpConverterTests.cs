using System.Net;
using System.Net.Sockets;
using System.Text;
using Midjourney.Base;
using Midjourney.Base.Models;
using Midjourney.Base.Util;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace Midjourney.Tests
{
    [CollectionDefinition("GlobalSetting", DisableParallelization = true)]
    public class GlobalSettingCollection
    {
    }

    [Collection("GlobalSetting")]
    public class PromptImageWebpConverterTests
    {
        private static Setting CreateSetting(bool enable = true, params string[] ignoreFormats)
        {
            return new Setting
            {
                EnablePromptImageWebp = enable,
                PromptImageWebpIgnoreFormats = ignoreFormats.ToList(),
            };
        }

        private static byte[] CreateImageBytes(IImageEncoder encoder, int width = 8, int height = 8)
        {
            using var image = new Image<Rgba32>(width, height, Color.CornflowerBlue);
            using var stream = new MemoryStream();
            image.Save(stream, encoder);
            return stream.ToArray();
        }

        private static byte[] CreatePng() => CreateImageBytes(new SixLabors.ImageSharp.Formats.Png.PngEncoder());

        private static byte[] CreateJpeg() => CreateImageBytes(new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder());

        private static byte[] CreateGif() => CreateImageBytes(new SixLabors.ImageSharp.Formats.Gif.GifEncoder());

        private static byte[] CreateWebp() => CreateImageBytes(new SixLabors.ImageSharp.Formats.Webp.WebpEncoder());

        private static byte[] CreateAnimatedGif()
        {
            using var image = new Image<Rgba32>(4, 4, Color.Red);
            using var second = new Image<Rgba32>(4, 4, Color.Blue);
            image.Frames.AddFrame(second.Frames.RootFrame);

            using var stream = new MemoryStream();
            image.Save(stream, new SixLabors.ImageSharp.Formats.Gif.GifEncoder());
            return stream.ToArray();
        }

        private static byte[] CreateJpegWithOrientation(ushort orientation)
        {
            using var image = new Image<Rgba32>(4, 2, Color.Red);
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Orientation, orientation);
            image.Metadata.ExifProfile = exif;

            using var stream = new MemoryStream();
            image.Save(stream, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder());
            return stream.ToArray();
        }

        private static T WithSetting<T>(Setting setting, Func<T> action)
        {
            var original = GlobalConfiguration.Setting;
            try
            {
                GlobalConfiguration.Setting = setting;
                return action();
            }
            finally
            {
                GlobalConfiguration.Setting = original;
            }
        }

        private static async Task<T> WithSettingAsync<T>(Setting setting, Func<Task<T>> action)
        {
            var original = GlobalConfiguration.Setting;
            try
            {
                GlobalConfiguration.Setting = setting;
                return await action();
            }
            finally
            {
                GlobalConfiguration.Setting = original;
            }
        }

        [Fact]
        public void Disabled_KeepsOriginalBytes()
        {
            var source = CreatePng();
            var dataUrl = new DataUrl("image/png", source);

            var converted = PromptImageWebpConverter.TryConvertToWebP(dataUrl, CreateSetting(false));

            Assert.False(converted);
            Assert.Equal("image/png", dataUrl.MimeType);
            Assert.Same(source, dataUrl.Data);
        }

        [Theory]
        [InlineData("png", "image/png")]
        [InlineData("jpg", "image/jpeg")]
        public void SupportedImage_IsConvertedToWebp(string sourceFormat, string mimeType)
        {
            var source = sourceFormat == "png" ? CreatePng() : CreateJpeg();
            var dataUrl = new DataUrl(mimeType, source);

            var converted = PromptImageWebpConverter.TryConvertToWebP(dataUrl, CreateSetting(true));

            Assert.True(converted);
            Assert.Equal(PromptImageWebpConverter.WebpMimeType, dataUrl.MimeType);
            Assert.Equal("webp", Image.DetectFormat(dataUrl.Data).Name, ignoreCase: true);
        }

        [Theory]
        [InlineData(".png")]
        [InlineData("png")]
        [InlineData(".PNG")]
        public void IgnoredFormat_KeepsOriginalBytes(string ignoreFormat)
        {
            var source = CreatePng();
            var dataUrl = new DataUrl("image/png", source);

            var converted = PromptImageWebpConverter.TryConvertToWebP(dataUrl, CreateSetting(true, ignoreFormat));

            Assert.False(converted);
            Assert.Equal("image/png", dataUrl.MimeType);
            Assert.Same(source, dataUrl.Data);
        }

        [Fact]
        public void IgnoreByMimeType_KeepsOriginalBytes()
        {
            var source = CreatePng();
            var dataUrl = new DataUrl("image/png", source);

            var converted = PromptImageWebpConverter.TryConvertToWebP(dataUrl, CreateSetting(true, "image/png"));

            Assert.False(converted);
            Assert.Same(source, dataUrl.Data);
        }

        [Fact]
        public void Gif_IsIgnoredByDefault()
        {
            var source = CreateGif();
            var dataUrl = new DataUrl("image/gif", source);
            var setting = new Setting { EnablePromptImageWebp = true };

            var converted = PromptImageWebpConverter.TryConvertToWebP(dataUrl, setting);

            Assert.False(converted);
            Assert.Equal("image/gif", dataUrl.MimeType);
            Assert.Same(source, dataUrl.Data);
        }

        [Fact]
        public void SingleFrameGif_IsConverted_WhenNotIgnored()
        {
            var source = CreateGif();
            var dataUrl = new DataUrl("image/gif", source);

            var converted = PromptImageWebpConverter.TryConvertToWebP(dataUrl, CreateSetting(true));

            Assert.True(converted);
            Assert.Equal("webp", Image.DetectFormat(dataUrl.Data).Name, ignoreCase: true);
        }

        [Fact]
        public void AnimatedGif_IsNeverConverted_ToAvoidLosingAnimation()
        {
            var source = CreateAnimatedGif();
            var dataUrl = new DataUrl("image/gif", source);

            var converted = PromptImageWebpConverter.TryConvertToWebP(dataUrl, CreateSetting(true));

            Assert.False(converted);
            Assert.Same(source, dataUrl.Data);
        }

        [Fact]
        public void Webp_IsNotReencoded()
        {
            var source = CreateWebp();
            var dataUrl = new DataUrl("image/webp", source);

            var converted = PromptImageWebpConverter.TryConvertToWebP(dataUrl, CreateSetting(true));

            Assert.False(converted);
            Assert.Same(source, dataUrl.Data);
        }

        [Fact]
        public void UnknownFormat_KeepsOriginalWithoutThrowing()
        {
            var source = new byte[] { 0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99 };
            var dataUrl = new DataUrl("application/octet-stream", source);

            var converted = PromptImageWebpConverter.TryConvertToWebP(dataUrl, CreateSetting(true));

            Assert.False(converted);
            Assert.Equal("application/octet-stream", dataUrl.MimeType);
            Assert.Same(source, dataUrl.Data);
        }

        [Fact]
        public void CorruptImageWithKnownHeader_KeepsOriginalWithoutThrowing()
        {
            var source = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x00 };
            var dataUrl = new DataUrl("image/png", source);

            var converted = PromptImageWebpConverter.TryConvertToWebP(dataUrl, CreateSetting(true));

            Assert.False(converted);
            Assert.Same(source, dataUrl.Data);
        }

        [Fact]
        public void TransparentPng_KeepsAlpha()
        {
            using var image = new Image<Rgba32>(2, 2, new Rgba32(255, 0, 0, 0));
            using var stream = new MemoryStream();
            image.Save(stream, new SixLabors.ImageSharp.Formats.Png.PngEncoder());

            var dataUrl = new DataUrl("image/png", stream.ToArray());

            Assert.True(PromptImageWebpConverter.TryConvertToWebP(dataUrl, CreateSetting(true)));

            using var decoded = Image.Load<Rgba32>(dataUrl.Data);
            Assert.Equal(0, decoded[0, 0].A);
        }

        [Fact]
        public void ExifOrientation_IsAppliedDuringConversion()
        {
            var source = CreateJpegWithOrientation(6);

            using var sourceImage = Image.Load(source);
            var exifProfile = sourceImage.Metadata.ExifProfile;
            Assert.NotNull(exifProfile);
            Assert.True(exifProfile!.TryGetValue(ExifTag.Orientation, out var orientation));
            Assert.Equal((ushort)6, orientation.Value);

            var dataUrl = new DataUrl("image/jpeg", source);
            Assert.True(PromptImageWebpConverter.TryConvertToWebP(dataUrl, CreateSetting(true)));

            var info = Image.Identify(dataUrl.Data);
            Assert.Equal(2, info.Width);
            Assert.Equal(4, info.Height);
        }

        [Fact]
        public void ByteOverload_UpdatesMimeTypeAndFileName()
        {
            var source = CreatePng();

            var converted = PromptImageWebpConverter.TryConvertToWebP(
                source,
                "image/png",
                "reference.png",
                CreateSetting(true),
                out var data,
                out var mimeType,
                out var fileName);

            Assert.True(converted);
            Assert.Equal("image/webp", mimeType);
            Assert.Equal("reference.webp", fileName);
            Assert.Equal("webp", Image.DetectFormat(data).Name, ignoreCase: true);
        }

        [Fact]
        public void DataUrlParse_DoesNotConvertByDefault()
        {
            var png = CreatePng();
            var dataUrl = WithSetting(CreateSetting(true),
                () => DataUrl.Parse($"data:image/png;base64,{Convert.ToBase64String(png)}"));

            Assert.Equal("image/png", dataUrl.MimeType);
            Assert.Equal("png", Image.DetectFormat(dataUrl.Data).Name, ignoreCase: true);
        }

        [Fact]
        public void DataUrlParse_ConvertsBase64_WhenRequested()
        {
            var png = CreatePng();
            var dataUrl = WithSetting(CreateSetting(true),
                () => DataUrl.Parse($"data:image/png;base64,{Convert.ToBase64String(png)}", true));

            Assert.Equal("image/webp", dataUrl.MimeType);
            Assert.Equal("webp", Image.DetectFormat(dataUrl.Data).Name, ignoreCase: true);
        }

        [Fact]
        public async Task ConvertBase64Array_ConvertsOnlyWhenRequested()
        {
            var png = CreatePng();
            var base64 = $"data:image/png;base64,{Convert.ToBase64String(png)}";

            var converted = await WithSettingAsync(CreateSetting(true), () =>
            {
                var list = DataUrl.ConvertBase64Array([base64], true);
                return Task.FromResult(list);
            });

            Assert.Single(converted);
            Assert.Equal("image/webp", converted[0].MimeType);
            Assert.Equal("webp", Image.DetectFormat(converted[0].Data).Name, ignoreCase: true);
            Assert.EndsWith(".webp", await converted[0].GenerateFileName());

            var original = WithSetting(CreateSetting(true), () => DataUrl.ConvertBase64Array([base64]));
            Assert.Single(original);
            Assert.Equal("image/png", original[0].MimeType);
        }

        [Fact]
        public void SettingJsonRoundTrip_DoesNotDuplicateIgnoreFormats()
        {
            var setting = CreateSetting(true, ".gif");

            var json = setting.ToJson();
            var roundTrip = json.ToObject<Setting>();

            Assert.NotNull(roundTrip);
            Assert.Single(roundTrip.PromptImageWebpIgnoreFormats);
            Assert.Equal(".gif", roundTrip.PromptImageWebpIgnoreFormats[0]);
        }

        [Fact]
        public async Task FetchFileAsync_ConvertsDownloadedImage_WhenRequested()
        {
            var png = CreatePng();
            using var server = new LocalHttpServer(png, "image/png");
            var url = $"http://127.0.0.1:{server.Port}/test.png";

            var res = await WithSettingAsync(CreateSetting(true),
                () => MjImageFetchHelper.FetchFileAsync(url, convertToWebp: true));

            Assert.True(res.Success, res.Msg);
            Assert.EndsWith(".webp", res.FileName);
            Assert.Equal("image/webp", res.ContentType);
            Assert.Equal("webp", Image.DetectFormat(res.FileBytes).Name, ignoreCase: true);
        }

        [Fact]
        public async Task FetchFileAsync_KeepsOriginalFormat_ByDefault()
        {
            var png = CreatePng();
            using var server = new LocalHttpServer(png, "image/png");
            var url = $"http://127.0.0.1:{server.Port}/test.png";

            var res = await WithSettingAsync(CreateSetting(true),
                () => MjImageFetchHelper.FetchFileAsync(url));

            Assert.True(res.Success, res.Msg);
            Assert.EndsWith(".png", res.FileName);
            Assert.Equal("image/png", res.ContentType);
            Assert.Equal("png", Image.DetectFormat(res.FileBytes).Name, ignoreCase: true);
        }

        [Fact]
        public async Task FetchFileAsync_WhiteHost_ReturnsUrlWithoutDownloading()
        {
            const string url = "https://cdn.discordapp.com/attachments/1/2/a.png";

            var res = await MjImageFetchHelper.FetchFileAsync(url, convertToWebp: true);

            Assert.True(res.Success);
            Assert.Equal(url, res.Url);
            Assert.Null(res.FileBytes);
        }

        [Fact]
        public void LegacyPublicSignatures_ArePreserved()
        {
            Assert.NotNull(typeof(DataUrl).GetMethod(nameof(DataUrl.Parse), [typeof(string)]));
            Assert.NotNull(typeof(DataUrl).GetMethod(nameof(DataUrl.ConvertBase64Array), [typeof(List<string>)]));
            Assert.NotNull(typeof(MjImageFetchHelper).GetMethod(
                nameof(MjImageFetchHelper.FetchFileAsync),
                [typeof(string), typeof(int), typeof(bool), typeof(bool)]));
        }

        private sealed class LocalHttpServer : IDisposable
        {
            private readonly TcpListener _listener;
            private readonly Task _serveTask;

            public int Port { get; }

            public LocalHttpServer(byte[] body, string contentType)
            {
                _listener = new TcpListener(IPAddress.Loopback, 0);
                _listener.Start();
                Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

                _serveTask = Task.Run(async () =>
                {
                    try
                    {
                        using var client = await _listener.AcceptTcpClientAsync();
                        using var stream = client.GetStream();

                        var buffer = new byte[4096];
                        var request = new StringBuilder();
                        while (!request.ToString().Contains("\r\n\r\n"))
                        {
                            var read = await stream.ReadAsync(buffer, 0, buffer.Length);
                            if (read <= 0)
                            {
                                break;
                            }
                            request.Append(Encoding.ASCII.GetString(buffer, 0, read));
                        }

                        var header = $"HTTP/1.1 200 OK\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
                        var headerBytes = Encoding.ASCII.GetBytes(header);
                        await stream.WriteAsync(headerBytes, 0, headerBytes.Length);
                        await stream.WriteAsync(body, 0, body.Length);
                        await stream.FlushAsync();
                    }
                    catch
                    {
                        // 测试服务器被提前关闭时忽略
                    }
                });
            }

            public void Dispose()
            {
                try
                {
                    _listener.Stop();
                }
                catch
                {
                }

                try
                {
                    _serveTask.Wait(2000);
                }
                catch
                {
                }
            }
        }
    }
}
