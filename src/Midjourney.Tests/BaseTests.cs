using System.Text;

namespace Midjourney.Tests
{
    /// <summary>
    /// 测试基类
    /// </summary>
    public class BaseTests : IDisposable
    {
        public BaseTests()
        {
            // 避免中文输出乱码问题
            Console.OutputEncoding = Encoding.UTF8;
        }

        public virtual void Dispose()
        {
        }

        /// <summary>
        /// 避免中文输出乱码问题 - 用于方法
        /// </summary>
        public virtual void SetOutputUTF8()
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
    }
}