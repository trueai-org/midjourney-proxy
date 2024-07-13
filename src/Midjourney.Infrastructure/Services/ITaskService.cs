using Midjourney.Infrastructure.Dto;

namespace Midjourney.Infrastructure.Services
{
    /// <summary>
    /// 任务服务接口，定义了与任务相关的操作方法。
    /// </summary>
    public interface ITaskService
    {
        /// <summary>
        /// 提交 Imagine 任务。
        /// </summary>
        /// <param name="task">任务对象。</param>
        /// <param name="dataUrls">图片数据列表。</param>
        /// <returns>提交结果。</returns>
        SubmitResultVO SubmitImagine(TaskInfo task, List<DataUrl> dataUrls);

        /// <summary>
        /// 提交放大任务。
        /// </summary>
        /// <param name="task">任务对象。</param>
        /// <param name="targetMessageId">目标消息ID。</param>
        /// <param name="targetMessageHash">目标消息哈希。</param>
        /// <param name="index">索引。</param>
        /// <param name="messageFlags">消息标志。</param>
        /// <returns>提交结果。</returns>
        SubmitResultVO SubmitUpscale(TaskInfo task, string targetMessageId, string targetMessageHash, int index, int messageFlags);

        /// <summary>
        /// 提交变换任务。
        /// </summary>
        /// <param name="task">任务对象。</param>
        /// <param name="targetMessageId">目标消息ID。</param>
        /// <param name="targetMessageHash">目标消息哈希。</param>
        /// <param name="index">索引。</param>
        /// <param name="messageFlags">消息标志。</param>
        /// <returns>提交结果。</returns>
        SubmitResultVO SubmitVariation(TaskInfo task, string targetMessageId, string targetMessageHash, int index, int messageFlags);

        /// <summary>
        /// 提交重新生成任务。
        /// </summary>
        /// <param name="task">任务对象。</param>
        /// <param name="targetMessageId">目标消息ID。</param>
        /// <param name="targetMessageHash">目标消息哈希。</param>
        /// <param name="messageFlags">消息标志。</param>
        /// <returns>提交结果。</returns>
        SubmitResultVO SubmitReroll(TaskInfo task, string targetMessageId, string targetMessageHash, int messageFlags);

        /// <summary>
        /// 提交描述任务。
        /// </summary>
        /// <param name="task">任务对象。</param>
        /// <param name="dataUrl">图片数据。</param>
        /// <returns>提交结果。</returns>
        SubmitResultVO SubmitDescribe(TaskInfo task, DataUrl dataUrl);

        /// <summary>
        /// 提交混合任务。
        /// </summary>
        /// <param name="task">任务对象。</param>
        /// <param name="dataUrls">图片数据列表。</param>
        /// <param name="dimensions">混合维度。</param>
        /// <returns>提交结果。</returns>
        SubmitResultVO SubmitBlend(TaskInfo task, List<DataUrl> dataUrls, BlendDimensions dimensions);

        /// <summary>
        /// 执行动作
        /// </summary>
        /// <param name="task"></param>
        /// <param name="submitAction"></param>
        /// <returns></returns>
        SubmitResultVO SubmitAction(TaskInfo task, SubmitActionDTO submitAction);

        /// <summary>
        /// 执行 Modal
        /// </summary>
        /// <param name="task"></param>
        /// <param name="submitAction"></param>
        /// <returns></returns>
        SubmitResultVO SubmitModal(TaskInfo task, SubmitModalDTO submitAction, DataUrl dataUrl = null);

        /// <summary>
        /// 获取图片 seed
        /// </summary>
        /// <param name="task"></param>
        /// <returns></returns>
        Task<SubmitResultVO> SubmitSeed(TaskInfo task);

        /// <summary>
        /// 执行 info setting 操作
        /// </summary>
        /// <returns></returns>
        Task InfoSetting(string id);

        /// <summary>
        /// 修改版本
        /// </summary>
        /// <param name="id"></param>
        /// <param name="version"></param>
        /// <returns></returns>
        Task AccountChangeVersion(string id, string version);

        /// <summary>
        /// 执行操作
        /// </summary>
        /// <param name="id"></param>
        /// <param name="customId"></param>
        /// <returns></returns>
        Task AccountAction(string id, string customId);
    }
}