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

namespace Midjourney.Infrastructure
{
    /// <summary>
    /// 返回码
    /// </summary>
    public static class ReturnCode
    {
        /// <summary>
        /// 成功
        /// </summary>
        public const int SUCCESS = 1;

        /// <summary>
        /// 数据未找到
        /// </summary>
        public const int NOT_FOUND = 3;

        /// <summary>
        /// 校验错误
        /// </summary>
        public const int VALIDATION_ERROR = 4;

        /// <summary>
        /// 系统异常
        /// </summary>
        public const int FAILURE = 9;

        /// <summary>
        /// 已存在
        /// </summary>
        public const int EXISTED = 21;

        /// <summary>
        /// 排队中
        /// </summary>
        public const int IN_QUEUE = 22;

        /// <summary>
        /// 队列已满
        /// </summary>
        public const int QUEUE_REJECTED = 23;

        /// <summary>
        /// 提示词 Prompt 包含敏感词
        /// </summary>
        public const int BANNED_PROMPT = 24;
    }
}