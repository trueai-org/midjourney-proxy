using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Midjourney.Infrastructure.Services
{
    /// <summary>
    /// 2fa 验证帮助类
    /// https://github.com/wuzf/2fa/blob/main/worker.js
    /// </summary>
    public class TwoFAHelper
    {
        public static string GenerateOtp(string secret)
        {
            var loadTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var otp = GenerateOtp(secret, loadTime);
            return otp;
        }

        public static string GenerateOtp(string secret, long loadTime)
        {
            const int timeStep = 30;

            var counter = loadTime / timeStep;
            var counterBytes = BitConverter.GetBytes(counter);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(counterBytes);
            }

            var key = Base32Decode(secret);

            using var hmac = new HMACSHA1(key);
            var hash = hmac.ComputeHash(counterBytes);

            var offset = hash[^1] & 0xF;
            var binaryCode = ((hash[offset] & 0x7F) << 24) |
                             ((hash[offset + 1] & 0xFF) << 16) |
                             ((hash[offset + 2] & 0xFF) << 8) |
                             (hash[offset + 3] & 0xFF);

            var otp = (binaryCode % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
            return otp;
        }

        private static byte[] Base32Decode(string base32)
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
            base32 = base32.ToUpperInvariant();

            var bits = new StringBuilder();
            foreach (var c in base32)
            {
                var index = alphabet.IndexOf(c);
                if (index < 0) throw new ArgumentException("Invalid Base32 character.");
                bits.Append(Convert.ToString(index, 2).PadLeft(5, '0'));
            }

            var byteList = new List<byte>();
            for (int i = 0; i + 8 <= bits.Length; i += 8)
            {
                byteList.Add(Convert.ToByte(bits.ToString(i, 8), 2));
            }

            return byteList.ToArray();
        }

        public static int CalculateRemainingTime(long loadTime)
        {
            const int timeStep = 30;
            var epochTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var currentCounter = epochTime / timeStep;
            var expirationTime = (currentCounter + 1) * timeStep;
            return (int)(expirationTime - loadTime);
        }
    }
}