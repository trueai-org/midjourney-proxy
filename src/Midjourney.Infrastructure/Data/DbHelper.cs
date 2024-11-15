// Midjourney Proxy - Proxy for Midjourney's Discord, enabling AI drawings via API with one-click face swap. A free, non-profit drawing API project.
// Copyright (C) 2024 trueai.org

// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.

// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

// Additional Terms:
// This software shall not be used for any illegal activities. 
// Users must comply with all applicable laws and regulations,
// particularly those related to image and video processing. 
// The use of this software for any form of illegal face swapping,
// invasion of privacy, or any other unlawful purposes is strictly prohibited. 
// Violation of these terms may result in termination of the license and may subject the violator to legal action.

namespace Midjourney.Infrastructure.Data
{
    /// <summary>
    /// 任务帮助类
    /// </summary>
    public class DbHelper : SingletonBase<DbHelper>
    {
        private readonly IDataHelper<TaskInfo> _taskStore;
        private readonly IDataHelper<DiscordAccount> _accountStore;
        private readonly IDataHelper<User> _userStore;
        private readonly IDataHelper<DomainTag> _domainStore;
        private readonly IDataHelper<BannedWord> _bannedWordStore;
        //private readonly IDataHelper<Setting> _settingStore;

        public DbHelper()
        {
            if (GlobalConfiguration.Setting.IsMongo)
            {
                _taskStore = new MongoDBRepository<TaskInfo>();
                _accountStore = new MongoDBRepository<DiscordAccount>();
                _userStore = new MongoDBRepository<User>();
                _domainStore = new MongoDBRepository<DomainTag>();
                _bannedWordStore = new MongoDBRepository<BannedWord>();
                //_settingStore = new MongoDBRepository<Setting>();
            }
            else
            {
                _taskStore = LiteDBHelper.TaskStore;
                _accountStore = LiteDBHelper.AccountStore;
                _userStore = LiteDBHelper.UserStore;
                _domainStore = LiteDBHelper.DomainStore;
                _bannedWordStore = LiteDBHelper.BannedWordStore;
                //_settingStore = LiteDBHelper.SettingStore;
            }
        }

        /// <summary>
        /// Task 数据库操作
        /// </summary>
        public IDataHelper<TaskInfo> TaskStore => _taskStore;

        /// <summary>
        /// Discord 账号数据库操作
        /// </summary>
        public IDataHelper<DiscordAccount> AccountStore => _accountStore;

        /// <summary>
        /// User 数据库操作
        /// </summary>
        public IDataHelper<User> UserStore => _userStore;

        /// <summary>
        /// 领域标签数据库操作
        /// </summary>
        public IDataHelper<DomainTag> DomainStore => _domainStore;

        /// <summary>
        /// 禁用词数据库操作
        /// </summary>
        public IDataHelper<BannedWord> BannedWordStore => _bannedWordStore;

        ///// <summary>
        ///// 系统配置数据库操作
        ///// </summary>
        //public IDataHelper<Setting> SettingStore => _settingStore;
    }
}
