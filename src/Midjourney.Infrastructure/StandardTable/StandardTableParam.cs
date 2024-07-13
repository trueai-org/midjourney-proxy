namespace Midjourney.Infrastructure.StandardTable
{
    public class StandardTableParam : StandardTableParam<StandardSearch>
    {
        public override StandardSearch Search { get; set; } = new StandardSearch();
    }

    public class StandardTableParam<T> where T : class, new()
    {
        public virtual T Search { get; set; } = new T();

        public StandardTablePaginationRequest Pagination { get; set; } = new StandardTablePaginationRequest();

        public StandardSort Sort { get; set; } = new StandardSort();

        public StandardTableResult<T2> ToEmptyResult<T2>(int total = 0)
        {
            return StandardTableResult<T2>.EmptyResult(Pagination?.Current ?? 1, Pagination?.PageSize ?? 10, total);
        }
    }
}
