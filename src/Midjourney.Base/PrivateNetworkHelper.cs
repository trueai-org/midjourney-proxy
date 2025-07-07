using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Midjourney.Base
{
    /// <summary>
    /// 私有网络帮助类 - 提供完整的私有网络地址获取和判断功能
    /// </summary>
    public static class PrivateNetworkHelper
    {
        #region 私有网络地址范围判断

        /// <summary>
        /// 判断是否为 RFC 1918 标准私有地址
        /// </summary>
        public static bool IsRFC1918PrivateIP(IPAddress ip)
        {
            if (ip.AddressFamily != AddressFamily.InterNetwork)
                return false;

            byte[] bytes = ip.GetAddressBytes();

            // 10.0.0.0/8 (10.0.0.0 - 10.255.255.255)
            if (bytes[0] == 10)
                return true;

            // 172.16.0.0/12 (172.16.0.0 - 172.31.255.255)
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                return true;

            // 192.168.0.0/16 (192.168.0.0 - 192.168.255.255)
            if (bytes[0] == 192 && bytes[1] == 168)
                return true;

            return false;
        }

        /// <summary>
        /// 判断是否为 Docker 网段
        /// </summary>
        public static bool IsDockerNetwork(IPAddress ip)
        {
            if (ip.AddressFamily != AddressFamily.InterNetwork)
                return false;

            byte[] bytes = ip.GetAddressBytes();

            // Docker 默认网桥网络 172.17.0.0/16
            if (bytes[0] == 172 && bytes[1] == 17)
                return true;

            // Docker 用户自定义网络池 172.18.0.0/16 - 172.31.0.0/16 (与 RFC1918 重叠但在 Docker 环境中)
            if (bytes[0] == 172 && bytes[1] >= 18 && bytes[1] <= 31 && IsRunningInDocker())
                return true;

            // Docker Swarm 网络 10.0.0.0/8 的特定子网
            if (bytes[0] == 10 && bytes[1] == 0 && IsRunningInDocker())
                return true;

            // Docker Desktop 网段 192.168.65.0/24
            if (bytes[0] == 192 && bytes[1] == 168 && bytes[2] == 65)
                return true;

            return false;
        }

        /// <summary>
        /// 判断是否为其他保留/特殊地址
        /// </summary>
        public static bool IsReservedIP(IPAddress ip)
        {
            if (ip.AddressFamily != AddressFamily.InterNetwork)
                return false;

            byte[] bytes = ip.GetAddressBytes();

            // 环回地址 127.0.0.0/8
            if (bytes[0] == 127)
                return true;

            // 链路本地地址 169.254.0.0/16 (APIPA)
            if (bytes[0] == 169 && bytes[1] == 254)
                return true;

            // 组播地址 224.0.0.0/4
            if (bytes[0] >= 224 && bytes[0] <= 239)
                return true;

            // 文档用途地址
            if (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 2)
                return true;
            if (bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100)
                return true;
            if (bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113)
                return true;

            // 基准测试地址 198.18.0.0/15
            if (bytes[0] == 198 && (bytes[1] == 18 || bytes[1] == 19))
                return true;

            // IANA 保留地址 240.0.0.0/4
            if (bytes[0] >= 240 && bytes[0] <= 254)
                return true;

            // 当前网络 0.0.0.0/8
            if (bytes[0] == 0)
                return true;

            return false;
        }

        /// <summary>
        /// 判断是否为私有或保留地址（包含所有非公网地址）
        /// </summary>
        public static bool IsPrivateOrReservedIP(IPAddress ip)
        {
            return IsRFC1918PrivateIP(ip) || IsDockerNetwork(ip) || IsReservedIP(ip);
        }

        #endregion 私有网络地址范围判断

        #region 环境检测

        /// <summary>
        /// 判断是否在 Docker 容器内运行
        /// </summary>
        public static bool IsRunningInDocker()
        {
            try
            {
                // 检查 /.dockerenv 文件
                if (File.Exists("/.dockerenv"))
                    return true;

                // 检查 /proc/1/cgroup 文件
                if (File.Exists("/proc/1/cgroup"))
                {
                    string cgroup = File.ReadAllText("/proc/1/cgroup");
                    if (cgroup.Contains("docker") || cgroup.Contains("containerd"))
                        return true;
                }

                // 检查环境变量
                string dockerEnv = Environment.GetEnvironmentVariable("DOCKER_CONTAINER");
                if (!string.IsNullOrEmpty(dockerEnv))
                    return true;
            }
            catch
            {
                // 忽略异常
            }

            return false;
        }

        #endregion 环境检测

        #region 网络地址获取

        /// <summary>
        /// 获取本机主要的私有网络地址（推荐使用）
        /// </summary>
        public static string GetPrimaryPrivateIP()
        {
            // 方法0：通过环境变量获取（如果有设置）
            string envIP = Environment.GetEnvironmentVariable("HOST_IP");
            if (!string.IsNullOrEmpty(envIP) && IsRFC1918PrivateIP(IPAddress.Parse(envIP)))
            {
                return envIP;
            }

            // 方法1：通过连接外部地址获取本机对外 IP（优先级最高）
            var connectionIP = GetIPByConnection();
            if (!string.IsNullOrEmpty(connectionIP) && IsRFC1918PrivateIP(IPAddress.Parse(connectionIP)))
            {
                return connectionIP;
            }

            // 方法2：从网络接口中选择最佳的私有 IP
            var bestPrivateIP = GetBestPrivateIPFromInterfaces();
            if (!string.IsNullOrEmpty(bestPrivateIP))
            {
                return bestPrivateIP;
            }

            // 方法3：通过 DNS 解析获取
            var dnsIP = GetPrivateIPFromDNS();
            if (!string.IsNullOrEmpty(dnsIP))
            {
                return dnsIP;
            }

            // 如果在 Docker 中且前面方法都失败，返回 Docker IP
            if (IsRunningInDocker())
            {
                var dockerIP = GetDockerIP();
                if (!string.IsNullOrEmpty(dockerIP))
                {
                    return dockerIP;
                }
            }

            return "127.0.0.1";
        }

        /// <summary>
        /// 通过连接外部地址获取本机 IP
        /// </summary>
        private static string GetIPByConnection()
        {
            // 常用的外部 DNS 服务器
            var externalHosts = new[]
            {
            ("223.5.5.5", 65530),    // 阿里 DNS
            ("8.8.8.8", 65530),      // Google DNS
            ("114.114.114.114", 65530), // 114 DNS
            ("1.1.1.1", 65530)       // Cloudflare DNS
        };

            foreach (var (host, port) in externalHosts)
            {
                try
                {
                    using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
                    socket.Connect(host, port);
                    var endPoint = socket.LocalEndPoint as IPEndPoint;
                    var ip = endPoint?.Address.ToString();

                    if (!string.IsNullOrEmpty(ip) && ip != "127.0.0.1")
                    {
                        return ip;
                    }
                }
                catch
                {
                    continue;
                }
            }

            return null;
        }

        /// <summary>
        /// 从网络接口中获取最佳的私有 IP
        /// </summary>
        private static string GetBestPrivateIPFromInterfaces()
        {
            var candidateIPs = new List<IPCandidate>();

            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    // 只处理活动的网络接口
                    if (ni.OperationalStatus != OperationalStatus.Up)
                        continue;

                    // 排除环回接口
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;

                    foreach (UnicastIPAddressInformation ip in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            var ipAddr = ip.Address;

                            // 只考虑私有地址
                            if (IsRFC1918PrivateIP(ipAddr))
                            {
                                candidateIPs.Add(new IPCandidate
                                {
                                    IPAddress = ipAddr.ToString(),
                                    NetworkInterface = ni,
                                    Priority = GetNetworkPriority(ni, ipAddr)
                                });
                            }
                        }
                    }
                }

                // 按优先级排序并返回最佳选择
                var bestCandidate = candidateIPs
                    .OrderByDescending(c => c.Priority)
                    .FirstOrDefault();

                return bestCandidate?.IPAddress;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 计算网络接口的优先级
        /// </summary>
        private static int GetNetworkPriority(NetworkInterface ni, IPAddress ip)
        {
            int priority = 0;
            byte[] bytes = ip.GetAddressBytes();

            // 网络接口类型优先级
            switch (ni.NetworkInterfaceType)
            {
                case NetworkInterfaceType.Ethernet:
                case NetworkInterfaceType.GigabitEthernet:
                case NetworkInterfaceType.FastEthernetT:
                    priority += 100; // 有线网络优先级最高
                    break;

                case NetworkInterfaceType.Wireless80211:
                    priority += 80;  // 无线网络次之
                    break;

                default:
                    priority += 50;  // 其他网络类型
                    break;
            }

            // IP 地址段优先级
            if (bytes[0] == 192 && bytes[1] == 168)
            {
                priority += 30; // 192.168.x.x 优先级较高（常用家庭网络）
            }
            else if (bytes[0] == 10)
            {
                priority += 20; // 10.x.x.x 次之（常用企业网络）
            }
            else if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            {
                // 区分是否为 Docker 网络
                if (IsDockerNetwork(ip))
                {
                    priority += 5; // Docker 网络优先级较低
                }
                else
                {
                    priority += 15; // 非 Docker 的 172.x 网络
                }
            }

            // 网络速度加分
            if (ni.Speed > 1000000000) // 1Gbps+
            {
                priority += 10;
            }
            else if (ni.Speed > 100000000) // 100Mbps+
            {
                priority += 5;
            }

            return priority;
        }

        /// <summary>
        /// 通过 DNS 解析获取私有 IP
        /// </summary>
        private static string GetPrivateIPFromDNS()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                var privateIP = host.AddressList
                    .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork &&
                                        IsRFC1918PrivateIP(ip) &&
                                        !IPAddress.IsLoopback(ip));

                return privateIP?.ToString();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 获取 Docker 环境中的 IP 地址
        /// </summary>
        private static string GetDockerIP()
        {
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus == OperationalStatus.Up)
                    {
                        foreach (UnicastIPAddressInformation ip in ni.GetIPProperties().UnicastAddresses)
                        {
                            if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                            {
                                var ipAddr = ip.Address;
                                if (IsDockerNetwork(ipAddr))
                                {
                                    return ipAddr.ToString();
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // 忽略异常
            }

            return null;
        }

        /// <summary>
        /// 获取所有私有网络地址
        /// </summary>
        public static List<PrivateIPInfo> GetAllPrivateIPs()
        {
            var privateIPs = new List<PrivateIPInfo>();

            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus == OperationalStatus.Up &&
                        ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    {
                        foreach (UnicastIPAddressInformation ip in ni.GetIPProperties().UnicastAddresses)
                        {
                            if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                            {
                                var ipAddr = ip.Address;

                                if (IsRFC1918PrivateIP(ipAddr) || IsDockerNetwork(ipAddr))
                                {
                                    privateIPs.Add(new PrivateIPInfo
                                    {
                                        IPAddress = ipAddr.ToString(),
                                        NetworkInterfaceName = ni.Name,
                                        NetworkInterfaceType = ni.NetworkInterfaceType.ToString(),
                                        Description = ni.Description,
                                        IsRFC1918 = IsRFC1918PrivateIP(ipAddr),
                                        IsDocker = IsDockerNetwork(ipAddr),
                                        Priority = GetNetworkPriority(ni, ipAddr)
                                    });
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // 忽略异常
            }

            return privateIPs.OrderByDescending(ip => ip.Priority).ToList();
        }

        /// <summary>
        /// 获取网络诊断信息
        /// </summary>
        public static NetworkDiagnosticInfo GetNetworkDiagnosticInfo()
        {
            var info = new NetworkDiagnosticInfo
            {
                IsInDockerContainer = IsRunningInDocker(),
                PrimaryPrivateIP = GetPrimaryPrivateIP(),
                AllPrivateIPs = GetAllPrivateIPs(),
                ConnectionTestIP = GetIPByConnection()
            };

            return info;
        }

        #endregion 网络地址获取

        #region 兼容你的原始方法

        /// <summary>
        /// 兼容你原有的 GetLocalIPAddress 方法，但增强了私有网络判断
        /// </summary>
        public static string GetLocalIPAddress()
        {
            // 使用增强版本的主要私有 IP 获取方法
            return GetPrimaryPrivateIP();
        }

        #endregion 兼容你的原始方法
    }

    #region 数据模型

    /// <summary>
    /// IP 候选者（内部使用）
    /// </summary>
    internal class IPCandidate
    {
        public string IPAddress { get; set; }
        public NetworkInterface NetworkInterface { get; set; }
        public int Priority { get; set; }
    }

    /// <summary>
    /// 私有 IP 信息
    /// </summary>
    public class PrivateIPInfo
    {
        public string IPAddress { get; set; }
        public string NetworkInterfaceName { get; set; }
        public string NetworkInterfaceType { get; set; }
        public string Description { get; set; }
        public bool IsRFC1918 { get; set; }
        public bool IsDocker { get; set; }
        public int Priority { get; set; }
    }

    /// <summary>
    /// 网络诊断信息
    /// </summary>
    public class NetworkDiagnosticInfo
    {
        public bool IsInDockerContainer { get; set; }
        public string PrimaryPrivateIP { get; set; }
        public string ConnectionTestIP { get; set; }
        public List<PrivateIPInfo> AllPrivateIPs { get; set; }
    }

    #endregion 数据模型
}