namespace Midjourney.Base.Dto
{
    /// <summary>
    /// 任务信息查询统计结果
    /// </summary>
    public class TaskInfoQueryStatResult
    {
        /// <summary>
        /// 总数
        /// </summary>
        public int Total { get; set; }

        /// <summary>
        /// 用户操作统计
        /// </summary>
        public Dictionary<TaskAction, int> ActionStats { get; set; }

        /// <summary>
        /// 用户操作计数，排除放大，暂定（后面精确计算）：视频 = 8，非视频 = 1
        /// </summary>
        public int ActionUseCount => ActionStats?.Where(c => c.Key != TaskAction.UPSCALE).Select(c => (c.Key == TaskAction.VIDEO ? 8 : 1) * c.Value).Sum() ?? 0;
    }
}