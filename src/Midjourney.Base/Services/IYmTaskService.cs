using System.Collections.Generic;
using System.Threading.Tasks;
using Midjourney.Base.Dto;

namespace Midjourney.Base.Services
{
    /// <summary>
    ///
    /// </summary>
    public interface IYmTaskService
    {
        /// <summary>
        /// 悠船令牌
        /// </summary>
        string YouChuanToken { get; }

        /// <summary>
        /// 官方令牌
        /// </summary>
        string OfficialToken { get; }

        /// <summary>
        /// 悠船登录
        /// </summary>
        /// <returns></returns>
        Task YouChuanLogin();

        /// <summary>
        /// 提交任务
        /// </summary>
        /// <param name="task"></param>
        /// <param name="taskStoreService"></param>
        /// <param name="instance"></param>
        /// <returns></returns>
        Task<Message> SubmitTaskAsync(TaskInfo task, ITaskStoreService taskStoreService, IDiscordInstance instance);

        /// <summary>
        /// 变化任务
        /// </summary>
        /// <returns></returns>
        Task<Message> SubmitActionAsync(TaskInfo task,
             SubmitActionDTO submitAction,
             TaskInfo targetTask,
             ITaskStoreService taskStoreService,
             IDiscordInstance discordInstance);

        /// <summary>
        /// 获取并更新任务状态
        /// </summary>
        /// <param name="info"></param>
        /// <returns></returns>
        Task UpdateStatus(TaskInfo info, ITaskStoreService taskStoreService, DiscordAccount account);

        /// <summary>
        /// 悠船每 n 分钟同步一次账号信息
        /// </summary>
        /// <returns></returns>
        Task YouChuanSyncInfo(bool isClearCache = false);

        /// <summary>
        /// 官方每 n 分钟同步一次账号信息
        /// </summary>
        /// <returns></returns>
        Task OfficialSyncInfo(bool isClearCache = false);

        /// <summary>
        /// 获取种子
        /// </summary>
        /// <param name="task"></param>
        /// <returns></returns>
        Task<string> GetSeedAsync(TaskInfo task);

        /// <summary>
        /// 获取图片文本数据
        /// </summary>
        /// <param name="imageUrl"></param>
        /// <param name="channelId"></param>
        /// <param name="isPrivate"></param>
        /// <param name="mode"></param>
        /// <returns></returns>
        Task DescribeAsync(TaskInfo task);

        /// <summary>
        /// 提交模态框任务
        /// </summary>
        /// <param name="task"></param>
        /// <param name="parentTask"></param>
        /// <returns></returns>
        Task<Message> SubmitModalAsync(TaskInfo task, TaskInfo parentTask, SubmitModalDTO submitAction, ITaskStoreService taskStoreService);

        /// <summary>
        /// 上传文件到悠船服务
        /// </summary>
        /// <param name="fileContent">文件内容</param>
        /// <param name="fileName">文件名</param>
        /// <param name="type">文件类型，默认为0</param>
        /// <returns>上传后的文件URL</returns>
        Task<string> UploadFileAsync(TaskInfo task, byte[] fileContent, string fileName, int type = 0);
    }
}