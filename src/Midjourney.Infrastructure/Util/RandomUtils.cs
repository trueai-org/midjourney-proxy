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
using System.Security.Cryptography;

namespace Midjourney.Infrastructure.Util
{
    /// <summary>
    /// 随机工具类
    /// </summary>
    public static class RandomUtils
    {
        private static readonly char[] Characters = "0123456789".ToCharArray();

        /// <summary>
        /// 生成指定长度的随机字符串
        /// </summary>
        /// <param name="length">字符串长度</param>
        /// <returns>随机字符串</returns>
        public static string RandomString(int length)
        {
            if (length < 1) throw new ArgumentException("Length must be greater than 0", nameof(length));

            var randomString = new char[length];
            using (var rng = new RNGCryptoServiceProvider())
            {
                var buffer = new byte[sizeof(uint)];
                for (var i = 0; i < length; i++)
                {
                    rng.GetBytes(buffer);
                    var num = BitConverter.ToUInt32(buffer, 0);
                    randomString[i] = Characters[num % Characters.Length];
                }
            }

            return new string(randomString);
        }

        /// <summary>
        /// 生成指定长度的随机数字字符串
        /// </summary>
        /// <param name="length">数字字符串长度</param>
        /// <returns>随机数字字符串</returns>
        public static string RandomNumbers(int length)
        {
            if (length < 1) throw new ArgumentException("Length must be greater than 0", nameof(length));

            var randomNumbers = new char[length];
            using (var rng = new RNGCryptoServiceProvider())
            {
                var buffer = new byte[sizeof(uint)];
                for (var i = 0; i < length; i++)
                {
                    rng.GetBytes(buffer);
                    var num = BitConverter.ToUInt32(buffer, 0);
                    randomNumbers[i] = Characters[num % 10]; // Only use '0' - '9'
                }
            }

            return new string(randomNumbers);
        }
    }
}