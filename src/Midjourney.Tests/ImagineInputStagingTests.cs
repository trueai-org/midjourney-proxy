using System.Net;
using System.Net.Sockets;
using System.Text;
using Midjourney.Base;
using Midjourney.Base.Models;
using Midjourney.Base.Util;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Midjourney.Tests
{
    public class ImagineInputStagingTests
    {
        private static byte[] CreatePng(int size = 8)
        {
            using var image = new Image<Rgba32>(size, size, Color.CornflowerBlue);
            using var stream = new MemoryStream();
            image.Save(stream, new PngEncoder());
            return stream.ToArray();
        }

        private static byte[] CreateGif(int size = 8)
        {
            using var image = new Image<Rgba32>(size, size, Color.CornflowerBlue);
            using var stream = new MemoryStream();
            image.Save(stream, new SixLabors.ImageSharp.Formats.Gif.GifEncoder());
            return stream.ToArray();
        }

        [Fact]
        public void BuildKey_UsesTaskIdAndIndex()
        {
            Assert.Equal("mj:imagine:input:task-1:0", ImagineInputRef.BuildKey("task-1", 0));
            Assert.Equal("mj:imagine:input:task-1:2", ImagineInputRef.BuildKey("task-1", 2));
        }

        [Fact]
        public void SerializeAndParse_RoundTripsMetadata()
        {
            var refs = new List<ImagineInputRef>
            {
                new() { Key = "mj:imagine:input:t:0", MimeType = "image/png", Index = 0, Size = 123 },
                new() { Key = "mj:imagine:input:t:1", MimeType = "image/jpeg", Index = 1, Size = 456 },
            };

            var json = ImagineInputRef.Serialize(refs);

            var fromString = ImagineInputRef.Parse(json);
            var fromJArray = ImagineInputRef.Parse(JArray.Parse(json));
            var fromList = ImagineInputRef.Parse(refs);

            Assert.Equal(2, fromString.Count);
            Assert.Equal(refs[1].Key, fromString[1].Key);
            Assert.Equal("image/jpeg", fromString[1].MimeType);
            Assert.Equal(1, fromString[1].Index);
            Assert.Equal(456, fromString[1].Size);

            Assert.Equal(2, fromJArray.Count);
            Assert.Equal(2, fromList.Count);
        }

        [Fact]
        public void Parse_InvalidOrEmptyValue_ReturnsEmptyList()
        {
            Assert.Empty(ImagineInputRef.Parse(null));
            Assert.Empty(ImagineInputRef.Parse(""));
            Assert.Empty(ImagineInputRef.Parse("not-json"));
            Assert.Empty(ImagineInputRef.Parse(new JArray()));
        }

        [Fact]
        public void ParseUrls_ReadsJsonAndSkipsBlank()
        {
            var urls = ImagineInputRef.ParseUrls("[\"https://a/1.png\",\"\",\"https://b/2.png\"]");

            Assert.Equal(2, urls.Count);
            Assert.Contains("https://a/1.png", urls);
            Assert.Contains("https://b/2.png", urls);
            Assert.Empty(ImagineInputRef.ParseUrls(null));
        }

        [Fact]
        public void PrepareSetting_ConvertsWebp_WhenSnapshotEnabled()
        {
            var source = CreatePng();
            var dataUrl = new DataUrl("image/png", source);

            var converted = PromptImageWebpConverter.TryConvertToWebP(
                dataUrl,
                ImagineInputRef.CreatePrepareSetting(true, null));

            Assert.True(converted);
            Assert.Equal(PromptImageWebpConverter.WebpMimeType, dataUrl.MimeType);
        }

        [Fact]
        public void PrepareSetting_KeepsOriginal_WhenSnapshotDisabled()
        {
            var source = CreatePng();
            var dataUrl = new DataUrl("image/png", source);

            var converted = PromptImageWebpConverter.TryConvertToWebP(
                dataUrl,
                ImagineInputRef.CreatePrepareSetting(false, null));

            Assert.False(converted);
            Assert.Same(source, dataUrl.Data);
            Assert.Equal("image/png", dataUrl.MimeType);
        }

        [Fact]
        public void PrepareSetting_DefaultGifIgnore_IsPreservedWhenSnapshotIsNull()
        {
            var source = CreateGif();
            var dataUrl = new DataUrl("image/gif", source);

            var converted = PromptImageWebpConverter.TryConvertToWebP(
                dataUrl,
                ImagineInputRef.CreatePrepareSetting(true, null));

            Assert.False(converted);
            Assert.Same(source, dataUrl.Data);
            Assert.Equal("image/gif", dataUrl.MimeType);
        }

        [Fact]
        public async Task FetchFileAsync_MaxBytes_ReturnsFailure()
        {
            using var server = new FixedBodyTcpServer(4096);
            var url = $"http://127.0.0.1:{server.Port}/big.png";

            var res = await MjImageFetchHelper.FetchFileAsync(
                url,
                convertToWebp: false,
                timeout: TimeSpan.FromSeconds(5),
                maxBytes: 1024);

            Assert.False(res.Success);
            Assert.Contains("size", res.Msg.ToLowerInvariant());
        }

        [Fact]
        public async Task FetchFileAsync_MaxBytes_AllowsExactlyLimit()
        {
            using var server = new FixedBodyTcpServer(1024);
            var url = $"http://127.0.0.1:{server.Port}/limit.png";

            var res = await MjImageFetchHelper.FetchFileAsync(
                url,
                convertToWebp: false,
                timeout: TimeSpan.FromSeconds(5),
                maxBytes: 1024,
                detectFileName: false);

            Assert.True(res.Success);
            Assert.Equal(1024, res.FileBytes.Length);
        }

        [Fact]
        public async Task FetchFileAsync_WithTimeout_ReturnsFailureForSlowServer()
        {
            using var server = new SilentTcpServer();
            var url = $"http://127.0.0.1:{server.Port}/slow.png";

            var res = await MjImageFetchHelper.FetchFileAsync(
                url,
                convertToWebp: false,
                timeout: TimeSpan.FromMilliseconds(300));

            Assert.False(res.Success);
            Assert.Equal("Fetch timeout", res.Msg);
        }

        [Fact]
        public async Task FetchFileAsync_DetectFileNameDisabled_SkipsFileNameDetection()
        {
            using var server = new FixedBodyTcpServer(16);
            var url = $"http://127.0.0.1:{server.Port}/input.bin";

            var res = await MjImageFetchHelper.FetchFileAsync(
                url,
                convertToWebp: false,
                timeout: TimeSpan.FromSeconds(5),
                detectFileName: false);

            Assert.True(res.Success);
            Assert.Null(res.FileName);
            Assert.Equal(16, res.FileBytes.Length);
        }

        [Fact]
        public async Task FetchFileAsync_CancelledToken_ReturnsCancelled()
        {
            using var server = new SilentTcpServer();
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var url = $"http://127.0.0.1:{server.Port}/slow.png";

            var res = await MjImageFetchHelper.FetchFileAsync(
                url,
                convertToWebp: false,
                timeout: TimeSpan.FromSeconds(5),
                cancellationToken: cts.Token);

            Assert.False(res.Success);
            Assert.Equal("Fetch cancelled", res.Msg);
        }

        private sealed class FixedBodyTcpServer : IDisposable
        {
            private readonly TcpListener _listener;
            private readonly Task _acceptTask;

            public int Port { get; }

            public FixedBodyTcpServer(int bodyLength)
            {
                _listener = new TcpListener(IPAddress.Loopback, 0);
                _listener.Start();
                Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

                _acceptTask = Task.Run(async () =>
                {
                    try
                    {
                        using var client = await _listener.AcceptTcpClientAsync();
                        using var stream = client.GetStream();
                        var buffer = new byte[4096];
                        await stream.ReadAsync(buffer, 0, buffer.Length);

                        var header = Encoding.ASCII.GetBytes(
                            $"HTTP/1.1 200 OK\r\nContent-Length: {bodyLength}\r\nContent-Type: image/png\r\nConnection: close\r\n\r\n");
                        await stream.WriteAsync(header, 0, header.Length);
                        await stream.WriteAsync(new byte[bodyLength], 0, bodyLength);
                    }
                    catch
                    {
                        // 测试结束时监听器被关闭
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
                    _acceptTask.Wait(1000);
                }
                catch
                {
                }
            }
        }

        private sealed class SilentTcpServer : IDisposable
        {
            private readonly TcpListener _listener;
            private readonly Task _acceptTask;

            public int Port { get; }

            public SilentTcpServer()
            {
                _listener = new TcpListener(IPAddress.Loopback, 0);
                _listener.Start();
                Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

                _acceptTask = Task.Run(async () =>
                {
                    try
                    {
                        using var client = await _listener.AcceptTcpClientAsync();
                        // 接受连接但不返回响应，模拟卡住的 CDN
                        await Task.Delay(TimeSpan.FromSeconds(5));
                    }
                    catch
                    {
                        // 测试结束时监听器被关闭
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
                    _acceptTask.Wait(1000);
                }
                catch
                {
                }
            }
        }
    }
}
