using System.Diagnostics;
using System.Text;
using Xunit.Abstractions;

namespace Midjourney.Tests
{
    /// <summary>
    /// 同时输出到 ITestOutputHelper 和 Console 的包装类
    /// </summary>
    public class TestOutputWrapper
    {
        private readonly ITestOutputHelper? _output;

        public TestOutputWrapper(ITestOutputHelper? output = null)
        {
            _output = output;

            // 避免中文输出乱码问题
            Console.OutputEncoding = Encoding.UTF8;
        }

        public void WriteLine(string message)
        {
            Console.WriteLine(message);
            Debug.WriteLine(message);

            //Debug.WriteLine("这会输出到调试窗口");
            //Trace.WriteLine("这也会输出到调试窗口");

            _output?.WriteLine(message);
        }

        public void WriteLine(string format, params object[] args)
        {
            var message = string.Format(format, args);
            WriteLine(message);
        }
    }
}