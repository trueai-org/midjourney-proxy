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
    /// 变化任务提交参数。
    /// </summary>
    [SwaggerSchema("变化任务提交参数")]
    public class SubmitChangeDTO : BaseSubmitDTO
    {
        /// <summary>
        /// 任务ID。
        /// </summary>
        [SwaggerSchema("任务ID", Description = "\"1320098173412546\"")]
        public string TaskId { get; set; }

        /// <summary>
        /// 操作类型: UPSCALE(放大); VARIATION(变换); REROLL(重新生成)。
        /// </summary>
        [SwaggerSchema("UPSCALE(放大); VARIATION(变换); REROLL(重新生成)", Description = "UPSCALE")]
        public TaskAction Action { get; set; }

        /// <summary>
        /// 序号(1~4), action为UPSCALE, VARIATION时必传。
        /// </summary>
        [SwaggerSchema("序号(1~4), action为UPSCALE,VARIATION时必传", Description = "1")]
        public int? Index { get; set; }
    }
}