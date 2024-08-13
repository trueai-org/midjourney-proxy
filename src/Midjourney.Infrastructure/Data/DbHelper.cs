namespace Midjourney.Infrastructure.Data
{
    /// <summary>
    /// 数据库帮助类。
    /// </summary>
    public class DbHelper
    {
        /// <summary>
        /// 任务存储。
        /// </summary>
        public static LiteDBRepository<TaskInfo> TaskStore = new LiteDBRepository<TaskInfo>("data/mj.db");

        /// <summary>
        /// Discord 账号存储。
        /// </summary>
        public static LiteDBRepository<DiscordAccount> AccountStore = new LiteDBRepository<DiscordAccount>("data/mj.db");

        /// <summary>
        /// User 账号存储。
        /// </summary>
        public static LiteDBRepository<User> UserStore = new LiteDBRepository<User>("data/mj.db");

        /// <summary>
        /// 领域标签存储。
        /// </summary>
        public static LiteDBRepository<DomainTag> DomainStore = new LiteDBRepository<DomainTag>("data/mj.db");

        /// <summary>
        /// 系统配置存储。
        /// </summary>
        public static LiteDBRepository<Setting> SettingStore = new LiteDBRepository<Setting>("data/mj.db");

        /// <summary>
        /// 禁用词存储。
        /// </summary>
        public static LiteDBRepository<BannedWord> BannedWordStore = new LiteDBRepository<BannedWord>("data/mj.db");

    }
}