using Midjourney.Infrastructure.Domain;
using Midjourney.Infrastructure.Services;

namespace Midjourney.Infrastructure
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
        /// Discord账号存储。
        /// </summary>

        public static LiteDBRepository<DiscordAccount> AccountStore = new LiteDBRepository<DiscordAccount>("data/mj.db");
    }
}
