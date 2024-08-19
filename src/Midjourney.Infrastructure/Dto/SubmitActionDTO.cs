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
using Swashbuckle.AspNetCore.Annotations;

namespace Midjourney.Infrastructure.Dto
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
    }
}