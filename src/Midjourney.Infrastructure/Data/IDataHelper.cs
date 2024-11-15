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

namespace Midjourney.Infrastructure.Data
{
    /// <summary>
    /// 定义数据操作的通用接口。
    /// </summary>
    /// <typeparam name="T">数据实体类型，必须实现 <see cref="IBaseId"/> 接口。</typeparam>
    public interface IDataHelper<T> where T : IBaseId
    {
        /// <summary>
        /// 添加一个实体到数据库。
        /// </summary>
        /// <param name="entity">要添加的实体。</param>
        void Add(T entity);

        /// <summary>
        /// 批量添加多个实体到数据库。
        /// </summary>
        /// <param name="entities">要添加的实体集合。</param>
        void AddRange(IEnumerable<T> entities);

        /// <summary>
        /// 根据 ID 删除实体。
        /// </summary>
        /// <param name="id">实体的 ID。</param>
        void Delete(string id);

        /// <summary>
        /// 删除指定的实体。
        /// </summary>
        /// <param name="entity">要删除的实体。</param>
        void Delete(T entity);

        /// <summary>
        /// 根据条件删除实体。
        /// </summary>
        /// <param name="predicate">删除条件表达式。</param>
        /// <returns>删除的实体数量。</returns>
        int Delete(Expression<Func<T, bool>> predicate);

        /// <summary>
        /// 更新指定的实体。
        /// </summary>
        /// <param name="entity">要更新的实体。</param>
        void Update(T entity);

        /// <summary>
        /// 部分更新
        /// </summary>
        /// <param name="fields">BotToken,IsBlend,Properties</param>
        /// <param name="item"></param>
        /// <returns></returns>
        bool Update(string fields, T item);
        
        /// <summary>
        /// 获取所有实体。
        /// </summary>
        /// <returns>实体列表。</returns>
        List<T> GetAll();

        /// <summary>
        /// 获取所有实体的 ID 列表。
        /// </summary>
        /// <returns></returns>
        List<string> GetAllIds();

        /// <summary>
        /// 根据 ID 获取实体。
        /// </summary>
        /// <param name="id">实体的 ID。</param>
        /// <returns>对应的实体对象。</returns>
        T Get(string id);

        /// <summary>
        /// 根据条件查询实体。
        /// </summary>
        /// <param name="predicate">查询条件表达式。</param>
        /// <returns>满足条件的实体列表。</returns>
        List<T> Where(Expression<Func<T, bool>> predicate);

        /// <summary>
        /// 根据条件查询实体，并进行排序。
        /// </summary>
        /// <param name="filter">查询条件表达式。</param>
        /// <param name="orderBy">排序字段表达式。</param>
        /// <param name="orderByAsc">是否升序排序。</param>
        /// <returns>满足条件的实体列表。</returns>
        List<T> Where(Expression<Func<T, bool>> filter, Expression<Func<T, object>> orderBy, bool orderByAsc = true);

        /// <summary>
        /// 获取满足条件的单个实体。
        /// </summary>
        /// <param name="predicate">查询条件表达式。</param>
        /// <returns>满足条件的单个实体。</returns>
        T Single(Expression<Func<T, bool>> predicate);

        /// <summary>
        /// 获取满足条件的单个实体，并进行排序。
        /// </summary>
        /// <param name="filter">查询条件表达式。</param>
        /// <param name="orderBy">排序字段表达式。</param>
        /// <param name="orderByAsc">是否升序排序。</param>
        /// <returns>满足条件的单个实体。</returns>
        T Single(Expression<Func<T, bool>> filter, Expression<Func<T, object>> orderBy, bool orderByAsc = true);

        /// <summary>
        /// 判断是否存在满足条件的实体。
        /// </summary>
        /// <param name="predicate">查询条件表达式。</param>
        /// <returns>是否存在满足条件的实体。</returns>
        bool Any(Expression<Func<T, bool>> predicate);

        /// <summary>
        /// 获取满足条件的实体数量。
        /// </summary>
        /// <param name="predicate">查询条件表达式。</param>
        /// <returns>满足条件的实体数量。</returns>
        long Count(Expression<Func<T, bool>> predicate);

        /// <summary>
        /// 保存（新增或更新）实体。
        /// </summary>
        /// <param name="entity">要保存的实体。</param>
        void Save(T entity);

        /// <summary>
        /// 获取所有实体的列表。
        /// </summary>
        /// <returns>实体列表。</returns>
        List<T> List();

        /// <summary>
        /// 根据条件查询实体，并进行排序和限制返回数量。
        /// </summary>
        /// <param name="filter">查询条件表达式。</param>
        /// <param name="orderBy">排序字段表达式。</param>
        /// <param name="orderByAsc">是否升序排序。</param>
        /// <param name="limit">返回的最大记录数。</param>
        /// <returns>满足条件的实体列表。</returns>
        List<T> Where(Expression<Func<T, bool>> filter, Expression<Func<T, object>> orderBy, bool orderByAsc, int limit);
    }
}
