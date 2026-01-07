using Midjourney.Base.Data;
using Midjourney.Base.Models;
using Midjourney.Base.Util;
using Midjourney.Services;
using Xunit.Abstractions;

namespace Midjourney.Tests
{
    public class MjPromptParserTests
    {
        private readonly TestOutputWrapper _output;

        public MjPromptParserTests(ITestOutputHelper output)
        {
            _output = new TestOutputWrapper(output);
        }

        private async Task Init()
        {
            await SettingService.Instance.InitAsync();

            var setting = SettingService.Instance.Current;

            var freeSql = FreeSqlHelper.Init(setting.DatabaseType, setting.DatabaseConnectionString, true);
            if (freeSql != null)
            {
                FreeSqlHelper.Configure(freeSql);
            }
        }

        [Fact]
        public async Task Test_Content()
        {
            try
            {
                await Init();

                var fsql = FreeSqlHelper.FreeSql;

                string[] ids = [];

                var list = fsql.Select<TaskInfo>()
                    .Where(c => c.Status == Base.TaskStatus.SUCCESS && c.IsPartner == false && c.IsOfficial == false)
                    .WhereIf(ids.Length > 0, c => ids.Contains(c.Id))
                    .OrderByRandom()
                    .Take(50000)
                    .ToList();

                foreach (var item in list)
                {
                    try
                    {
                        var content = item.GetProperty(Constants.TASK_PROPERTY_MESSAGE_CONTENT, "");
                        if (!string.IsNullOrWhiteSpace(content))
                        {
                            var pp = MjMessageParser.Parse(content);

                            var prompt = pp.Prompt;

                            if (!string.IsNullOrWhiteSpace(prompt))
                            {
                                _output.WriteLine($"原始: {prompt}");
                                var r = MjPromptParser.Parse(prompt);
                                _output.WriteLine($"干净: {r.CleanPrompt}");
                                _output.WriteLine("参数:");

                                foreach (var (p, info) in MjPromptParser.GetParamsWithInfo(r))
                                {
                                    var full = MjPromptParser.GetFullName(p.Name);
                                    var alias = p.Name != full ? $" ({full})" : "";
                                    var desc = info?.Description ?? "未知参数";
                                    _output.WriteLine($"  --{p.Name}{alias} = {p.Value ?? "(flag)"}  // {desc}");
                                }

                                var seed = r.GetSeed();
                                _output.WriteLine($"Seed: {seed?.ToString() ?? "null"}");
                                _output.WriteLine($"全名重建: {MjPromptParser.Rebuild(r, useFullName: true)}");
                                _output.WriteLine(new string('-', 70));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _output.WriteLine("解析失败: {0}", ex.Message);

                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine("解析失败: {0}", ex.Message);

                throw;
            }
        }

        [Fact]
        public void Parse_BasicPrompts_ShouldExtractParametersCorrectly()
        {
            var tests = new[]
            {
                "BIg a cute cat --v 7 --ar 16:9 --s 500 --c 30",
                "dog --seed 123 --seed 456 --no girl, boy --no cat --w 1000 --relax",
                "robot --p 123 --tile --turbo --q .5 --r 3",
                "girl --no boy --no car , dog",
                "https://s.mj.run/JJl2ZVRyWyg https://s.mj.run/pnKPjAjFpAo --ar 1:1 --v 6.1 --s 250 --raw"
            };

            _output.WriteLine("=== Midjourney 参数解析器测试 ===\n");

            foreach (var prompt in tests)
            {
                _output.WriteLine($"原始: {prompt}");
                var r = MjPromptParser.Parse(prompt);
                _output.WriteLine($"干净: {r.CleanPrompt}");
                _output.WriteLine("参数:");

                foreach (var (p, info) in MjPromptParser.GetParamsWithInfo(r))
                {
                    var full = MjPromptParser.GetFullName(p.Name);
                    var alias = p.Name != full ? $" ({full})" : "";
                    var desc = info?.Description ?? "未知参数";
                    _output.WriteLine($"  --{p.Name}{alias} = {p.Value ?? "(flag)"}  // {desc}");
                }

                var seed = r.GetSeed();
                _output.WriteLine($"Seed: {seed?.ToString() ?? "null"}");
                _output.WriteLine($"全名重建: {MjPromptParser.Rebuild(r, useFullName: true)}");
                _output.WriteLine(new string('-', 70));
            }
        }

        [Fact]
        public void Parse_EmptyPrompt_ShouldReturnEmptyResult()
        {
            var result = MjPromptParser.Parse("");
            Assert.Empty(result.CleanPrompt);
            Assert.Empty(result.Parameters);
        }

        [Fact]
        public void Parse_PromptWithoutParams_ShouldReturnOriginalPrompt()
        {
            var prompt = "a beautiful sunset over mountains";
            var result = MjPromptParser.Parse(prompt);

            Assert.Equal(prompt, result.CleanPrompt);
            Assert.Empty(result.Parameters);
        }

        [Fact]
        public void Parse_Version_ShouldExtractCorrectly()
        {
            var result = MjPromptParser.Parse("cat --v 7");
            Assert.Equal("7", result.GetVersion());

            var result2 = MjPromptParser.Parse("cat --version 6.1");
            Assert.Equal("6.1", result2.GetVersion());
        }

        [Fact]
        public void Parse_AspectRatio_ShouldExtractCorrectly()
        {
            var result = MjPromptParser.Parse("cat --ar 16:9");
            Assert.Equal("16:9", result.GetAspectRatio());

            var result2 = MjPromptParser.Parse("cat --aspect 2:3");
            Assert.Equal("2:3", result2.GetAspectRatio());
        }

        [Fact]
        public void Parse_Seed_ShouldExtractCorrectly()
        {
            var result = MjPromptParser.Parse("cat --seed 12345");
            Assert.Equal(12345L, result.GetSeed());
        }

        [Fact]
        public void Parse_MultipleSameParams_ShouldExtractAll()
        {
            var result = MjPromptParser.Parse("dog --seed 123 --seed 456");
            var seeds = result.GetValues("seed");

            Assert.Equal(2, seeds.Count);
            Assert.Contains("123", seeds);
            Assert.Contains("456", seeds);
        }

        [Fact]
        public void Parse_NoParams_ShouldExtractAllItems()
        {
            var result = MjPromptParser.Parse("cat --no dogs, text --no cars");
            var noItems = result.GetNoItems();

            Assert.Contains("dogs", noItems);
            Assert.Contains("text", noItems);
            Assert.Contains("cars", noItems);
        }

        [Fact]
        public void Parse_FlagParams_ShouldRecognize()
        {
            var result = MjPromptParser.Parse("cat --raw --tile --turbo --relax");

            Assert.True(result.IsRawMode);
            Assert.True(result.IsTileMode);
            Assert.True(result.IsTurboMode);
            Assert.True(result.IsRelaxMode);
        }

        [Fact]
        public void Parse_Stylize_ShouldExtractCorrectly()
        {
            var result = MjPromptParser.Parse("cat --s 500");
            Assert.Equal(500, result.GetStylize());

            var result2 = MjPromptParser.Parse("cat --stylize 750");
            Assert.Equal(750, result2.GetStylize());
        }

        [Fact]
        public void Parse_Chaos_ShouldExtractCorrectly()
        {
            var result = MjPromptParser.Parse("cat --c 50");
            Assert.Equal(50, result.GetChaos());

            var result2 = MjPromptParser.Parse("cat --chaos 30");
            Assert.Equal(30, result2.GetChaos());
        }

        [Fact]
        public void Parse_Weird_ShouldExtractCorrectly()
        {
            var result = MjPromptParser.Parse("cat --w 1000");
            Assert.Equal(1000, result.GetWeird());
        }

        [Fact]
        public void Parse_Quality_ShouldExtractCorrectly()
        {
            var result = MjPromptParser.Parse("cat --q .5");
            Assert.Equal(0.5, result.GetQuality());

            var result2 = MjPromptParser.Parse("cat --quality 1");
            Assert.Equal(1.0, result2.GetQuality());
        }

        [Fact]
        public void Parse_ImageUrls_ShouldKeepInCleanPrompt()
        {
            var prompt = "https://s.mj.run/abc123 cute cat --v 7";
            var result = MjPromptParser.Parse(prompt);

            Assert.Contains("https://s.mj.run/abc123", result.CleanPrompt);
            Assert.Equal("7", result.GetVersion());
        }

        [Fact]
        public void Parse_Niji_ShouldRecognize()
        {
            var result = MjPromptParser.Parse("anime girl --niji 6");
            Assert.True(result.IsNijiMode);
            Assert.Equal("6", result.GetValue("niji"));
        }

        [Fact]
        public void Parse_DraftMode_ShouldRecognize()
        {
            var result = MjPromptParser.Parse("cat --draft");
            Assert.True(result.IsDraftMode);
        }

        [Fact]
        public void Parse_VideoParams_ShouldExtractCorrectly()
        {
            var result = MjPromptParser.Parse("landscape --motion high --loop --bs 4");

            Assert.Equal("high", result.GetValue("motion"));
            Assert.True(result.HasParam("loop"));
            Assert.Equal("4", result.GetValue("bs"));
        }

        [Fact]
        public void Parse_V7Params_ShouldExtractCorrectly()
        {
            var result = MjPromptParser.Parse("portrait --oref https://img.com/ref.png --ow 400 --exp 25");

            Assert.Equal("https://img.com/ref.png", result.GetValue("oref"));
            Assert.Equal("400", result.GetValue("ow"));
            Assert.Equal("25", result.GetValue("exp"));
        }

        [Fact]
        public void Parse_Personalize_ShouldExtractCorrectly()
        {
            var result = MjPromptParser.Parse("cat --p abc123");
            Assert.Equal("abc123", result.GetValue("profile"));

            var result2 = MjPromptParser.Parse("cat --profile xyz789");
            Assert.Equal("xyz789", result2.GetValue("profile"));
        }

        [Fact]
        public void GetFullName_ShouldConvertAliases()
        {
            Assert.Equal("version", MjPromptParser.GetFullName("v"));
            Assert.Equal("aspect", MjPromptParser.GetFullName("ar"));
            Assert.Equal("chaos", MjPromptParser.GetFullName("c"));
            Assert.Equal("stylize", MjPromptParser.GetFullName("s"));
            Assert.Equal("weird", MjPromptParser.GetFullName("w"));
            Assert.Equal("quality", MjPromptParser.GetFullName("q"));
            Assert.Equal("repeat", MjPromptParser.GetFullName("r"));
            Assert.Equal("profile", MjPromptParser.GetFullName("p"));
        }

        [Fact]
        public void GetFullName_UnknownParam_ShouldReturnOriginal()
        {
            Assert.Equal("unknown", MjPromptParser.GetFullName("unknown"));
            Assert.Equal("seed", MjPromptParser.GetFullName("seed"));
        }

        [Fact]
        public void IsValidParam_ShouldRecognizeOfficialParams()
        {
            Assert.True(MjPromptParser.IsValidParam("v"));
            Assert.True(MjPromptParser.IsValidParam("version"));
            Assert.True(MjPromptParser.IsValidParam("ar"));
            Assert.True(MjPromptParser.IsValidParam("seed"));
            Assert.True(MjPromptParser.IsValidParam("oref"));
            Assert.True(MjPromptParser.IsValidParam("exp"));
            Assert.False(MjPromptParser.IsValidParam("unknown"));
        }

        [Fact]
        public void Rebuild_ShouldRecreatePrompt()
        {
            var prompt = "cat --v 7 --ar 16:9";
            var result = MjPromptParser.Parse(prompt);
            var rebuilt = MjPromptParser.Rebuild(result);

            Assert.Contains("cat", rebuilt);
            Assert.Contains("--v 7", rebuilt);
            Assert.Contains("--ar 16:9", rebuilt);
        }

        [Fact]
        public void Rebuild_WithFullName_ShouldUseFullNames()
        {
            var prompt = "cat --v 7 --s 500 --c 30";
            var result = MjPromptParser.Parse(prompt);
            var rebuilt = MjPromptParser.Rebuild(result, useFullName: true);

            Assert.Contains("--version 7", rebuilt);
            Assert.Contains("--stylize 500", rebuilt);
            Assert.Contains("--chaos 30", rebuilt);
        }

        [Fact]
        public void Rebuild_MergeNo_ShouldCombineNoParams()
        {
            var prompt = "cat --no dogs --no cars --no text";
            var result = MjPromptParser.Parse(prompt);
            var rebuilt = MjPromptParser.Rebuild(result, mergeNo: true);

            // 应该只有一个 --no，包含所有项
            Assert.Contains("--no dogs, cars, text", rebuilt);
        }

        [Fact]
        public void NormalizeToFullName_ShouldConvertAllParams()
        {
            var prompt = "cat --v 7 --ar 16:9 --s 500";
            var result = MjPromptParser.Parse(prompt);
            var normalized = MjPromptParser.NormalizeToFullName(result);

            Assert.All(normalized.Parameters, p =>
            {
                Assert.DoesNotContain(p.Name, new[] { "v", "ar", "s" });
            });

            Assert.Contains(normalized.Parameters, p => p.Name == "version");
            Assert.Contains(normalized.Parameters, p => p.Name == "aspect");
            Assert.Contains(normalized.Parameters, p => p.Name == "stylize");
        }

        [Fact]
        public void GetParamInfo_ShouldReturnCorrectInfo()
        {
            var info = MjPromptParser.GetParamInfo("v");

            Assert.NotNull(info);
            Assert.Equal("version", info.FullName);
            Assert.Equal("v", info.ShortName);
            Assert.Contains("模型版本", info.Description);
        }

        [Fact]
        public void GetParamInfo_UnknownParam_ShouldReturnNull()
        {
            var info = MjPromptParser.GetParamInfo("unknown");
            Assert.Null(info);
        }

        [Fact]
        public void GetAllParams_ShouldReturnAllDefinitions()
        {
            var allParams = MjPromptParser.GetAllParams().ToList();

            Assert.NotEmpty(allParams);
            Assert.Contains(allParams, p => p.FullName == "version");
            Assert.Contains(allParams, p => p.FullName == "aspect");
            Assert.Contains(allParams, p => p.FullName == "oref");
            Assert.Contains(allParams, p => p.FullName == "motion");
        }

        [Fact]
        public void Parse_ComplexPrompt_ShouldHandleCorrectly()
        {
            var prompt = "https://s.mj.run/abc123 a beautiful girl with long hair, sunset, detailed --v 7 --ar 2:3 --s 750 --c 20 --no text, watermark, blur --seed 987654321 --raw --tile";
            var result = MjPromptParser.Parse(prompt);

            Assert.Contains("https://s.mj.run/abc123", result.CleanPrompt);
            Assert.Contains("a beautiful girl", result.CleanPrompt);
            Assert.Equal("7", result.GetVersion());
            Assert.Equal("2:3", result.GetAspectRatio());
            Assert.Equal(750, result.GetStylize());
            Assert.Equal(20, result.GetChaos());
            Assert.Equal(987654321L, result.GetSeed());
            Assert.True(result.IsRawMode);
            Assert.True(result.IsTileMode);

            var noItems = result.GetNoItems();
            Assert.Contains("text", noItems);
            Assert.Contains("watermark", noItems);
            Assert.Contains("blur", noItems);
        }
    }
}