using System.Security.Cryptography;

namespace Midjourney.Infrastructure.Util
{
    /// <summary>
    /// 随机工具类
    /// </summary>
    public static class RandomUtils
    {
        private static readonly char[] Characters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789".ToCharArray();

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