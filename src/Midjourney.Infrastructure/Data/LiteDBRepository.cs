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
using Midjourney.Infrastructure.Services;
using System.Linq.Expressions;

namespace Midjourney.Infrastructure.Data
{
    public interface IBaseId
    {
        string Id { get; set; }
    }

    /// <summary>
    /// 任务存储服务接口。
    /// </summary>
    public class TaskRepository : ITaskStoreService
    {
        public void Save(TaskInfo task)
        {
            //DbHelper.TaskStore.Save(task);
            DbHelper.Instance.TaskStore.Save(task);
        }

        public void Delete(string id)
        {
            //DbHelper.TaskStore.Delete(id);
            DbHelper.Instance.TaskStore.Delete(id);
        }

        public TaskInfo Get(string id)
        {
            //return DbHelper.TaskStore.Get(id);
            return DbHelper.Instance.TaskStore.Get(id);
        }

        public List<TaskInfo> GetList(List<string> ids)
        {
            if (ids == null || ids.Count <= 0)
            {
                return new List<TaskInfo>();
            }

            return DbHelper.Instance.TaskStore.Where(c => ids.Contains(c.Id));
        }
    }

    /// <summary>
    /// LiteDB 数据库的泛型仓库类。
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class LiteDBRepository<T> : IDataHelper<T> where T : IBaseId
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

        public LiteDatabase LiteDatabase => _db;

        /// <summary>
        /// 初始化仓库，创建必要的索引。
        /// </summary>
        public void Init()
        {
        }

        /// <summary>
        /// 添加一个实体到仓库。
        /// </summary>
        /// <param name="entity">要添加的实体。</param>
        public void Add(T entity)
        {
            var col = _db.GetCollection<T>();
            var id = col.Insert(entity);

            if (!string.IsNullOrWhiteSpace(id))
            {
                entity.Id = id;
            }
        }

        /// <summary>
        /// 批量添加实体到仓库，优化处理超过 10000 条记录的情况。
        /// </summary>
        /// <param name="entities">要添加的实体集合。</param>
        public void AddRange(IEnumerable<T> entities)
        {
            const int batchSize = 10000;
            var col = _db.GetCollection<T>();

            var entityList = entities.ToList();
            for (int i = 0; i < entityList.Count; i += batchSize)
            {
                var batch = entityList.Skip(i).Take(batchSize);
                col.InsertBulk(batch);
            }
        }

        /// <summary>
        /// 根据实体ID删除一个实体。
        /// </summary>
        /// <param name="id">实体的ID。</param>
        public void Delete(int id)
        {
            var col = _db.GetCollection<T>();
            col.Delete(new BsonValue(id));
        }

        /// <summary>
        /// 删除指定的实体。
        /// </summary>
        /// <param name="obj">要删除的实体对象。</param>
        public void Delete(T obj)
        {
            var col = _db.GetCollection<T>();
            col.Delete(new BsonValue(obj.Id));
        }

        /// <summary>
        /// 根据条件删除实体。
        /// </summary>
        /// <param name="predicate">删除条件表达式。</param>
        /// <returns>删除的实体数量。</returns>
        public int Delete(Expression<Func<T, bool>> predicate)
        {
            return _db.GetCollection<T>().DeleteMany(predicate);
        }

        /// <summary>
        /// 更新指定的实体。
        /// </summary>
        /// <param name="entity">要更新的实体对象。</param>
        public void Update(T entity)
        {
            var col = _db.GetCollection<T>();
            col.Update(entity);
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
            var col = _db.GetCollection<T>();
            var model = col.FindById(item.Id);
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
            return col.Update(model);
        }

        /// <summary>
        /// 获取所有实体。
        /// </summary>
        /// <returns>实体列表。</returns>
        public List<T> GetAll()
        {
            return _db.GetCollection<T>().FindAll().ToList();
        }

        /// <summary>
        /// 获取所有实体的 ID 列表。
        /// </summary>
        /// <returns></returns>
        public List<string> GetAllIds()
        {
            return _db.GetCollection<T>().Query().Select(x => x.Id).ToList();
        }

        /// <summary>
        /// 根据实体ID获取实体。
        /// </summary>
        /// <param name="id">实体的ID。</param>
        /// <returns>对应的实体对象。</returns>
        public T Get(int id)
        {
            return _db.GetCollection<T>().FindById(new BsonValue(id));
        }

        /// <summary>
        /// 根据条件查询实体。
        /// </summary>
        /// <param name="predicate">查询条件表达式。</param>
        /// <returns>满足条件的实体列表。</returns>
        public List<T> Where(Expression<Func<T, bool>> predicate)
        {
            return _db.GetCollection<T>().Find(predicate).ToList();
        }

        /// <summary>
        /// 根据条件查询实体，并进行排序。
        /// </summary>
        /// <param name="filter">查询条件表达式。</param>
        /// <param name="orderBy">排序字段表达式。</param>
        /// <param name="orderByAsc">是否升序排序。</param>
        /// <returns>满足条件的实体列表。</returns>
        public List<T> Where(Expression<Func<T, bool>> filter = null, Expression<Func<T, object>> orderBy = null, bool orderByAsc = true)
        {
            var query = _db.GetCollection<T>().Query();
            if (filter != null)
            {
                query = query.Where(filter);
            }

            if (orderBy != null)
            {
                query = orderByAsc ? query.OrderBy(orderBy) : query.OrderByDescending(orderBy);
            }

            return query.ToList();
        }

        public ILiteCollection<T> GetCollection()
        {
            return _db.GetCollection<T>();
        }

        /// <summary>
        /// 获取单个满足条件的实体。
        /// </summary>
        /// <param name="predicate">查询条件表达式。</param>
        /// <returns>满足条件的单个实体。</returns>
        public T Single(Expression<Func<T, bool>> predicate)
        {
            return _db.GetCollection<T>().FindOne(predicate);
        }

        /// <summary>
        /// 获取单个满足条件的实体，并进行排序。
        /// </summary>
        /// <param name="filter">查询条件表达式。</param>
        /// <param name="orderBy">排序字段表达式。</param>
        /// <param name="orderByAsc">是否升序排序。</param>
        /// <returns>满足条件的单个实体。</returns>
        public T Single(Expression<Func<T, bool>> filter = null, Expression<Func<T, object>> orderBy = null, bool orderByAsc = true)
        {
            var query = _db.GetCollection<T>().Query();
            if (filter != null)
            {
                query = query.Where(filter);
            }

            if (orderBy != null)
            {
                query = orderByAsc ? query.OrderBy(orderBy) : query.OrderByDescending(orderBy);
            }

            return query.FirstOrDefault();
        }

        /// <summary>
        /// 判断是否存在满足条件的实体。
        /// </summary>
        /// <param name="predicate">查询条件表达式。</param>
        /// <returns>是否存在满足条件的实体。</returns>
        public bool Any(Expression<Func<T, bool>> predicate)
        {
            return _db.GetCollection<T>().Exists(predicate);
        }

        /// <summary>
        /// 获取满足条件的实体数量。
        /// </summary>
        /// <param name="predicate">查询条件表达式。</param>
        /// <returns>满足条件的实体数量。</returns>
        public long Count(Expression<Func<T, bool>> predicate)
        {
            return _db.GetCollection<T>().Count(predicate);
        }

        /// <summary>
        /// 对数据库进行压缩。
        /// </summary>
        public void Compact()
        {
            _db.Rebuild();
        }

        /// <summary>
        /// 保存（新增或更新）
        /// </summary>
        /// <param name="task"></param>
        public void Save(T task)
        {
            var col = _db.GetCollection<T>();
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

        /// <summary>
        /// 删除
        /// </summary>
        /// <param name="id"></param>
        public void Delete(string id)
        {
            _db.GetCollection<T>().Delete(id);
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public T Get(string id)
        {
            return _db.GetCollection<T>().FindById(id);
        }

        /// <summary>
        ///
        /// </summary>
        /// <returns></returns>
        public List<T> List()
        {
            return _db.GetCollection<T>().Query().ToList();
        }

        public List<T> Where(Expression<Func<T, bool>> filter, Expression<Func<T, object>> orderBy, bool orderByAsc, int limit)
        {
            var query = _db.GetCollection<T>().Query();
            if (orderByAsc)
            {
                return query.OrderBy(orderBy).Where(filter).Limit(limit).ToList();
            }
            else
            {
                return query.OrderByDescending(orderBy).Where(filter).Limit(limit).ToList();
            }
        }
    }
}