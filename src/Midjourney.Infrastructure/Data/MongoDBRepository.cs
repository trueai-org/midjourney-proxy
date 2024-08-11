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

        public List<T> GetAll()
        {
            return _collection.Find(Builders<T>.Filter.Empty).ToList();
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
            var filter = Builders<T>.Filter.Eq("_id", entity.Id);
            var existingEntity = _collection.Find(filter).FirstOrDefault();
            if (existingEntity == null)
            {
                _collection.InsertOne(entity);
            }
            else
            {
                _collection.ReplaceOne(filter, entity);
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
