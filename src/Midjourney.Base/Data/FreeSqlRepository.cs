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

using System.Linq.Expressions;

namespace Midjourney.Base.Data
{
    public class FreeSqlRepository<T> : IDataHelper<T> where T : class, IBaseId
    {
        private readonly IFreeSql _freeSql;

        public FreeSqlRepository()
        {
            _freeSql = FreeSqlHelper.FreeSql;
        }

        public void Init()
        {

        }

        public void Add(T entity)
        {
            _freeSql.Insert(entity).ExecuteAffrows();
        }

        public void AddRange(IEnumerable<T> entities)
        {
            _freeSql.Insert(entities).ExecuteAffrows();
        }

        public void Delete(T entity)
        {
            _freeSql.Delete<T>(entity.Id).ExecuteAffrows();
        }

        public int Delete(Expression<Func<T, bool>> predicate)
        {
            return _freeSql.Delete<T>().Where(predicate).ExecuteAffrows();
        }

        public void Update(T entity)
        {
            _freeSql.Update<T>().SetSource(entity).ExecuteAffrows();
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
            var model = _freeSql.Select<T>().Where(c => c.Id == item.Id).First();
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
            _freeSql.Update(model);

            return true;
        }

        public List<T> GetAll()
        {
            return _freeSql.Select<T>().ToList();
        }

        /// <summary>
        /// 获取所有实体的 ID 列表。
        /// </summary>
        /// <returns></returns>
        public List<string> GetAllIds()
        {
            return _freeSql.Queryable<T>().Select(x => x.Id).ToList();
        }

        public List<T> Where(Expression<Func<T, bool>> predicate)
        {
            return _freeSql.Select<T>().Where(predicate).ToList();
        }

        public List<T> Where(Expression<Func<T, bool>> filter, Expression<Func<T, object>> orderBy, bool orderByAsc = true)
        {
            var query = _freeSql.Select<T>().Where(filter);
            if (orderByAsc)
            {
                query = query.OrderBy(orderBy);
            }
            else
            {
                query = query.OrderByDescending(orderBy);
            }
            return query.ToList();
        }

        public T Single(Expression<Func<T, bool>> predicate)
        {
            return _freeSql.Select<T>().Where(predicate).First();
        }

        public T Single(Expression<Func<T, bool>> filter, Expression<Func<T, object>> orderBy, bool orderByAsc = true)
        {
            var query = _freeSql.Select<T>().Where(filter);
            if (orderByAsc)
            {
                query = query.OrderBy(orderBy);
            }
            else
            {
                query = query.OrderByDescending(orderBy);
            }
            return query.First();
        }

        public bool Any(Expression<Func<T, bool>> predicate)
        {
            return _freeSql.Select<T>().Where(predicate).Any();
        }

        public long Count(Expression<Func<T, bool>> predicate)
        {
            return _freeSql.Select<T>().Where(predicate).Count();
        }

        public long Count()
        {
            return _freeSql.Select<T>().Count();
        }

        public void Save(T entity)
        {
            if (entity != null && !string.IsNullOrEmpty(entity.Id))
            {
                _freeSql.InsertOrUpdate<T>().SetSource(entity).ExecuteAffrows();
            }
        }

        public void Delete(string id)
        {
            _freeSql.Delete<T>().Where(c => c.Id == id).ExecuteAffrows();
        }

        public T Get(string id)
        {
            return _freeSql.Select<T>().Where(c => c.Id == id).First();
        }

        public List<T> List()
        {
            return _freeSql.Select<T>().ToList();
        }

        public List<T> Where(Expression<Func<T, bool>> filter, Expression<Func<T, object>> orderBy, bool orderByAsc, int limit)
        {
            var query = _freeSql.Select<T>().Where(filter);
            if (orderByAsc)
            {
                query = query.OrderBy(orderBy).Limit(limit);
            }
            else
            {
                query = query.OrderByDescending(orderBy).Limit(limit);
            }
            return query.ToList();
        }
    }
}