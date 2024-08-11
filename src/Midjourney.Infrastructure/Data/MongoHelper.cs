using MongoDB.Driver;

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
    }
}
