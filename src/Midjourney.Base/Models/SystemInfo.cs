using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;

namespace Midjourney.Base.Models
{
    public class SystemInfo
    {
        private static readonly DateTime _programStartTime = DateTime.Now;

        public string ProgramStartTime { get; set; }
        public string ProgramUptime { get; set; }

        //public string FrontendVersion { get; set; }

        public int CpuCores { get; set; }
        public string OperatingSystem { get; set; }
        public string Architecture { get; set; }
        public string Hostname { get; set; }
        public string ServerStartTime { get; set; }
        public string ServerUptime { get; set; }
        public string TotalMemory { get; set; }
        public string FreeMemory { get; set; }
        public string SystemPlatform { get; set; }
        public int ProcessCount { get; set; }
        public string AvailableMemory { get; set; }

        //public string ProgramVersion { get; set; }

        public string ProgramDirectory { get; set; }
        public bool IsDocker { get; set; }

        public static SystemInfo GetCurrentSystemInfo()
        {
            return new SystemInfo
            {
                //ProgramVersion = GetVersion(),
                //FrontendVersion = GetVersion(),
                ProgramStartTime = _programStartTime.ToString("yyyy-MM-dd HH:mm:ss"),
                ProgramUptime = GetProgramUptime(),
                CpuCores = Environment.ProcessorCount,
                OperatingSystem = GetOperatingSystem(),
                Architecture = GetArchitecture(),
                Hostname = Environment.MachineName,
                ServerStartTime = GetServerStartTime(),
                ServerUptime = GetServerUptime(),
                TotalMemory = GetTotalMemory(),
                FreeMemory = GetFreeMemory(),
                SystemPlatform = GetSystemPlatform(),
                ProcessCount = GetProcessCount(),
                AvailableMemory = GetAvailableMemory(),
                ProgramDirectory = Environment.CurrentDirectory,
                IsDocker = IsRunningInDocker()
            };
        }

        private static string GetProgramUptime()
        {
            var uptime = DateTime.Now - _programStartTime;
            return FormatTimeSpan(uptime);
        }

        private static string GetOperatingSystem()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return "windows";
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return "linux";
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return "mac";
            else
                return "unknown";
        }

        private static string GetArchitecture()
        {
            return RuntimeInformation.OSArchitecture.ToString().ToLower();
        }

        private static string GetServerStartTime()
        {
            try
            {
                var startTime = DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount64);
                return startTime.ToString("yyyy-MM-dd HH:mm:ss");
            }
            catch
            {
                return "-";
            }
        }

        private static string GetServerUptime()
        {
            try
            {
                var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
                return $"{(int)uptime.TotalDays} Days";
            }
            catch
            {
                return "-";
            }
        }

        private static string GetTotalMemory()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return GetWindowsTotalMemory();
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    return GetLinuxTotalMemory();
                }
                else
                {
                    return GetGenericTotalMemory();
                }
            }
            catch
            {
                return "-";
            }
        }

        private static string GetFreeMemory()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return GetWindowsFreeMemory();
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    return GetLinuxFreeMemory();
                }
                else
                {
                    return "-";
                }
            }
            catch
            {
                return "未知";
            }
        }

        private static string GetWindowsTotalMemory()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    using var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        var totalMemory = Convert.ToInt64(obj["TotalPhysicalMemory"]);
                        return $"{totalMemory / (1024 * 1024)}MB";
                    }
                }
            }
            catch { }
            return "-";
        }

        private static string GetWindowsFreeMemory()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    using var searcher = new ManagementObjectSearcher("SELECT AvailableBytes FROM Win32_PerfRawData_PerfOS_Memory");
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        var freeMemory = Convert.ToInt64(obj["AvailableBytes"]);
                        return $"{freeMemory / (1024 * 1024)}MB";
                    }
                }
            }
            catch { }
            return "-";
        }

        private static string GetLinuxTotalMemory()
        {
            try
            {
                var memInfo = File.ReadAllText("/proc/meminfo");
                var lines = memInfo.Split('\n');

                foreach (var line in lines)
                {
                    if (line.StartsWith("MemTotal:"))
                    {
                        var parts = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2 && long.TryParse(parts[1], out long totalKb))
                        {
                            return $"{totalKb / 1024}MB";
                        }
                    }
                }
            }
            catch { }
            return "-";
        }

        private static string GetLinuxFreeMemory()
        {
            try
            {
                var memInfo = File.ReadAllText("/proc/meminfo");
                var lines = memInfo.Split('\n');
                long freeKb = 0, availableKb = 0, buffersKb = 0, cachedKb = 0;

                foreach (var line in lines)
                {
                    if (line.StartsWith("MemFree:"))
                    {
                        var parts = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2) long.TryParse(parts[1], out freeKb);
                    }
                    else if (line.StartsWith("MemAvailable:"))
                    {
                        var parts = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2) long.TryParse(parts[1], out availableKb);
                    }
                    else if (line.StartsWith("Buffers:"))
                    {
                        var parts = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2) long.TryParse(parts[1], out buffersKb);
                    }
                    else if (line.StartsWith("Cached:"))
                    {
                        var parts = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2) long.TryParse(parts[1], out cachedKb);
                    }
                }

                // 优先使用 MemAvailable，如果不存在则计算 Free + Buffers + Cached
                var actualFree = availableKb > 0 ? availableKb : (freeKb + buffersKb + cachedKb);
                return $"{actualFree / 1024}MB";
            }
            catch { }
            return "-";
        }

        private static string GetGenericTotalMemory()
        {
            try
            {
                var gcMemoryInfo = GC.GetGCMemoryInfo();
                return $"{gcMemoryInfo.TotalAvailableMemoryBytes / (1024 * 1024)}MB";
            }
            catch
            {
                return "-";
            }
        }

        private static string GetSystemPlatform()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return GetWindowsVersion();
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    return GetLinuxDistribution();
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    return "macOS";
                }
                else
                {
                    return RuntimeInformation.OSDescription;
                }
            }
            catch
            {
                return "-";
            }
        }

        private static string GetWindowsVersion()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    using var searcher = new ManagementObjectSearcher("SELECT Caption FROM Win32_OperatingSystem");
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        return obj["Caption"]?.ToString() ?? "Windows";
                    }
                }
            }
            catch { }
            return "windows";
        }

        private static string GetLinuxDistribution()
        {
            try
            {
                // 检查是否在 Docker 容器中
                if (File.Exists("/.dockerenv"))
                {
                    return "docker";
                }

                // 尝试读取发行版信息
                string[] releaseFiles = { "/etc/os-release", "/etc/lsb-release", "/etc/redhat-release" };

                foreach (var file in releaseFiles)
                {
                    if (File.Exists(file))
                    {
                        var content = File.ReadAllText(file).ToLower();
                        if (content.Contains("ubuntu")) return "ubuntu";
                        if (content.Contains("debian")) return "debian";
                        if (content.Contains("centos")) return "centos";
                        if (content.Contains("red hat")) return "redhat";
                        if (content.Contains("suse")) return "suse";
                        if (content.Contains("alpine")) return "alpine";
                    }
                }

                return "linux";
            }
            catch
            {
                return "linux";
            }
        }

        private static int GetProcessCount()
        {
            try
            {
                return Process.GetProcesses().Length;
            }
            catch
            {
                return 0;
            }
        }

        private static string GetAvailableMemory()
        {
            try
            {
                var currentProcess = Process.GetCurrentProcess();
                var workingSet = currentProcess.WorkingSet64;
                return $"{workingSet / (1024 * 1024)}MB";
            }
            catch
            {
                return "-";
            }
        }

        private static string GetVersion()
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version;
                return $"V{version?.Major}.{version?.Minor}.{version?.Build}";
            }
            catch
            {
                return "V1.0.0";
            }
        }

        private static bool IsRunningInDocker()
        {
            try
            {
                return File.Exists("/.dockerenv") ||
                Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true";
            }
            catch
            {
                return false;
            }
        }

        private static string FormatTimeSpan(TimeSpan timeSpan)
        {
            if (timeSpan.TotalDays >= 1)
                return $"{(int)timeSpan.TotalDays} Days {timeSpan.Hours} Hours";
            else if (timeSpan.TotalHours >= 1)
                return $"{(int)timeSpan.TotalHours} Hours {timeSpan.Minutes} Minutes";
            else if (timeSpan.TotalMinutes >= 1)
                return $"{timeSpan.Minutes} Minute";
            else
                return $"{timeSpan.Seconds} Seconds";
        }
    }
}