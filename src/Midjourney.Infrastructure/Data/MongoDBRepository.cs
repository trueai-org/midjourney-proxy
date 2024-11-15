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

using MongoDB.Driver;
using System.Linq.Expressions;

namespace Midjourney.Infrastructure.Data
{
    public class MongoDBRepository<T> : IDataHelper<T> where T : IBaseId
    {
        private readonly IMongoCollection<T> _collection;

        public MongoDBRepository()
        {
            _collection = MongoHelper.GetCollection<T>();
        }

        public IMongoCollection<T> MongoCollection => _collection;

        public void Init()
        {
            // MongoDB 不需要显式地创建索引，除非你需要额外的索引
        }

        public void Add(T entity)
        {
            _collection.InsertOne(entity);
        }

        public void AddRange(IEnumerable<T> entities)
        {
            _collection.InsertMany(entities);
        }

        public void Delete(T entity)
        {
            var filter = Builders<T>.Filter.Eq("_id", entity.Id);
            _collection.DeleteOne(filter);
        }

        public int Delete(Expression<Func<T, bool>> predicate)
        {
            var result = _collection.DeleteMany(predicate);
            return (int)result.DeletedCount;
        }

        public void Update(T entity)
        {
            var filter = Builders<T>.Filter.Eq("_id", entity.Id);
            _collection.ReplaceOne(filter, entity);
        }

        /// <summary>
        /// 部分更新
        /// </summary>
        /// <param name="fields">BotToken,IsBlend,Properties</param>
        /// <param name="item"></param>
        /// <returns></returns>
        public bool Update(string fields, T item)
        {
            // 获取现有文档
            var model = _collection.Find(c => c.Id == item.Id).FirstOrDefault();
            if (model == null)
                return false;

            // 将更新对象的字段值复制到现有文档
            var fieldArray = fields.Split(',');
            foreach (var field in fieldArray)
            {
                var prop = typeof(T).GetProperty(field.Trim());
                if (prop != null)
                {
                    var newValue = prop.GetValue(item);
                    prop.SetValue(model, newValue);
                }
            }

            // 更新文档
            _collection.ReplaceOne(c => c.Id == item.Id, model);

            return true;
        }


        public List<T> GetAll()
        {
            return _collection.Find(Builders<T>.Filter.Empty).ToList();
        }


        /// <summary>
        /// 获取所有实体的 ID 列表。
        /// </summary>
        /// <returns></returns>
        public List<string> GetAllIds()
        {
            return _collection.Find(Builders<T>.Filter.Empty).Project(x => x.Id).ToList();
        }

        public List<T> Where(Expression<Func<T, bool>> predicate)
        {
            return _collection.Find(predicate).ToList();
        }

        public List<T> Where(Expression<Func<T, bool>> filter, Expression<Func<T, object>> orderBy, bool orderByAsc = true)
        {
            var query = _collection.Find(filter);
            if (orderByAsc)
            {
                query = query.SortBy(orderBy);
            }
            else
            {
                query = query.SortByDescending(orderBy);
            }
            return query.ToList();
        }

        public T Single(Expression<Func<T, bool>> predicate)
        {
            return _collection.Find(predicate).FirstOrDefault();
        }

        public T Single(Expression<Func<T, bool>> filter, Expression<Func<T, object>> orderBy, bool orderByAsc = true)
        {
            var query = _collection.Find(filter);
            if (orderByAsc)
            {
                query = query.SortBy(orderBy);
            }
            else
            {
                query = query.SortByDescending(orderBy);
            }
            return query.FirstOrDefault();
        }

        public bool Any(Expression<Func<T, bool>> predicate)
        {
            return _collection.Find(predicate).Any();
        }

        public long Count(Expression<Func<T, bool>> predicate)
        {
            return _collection.CountDocuments(predicate);
        }

        public void Save(T entity)
        {
            if (entity != null && !string.IsNullOrEmpty(entity.Id))
            {
                var model = _collection.Find(c => c.Id == entity.Id).FirstOrDefault();
                if (model == null)
                {
                    _collection.InsertOne(entity);
                }
                else
                {
                    _collection.ReplaceOne(c => c.Id == entity.Id, entity);
                }
            }
        }

        public void Delete(string id)
        {
            var filter = Builders<T>.Filter.Eq("_id", id);
            _collection.DeleteOne(filter);
        }

        public T Get(string id)
        {
            return _collection.Find(Builders<T>.Filter.Eq("_id", id)).FirstOrDefault();
        }

        public List<T> List()
        {
            return _collection.Find(Builders<T>.Filter.Empty).ToList();
        }

        public List<T> Where(Expression<Func<T, bool>> filter, Expression<Func<T, object>> orderBy, bool orderByAsc, int limit)
        {
            var query = _collection.Find(filter);
            if (orderByAsc)
            {
                return query.SortBy(orderBy).Limit(limit).ToList();
            }
            else
            {
                return query.SortByDescending(orderBy).Limit(limit).ToList();
            }
        }
    }
}
