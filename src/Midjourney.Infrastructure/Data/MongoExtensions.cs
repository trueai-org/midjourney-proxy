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
using LiteDB;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using System.Linq.Expressions;

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

        /// <summary>
        /// 排序条件扩展
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="query"></param>
        /// <param name="where"></param>
        /// <param name="keySelector"></param>
        /// <param name="desc"></param>
        /// <returns></returns>
        public static IMongoQueryable<T> OrderByIf<T>(this IMongoQueryable<T> query, bool where, Expression<Func<T, object>> keySelector, bool desc = true)
        {
            if (desc)
            {
                return where ? query.OrderByDescending(keySelector) : query;
            }
            else
            {
                return where ? query.OrderBy(keySelector) : query;
            }
        }
    }
}
