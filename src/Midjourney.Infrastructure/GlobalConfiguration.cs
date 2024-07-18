using System.Runtime.InteropServices;

namespace Midjourney.Infrastructure
{
    /// <summary>
    /// 全局配置
    /// </summary>
    public class GlobalConfiguration
    {
        /// <summary>
        /// 网站配置为演示模式
        /// </summary>
        public static bool? IsDemoMode { get; set; }

        /// <summary>
        /// 版本号
        /// </summary>
        public static string Version { get; set; } = "v2.2.19";

        /// <summary>
        /// 判断是否是 Windows 系统
        /// </summary>
        /// <returns></returns>
        public static bool IsWindows()
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        }

        /// <summary>
        /// 判断是否是 Linux 系统
        /// </summary>
        /// <returns></returns>
        public static bool IsLinux()
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        }

        /// <summary>
        /// 判断是否是 macOS 系统
        /// </summary>
        /// <returns></returns>
        public static bool IsMacOS()
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                || Environment.OSVersion.Platform == PlatformID.MacOSX;
        }
    }
}