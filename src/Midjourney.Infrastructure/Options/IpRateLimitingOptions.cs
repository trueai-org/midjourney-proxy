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

using LiteDB;
using System.Net;

namespace Midjourney.Infrastructure.Options
{
    /// <summary>
    /// 表示 IP 限流配置选项。
    /// </summary>
    public class IpRateLimitingOptions
    {
        /// <summary>
        /// 是否启用限流
        /// </summary>
        public bool Enable { get; set; }

        /// <summary>
        /// 白名单列表
        /// </summary>
        public List<string> Whitelist { get; set; } = new List<string>();

        /// <summary>
        /// 白名单 IP 网络
        /// </summary>
        [BsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        [System.Text.Json.Serialization.JsonIgnore]
        public List<IPNetwork2> WhitelistNetworks
        {
            get
            {
                try
                {
                    // 格式化白名单
                    // 如果没有 / , 则默认为 /32
                    return Whitelist.Select(ip => !ip.Contains("/") ? IPNetwork2.Parse(ip + "/32") : IPNetwork2.Parse(ip)).ToList();
                }
                catch
                {
                }
                return new List<IPNetwork2>();
            }
        }

        /// <summary>
        /// 黑名单列表
        /// </summary>
        public List<string> Blacklist { get; set; } = new List<string>();

        /// <summary>
        /// 黑名单 IP 网络
        /// </summary>
        [BsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        [System.Text.Json.Serialization.JsonIgnore]
        public List<IPNetwork2> BlacklistNetworks
        {
            get
            {
                try
                {
                    // 格式化黑名单
                    return Blacklist.Select(ip => IPNetwork2.Parse(ip + "/32")).ToList();
                }
                catch
                {
                }

                return new List<IPNetwork2>();
            }
        }

        /// <summary>
        /// IP 限流规则 (0.0.0.0/32)
        /// </summary>
        public Dictionary<string, Dictionary<int, int>> IpRules { get; set; } = new Dictionary<string, Dictionary<int, int>>();

        /// <summary>
        /// IP /24 段限流规则 (0.0.0.0/24)
        /// </summary>
        public Dictionary<string, Dictionary<int, int>> Ip24Rules { get; set; } = new Dictionary<string, Dictionary<int, int>>();

        /// <summary>
        /// IP /16 段限流规则 (0.0.0.0/16)
        /// </summary>
        public Dictionary<string, Dictionary<int, int>> Ip16Rules { get; set; } = new Dictionary<string, Dictionary<int, int>>();
    }
}
