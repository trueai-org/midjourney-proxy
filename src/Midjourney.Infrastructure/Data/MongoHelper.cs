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
using MongoDB.Bson;
using MongoDB.Driver;
using Serilog;

namespace Midjourney.Infrastructure.Data
{
    /// <summary>
    /// Mongo DB 单例辅助
    /// </summary>
    public abstract class MongoHelper : MongoHelper<MongoHelper> { }

    /// <summary>
    /// Mongo DB 单例辅助
    /// </summary>
    public abstract class MongoHelper<TMark>
    {
        private static readonly object _locker = new object();

        private static IMongoDatabase _instance;

        static MongoHelper()
        {

        }

        /// <summary>
        /// MongoDB 静态实列
        /// </summary>
        public static IMongoDatabase Instance
        {
            get
            {
                if (_instance != null)
                {
                    return _instance;
                }

                lock (_locker)
                {
                    if (_instance == null)
                    {
                        if (typeof(TMark) == typeof(MongoHelper))
                        {
                            var connectionString = GlobalConfiguration.Setting.MongoDefaultConnectionString;
                            var name = GlobalConfiguration.Setting.MongoDefaultDatabase;

                            if (!string.IsNullOrEmpty(connectionString) && !string.IsNullOrEmpty(name))
                            {
                                var client = new MongoClient(connectionString);
                                var database = client.GetDatabase(name);
                                _instance = database;
                            }
                        }
                    }
                }

                if (_instance == null)
                {
                    throw new Exception("使用前请初始化 MongoHelper.Initialization(); ");
                }

                return _instance;
            }
        }

        /// <summary>
        /// Gets a collection
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static IMongoCollection<T> GetCollection<T>() => Instance.GetCollection<T>();

        /// <summary>
        /// Gets a collection by name
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="name"></param>
        /// <param name="settings"></param>
        /// <returns></returns>
        public static IMongoCollection<T> GetCollection<T>(string name, MongoCollectionSettings settings = null)
            => Instance.GetCollection<T>(name, settings);

        /// <summary>
        /// 初始化 Mongo DB，使用默认的 Mongo DB 不需要初始化
        /// </summary>
        /// <param name="database"></param>
        public static void Initialization(IMongoDatabase database)
        {
            _instance = database;
        }

        /// <summary>
        /// 验证 mongo 连接
        /// </summary>
        /// <returns></returns>
        public static bool Verify()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(GlobalConfiguration.Setting.MongoDefaultConnectionString)
                    || string.IsNullOrWhiteSpace(GlobalConfiguration.Setting.MongoDefaultDatabase))
                {
                    return false;
                }

                var client = new MongoClient(GlobalConfiguration.Setting.MongoDefaultConnectionString);
                var database = client.GetDatabase(GlobalConfiguration.Setting.MongoDefaultDatabase);
                return database.RunCommandAsync((Command<BsonDocument>)"{ping:1}").Wait(1000);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "MongoDB 连接失败");

                return false;
            }
        }
    }
}
