namespace Midjourney.Infrastructure.StandardTable
{
    public class StandardTablePaginationRequest
    {
        /// <summary>
        /// 页码
        /// </summary>
        public int Current { get; set; } = 1;

        /// <summary>
        /// 页大小
        /// </summary>
        public int PageSize { get; set; } = 10;
    }
}
