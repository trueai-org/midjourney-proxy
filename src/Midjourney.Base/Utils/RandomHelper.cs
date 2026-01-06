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

namespace Midjourney.Base.Util
{
    /// <summary>
    /// 高性能密码学安全的随机数工具类 - v20260106
    /// </summary>
    public static class RandomHelper
    {
        private static readonly char[] Digits = "0123456789".ToCharArray();

        /// <summary>
        /// 生成指定长度的随机数字字符串（均匀分布）
        /// </summary>
        /// <param name="length">长度，必须大于 0</param>
        /// <returns>随机数字字符串</returns>
        public static string RandomNumbers(int length)
        {
            if (length < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }

            Span<char> result = stackalloc char[length];
            Span<byte> buffer = stackalloc byte[length];

            RandomNumberGenerator.Fill(buffer);

            for (int i = 0; i < length; i++)
            {
                result[i] = Digits[buffer[i] % 10];
            }

            return new string(result);
        }

        /// <summary>
        /// 生成指定范围内的随机整数（均匀分布）
        /// </summary>
        /// <param name="minValue">最小值（包含）</param>
        /// <param name="maxValue">最大值（不包含）</param>
        public static int RandomInt(int minValue, int maxValue)
        {
            if (minValue >= maxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(maxValue));
            }

            return RandomNumberGenerator.GetInt32(minValue, maxValue);
        }
    }
}