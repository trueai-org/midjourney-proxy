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
