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
using IdGen;
using System.Net;
using System.Text;

namespace Midjourney.Infrastructure.Util
{
    /// <summary>
    /// 雪花算法生成唯一ID的工具类
    /// </summary>
    public static class SnowFlake
    {
        private static readonly IdGenerator Generator;

        static SnowFlake()
        {
            var epoch = new DateTime(2010, 11, 4, 1, 42, 54, 657, DateTimeKind.Utc);
            var structure = new IdStructure(41, 10, 12);  // 41 bits for timestamp, 10 bits for node, 12 bits for sequence
            var options = new IdGeneratorOptions(structure, new DefaultTimeSource(epoch));
            Generator = new IdGenerator(GetWorkerId(), options);
        }

        /// <summary>
        /// 生成下一个唯一ID
        /// </summary>
        /// <returns>唯一ID字符串</returns>
        public static string NextId()
        {
            return Generator.CreateId().ToString();
        }

        /// <summary>
        /// 获取工作ID
        /// </summary>
        /// <returns>工作ID</returns>
        private static int GetWorkerId()
        {
            try
            {
                var hostName = Dns.GetHostName();
                var hostBytes = Encoding.UTF8.GetBytes(hostName);
                return hostBytes.Sum(b => b) % 1024; // 1024 = 2^10
            }
            catch
            {
                return 1;
            }
        }
    }
}