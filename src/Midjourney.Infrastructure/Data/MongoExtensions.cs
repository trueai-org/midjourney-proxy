using MongoDB.Driver;

namespace Midjourney.Infrastructure.Data
{
    public static class MongoExtensions
    {
        /// <summary>
        /// Get name of the collection
        /// </summary>
        /// <typeparam name="TDocument"></typeparam>
        /// <returns></returns>
        private static string GetCollectionName<TDocument>()
        {
            return (typeof(TDocument).GetCustomAttributes(typeof(BsonCollectionAttribute), true).FirstOrDefault() as BsonCollectionAttribute).CollectionName;
        }

        /// <summary>
        /// Gets type of the collection
        /// </summary>
        /// <typeparam name="TDocument"></typeparam>
        /// <param name="database"></param>
        /// <param name="settings"></param>
        /// <returns></returns>
        public static IMongoCollection<TDocument> GetCollection<TDocument>(this IMongoDatabase database, MongoCollectionSettings settings = null)
        {
            return database.GetCollection<TDocument>(GetCollectionName<TDocument>(), settings);
        }
    }
}
