using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Serilog;

namespace Midjourney.Infrastructure.Services
{
    /// <summary>
    /// 升级服务接口
    /// </summary>
    public interface IUpgradeService
    {
        /// <summary>
        /// 检查最新版本
        /// </summary>
        Task<UpgradeInfo> CheckForUpdatesAsync();

        /// <summary>
        /// 开始下载升级包
        /// </summary>
        Task<bool> StartDownloadAsync();

        /// <summary>
        /// 获取升级状态
        /// </summary>
        UpgradeInfo GetUpgradeStatus();

        /// <summary>
        /// 取消更新
        /// </summary>
        void CancelUpdate();

        /// <summary>
        /// 是否支持当前平台
        /// </summary>
        bool IsSupportedPlatform { get; }

        /// <summary>
        /// 获取升级信息
        /// </summary>
        UpgradeInfo UpgradeInfo { get; }
    }

    /// <summary>
    /// 检查升级服务 - 适用于 docker linux-x64
    /// </summary>
    public class UpgradeService : IUpgradeService
    {
        // 升级目录
        private const string UpgradeDirectory = "Upgrade";

        // 解压目录
        private const string ExtractDirectory = "Extract";

        // GitHub API URL
        private const string GitHubApiUrlProxy = "https://api.github.com/repos/trueai-org/midjourney-proxy/releases/latest";

        private readonly string _upgradePath;
        private readonly HttpClient _httpClient;
        private UpgradeInfo _upgradeInfo = new();
        private bool _isDownloading = false;
        private CancellationTokenSource _downloadCancellation;

        public UpgradeService(HttpClient httpClient)
        {
            _httpClient = httpClient;

            // 不使用项目目录
            //_upgradePath = Path.Combine(Directory.GetCurrentDirectory(), UpgradeDirectory);

            // 使用应用程序基目录
            _upgradePath = Path.Combine(AppContext.BaseDirectory, UpgradeDirectory);

            // 初始化升级信息
            _upgradeInfo.Platform = GetCurrentPlatform();
            _upgradeInfo.SupportedPlatform = IsSupportedPlatform;

            // 确保升级目录存在
            EnsureUpgradeDirectory();
        }

        /// <summary>
        /// 获取升级信息
        /// </summary>
        public UpgradeInfo UpgradeInfo => _upgradeInfo;

        public bool IsSupportedPlatform
        {
            get
            {
                var platform = GetCurrentPlatform();

#if DEBUG
                // 开发模式允许 linux-x64 win-x64
                return platform == "linux-x64" || platform == "win-x64";
#endif

                // 生产环境只支持 linux-x64
                return platform == "linux-x64" && IsDockerEnvironment();
            }
        }

        private bool IsDockerEnvironment()
        {
            try
            {
                return System.IO.File.Exists("/.dockerenv") ||
                Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true" ||
                Environment.GetEnvironmentVariable("DOCKER_CONTAINER") != null;
            }
            catch
            {
                return false;
            }
        }

        public async Task CheckAndPerformStartupUpgradeAsync()
        {
            try
            {
                Log.Information("启动时检查升级文件...");

                // 查找升级文件
                var upgradeFile = FindUpgradeFile();
                if (upgradeFile == null)
                {
                    Log.Information("未找到升级文件");
                    return;
                }

                var fileVersion = ExtractVersionFromFileName(upgradeFile);

                var currentVersion = _upgradeInfo.CurrentVersion;

                Log.Information("找到升级文件: {File}, 版本: {FileVersion}, 当前版本: {CurrentVersion}",
                    upgradeFile, fileVersion, currentVersion);

                if (IsNewerVersion(fileVersion, currentVersion))
                {
                    Log.Information("开始执行启动升级...");
                    await PerformUpgradeAsync(upgradeFile);
                }
                else
                {
                    Log.Information("升级文件版本不比当前版本新，跳过升级");

                    // 删除旧的升级文件
                    File.Delete(upgradeFile);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "启动时升级检查失败");
            }
        }

        public async Task<UpgradeInfo> CheckForUpdatesAsync()
        {
            if (!IsSupportedPlatform)
            {
                _upgradeInfo.Status = UpgradeStatus.Failed;
                _upgradeInfo.Message = $"不支持当前平台: {_upgradeInfo.Platform}";
                return _upgradeInfo;
            }

            try
            {
                _upgradeInfo.Status = UpgradeStatus.Checking;
                _upgradeInfo.Message = "检查最新版本...";
                _upgradeInfo.Progress = 0;

                var latestRelease = await GetLatestReleaseAsync();
                if (latestRelease == null)
                {
                    throw new Exception("无法获取最新版本信息");
                }

                _upgradeInfo.LatestVersion = latestRelease.TagName;
                _upgradeInfo.Body = latestRelease.Body;
                _upgradeInfo.HasUpdate = IsNewerVersion(_upgradeInfo.LatestVersion, _upgradeInfo.CurrentVersion);

                if (_upgradeInfo.HasUpdate)
                {
                    _upgradeInfo.Status = UpgradeStatus.Idle;
                    _upgradeInfo.Message = $"发现新版本 {_upgradeInfo.LatestVersion}";
                }
                else
                {
                    _upgradeInfo.Status = UpgradeStatus.Success;
                    _upgradeInfo.Message = "当前已是最新版本";
                }

                _upgradeInfo.Progress = 100;
                return _upgradeInfo;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "检查更新失败");
                _upgradeInfo.Status = UpgradeStatus.Failed;
                _upgradeInfo.Message = "检查更新失败";
                _upgradeInfo.ErrorMessage = ex.Message;
                return _upgradeInfo;
            }
        }

        public async Task<bool> StartDownloadAsync()
        {
            if (!IsSupportedPlatform || _isDownloading)
            {
                return false;
            }

            try
            {
                var latestRelease = await GetLatestReleaseAsync();
                if (latestRelease == null)
                {
                    return false;
                }

                var asset = FindAssetForCurrentPlatform(latestRelease.Assets);
                if (asset == null)
                {
                    _upgradeInfo.Status = UpgradeStatus.Failed;
                    _upgradeInfo.Message = $"未找到适用于 {_upgradeInfo.Platform} 的升级包";
                    return false;
                }

                // 判断是否已下载过升级包
                var existingFile = FindUpgradeFile();
                if (asset.Name == Path.GetFileName(existingFile))
                {
                    // 解压升级包
                    await ExtractTarGzAsync(existingFile);

                    _upgradeInfo.Status = UpgradeStatus.ReadyToRestart;
                    _upgradeInfo.Progress = 100;
                    _upgradeInfo.Message = "升级包下载完成，重启应用程序即可升级";

                    _upgradeInfo.Status = UpgradeStatus.ReadyToRestart;
                    _upgradeInfo.Message = "最新版已下载完成，等待重启应用程序";
                    return true;
                }

                _isDownloading = true;
                _downloadCancellation = new CancellationTokenSource();

                // 后台下载
                _ = Task.Run(async () => await DownloadUpgradePackageAsync(asset, latestRelease.TagName));

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "启动下载失败");
                _upgradeInfo.Status = UpgradeStatus.Failed;
                _upgradeInfo.Message = "启动下载失败";
                _upgradeInfo.ErrorMessage = ex.Message;
                _isDownloading = false;
                return false;
            }
        }

        public UpgradeInfo GetUpgradeStatus()
        {
            return _upgradeInfo;
        }

        private void EnsureUpgradeDirectory()
        {
            if (!Directory.Exists(_upgradePath))
            {
                Directory.CreateDirectory(_upgradePath);
            }
        }

        private string FindUpgradeFile()
        {
            var platform = GetCurrentPlatform();

            var pattern = $"midjourney-proxy-{platform}-v*-docker.tar.gz";
            if (platform == "win-x64")
            {
                pattern = $"midjourney-proxy-{platform}-v*-docker.zip";
            }

            var files = Directory.GetFiles(_upgradePath, pattern);

            // 最后一个包
            return files.OrderBy(c => c).LastOrDefault();
        }

        private string ExtractVersionFromFileName(string fileName)
        {
            var match = Regex.Match(Path.GetFileName(fileName), @"v(\d+\.\d+\.\d+(?:\.\d+)?)");
            return match.Success ? match.Groups[1].Value : "0.0.0";
        }

        private string GetCurrentPlatform()
        {
            // 判断是否为 x64 平台
            if (RuntimeInformation.OSArchitecture != Architecture.X64)
            {
                return "unknown";
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return "win-x64";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return "linux-x64";
            }
            else
            {
                return "unknown";
            }
        }

        private bool IsNewerVersion(string newVersion, string currentVersion)
        {
            try
            {
                // 移除版本号前的 'v' 前缀
                newVersion = newVersion.TrimStart('v');
                currentVersion = currentVersion.TrimStart('v');

                var newVer = new Version(newVersion);
                var currentVer = new Version(currentVersion);

                return newVer > currentVer;
            }
            catch
            {
                return false;
            }
        }

        private async Task<GitHubRelease> GetLatestReleaseAsync()
        {
            try
            {
                var apiUrl = GitHubApiUrlProxy;

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "Midjourney-Proxy-Upgrade");

                var response = await _httpClient.GetStringAsync(apiUrl);
                return JsonSerializer.Deserialize<GitHubRelease>(response);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "获取最新版本信息失败");
                return null;
            }
        }

        private GitHubAsset FindAssetForCurrentPlatform(List<GitHubAsset> assets)
        {
            var platform = GetCurrentPlatform();

            var sufix = platform switch
            {
                "win-x64" => "-docker.zip",
                "linux-x64" => "-docker.tar.gz",
                _ => "-docker.tar.gz"
            };
            var fileName = $"midjourney-proxy-{platform}-{_upgradeInfo.LatestVersion}{sufix}";

            return assets.FirstOrDefault(a => a.Name == fileName);
        }

        private async Task DownloadUpgradePackageAsync(GitHubAsset asset, string version)
        {
            try
            {
                _upgradeInfo.Status = UpgradeStatus.Downloading;
                _upgradeInfo.Progress = 0;
                _upgradeInfo.Message = "正在下载升级包...";

                var targetPath = Path.Combine(_upgradePath, asset.Name);
                var tmpPath = targetPath + ".tmp";

                Log.Information("开始下载升级包: {Url} -> {Path}", asset.BrowserDownloadUrl, tmpPath);

                using var response = await _httpClient.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead, _downloadCancellation!.Token);
                response.EnsureSuccessStatusCode();

                var totalSize = response.Content.Headers.ContentLength ?? asset.Size;
                var downloadedSize = 0L;

                using var contentStream = await response.Content.ReadAsStreamAsync(_downloadCancellation.Token);
                using var fileStream = File.Create(tmpPath);

                var buffer = new byte[8192];
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, _downloadCancellation.Token)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, _downloadCancellation.Token);
                    downloadedSize += bytesRead;

                    if (totalSize > 0)
                    {
                        _upgradeInfo.Progress = (int)((downloadedSize * 100) / totalSize);
                        _upgradeInfo.Message = $"下载中... {downloadedSize / 1024 / 1024}MB / {totalSize / 1024 / 1024}MB";
                    }
                }

                // 释放文件流
                fileStream.Close();

                // 重命名临时文件为正式文件
                if (File.Exists(targetPath))
                {
                    File.Delete(targetPath);
                }
                File.Move(tmpPath, targetPath);

                Log.Information("升级包下载完成: {Path}", targetPath);

                _upgradeInfo.Progress = 99;

                // 解压升级包
                await ExtractTarGzAsync(targetPath);

                _upgradeInfo.Status = UpgradeStatus.ReadyToRestart;
                _upgradeInfo.Progress = 100;
                _upgradeInfo.Message = "升级包下载完成，重启应用程序即可升级";
            }
            catch (OperationCanceledException)
            {
                _upgradeInfo.Status = UpgradeStatus.Failed;
                _upgradeInfo.Message = "下载已取消";
                Log.Information("升级包下载已取消");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "下载升级包失败");
                _upgradeInfo.Status = UpgradeStatus.Failed;
                _upgradeInfo.Message = "下载失败";
                _upgradeInfo.ErrorMessage = ex.Message;
            }
            finally
            {
                _isDownloading = false;
            }
        }

        private async Task PerformUpgradeAsync(string upgradeFilePath)
        {
            try
            {
                Log.Information("开始执行升级: {File}", upgradeFilePath);

                var extractPath = Path.Combine(_upgradePath, ExtractDirectory);

                // 解压升级包
                await ExtractTarGzAsync(upgradeFilePath);

                // 查找应用程序目录
                var appPath = FindApplicationPath(extractPath);
                if (string.IsNullOrEmpty(appPath))
                {
                    throw new Exception("在升级包中未找到应用程序文件");
                }

                // 备份当前应用
                var backupPath = Path.Combine(Path.GetTempPath(), $"backup_{DateTime.Now:yyyyMMdd_HHmmss}");
                await BackupCurrentApplicationAsync(backupPath);

                // 执行升级脚本
                await ExecuteUpgradeScript(extractPath);

                //// 清理升级文件
                //File.Delete(upgradeFilePath);
                //Directory.Delete(extractPath, true);

                Log.Information("执行升级脚本成功，准备重启应用程序");

                // 重启应用程序
                await Task.Delay(100);

                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "执行升级失败");
                throw;
            }
        }

        private async Task ExtractTarGzAsync(string tarGzPath)
        {
            try
            {
                // 下载完成后解压文件
                var extractPath = Path.Combine(_upgradePath, ExtractDirectory);

                // 清理旧的解压目录
                if (Directory.Exists(extractPath))
                {
                    Directory.Delete(extractPath, true);
                }

                Directory.CreateDirectory(extractPath);

                // 在实际项目中，您可能需要使用 SharpZipLib 或其他库来处理 tar.gz
                // 使用外部工具解压 tar.gz（Linux 环境）
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    var process = Process.Start("tar", $"-xzf \"{tarGzPath}\" -C \"{extractPath}\"");
                    await process!.WaitForExitAsync();

                    if (process.ExitCode != 0)
                    {
                        throw new Exception("解压 tar.gz 文件失败");
                    }
                }
                else
                {
                    // 如果文件实际上是 zip 格式
                    if (await IsZipFileAsync(tarGzPath))
                    {
                        ZipFile.ExtractToDirectory(tarGzPath, extractPath);
                    }
                    else
                    {
                        throw new Exception("不支持的升级包格式，必须是 zip 或 tar.gz 格式");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "解压升级包失败");

                throw;
            }
        }

        private async Task<bool> IsZipFileAsync(string filePath)
        {
            try
            {
                using var stream = File.OpenRead(filePath);
                var buffer = new byte[4];
                await stream.ReadAsync(buffer, 0, 4);

                // ZIP 文件的魔数
                return buffer[0] == 0x50 && buffer[1] == 0x4B;
            }
            catch
            {
                return false;
            }
        }

        private string FindApplicationPath(string extractPath)
        {
            // 如果是 linux
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // 查找包含 Midjourney.API.dll 的目录
                var appFiles = Directory.GetFiles(extractPath, "Midjourney.API.dll", SearchOption.AllDirectories);
                if (appFiles.Length > 0)
                {
                    return Path.GetDirectoryName(appFiles[0]) ?? "";
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // 查找包含 Midjourney.API.dll 的目录
                var appFiles = Directory.GetFiles(extractPath, "Midjourney.API.exe", SearchOption.AllDirectories);
                if (appFiles.Length > 0)
                {
                    return Path.GetDirectoryName(appFiles[0]) ?? "";
                }
            }
            return "";
        }

        private async Task BackupCurrentApplicationAsync(string backupPath)
        {
            try
            {
                Directory.CreateDirectory(backupPath);

                // 复制当前应用程序文件
                await CopyDirectoryAsync(AppContext.BaseDirectory, backupPath);

                Log.Information("应用程序已备份到: {Path}", backupPath);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "备份应用程序失败");
            }
        }



        private async Task CopyDirectoryAsync(string sourcePath, string targetPath, bool overwrite = false)
        {
            Directory.CreateDirectory(targetPath);

            foreach (var file in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(sourcePath, file);
                var targetFile = Path.Combine(targetPath, relativePath);
                var targetDir = Path.GetDirectoryName(targetFile);

                if (!string.IsNullOrEmpty(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                try
                {
                    if (overwrite || !File.Exists(targetFile))
                    {
                        File.Copy(file, targetFile, overwrite);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "复制文件失败: {Source} -> {Target}", file, targetFile);
                }
            }

            await Task.CompletedTask;
        }


        private async Task ExecuteUpgradeScript(string extractPath)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    await ExecuteWindowsUpgradeScript(extractPath);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    // 不需要执行
                    //await ExecuteLinuxUpgradeScript(extractPath);
                }
                else
                {
                    throw new PlatformNotSupportedException("不支持当前操作系统");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "执行升级脚本失败");
                throw;
            }
        }

        private async Task ExecuteWindowsUpgradeScript(string sourcePath)
        {
            var scriptPath = Path.Combine(_upgradePath, "win-upgrade.bat");

            Log.Information("启动 Windows 升级脚本: {ScriptPath}, 源目录: {SourcePath}", scriptPath, sourcePath);

            var startInfo = new ProcessStartInfo
            {
                FileName = scriptPath,
                Arguments = $"\"{sourcePath}\"",
                WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                CreateNoWindow = true,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            Process.Start(startInfo);

            // 等待脚本启动
            await Task.Delay(500);

            Log.Information("升级脚本已启动，应用程序即将退出");
            Environment.Exit(0);
        }

        private async Task ExecuteLinuxUpgradeScript(string sourcePath)
        {
            var scriptPath = Path.Combine(_upgradePath, "linux-upgrade.sh");

            Log.Information("启动 Linux 升级脚本: {ScriptPath}, 源目录: {SourcePath}", scriptPath, sourcePath);

            var startInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"{scriptPath} \"{sourcePath}\"",
                WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                CreateNoWindow = true,
                UseShellExecute = false
            };

            Process.Start(startInfo);

            // 输出脚本日志
            Log.Information("脚本 {@0}", startInfo);

            // 等待脚本启动
            await Task.Delay(500);

            Log.Information("升级脚本已启动，应用程序即将退出");
            Environment.Exit(0);
        }

        /// <summary>
        /// 取消更新
        /// </summary>
        public void CancelUpdate()
        {
            if (_downloadCancellation != null && !_downloadCancellation.IsCancellationRequested)
            {
                _downloadCancellation.Cancel();

                _isDownloading = false;
                _upgradeInfo.Status = UpgradeStatus.Idle;
                _upgradeInfo.Message = "更新已取消";

                Log.Information("更新已取消");
            }
            else
            {
                Log.Warning("没有正在进行的下载任务");
            }
        }
    }

}