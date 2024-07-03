using LiteDB;

namespace Midjourney.Infrastructure.Services
{
    public interface IBaseId
    {
        string Id { get; set; }
    }

    /// <summary>
    /// LiteDB 数据库的泛型仓库类。
    /// </summary>
    /// <typeparam name="T">数据实体类型。</typeparam>
    public class LiteDBRepository : ITaskStoreService
    {
        private static readonly object _lock = new();
        private readonly LiteDatabase _db;

        /// <summary>
        /// 创建 LiteDB 数据库的实例。
        /// </summary>
        /// <param name="dbName">数据库名称。</param>
        /// <param name="password">数据库密码。</param>
        public LiteDBRepository(string dbName, string password = "")
        {
            // 修复 LiteDB 空字符串转换为 null 的问题。
            BsonMapper.Global.EmptyStringToNull = false;
            BsonMapper.Global.TrimWhitespace = false;

            var dbPath = Path.Combine(Directory.GetCurrentDirectory(), dbName);

            lock (_lock)
            {
                if (!Directory.Exists(Path.GetDirectoryName(dbPath)))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dbPath));
                }
            }

            var connectionString = $"Filename={dbPath};Connection=shared;";
            if (!string.IsNullOrEmpty(password))
            {
                connectionString += $"Password={password};";
            }

            _db = new LiteDatabase(connectionString);
        }

        /// <summary>
        /// 初始化仓库，创建必要的索引。
        /// </summary>
        public void Init()
        {
            //_db.GetCollection<RootFileset>().EnsureIndex(x => x.RootPackageId);
            //_db.GetCollection<RootFileset>().EnsureIndex(x => x.FilesetSourceKey);
            //_db.GetCollection<RootPackage>().EnsureIndex(x => x.Key);
        }

        ///// <summary>
        ///// 添加一个实体到仓库。
        ///// </summary>
        ///// <param name="entity">要添加的实体。</param>
        //public void Add(T entity)
        //{
        //    var col = _db.GetCollection<T>();
        //    entity.Id = col.Insert(entity);
        //}

        ///// <summary>
        ///// 批量添加实体到仓库，优化处理超过 10000 条记录的情况。
        ///// </summary>
        ///// <param name="entities">要添加的实体集合。</param>
        //public void AddRange(IEnumerable<T> entities)
        //{
        //    const int batchSize = 10000;
        //    var col = _db.GetCollection<T>();

        //    var entityList = entities.ToList();
        //    for (int i = 0; i < entityList.Count; i += batchSize)
        //    {
        //        var batch = entityList.Skip(i).Take(batchSize);
        //        col.InsertBulk(batch);
        //    }
        //}

        ///// <summary>
        ///// 根据实体ID删除一个实体。
        ///// </summary>
        ///// <param name="id">实体的ID。</param>
        //public void Delete(int id)
        //{
        //    var col = _db.GetCollection<T>();
        //    col.Delete(new BsonValue(id));
        //}

        ///// <summary>
        ///// 删除指定的实体。
        ///// </summary>
        ///// <param name="obj">要删除的实体对象。</param>
        //public void Delete(T obj)
        //{
        //    var col = _db.GetCollection<T>();
        //    col.Delete(new BsonValue(obj.Id));
        //}

        ///// <summary>
        ///// 根据条件删除实体。
        ///// </summary>
        ///// <param name="predicate">删除条件表达式。</param>
        ///// <returns>删除的实体数量。</returns>
        //public int Delete(Expression<Func<T, bool>> predicate)
        //{
        //    return _db.GetCollection<T>().DeleteMany(predicate);
        //}

        ///// <summary>
        ///// 更新指定的实体。
        ///// </summary>
        ///// <param name="entity">要更新的实体对象。</param>
        //public void Update(T entity)
        //{
        //    var col = _db.GetCollection<T>();
        //    col.Update(entity);
        //}

        ///// <summary>
        ///// 获取所有实体。
        ///// </summary>
        ///// <returns>实体列表。</returns>
        //public List<T> GetAll()
        //{
        //    return _db.GetCollection<T>().FindAll().ToList();
        //}

        ///// <summary>
        ///// 根据实体ID获取实体。
        ///// </summary>
        ///// <param name="id">实体的ID。</param>
        ///// <returns>对应的实体对象。</returns>
        //public T Get(int id)
        //{
        //    return _db.GetCollection<T>().FindById(new BsonValue(id));
        //}

        ///// <summary>
        ///// 根据条件查询实体。
        ///// </summary>
        ///// <param name="predicate">查询条件表达式。</param>
        ///// <returns>满足条件的实体列表。</returns>
        //public List<T> Where(Expression<Func<T, bool>> predicate)
        //{
        //    return _db.GetCollection<T>().Find(predicate).ToList();
        //}

        ///// <summary>
        ///// 根据条件查询实体，并进行排序。
        ///// </summary>
        ///// <param name="filter">查询条件表达式。</param>
        ///// <param name="orderBy">排序字段表达式。</param>
        ///// <param name="orderByAsc">是否升序排序。</param>
        ///// <returns>满足条件的实体列表。</returns>
        //public List<T> Where(Expression<Func<T, bool>> filter = null, Expression<Func<T, object>> orderBy = null, bool orderByAsc = true)
        //{
        //    var query = _db.GetCollection<T>().Query();
        //    if (filter != null)
        //    {
        //        query = query.Where(filter);
        //    }

        //    if (orderBy != null)
        //    {
        //        query = orderByAsc ? query.OrderBy(orderBy) : query.OrderByDescending(orderBy);
        //    }

        //    return query.ToList();
        //}

        ///// <summary>
        ///// 获取单个满足条件的实体。
        ///// </summary>
        ///// <param name="predicate">查询条件表达式。</param>
        ///// <returns>满足条件的单个实体。</returns>
        //public T Single(Expression<Func<T, bool>> predicate)
        //{
        //    return _db.GetCollection<T>().FindOne(predicate);
        //}

        ///// <summary>
        ///// 获取单个满足条件的实体，并进行排序。
        ///// </summary>
        ///// <param name="filter">查询条件表达式。</param>
        ///// <param name="orderBy">排序字段表达式。</param>
        ///// <param name="orderByAsc">是否升序排序。</param>
        ///// <returns>满足条件的单个实体。</returns>
        //public T Single(Expression<Func<T, bool>> filter = null, Expression<Func<T, object>> orderBy = null, bool orderByAsc = true)
        //{
        //    var query = _db.GetCollection<T>().Query();
        //    if (filter != null)
        //    {
        //        query = query.Where(filter);
        //    }

        //    if (orderBy != null)
        //    {
        //        query = orderByAsc ? query.OrderBy(orderBy) : query.OrderByDescending(orderBy);
        //    }

        //    return query.FirstOrDefault();
        //}

        ///// <summary>
        ///// 判断是否存在满足条件的实体。
        ///// </summary>
        ///// <param name="predicate">查询条件表达式。</param>
        ///// <returns>是否存在满足条件的实体。</returns>
        //public bool Any(Expression<Func<T, bool>> predicate)
        //{
        //    return _db.GetCollection<T>().Exists(predicate);
        //}

        ///// <summary>
        ///// 获取满足条件的实体数量。
        ///// </summary>
        ///// <param name="predicate">查询条件表达式。</param>
        ///// <returns>满足条件的实体数量。</returns>
        //public long Count(Expression<Func<T, bool>> predicate)
        //{
        //    return _db.GetCollection<T>().Count(predicate);
        //}

        /// <summary>
        /// 对数据库进行压缩。
        /// </summary>
        public void Compact()
        {
            _db.Rebuild();
        }

        public void Save(TaskInfo task)
        {
            var col = _db.GetCollection<TaskInfo>();
            var model = Get(task.Id);
            if (model == null)
            {
                col.Insert(task);
            }
            else
            {
                col.Update(task);
            }
        }

        public void Delete(string id)
        {
            _db.GetCollection<TaskInfo>().Delete(id);
        }

        public TaskInfo Get(string id)
        {
            return _db.GetCollection<TaskInfo>().FindOne(c => c.Id == id);
        }

        public List<TaskInfo> List()
        {
            return _db.GetCollection<TaskInfo>().Query().ToList();
        }
    }
}
