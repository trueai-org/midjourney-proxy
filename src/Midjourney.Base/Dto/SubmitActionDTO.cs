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
using System.Text.Json.Serialization;
using Swashbuckle.AspNetCore.Annotations;

namespace Midjourney.Base.Dto
{
    /// <summary>
    /// 执行动作。
    /// </summary>
    [SwaggerSchema("执行动作")]
    public class SubmitActionDTO : BaseSubmitDTO
    {
        /// <summary>
        /// 任务ID。
        /// </summary>
        [SwaggerSchema("任务ID", Description = "\"1320098173412546\"")]
        public string TaskId { get; set; }

        /// <summary>
        /// 动作标识
        /// </summary>
        [SwaggerSchema("MJ::JOB::upsample::2::3dbbd469-36af-4a0f-8f02-df6c579e7011")]
        public string CustomId { get; set; }

        /// <summary>
        /// 是否使用remix模式，可强制绕过账号指定的Remix自动提交
        /// </summary>
        public bool? EnableRemix { get; set; }

        /// <summary>
        /// 强度变化（适用于 V1, V2, V3, V4 变化强度)
        /// 默认：低变化，true：高变化
        /// </summary>
        public bool? Strong { get; set; }

        /// <summary>
        /// 工具列表
        /// </summary>
        [JsonPropertyName("tools")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<FunctionTool> Tools { get; set; }
    }

    /// <summary>
    /// 函数工具
    /// </summary>
    public class FunctionTool
    {
        /// <summary>
        /// 工具名称，如 "get_weather" "get_parent_task"
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// 工具描述，帮助模型理解何时调用
        /// </summary>
        [JsonPropertyName("description")]
        public string Description { get; set; }

        /// <summary>
        /// 输入参数的 JSON Schema | string
        /// </summary>
        [JsonPropertyName("input_schema")]
        public object InputSchema { get; set; } = new();
    }
}