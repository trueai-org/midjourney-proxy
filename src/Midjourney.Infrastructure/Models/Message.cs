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
namespace Midjourney.Infrastructure.Models
{
    /// <summary>
    /// 通用消息类，用于封装返回结果。
    /// </summary>
    public class Message
    {
        /// <summary>
        /// 状态码。
        /// </summary>
        public int Code { get; }

        /// <summary>
        /// 描述信息。
        /// </summary>
        public string Description { get; }

        protected Message(int code, string description)
        {
            Code = code;
            Description = description;
        }

        /// <summary>
        /// 返回成功的消息。
        /// </summary>
        public static Message Success() => new Message(ReturnCode.SUCCESS, "成功");


        /// <summary>
        /// 返回成功的消息。
        /// </summary>
        public static Message Success(string message) => new Message(ReturnCode.SUCCESS, message);

        /// <summary>
        /// 返回未找到的消息。
        /// </summary>
        public static Message NotFound() => new Message(ReturnCode.NOT_FOUND, "数据未找到");

        /// <summary>
        /// 返回校验错误的消息。
        /// </summary>
        public static Message ValidationError() => new Message(ReturnCode.VALIDATION_ERROR, "校验错误");

        /// <summary>
        /// 返回系统异常的消息。
        /// </summary>
        public static Message Failure() => new Message(ReturnCode.FAILURE, "系统异常");

        /// <summary>
        /// 返回带自定义描述的系统异常消息。
        /// </summary>
        public static Message Failure(string description) => new Message(ReturnCode.FAILURE, description);

        /// <summary>
        /// 返回自定义状态码和描述的消息。
        /// </summary>
        public static Message Of(int code, string description) => new Message(code, description);
    }

    /// <summary>
    /// 通用消息类，用于封装返回结果。
    /// </summary>
    /// <typeparam name="T">消息类型。</typeparam>
    public class Message<T> : Message
    {
        /// <summary>
        /// 返回结果。
        /// </summary>
        public T Result { get; }

        protected Message(int code, string description, T result = default)
            : base(code, description)
        {
            Result = result;
        }

        /// <summary>
        /// 返回成功的消息。
        /// </summary>
        /// <param name="result">结果。</param>
        public static Message<T> Success(T result) => new Message<T>(ReturnCode.SUCCESS, "成功", result);

        /// <summary>
        /// 返回带自定义状态码和描述的成功消息。
        /// </summary>
        public static Message<T> Success(int code, string description, T result) => new Message<T>(code, description, result);

        /// <summary>
        /// 返回自定义状态码、描述和结果的消息。
        /// </summary>
        public static Message<T> Of(int code, string description, T result) => new Message<T>(code, description, result);
    }
}