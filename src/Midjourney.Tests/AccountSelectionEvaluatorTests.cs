using System.Reflection;
using System.Runtime.CompilerServices;
using Midjourney.Base;
using Midjourney.Base.Models;
using Midjourney.Services;

namespace Midjourney.Tests
{
    /// <summary>
    /// 账号选号批量快照与纯内存判定逻辑测试。
    /// 覆盖快速/慢速/图生文/视频的边界分支，以及 200 账号下规则使用快照的行为。
    /// </summary>
    public class AccountSelectionEvaluatorTests
    {
        [Fact]
        public void Fast_QuotaQueueAndConcurrencyBranches()
        {
            var account = NewAccount();
            var service = CreateService(account);

            Assert.False(service.IsAllowContinue(GenerationSpeedMode.FAST, Snapshot(fastAvailableCount: 6)));
            Assert.True(service.IsAllowContinue(GenerationSpeedMode.FAST, Snapshot(fastAvailableCount: 7)));
            Assert.True(service.IsAllowContinue(GenerationSpeedMode.FAST, Snapshot(fastAvailableCount: 13)));
            Assert.True(service.IsAllowContinue(GenerationSpeedMode.FAST, Snapshot(fastAvailableCount: 7, fastConcurrency: 5)));
            Assert.False(service.IsAllowContinue(GenerationSpeedMode.FAST, Snapshot(fastAvailableCount: 7, fastConcurrency: 8)));
            Assert.False(service.IsAllowContinue(GenerationSpeedMode.FAST, Snapshot(fastAvailableCount: 7, fastQueueCount: 10)));

            Assert.False(service.IsAllowContinue(GenerationSpeedMode.TURBO, Snapshot(fastAvailableCount: 12)));
            Assert.True(service.IsAllowContinue(GenerationSpeedMode.TURBO, Snapshot(fastAvailableCount: 13)));
        }

        [Fact]
        public void Relax_YouChuanQuotaBranches()
        {
            var account = NewAccount(isYouChuan: true);
            var service = CreateService(account);

            Assert.True(service.IsAllowContinue(GenerationSpeedMode.RELAX, Snapshot(relaxCount: 0)));
            Assert.True(service.IsAllowContinue(GenerationSpeedMode.RELAX, Snapshot(relaxCount: 4, relaxConcurrency: 0, relaxQueueCount: 0)));
            Assert.False(service.IsAllowContinue(GenerationSpeedMode.RELAX, Snapshot(relaxCount: 4, relaxConcurrency: 8, relaxQueueCount: 5)));
            Assert.False(service.IsAllowContinue(GenerationSpeedMode.RELAX, Snapshot(relaxCount: 17)));
            Assert.False(service.IsAllowContinue(GenerationSpeedMode.RELAX, Snapshot(relaxCount: 0, relaxQueueCount: 10)));

            account.YouChuanRelaxedReset = DateTime.Now.Date.AddDays(1);
            Assert.False(service.IsAllowContinue(GenerationSpeedMode.RELAX, Snapshot(relaxCount: 0)));
        }

        [Fact]
        public void Describe_QuotaBranches()
        {
            var account = NewAccount(isYouChuan: true);
            account.IsDescribe = true;
            var service = CreateService(account);

            Assert.True(service.IsAllowDescribe(Snapshot(describeCount: 0)));
            Assert.True(service.IsAllowDescribe(Snapshot(describeCount: 187, describeConcurrency: 0, describeQueueCount: 0)));
            Assert.False(service.IsAllowDescribe(Snapshot(describeCount: 187, describeConcurrency: 10, describeQueueCount: 5)));
            Assert.False(service.IsAllowDescribe(Snapshot(describeCount: 200)));

            account.YouChuanPicreadReset = DateTime.Now.Date.AddDays(1);
            Assert.False(service.IsAllowDescribe(Snapshot(describeCount: 0)));

            var official = NewAccount(isOfficial: true);
            official.IsDescribe = true;
            Assert.True(CreateService(official).IsAllowDescribe(Snapshot(describeCount: 999)));

            var discord = NewAccount();
            discord.IsDescribe = true;
            Assert.True(CreateService(discord).IsAllowDescribe(Snapshot(describeCount: 999)));

            var disabled = NewAccount();
            disabled.IsDescribe = false;
            Assert.False(CreateService(disabled).IsAllowDescribe(Snapshot(describeCount: 0)));
        }

        [Fact]
        public void Video_QuotaBranches()
        {
            var youChuan = NewAccount(isYouChuan: true);
            var youChuanService = CreateService(youChuan);

            Assert.True(youChuanService.IsAllowGenerateVideo(GenerationSpeedMode.FAST, Snapshot(fastAvailableCount: 9)));
            Assert.False(youChuanService.IsAllowGenerateVideo(GenerationSpeedMode.FAST, Snapshot(fastAvailableCount: 8)));
            Assert.False(youChuanService.IsAllowGenerateVideo(GenerationSpeedMode.RELAX, Snapshot(fastAvailableCount: 99)));
            Assert.True(youChuanService.IsAllowGenerateVideo(GenerationSpeedMode.TURBO, Snapshot(fastAvailableCount: 17)));
            Assert.False(youChuanService.IsAllowGenerateVideo(GenerationSpeedMode.TURBO, Snapshot(fastAvailableCount: 16)));

            var officialRelax = NewAccount(isOfficial: true);
            officialRelax.IsRelaxVideo = true;
            Assert.True(CreateService(officialRelax).IsAllowGenerateVideo(GenerationSpeedMode.RELAX, Snapshot(fastAvailableCount: 0)));

            var officialNoRelax = NewAccount(isOfficial: true);
            officialNoRelax.IsRelaxVideo = false;
            Assert.False(CreateService(officialNoRelax).IsAllowGenerateVideo(GenerationSpeedMode.RELAX, Snapshot(fastAvailableCount: 0)));

            var discordRelax = NewAccount();
            discordRelax.IsRelaxVideo = true;
            Assert.True(CreateService(discordRelax).IsAllowGenerateVideo(GenerationSpeedMode.RELAX, Snapshot(fastAvailableCount: 0)));

            var discordNoRelax = NewAccount();
            discordNoRelax.IsRelaxVideo = false;
            Assert.False(CreateService(discordNoRelax).IsAllowGenerateVideo(GenerationSpeedMode.RELAX, Snapshot(fastAvailableCount: 0)));
        }

        [Fact]
        public void QueueAndRunningCounts_MatchAccountType()
        {
            var youChuan = NewAccount(isYouChuan: true);
            var youChuanService = CreateService(youChuan);
            var youChuanSnapshot = Snapshot(
                fastQueueCount: 1,
                relaxQueueCount: 2,
                describeQueueCount: 3,
                fastConcurrency: 4,
                relaxConcurrency: 5,
                describeConcurrency: 6);

            Assert.Equal(6, youChuanService.GetQueueTaskCountFromValues(youChuanSnapshot));
            Assert.Equal(15, youChuanService.GetRunningTaskCountFromValues(youChuanSnapshot));
            Assert.Equal(6, youChuanSnapshot.GetQueueTaskCount(youChuan));
            Assert.Equal(15, youChuanSnapshot.GetRunningTaskCount(youChuan));

            var discord = NewAccount();
            var discordService = CreateService(discord);
            var discordSnapshot = Snapshot(
                fastQueueCount: 1,
                relaxQueueCount: 2,
                describeQueueCount: 3,
                fastConcurrency: 4,
                relaxConcurrency: 5,
                describeConcurrency: 6);

            Assert.Equal(4, discordService.GetQueueTaskCountFromValues(discordSnapshot));
            Assert.Equal(10, discordService.GetRunningTaskCountFromValues(discordSnapshot));
            Assert.Equal(4, discordSnapshot.GetQueueTaskCount(discord));
            Assert.Equal(10, discordSnapshot.GetRunningTaskCount(discord));
        }

        [Fact]
        public void BestWaitIdleRule_200Accounts_UsesSnapshots()
        {
            var instances = new List<DiscordService>();
            var snapshots = new Dictionary<DiscordService, AccountRuntimeSnapshot>();

            for (var i = 0; i < 200; i++)
            {
                var account = NewAccount();
                account.QueueSize = 10;
                var service = CreateService(account);
                instances.Add(service);
                snapshots[service] = Snapshot(fastQueueCount: i % 10);
            }

            var chosen = new BestWaitIdleRule().Choose(instances, snapshots);

            Assert.NotNull(chosen);
            Assert.Equal(0, snapshots[chosen].FastQueueCount);
        }

        [Fact]
        public void AccountFillRule_200Accounts_UsesSnapshots()
        {
            var instances = new List<DiscordService>();
            var snapshots = new Dictionary<DiscordService, AccountRuntimeSnapshot>();
            var baseTime = DateTime.Now;

            for (var i = 0; i < 200; i++)
            {
                var account = NewAccount();
                account.CoreSize = 3;
                account.QueueSize = 10;
                account.DateCreated = baseTime.AddMinutes(i);
                var service = CreateService(account);
                instances.Add(service);
                snapshots[service] = Snapshot(
                    fastQueueCount: i == 0 ? 0 : 10,
                    fastConcurrency: i == 0 ? 0 : 3);
            }

            var chosen = new AccountFillRule().Choose(instances, snapshots);

            Assert.Same(instances[0], chosen);
        }

        private static DiscordAccount NewAccount(bool isYouChuan = false, bool isOfficial = false)
        {
            return new DiscordAccount
            {
                Id = Guid.NewGuid().ToString("N"),
                ChannelId = Guid.NewGuid().ToString("N"),
                Enable = true,
                IsYouChuan = isYouChuan,
                IsOfficial = isOfficial,
                IsDescribe = false,
                IsRelaxVideo = false,
                AllowModes = [],
                CoreSize = 3,
                RelaxCoreSize = 3,
                QueueSize = 10,
                RelaxQueueSize = 10,
                QueueCount = 10,
                DayDrawLimit = -1,
                DayRelaxDrawLimit = -1,
                YouChuanRelaxDailyLimit = 17
            };
        }

        private static DiscordService CreateService(DiscordAccount account)
        {
            var service = (DiscordService)RuntimeHelpers.GetUninitializedObject(typeof(DiscordService));
            var field = typeof(DiscordService).GetField("_account", BindingFlags.Instance | BindingFlags.NonPublic);
            field!.SetValue(service, account);
            return service;
        }

        private static AccountRuntimeSnapshot Snapshot(
            int fastAvailableCount = 0,
            int relaxCount = 0,
            int todayRelaxCount = 0,
            int todayFastCount = 0,
            int todayTurboCount = 0,
            int describeCount = 0,
            int fastQueueCount = 0,
            int relaxQueueCount = 0,
            int describeQueueCount = 0,
            int fastConcurrency = 0,
            int relaxConcurrency = 0,
            int describeConcurrency = 0)
        {
            return new AccountRuntimeSnapshot
            {
                FastAvailableCount = fastAvailableCount,
                RelaxCount = relaxCount,
                TodayRelaxCount = todayRelaxCount,
                TodayFastCount = todayFastCount,
                TodayTurboCount = todayTurboCount,
                DescribeCount = describeCount,
                FastQueueCount = fastQueueCount,
                RelaxQueueCount = relaxQueueCount,
                DescribeQueueCount = describeQueueCount,
                FastConcurrency = fastConcurrency,
                RelaxConcurrency = relaxConcurrency,
                DescribeConcurrency = describeConcurrency
            };
        }
    }
}
