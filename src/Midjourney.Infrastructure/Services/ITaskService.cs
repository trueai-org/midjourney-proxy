// Midjourney Proxy - Proxy for Midjourney's Discord, enabling AI drawings via API with one-click face swap. A free, non-profit drawing API project.
// Copyright (C) 2024 trueai.org

// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.

// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

// Additional Terms:
// This software shall not be used for any illegal activities. 
// Users must comply with all applicable laws and regulations,
// particularly those related to image and video processing. 
// The use of this software for any form of illegal face swapping,
// invasion of privacy, or any other unlawful purposes is strictly prohibited. 
// Violation of these terms may result in termination of the license and may subject the violator to legal action.

using Midjourney.Infrastructure.Dto;
using Midjourney.Infrastructure.Util;

namespace Midjourney.Infrastructure.Services
{
    /// <summary>
    /// 任务服务接口，定义了与任务相关的操作方法。
    /// </summary>
    public interface ITaskService
    {
        /// <summary>
        /// 获取领域缓存
        /// </summary>
        /// <returns></returns>
        public Dictionary<string, HashSet<string>> GetDomainCache();

        /// <summary>
        /// 清除领域缓存
        /// </summary>
        void ClearDomainCache();

        /// <summary>
        /// 违规词缓存
        /// </summary>
        /// <returns></returns>
        Dictionary<string, HashSet<string>> GetBannedWordsCache();

        /// <summary>
        /// 清除违规词缓存
        /// </summary>
        void ClearBannedWordsCache();

        /// <summary>
        /// 验证违规词
        /// </summary>
        /// <param name="promptEn"></param>
        /// <exception cref="BannedPromptException"></exception>
        void CheckBanned(string promptEn);

        /// <summary>
        /// 提交 Imagine 任务。
        /// </summary>
        /// <param name="task">任务对象。</param>
        /// <param name="dataUrls">图片数据列表。</param>
        /// <returns>提交结果。</returns>
        SubmitResultVO SubmitImagine(TaskInfo task, List<DataUrl> dataUrls);

        /// <summary>
        /// 提交 show 任务
        /// </summary>
        /// <param name="info"></param>
        /// <returns></returns>
        SubmitResultVO ShowImagine(TaskInfo info);

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
        /// 上传一个较长的提示词，mj 可以返回一组简要的提示词
        /// </summary>
        /// <param name="task"></param>
        /// <returns></returns>
        SubmitResultVO ShortenAsync(TaskInfo task);

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
        /// <param name="botType"></param>
        /// <returns></returns>
        Task AccountAction(string id, string customId, EBotType botType);

        /// <summary>
        /// MJ Plus 数据迁移
        /// </summary>
        /// <param name="dto"></param>
        /// <returns></returns>
        Task MjPlusMigration(MjPlusMigrationDto dto);
    }
}