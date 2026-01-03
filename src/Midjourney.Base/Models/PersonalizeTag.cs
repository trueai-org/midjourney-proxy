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

using FreeSql.DataAnnotations;
using Midjourney.Base.Dto;
using MongoDB.Bson.Serialization.Attributes;

namespace Midjourney.Base.Models
{
    /// <summary>
    /// 个性化配置 -p
    /// </summary>
    [Serializable]
    public class PersonalizeTag : DomainObject
    {
        public PersonalizeTag()
        {
        }

        /// <summary>
        /// 名称 Profile #3
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// 服务 main
        /// </summary>
        public string Service { get; set; }

        /// <summary>
        /// 版本 7
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// 总点击次数
        /// </summary>
        public int ClickTotal { get; set; }

        /// <summary>
        /// 左边点击次数
        /// </summary>
        public int ClickLeft { get; set; }

        /// <summary>
        /// 右边点击次数
        /// </summary>
        public int ClickRight { get; set; }

        /// <summary>
        /// 跳过的次数
        /// </summary>
        public int SkipCount { get; set; }

        /// <summary>
        /// 总评分次数
        /// </summary>
        public int WinTotal { get; set; }

        /// <summary>
        /// 用时 ms
        /// </summary>
        public long? UseTime { get; set; }

        /// <summary>
        /// 最后一次配对的响应对象
        /// </summary>
        [JsonMap]
        public ProfileGetRandomPairsResponse RandomPairs { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        [BsonDateTimeOptions(Kind = DateTimeKind.Local)]
        public DateTime CreateTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 创建时间
        /// </summary>
        public string CreateTimeFormat => CreateTime.ToString("yyyy-MM-dd HH:mm");

        /// <summary>
        /// 更新时间
        /// </summary>
        [BsonDateTimeOptions(Kind = DateTimeKind.Local)]
        public DateTime UpdateTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 更新时间
        /// </summary>
        public string UpdateTimeFormat => UpdateTime.ToString("yyyy-MM-dd HH:mm");

        /// <summary>
        /// 转换为结果对象
        /// </summary>
        /// <returns></returns>
        public PersonalizeTagResult ToResult()
        {
            return new PersonalizeTagResult
            {
                Id = Id,
                Title = Title,
                Service = Service,
                Version = Version,
                CreatedAt = CreateTimeFormat,
                UpdateeAt = UpdateTimeFormat,
                Status = (Version == "6" && WinTotal >= 40) || (Version == "7" && WinTotal >= 200) ? "UNLOCKED" : "BUILDING",
                ClickTotal = ClickTotal,
                ClickLeft = ClickLeft,
                ClickRight = ClickRight,
                SkipCount = SkipCount,
                WinTotal = WinTotal,
            };
        }
    }

    /// <summary>
    /// 个性化配置结果
    /// </summary>
    public class PersonalizeTagResult
    {
        public string Id { get; set; }

        /// <summary>
        /// 名称 Profile #3
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// 服务 main
        /// </summary>
        public string Service { get; set; }

        /// <summary>
        /// 版本 7
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public string CreatedAt { get; set; }

        /// <summary>
        /// 更新时间
        /// </summary>
        public string UpdateeAt { get; set; }

        /// <summary>
        /// 状态 BUILDING 构建中 | UNLOCKED 已解锁
        /// </summary>
        public string Status { get; set; }

        /// <summary>
        /// 总点击次数
        /// </summary>
        public int ClickTotal { get; set; }

        /// <summary>
        /// 左边点击次数
        /// </summary>
        public int ClickLeft { get; set; }

        /// <summary>
        /// 右边点击次数
        /// </summary>
        public int ClickRight { get; set; }

        /// <summary>
        /// 跳过的次数
        /// </summary>
        public int SkipCount { get; set; }

        /// <summary>
        /// 总评分次数
        /// </summary>
        public int WinTotal { get; set; }
    }
}