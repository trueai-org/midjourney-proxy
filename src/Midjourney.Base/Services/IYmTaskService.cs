using Midjourney.Base.Dto;

namespace Midjourney.Base.Services
{
    /// <summary>
    /// 悠船/官方服务接口
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
        /// <param name="instance"></param>
        /// <returns></returns>
        Task<Message> SubmitTaskAsync(TaskInfo task, IDiscordService instance);

        /// <summary>
        /// 变化任务
        /// </summary>
        /// <returns></returns>
        Task<Message> SubmitActionAsync(TaskInfo task, SubmitActionDTO submitAction, TaskInfo targetTask, IDiscordService discordInstance, string newPrompt = null);

        /// <summary>
        /// 获取并更新任务状态
        /// </summary>
        /// <param name="info"></param>
        /// <returns></returns>
        Task UpdateStatus(TaskInfo info, DiscordAccount account);

        /// <summary>
        /// 同步悠船账号信息
        /// </summary>
        /// <returns></returns>
        Task<bool> SyncYouchuanInfo();

        /// <summary>
        /// 同步官方账号信息
        /// </summary>
        /// <returns></returns>
        Task<bool> SyncOfficialInfo();

        /// <summary>
        /// 获取种子
        /// </summary>
        /// <param name="task"></param>
        /// <returns></returns>
        Task<string> GetSeed(TaskInfo task);

        /// <summary>
        /// 获取图片文本数据
        /// </summary>
        /// <param name="imageUrl"></param>
        /// <param name="channelId"></param>
        /// <param name="isPrivate"></param>
        /// <param name="mode"></param>
        /// <returns></returns>
        Task Describe(TaskInfo task);

        /// <summary>
        /// 提交模态框任务
        /// </summary>
        /// <param name="task"></param>
        /// <param name="parentTask"></param>
        /// <returns></returns>
        Task<Message> SubmitModal(TaskInfo task, TaskInfo parentTask, SubmitModalDTO submitAction);

        /// <summary>
        /// 上传文件到悠船服务
        /// </summary>
        /// <param name="fileContent">文件内容</param>
        /// <param name="fileName">文件名</param>
        /// <param name="type">文件类型，默认为0</param>
        /// <returns>上传后的文件URL</returns>
        Task<string> UploadFile(TaskInfo task, byte[] fileContent, string fileName, int type = 0);

        /// <summary>
        /// 创建个性化配置
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="request"></param>
        /// <returns></returns>
        Task<ProfileCreateResultDto> ProfileCreateAsync(ProfileCreateDto request);

        /// <summary>
        /// 创建个性化配置 - 跳过评分
        /// </summary>
        /// <param name="personalize"></param>
        /// <returns></returns>
        Task<ProfileGetRandomPairsResponse> ProfileCreateSkipAsync(PersonalizeTag personalize, string cursor = "");

        /// <summary>
        /// 创建个性化配置 - 评分
        /// </summary>
        /// <param name="personalize"></param>
        /// <param name="isRight"></param>
        /// <returns></returns>
        Task<ProfileGetRandomPairsResponse> ProfileCreateRateAsync(PersonalizeTag personalize, bool? isRight = null);
    }
}