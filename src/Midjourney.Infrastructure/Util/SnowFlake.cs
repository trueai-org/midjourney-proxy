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