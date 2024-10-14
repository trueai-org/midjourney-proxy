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
namespace Midjourney.Infrastructure.Data
{
    /// <summary>
    /// 常量类.
    /// </summary>
    public static class Constants
    {
        /// <summary>
        /// 默认配置 ID
        /// </summary>
        public const string DEFAULT_SETTING_ID = "default";

        /// <summary>
        /// 超管用户 ID
        /// </summary>
        public const string ADMIN_USER_ID = "admin";

        /// <summary>
        /// 默认普通绘图用户 ID
        /// </summary>
        public const string DEFAULT_USER_ID = "user";

        /// <summary>
        /// 简单的
        /// </summary>
        public const string DEFAULT_DOMAIN_ID = "simple";

        /// <summary>
        /// 完整的
        /// </summary>
        public const string DEFAULT_DOMAIN_FULL_ID = "full";

        /// <summary>
        /// 默认禁用词 ID
        /// </summary>
        public const string DEFAULT_BANNED_WORD_ID = "default";

        // -----------------

        /// <summary>
        /// niji journey Bot 应用 ID
        /// </summary>
        public const string NIJI_APPLICATION_ID = "1022952195194359889";

        /// <summary>
        /// Midjourney Bot 应用 ID
        /// </summary>
        public const string MJ_APPLICATION_ID = "936929561302675456";

        // 任务扩展属性 start

        /// <summary>
        /// 通知回调地址.
        /// </summary>
        public const string TASK_PROPERTY_NOTIFY_HOOK = "notifyHook";

        /// <summary>
        /// bot类型，mj(默认)或niji
        /// MID_JOURNEY | NIJI_JOURNEY
        /// </summary>
        public const string TASK_PROPERTY_BOT_TYPE = "botType";

        /// <summary>
        /// 最终提示.
        /// </summary>
        public const string TASK_PROPERTY_FINAL_PROMPT = "finalPrompt";

        /// <summary>
        /// 是否为重制 remix 模式
        /// </summary>
        public const string TASK_PROPERTY_REMIX = "remix";

        /// <summary>
        /// 原始消息内容
        /// </summary>
        public const string TASK_PROPERTY_MESSAGE_CONTENT = "messageContent";

        /// <summary>
        /// 消息ID.
        /// </summary>
        public const string TASK_PROPERTY_MESSAGE_ID = "messageId";

        /// <summary>
        /// 消息哈希.
        /// </summary>
        public const string TASK_PROPERTY_MESSAGE_HASH = "messageHash";

        /// <summary>
        /// 进度消息ID.
        /// </summary>
        public const string TASK_PROPERTY_PROGRESS_MESSAGE_ID = "progressMessageId";

        /// <summary>
        /// 执行动作 custom_id
        /// </summary>
        public const string TASK_PROPERTY_CUSTOM_ID = "custom_id";

        /// <summary>
        /// 标志.
        /// </summary>
        public const string TASK_PROPERTY_FLAGS = "flags";

        /// <summary>
        /// 随机数.
        /// </summary>
        public const string TASK_PROPERTY_NONCE = "nonce";

        /// <summary>
        /// Discord实例ID = Discord频道ID
        /// </summary>
        public const string TASK_PROPERTY_DISCORD_INSTANCE_ID = "discordInstanceId";

        /// <summary>
        /// Discord频道ID = Discord实例ID
        /// </summary>
        public const string TASK_PROPERTY_DISCORD_CHANNEL_ID = "discordChannelId";

        /// <summary>
        /// 引用消息ID.
        /// </summary>
        public const string TASK_PROPERTY_REFERENCED_MESSAGE_ID = "referencedMessageId";

        /// <summary>
        /// 局部重绘弹窗 custom_id
        /// </summary>
        public const string TASK_PROPERTY_IFRAME_MODAL_CREATE_CUSTOM_ID = "iframe_modal_custom_id";


        /// <summary>
        /// remix 模式下的自定义ID
        /// </summary>
        public const string TASK_PROPERTY_REMIX_CUSTOM_ID = "remix_custom_id";

        /// <summary>
        /// remix 模式下的弹窗
        /// </summary>

        public const string TASK_PROPERTY_REMIX_MODAL = "remix_modal";

        /// <summary>
        /// remix 模式下 U 图 custom_id
        /// </summary>
        public const string TASK_PROPERTY_REMIX_U_CUSTOM_ID = "remix_u_custom_id";

        // 任务扩展属性 end

        /// <summary>
        /// API密钥请求头名称.
        /// </summary>
        public const string API_SECRET_HEADER_NAME = "mj-api-secret";

        /// <summary>
        /// 默认的Discord用户代理.
        /// </summary>
        public const string DEFAULT_DISCORD_USER_AGENT = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/112.0.0.0 Safari/537.36";

        /// <summary>
        /// MJ消息已处理标志.
        /// </summary>
        public const string MJ_MESSAGE_HANDLED = "mj_proxy_handled";
    }
}