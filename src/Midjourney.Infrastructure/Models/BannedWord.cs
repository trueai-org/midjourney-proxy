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

using Midjourney.Infrastructure.Data;
using MongoDB.Bson.Serialization.Attributes;

namespace Midjourney.Infrastructure.Models
{
    /// <summary>
    /// 违规词管理
    /// </summary>
    [BsonCollection("word")]
    [MongoDB.Bson.Serialization.Attributes.BsonIgnoreExtraElements]
    [Serializable]
    public class BannedWord : DomainObject
    {
        public BannedWord()
        {
        }

        /// <summary>
        /// 名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 违规词
        /// </summary>
        public List<string> Keywords { get; set; } = new List<string>();

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool Enable { get; set; } = true;

        /// <summary>
        /// 权重
        /// </summary>
        public int Weight { get; set; }

        /// <summary>
        /// 序号
        /// </summary>
        public int Sort { get; set; }

        /// <summary>
        /// 描述
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        [BsonDateTimeOptions(Kind = DateTimeKind.Local)]
        public DateTime CreateTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 创建时间
        /// </summary>
        [LiteDB.BsonIgnore]
        [MongoDB.Bson.Serialization.Attributes.BsonIgnore]
        public string CreateTimeFormat => CreateTime.ToString("yyyy-MM-dd HH:mm");

        /// <summary>
        /// 更新时间
        /// </summary>
        [BsonDateTimeOptions(Kind = DateTimeKind.Local)]
        public DateTime UpdateTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 更新时间
        /// </summary>
        [LiteDB.BsonIgnore]
        [MongoDB.Bson.Serialization.Attributes.BsonIgnore]
        public string UpdateTimeFormat => UpdateTime.ToString("yyyy-MM-dd HH:mm");
    }
}