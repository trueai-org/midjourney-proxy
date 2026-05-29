using Midjourney.Base.Data;
using Midjourney.Base.Models;
using Midjourney.Base.Util;
using Midjourney.Base;
using Midjourney.Services;
using Xunit.Abstractions;

namespace Midjourney.Tests
{
    /// <summary>
    /// MjMessage 测试类
    /// </summary>
    public class MjMessageTests : BaseTests
    {
        private readonly TestOutputWrapper _output;

        public MjMessageTests(ITestOutputHelper output)
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
        public async Task Test_MjMessage_ParseContentAsync()
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

                            //if (pp.Action != item.Action && item.Action != TaskAction.REROLL && item.Action != TaskAction.BLEND)
                            //{
                            //}

                            _output.WriteLine("原始内容: {0}", content);
                            _output.WriteLine($"{item.Id}, 当前 Action: {item.Action}, 解析: {pp.Action}, {pp.ActionName}, {pp.Mode}, {pp.Status}, {pp.UpscaleType}, {pp.VariationType}");
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

        /// <summary>
        /// 测试视频
        /// </summary>
        /// <returns></returns>
        [Fact]
        public async Task Test_MjMessage_ParseContent_VideoAsync()
        {
            try
            {
                var content = """**car --ar 3:2 --seed 2960366558 --video 1** - <@1323696991334694963> (75%) (fast)""";
                var result = MjMessageParser.TryParseImagine(content);

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _output.WriteLine("解析失败: {0}", ex.Message);
                throw;
            }
        }

        /// <summary>
        /// 测试变化消息
        /// </summary>
        /// <returns></returns>
        [Fact]
        public async Task Test_Parse()
        {
            try
            {
                var content = """**cat** - Variations (Strong) by <@1217674642169528330> [(Open on website for full quality)](<https://midjourney.com/jobs/b21f08de-3d62-49aa-b38f-e06eddec9765>) (fast)""";
                var parse0 = MjMessageParser.Parse(content);
                Assert.NotNull(parse0);


                var parseResult = MjMessageParser.TryParseVariations(content);

                Assert.NotNull(parseResult);
                Assert.Equal(TaskAction.VARIATION, parseResult.Action);
                Assert.Equal("cat", parseResult.Prompt);
                Assert.Equal("Strong", parseResult.VariationType);
                Assert.Equal("1217674642169528330", parseResult.UserId);
                Assert.Equal("fast", parseResult.Mode);
                Assert.Equal("done", parseResult.Status);

                var legacyContent = """**car --v 7.0** - Variations (Strong) by <@123456789> (relaxed)""";
                var legacyResult = MjMessageParser.TryParseVariations(legacyContent);

                Assert.NotNull(legacyResult);
                Assert.Equal("car --v 7.0", legacyResult.Prompt);
                Assert.Equal("Strong", legacyResult.VariationType);
                Assert.Equal("123456789", legacyResult.UserId);
                Assert.Equal("relaxed", legacyResult.Mode);

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _output.WriteLine("解析失败: {0}", ex.Message);
                throw;
            }
        }
    }
}