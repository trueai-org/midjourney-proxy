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

namespace Midjourney.Base.Dto
{
    /// <summary>
    /// video 提交参数
    /// https://apiai.apifox.cn/api-315106648
    /// </summary>
    public class SubmitVideoDTO : BaseSubmitDTO
    {
        /// <summary>
        /// 提示词
        /// </summary>
        public string Prompt { get; set; }

        /// <summary>
        /// 视频运动模式 low | high
        /// </summary>
        public string Motion { get; set; } = "low";

        /// <summary>
        /// 首帧图片，扩展时可为空 url | base64
        /// </summary>
        public string Image { get; set; }

        /// <summary>
        /// 尾帧图片，可选 url | base64
        /// </summary>
        public string EndImage { get; set; }

        /// <summary>
        /// 循环播放视频，默认为 false
        /// </summary>
        public bool Loop { get; set; } = false;

        /// <summary>
        /// 视频操作 extend
        /// 对视频任务进行操作。不为空时，index、taskId必填
        /// </summary>
        public string Action { get; set; }

        /// <summary>
        /// 执行的视频索引号 0 | 1 | 2 | 3
        /// </summary>
        public int? Index { get; set; }

        /// <summary>
        /// 需要操作的视频父任务ID
        /// </summary>
        public string TaskId { get; set; }

        /// <summary>
        /// 视频分辨率类型，默认标清。
        /// 取值：vid_1.1_i2v_480 | vid_1.1_i2v_720
        /// SD: vid_1.1_i2v_480
        /// HD: vid_1.1_i2v_720
        /// </summary>
        public string VideoType { get; set; } = "vid_1.1_i2v_480";

        /// <summary>
        /// 批量大小 1 | 2 | 4
        /// 默认：4
        /// </summary>
        public int? BatchSize { get; set; } = 4;
    }
}