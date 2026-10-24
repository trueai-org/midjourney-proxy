using System.Text.Json;
using CSRedis;
using Midjourney.Base;
using Midjourney.Base.Data;
using Midjourney.Base.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Midjourney.Tests
{
    public class ImagineInputRedisIntegrationTests
    {
        private static byte[] CreatePng()
        {
            using var image = new Image<Rgba32>(8, 8, Color.CornflowerBlue);
            using var stream = new MemoryStream();
            image.Save(stream, new PngEncoder());
            return stream.ToArray();
        }

        [Fact]
        public async Task StageAndPrepare_WithRealRedis_RoundTripsAndDeletes()
        {
            var configPath = Environment.GetEnvironmentVariable("MJ_REAL_REDIS_CONFIG");
            if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
            {
                return;
            }

            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(configPath));
            var connectionString = doc.RootElement.GetProperty("RedisConnectionString").GetString();
            var redis = new CSRedisClient(connectionString);
            var keys = new List<string>();

            try
            {
                var info = new TaskInfo
                {
                    Id = "real-" + Guid.NewGuid().ToString("N"),
                    Prompt = "cat",
                    PromptEn = "cat",
                };
                var setting = new Setting { EnablePromptImageWebp = false };

                var stage = ImagineInputStager.Stage(
                    info,
                    [new DataUrl { MimeType = "image/png", Data = CreatePng() }],
                    60,
                    (key, bytes, ttl) =>
                    {
                        var ok = redis.Set(key, bytes, ttl);
                        if (ok)
                        {
                            keys.Add(key);
                        }
                        return ok;
                    });

                Assert.True(stage.Success);
                Assert.Single(stage.StagedKeys);
                Assert.InRange(redis.Ttl(stage.StagedKeys[0]), 1, 60);

                var refs = ImagineInputRef.Parse(info.GetProperty(Constants.TASK_PROPERTY_IMAGINE_INPUTS));
                var uploadedUrl = "https://cdn/real-test.png";

                var result = await ImagineInputPreparer.PrepareAsync(
                    info,
                    refs,
                    setting,
                    key => redis.Get<byte[]>(key),
                    (dataUrl, fileName) => Task.FromResult(Message.Success(uploadedUrl)),
                    _ => { },
                    toDelete =>
                    {
                        var toRemove = ImagineInputRef.CollectKeys(toDelete);
                        if (toRemove.Length > 0)
                        {
                            redis.Del(toRemove);
                        }
                        keys.Clear();
                    });

                Assert.Equal(ReturnCode.SUCCESS, result.Code);
                Assert.Contains(uploadedUrl, info.Prompt);
                Assert.False(redis.Exists(stage.StagedKeys[0]));
            }
            finally
            {
                if (keys.Count > 0)
                {
                    redis.Del(keys.ToArray());
                }
                redis.Dispose();
            }
        }
    }
}
