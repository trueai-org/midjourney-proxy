namespace Midjourney.Base.Models
{
    public class PageResult<T> where T : class
    {
        /// <summary>
        /// 列表
        /// </summary>
        public List<T> List { get; set; } = default!;

        /// <summary>
        /// 总条数
        /// </summary>
        public int Total { get; set; }

        /// <summary>
        /// 页码
        /// </summary>
        public int PageIndex { get; set; } = 1;

        /// <summary>
        /// 页大小
        /// </summary>
        public int PageSize { get; set; } = 10;

        /// <summary>
        /// 总页数
        /// </summary>
        public int PageCount => PageSize <= 0 ? 0 : (int)Math.Ceiling((decimal)Total / PageSize);
    }

    public static class PageResultExtentions
    {
        /// <summary>
        /// 转换为分页结果
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="list"></param>
        /// <param name="total"></param>
        /// <param name="pageIndex"></param>
        /// <param name="pageSize"></param>
        /// <returns></returns>
        public static PageResult<T> ToPageResult<T>(this List<T> list, long total, int pageIndex = 1, int pageSize = 10) where T : class
        {
            return new PageResult<T>
            {
                List = list,
                Total = (int)total,
                PageIndex = pageIndex,
                PageSize = pageSize
            };
        }
    }
}