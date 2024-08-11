namespace Midjourney.Infrastructure.Data
{
    /// <summary>
    /// 任务帮助类
    /// </summary>
    public class TaskHelper : SingletonBase<TaskHelper>
    {
        private readonly IDataHelper<TaskInfo> _dataHelper;

        public TaskHelper()
        {
            if (GlobalConfiguration.Setting.IsMongo)
            {
                _dataHelper = new MongoDBRepository<TaskInfo>();
            }
            else
            {
                _dataHelper = DbHelper.TaskStore;
            }
        }

        /// <summary>
        /// 数据帮助类
        /// </summary>
        public IDataHelper<TaskInfo> TaskStore => _dataHelper;
    }
}
