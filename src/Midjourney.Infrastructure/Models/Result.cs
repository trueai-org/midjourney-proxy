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
    /// 结果
    /// </summary>
    public class Result
    {
        public bool Success { get; set; }

        public int Code { get; set; }

        public string Message { get; set; }

        public string Timestamp { get; set; } = DateTime.Now.Ticks.ToString();

        public Result()
        {

        }

        protected Result(bool success)
        {
            Success = success;
        }

        protected Result(bool success, string message)
        {
            Success = success;
            Message = message;
        }

        protected Result(bool success, string message, int code)
            : this(success, message)
        {
            Code = code;
        }

        public static Result Ok()
        {
            return new Result(true);
        }

        public static Result Ok(string message)
        {
            return new Result(true, message);
        }

        public static Result Fail(string error)
        {
            return new Result(false, error, -1);
        }

        public static Result<TValue> Ok<TValue>(int code, TValue value) where TValue : class
        {
            return new Result<TValue>(value, code, true, null);
        }

        public static Result<TValue> Ok<TValue>(TValue value) where TValue : class
        {
            return new Result<TValue>(value, true, null);
        }

        public static Result<TValue> Ok<TValue>(TValue value, string message)
        {
            return new Result<TValue>(value, true, message);
        }

        public static Result<TValue> Fail<TValue>(string error)
        {
            return new Result<TValue>(default, false, error);
        }

        public static Result<TValue> Fail<TValue>(TValue value, string error)
        {
            return new Result<TValue>(value, false, error);
        }
    }

    public class Result<TValue> : Result
    {
        public TValue Data { get; set; }

        public Result()
        {

        }

        protected internal Result(TValue value, bool success, string message)
        : base(success, message, !success ? -1 : 0)
        {
            Data = value;
        }

        protected internal Result(TValue value, int code, bool success, string message)
            : base(success, message, !success ? -1 : 0)
        {
            Data = value;
            Code = code;
        }
    }
}
