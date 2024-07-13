namespace Midjourney.Infrastructure.StandardTable
{
    public class StandardTableResult<T>
    {
        public IEnumerable<T> List { get; set; } = Array.Empty<T>();

        public StandardTablePagination Pagination { get; set; } = new StandardTablePagination();

        public static StandardTableResult<T> EmptyResult(int pageIndex = 1, int pageSize = 10, int total = 0)
        {
            return new StandardTableResult<T>()
            {
                Pagination = new StandardTablePagination()
                {
                    Current = pageIndex,
                    PageSize = pageSize,
                    Total = total
                }
            };
        }
    }

    public class StandardTableResult<T, T2> : StandardTableResult<T>
    {
        public T2 ExtendData { get; set; }
    }
}