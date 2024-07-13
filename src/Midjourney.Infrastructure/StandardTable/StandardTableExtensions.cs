using System.Linq.Expressions;

namespace Midjourney.Infrastructure.StandardTable
{
    public static class StandardTableExtensions
    {
        /// <summary>
        /// 转分页
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="list"></param>
        /// <param name="page"></param>
        /// <param name="pageSize"></param>
        /// <param name="total"></param>
        /// <returns></returns>
        public static StandardTableResult<T> ToTableResult<T>(this IEnumerable<T> list, int page = 1, int pageSize = 10, int total = 0) where T : class, new()
        {
            return new StandardTableResult<T>()
            {
                List = list.ToList(),
                Pagination = new StandardTablePagination()
                {
                    Current = page,
                    PageSize = pageSize,
                    Total = total
                }
            };
        }

        /// <summary>
        /// 转分页
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="list"></param>
        /// <param name="page"></param>
        /// <param name="pageSize"></param>
        /// <param name="total"></param>
        /// <returns></returns>
        public static StandardTableResult<T> ToTableResult<T>(this IEnumerable<T> list, StandardTablePagination pagination, int total = 0) where T : class, new()
        {
            pagination ??= new StandardTablePagination() { Total = total };
            if (pagination.Total != total)
            {
                pagination.Total = total;
            }

            return new StandardTableResult<T>()
            {
                List = list.ToList(),
                Pagination = pagination
            };
        }

        /// <summary>
        /// 转分页
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="list"></param>
        /// <param name="page"></param>
        /// <param name="pageSize"></param>
        /// <param name="total"></param>
        /// <returns></returns>
        public static StandardTableResult<T> ToTableResult<T>(this IEnumerable<T> list, StandardTableParam param, int total = 0) where T : class, new()
        {
            return new StandardTableResult<T>()
            {
                List = list.ToList(),
                Pagination = new StandardTablePagination()
                {
                    Total = total
                }
            };
        }

        /// <summary>
        /// 转分页
        /// </summary>
        /// <typeparam name="dynamic"></typeparam>
        /// <param name="list"></param>
        /// <param name="page"></param>
        /// <param name="pageSize"></param>
        /// <param name="total"></param>
        /// <returns></returns>
        public static StandardTableResult<dynamic> ToTableDynamicResult(this IEnumerable<dynamic> list, int page = 1, int pageSize = 10, int total = 0)
        {
            return new StandardTableResult<dynamic>()
            {
                List = list.ToList(),
                Pagination = new StandardTablePagination()
                {
                    Current = page,
                    PageSize = pageSize,
                    Total = total
                }
            };
        }

        /// <summary>
        /// 转分页
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <typeparam name="T1"></typeparam>
        /// <param name="list"></param>
        /// <param name="page"></param>
        /// <param name="pageSize"></param>
        /// <param name="total"></param>
        /// <param name="extendData"></param>
        /// <returns></returns>
        public static StandardTableResult<T, T1> ToTableResult<T, T1>(this IEnumerable<T> list, int page = 1, int pageSize = 10, int total = 0, T1 extendData = default) where T : class, new()
        {
            return new StandardTableResult<T, T1>()
            {
                List = list.ToList(),
                Pagination = new StandardTablePagination()
                {
                    Current = page,
                    PageSize = pageSize,
                    Total = total
                },
                ExtendData = extendData
            };
        }

        /// <summary>
        /// 查询条件扩展
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="query"></param>
        /// <param name="condition"></param>
        /// <param name="predicate"></param>
        /// <returns></returns>
        public static IEnumerable<T> WhereIf<T>(this IEnumerable<T> query, bool condition, Func<T, bool> predicate)
        {
            return condition ? query.Where(predicate) : query;
        }

        /// <summary>
        /// 查询条件扩展
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="query"></param>
        /// <param name="condition"></param>
        /// <param name="predicate"></param>
        /// <returns></returns>
        public static IEnumerable<T> WhereIf<T>(this IEnumerable<T> query, bool condition, Func<T, int, bool> predicate)
        {
            return condition ? query.Where(predicate) : query;
        }

        /// <summary>
        /// 排序条件扩展
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="query"></param>
        /// <param name="condition"></param>
        /// <param name="predicate"></param>
        /// <returns></returns>
        public static IEnumerable<TSource> OrderByIf<TSource, TKey>(this IEnumerable<TSource> source, bool condition, Func<TSource, TKey> predicate)
        {
            return condition ? source.OrderBy(predicate) : source;
        }

        /// <summary>
        /// 排序条件扩展
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="query"></param>
        /// <param name="condition"></param>
        /// <param name="predicate"></param>
        /// <returns></returns>
        public static IEnumerable<TSource> OrderByDescendingIf<TSource, TKey>(this IEnumerable<TSource> source, bool condition, Func<TSource, TKey> predicate)
        {
            return condition ? source.OrderByDescending(predicate) : source;
        }

        /// <summary>
        /// 查询条件扩展
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="query"></param>
        /// <param name="condition"></param>
        /// <param name="predicate"></param>
        /// <returns></returns>
        public static IQueryable<T> WhereIf<T>(this IQueryable<T> query, bool condition, Expression<Func<T, bool>> predicate)
        {
            return condition ? query.Where(predicate) : query;
        }

        /// <summary>
        /// 获取文件大小格式化显示 B/KB/MB
        /// </summary>
        /// <param name="size"></param>
        /// <returns></returns>
        public static string FormatSize(this long size)
        {
            return size < 1024 ? (size + "B") : size / 1024 < 1024 ? (size / 1024 + "KB") : (((int)(size / 1024 / 1024)) + "MB");
        }
    }
}