namespace Midjourney.Infrastructure.StandardTable
{
    public class StandardTablePagination : StandardTablePaginationRequest
    {
        /// <summary>
        /// 总条数
        /// </summary>
        public int Total { get; set; }

        /// <summary>
        /// 总页数
        /// </summary>
        public int PageCount => PageSize <= 0 ? 0 : (int)Math.Ceiling((decimal)Total / PageSize);
    }
}