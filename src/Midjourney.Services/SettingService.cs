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

using System.Text;
using Consul;
using CSRedis;
using Serilog;

namespace Midjourney.Services
{
    /// <summary>
    /// 系统配置服务（单例）。
    /// 启用 Consul 时，Consul KV 是唯一的期望状态源：启动时从远程加载配置，data/mj.json 只作为本地缓存和 Consul 不可用时的应急启动配置。
    /// 只有管理员显式保存才会写 Consul；节点启动或运行时降级状态不会写回 Consul。
    /// 保存 Consul 配置时使用 CAS + Revision，旧版本写入会失败，避免覆盖新配置。
    /// </summary>
    public class SettingService : IDisposable
    {
        // 使用 Lazy<T> 确保线程安全和延迟初始化
        // 保证静态构造函数只执行一次
        private static readonly Lazy<SettingService> _instance = new(() => new(),
            LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>
        /// 获取单例实例。
        /// </summary>
        public static SettingService Instance => _instance.Value;

        /// <summary>
        /// 配置文件路径
        /// </summary>
        private readonly string _configPath;

        /// <summary>
        /// 配置加载/保存串行锁，避免同一进程内并发重载或保存互相覆盖。
        /// </summary>
        private readonly SemaphoreSlim _settingLock = new(1, 1);

        /// <summary>
        /// 受保护的构造函数以防止外部实例化
        /// </summary>
        protected SettingService()
        {
            _configPath = Path.Combine(Directory.GetCurrentDirectory(), Path.Combine("data", "mj.json"));

            // 确保目录存在
            var dir = Path.GetDirectoryName(_configPath);
            Directory.CreateDirectory(dir);
        }

        /// <summary>
        /// 当前生效配置缓存
        /// </summary>
        public Setting Current { get; private set; }

        /// <summary>
        /// 启动初始化（程序启动时调用一次）。
        /// 该方法会：
        /// 1) 读取本地 data/mj.json 作为缓存/应急启动配置（同时提供 Consul 引导信息）；
        /// 2) 启用 Consul 时，以 Consul KV 为期望状态源，从远程加载配置并覆盖本地缓存；
        /// 3) Consul 不可用或远程配置缺失时，才使用本地缓存应急启动，绝不自动写回 Consul。
        /// </summary>
        public async Task InitAsync()
        {
            await LoadAsync();
        }

        /// <summary>
        /// 根据配置项初始化其他相关服务，例如日志等级、翻译服务、锁等
        /// </summary>
        public void ApplySettings()
        {
            var setting = Current;

            GlobalConfiguration.Setting = Current;
            GlobalConfiguration.LogLevel.MinimumLevel = setting.LogEventLevel;

            Log.Write(setting.LogEventLevel, "日志级别已设置为: {Level}", setting.LogEventLevel);

            // 存储服务
            StorageHelper.Configure();

            // 翻译服务
            if (setting.TranslateWay == TranslateWay.GPT && !string.IsNullOrWhiteSpace(setting.Openai?.GptApiKey))
            {
                GlobalConfiguration.TranslateService = new GptTranslateService();
            }
            else if (setting.TranslateWay == TranslateWay.BAIDU && !string.IsNullOrWhiteSpace(setting.BaiduTranslate?.AppSecret))
            {
                GlobalConfiguration.TranslateService = new BaiduTranslateService();
            }
            else
            {
                GlobalConfiguration.TranslateService = null;
            }

            // 应用 Redis
            ApplyRedis();
        }

        /// <summary>
        /// 应用 Redis
        /// 缓存 / Redis / Reids 锁
        /// </summary>
        public void ApplyRedis()
        {
            var setting = Current;
            if (setting.IsValidRedis)
            {
                var csredis = new CSRedisClient(setting.RedisConnectionString);
                AdaptiveLock.Initialization(csredis);
                AdaptiveCache.Initialization(csredis);
            }
        }

        /// <summary>
        /// 启动时加载配置。
        /// 本地 data/mj.json 只作为缓存和 Consul 不可用时的应急启动配置；
        /// 运行时同步请使用 <see cref="ReloadAsync"/>，避免 Consul 短暂异常时用旧缓存覆盖当前配置。
        /// </summary>
        /// <param name="cancellation">取消令牌</param>
        /// <returns>加载完成</returns>
        public async Task LoadAsync(CancellationToken cancellation = default)
        {
            await _settingLock.WaitAsync(cancellation);
            try
            {
                await LoadCoreAsync(true, cancellation);
            }
            finally
            {
                _settingLock.Release();
            }
        }

        /// <summary>
        /// 运行时重新加载配置（其他节点保存配置后触发）。
        /// 启用 Consul 时只接受 Consul 的远程配置；Consul 不可用或远程配置缺失时保留当前内存配置，
        /// 不回退到本地 data/mj.json，避免节点回滚到旧配置。
        /// </summary>
        /// <param name="cancellation">取消令牌</param>
        /// <returns>加载完成</returns>
        public async Task ReloadAsync(CancellationToken cancellation = default)
        {
            await _settingLock.WaitAsync(cancellation);
            try
            {
                await LoadCoreAsync(false, cancellation);
            }
            finally
            {
                _settingLock.Release();
            }
        }

        /// <summary>
        /// 加载配置的核心逻辑。
        /// </summary>
        /// <param name="allowLocalFallback">Consul 不可用时是否允许回退到本地缓存，仅启动时允许</param>
        /// <param name="cancellation">取消令牌</param>
        private async Task LoadCoreAsync(bool allowLocalFallback, CancellationToken cancellation)
        {
            try
            {
                Setting setting = null;
                var localJson = File.Exists(_configPath) ? await File.ReadAllTextAsync(_configPath, cancellation) : null;
                var hasLocalJson = !string.IsNullOrWhiteSpace(localJson);
                if (hasLocalJson)
                {
                    try
                    {
                        setting = localJson.ToObject<Setting>();
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Failed to deserialize local setting json file.");
                    }
                }
                var localSettingInvalid = hasLocalJson && setting == null;

                // 不再从 LiteDB 加载配置，直接使用本地 json 文件作为唯一本地配置来源，避免加载就配置导致的混乱和不一致问题。
                //// 如果本地中没有配置，则从 litedb 中加载
                //if (setting == null)
                //{
                //    // 判断是否存在旧版 litedb 文件
                //    var litedbPath = Path.Combine(Directory.GetCurrentDirectory(), Path.Combine("data", "mj.db"));
                //    if (File.Exists(litedbPath))
                //    {
                //        var liteDb = new LiteDBRepository<Setting>("data/mj.db");

                //        setting = liteDb.Get(Constants.DEFAULT_SETTING_ID);

                //        // 写入本地
                //        if (setting != null)
                //        {
                //            await File.WriteAllTextAsync(_configPath, setting.ToJson(new Newtonsoft.Json.JsonSerializerSettings()
                //            {
                //                NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore,
                //                Formatting = Newtonsoft.Json.Formatting.Indented
                //            }), cancellation);
                //        }
                //    }
                //}

                if (setting == null)
                {
                    // 运行时重载不能因为本地缓存缺失/损坏就把当前配置重置为默认值。
                    // 使用当前配置继续尝试 Consul，Consul 不可用时由下面的运行时保护保留当前配置。
                    if (!allowLocalFallback && Current != null)
                    {
                        Log.Warning("Local setting cache is missing or invalid; use current setting as base for runtime reload.");
                        setting = Current;
                    }
                    else
                    {
                        setting = new Setting
                        {
                            Id = Constants.DEFAULT_SETTING_ID,

                            EnableRegister = true,
                            EnableGuest = true,

                            RegisterUserDefaultDayLimit = -1,
                            RegisterUserDefaultCoreSize = -1,
                            RegisterUserDefaultQueueSize = -1,
                            RegisterUserDefaultTotalLimit = -1,

                            GuestDefaultDayLimit = -1,
                            GuestDefaultCoreSize = -1,
                            GuestDefaultQueueSize = -1,
                        };

                        if (localSettingInvalid)
                        {
                            // 保留损坏的缓存文件，避免覆盖后丢失 Consul 引导信息；本次以默认配置应急启动。
                            Log.Error("Local setting cache is invalid; keep the file for recovery and start with default setting.");
                        }
                        else
                        {
                            try
                            {
                                UpsertLocal(setting);
                            }
                            catch (Exception ex)
                            {
                                Log.Warning(ex, "Failed to write default local setting cache.");
                            }
                        }
                    }
                }

                // 启动时使用本地缓存中的 Consul 引导信息；运行时只使用当前生效配置，避免过期缓存把节点重新拉回旧集群。
                var consulOptions = allowLocalFallback ? setting.ConsulOptions : (Current != null ? Current.ConsulOptions : setting.ConsulOptions);

                // 检查是否包含 Consul 连接配置
                if (consulOptions?.IsValid == true)
                {
                    try
                    {
                        var consulConfigAddress = consulOptions.ConsulUrl;
                        var consulKvKey = consulOptions.ServiceName + "/setting";

                        // Consul 客户端按操作创建，避免运行时修改 Consul 地址/Token 后继续使用旧客户端。
                        using var consulClient = CreateConsulClient(consulOptions);

                        // 测试连接（获取 leader）
                        var status = await consulClient.Status.Leader(cancellation);
                        if (!string.IsNullOrWhiteSpace(status))
                        {
                            Log.Information($"Connected to Consul at {consulConfigAddress}, leader: {status}.");

                            // 从 leader 读取一致数据
                            var kv = await consulClient.KV.Get(consulKvKey, new QueryOptions { Consistency = ConsistencyMode.Consistent }, cancellation);
                            if (kv.Response != null && kv.Response.Value != null && kv.Response.Value.Length > 0)
                            {
                                var json = Encoding.UTF8.GetString(kv.Response.Value);
                                try
                                {
                                    var remoteSetting = json.ToObject<Setting>();
                                    if (remoteSetting != null)
                                    {
                                        // Consul 是期望状态源；本地文件只做缓存，缓存写入失败不能影响远程配置生效。
                                        Current = remoteSetting;

                                        try
                                        {
                                            UpsertLocal(Current);
                                            Log.Information("Loaded setting from Consul KV and cached to local file.");
                                        }
                                        catch (Exception ex)
                                        {
                                            Log.Warning(ex, "Loaded setting from Consul KV but failed to update local cache.");
                                        }

                                        GlobalConfiguration.Setting = Current;

                                        return;
                                    }
                                    else
                                    {
                                        Log.Warning("Consul KV contains invalid json for Setting.");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Log.Error(ex, "Deserialize remote setting failed.");
                                }
                            }
                            else
                            {
                                Log.Warning(allowLocalFallback
                                    ? "No remote setting found in Consul KV. Local data/mj.json will be used as emergency startup setting and will not be written back to Consul automatically."
                                    : "No remote setting found in Consul KV; keep current setting during runtime reload.");
                            }
                        }
                        else
                        {
                            Log.Warning("Connected to Consul but returned empty leader; treat as not connected.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, allowLocalFallback
                            ? "Failed to load setting from Consul. Local data/mj.json will be used as emergency startup setting and will not be written back to Consul automatically."
                            : "Failed to reload setting from Consul; keep current setting and do not fall back to local cache.");
                    }
                }
                else
                {
                    Log.Information("Local setting does not contain Consul connection info, skip consul load.");
                }

                // 运行时重载时，只要本地缓存或当前配置处于 Consul 模式，就不能用本地缓存覆盖当前配置。
                var isConsulConfigured = setting.ConsulOptions?.IsValid == true || Current?.ConsulOptions?.IsValid == true;
                if (!allowLocalFallback && isConsulConfigured && Current != null)
                {
                    Log.Warning("Consul setting was not applied during runtime reload; keep current setting and do not fall back to local cache.");
                    return;
                }

                // 如果没有从远程加载成功，则使用本地配置作为应急启动配置。
                // 这里只读取本地缓存，绝不把本地配置写回 Consul；共享配置只能由管理员显式保存。
                if (setting.ConsulOptions?.IsValid == true)
                {
                    Log.Warning("Consul 配置未加载成功，使用本地 data/mj.json 作为应急启动配置；不会自动写回 Consul。");
                }

                Current = setting;

                GlobalConfiguration.Setting = Current;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Unexpected error while loading settings.");
            }
        }

        /// <summary>
        /// 从 Consul 远程加载配置
        /// </summary>
        /// <param name="consul"></param>
        /// <param name="cancellation"></param>
        /// <returns></returns>
        public static async Task<(bool success, Setting setting)> LoadFromConsulAsync(ConsulOptions consul, CancellationToken cancellation = default)
        {
            try
            {
                if (consul == null || !consul.IsValid)
                {
                    return (false, null);
                }

                var consulConfigAddress = consul.ConsulUrl;
                var consulKvKey = consul.ServiceName + "/setting";

                using var consulClient = CreateConsulClient(consul);

                // 测试连接（获取 leader）
                var status = await consulClient.Status.Leader(cancellation);
                if (!string.IsNullOrWhiteSpace(status))
                {
                    Log.Information($"Connected to Consul at {consulConfigAddress}, leader: {status}.");

                    // 从 leader 读取一致数据，避免加载到 follower 的旧配置
                    var kv = await consulClient.KV.Get(consulKvKey, new QueryOptions { Consistency = ConsistencyMode.Consistent }, cancellation);
                    if (kv.Response != null && kv.Response.Value != null && kv.Response.Value.Length > 0)
                    {
                        var json = Encoding.UTF8.GetString(kv.Response.Value);
                        try
                        {
                            var remoteSetting = json.ToObject<Setting>();
                            if (remoteSetting != null)
                            {
                                return (true, remoteSetting);
                            }
                            else
                            {
                                Log.Warning("Consul KV contains invalid json for Setting.");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "Deserialize remote setting failed.");
                        }
                    }
                    else
                    {
                        Log.Information("No remote setting found in Consul KV.");
                        return (true, null);
                    }
                }
                else
                {
                    Log.Warning("Connected to Consul but returned empty leader; treat as not connected.");
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to connect to Consul using local consul configuration. Continue with local settings.");
            }

            return (false, null);
        }

        /// <summary>
        /// 管理员显式保存系统配置。
        /// 启用 Consul 时：先以 CAS + Revision 写入 Consul KV，成功后再更新本地缓存和 Current；
        /// 提交的 Revision 与 Consul 当前版本不一致时直接失败，避免旧配置覆盖新配置。
        /// 未启用 Consul 时：只写本地 data/mj.json，并校验 Revision。
        /// 运行时降级状态不会调用本方法持久化。
        /// 同一进程内的加载和保存串行执行，避免运行时重载覆盖管理员刚保存的配置。
        /// </summary>
        /// <param name="setting">管理员提交的新配置</param>
        /// <param name="cancellation">取消令牌</param>
        /// <returns>保存结果；失败时不会更新本地缓存或当前配置</returns>
        public async Task<Result> SaveAdminAsync(Setting setting, CancellationToken cancellation = default)
        {
            if (setting == null)
                return Result.Fail("配置不能为空");

            await _settingLock.WaitAsync(cancellation);
            try
            {
                return await SaveAdminCoreAsync(setting, cancellation);
            }
            finally
            {
                _settingLock.Release();
            }
        }

        /// <summary>
        /// 管理员保存配置的核心逻辑（调用方已持有配置锁）。
        /// </summary>
        /// <param name="setting">管理员提交的新配置</param>
        /// <param name="cancellation">取消令牌</param>
        /// <returns>保存结果</returns>
        private async Task<Result> SaveAdminCoreAsync(Setting setting, CancellationToken cancellation)
        {
            var currentConsulOptions = Current?.ConsulOptions;
            var wasConsulMode = currentConsulOptions?.IsValid == true;
            var willConsulMode = setting.ConsulOptions?.IsValid == true;

            // 单机模式：只写本地并校验 Revision。
            if (!wasConsulMode && !willConsulMode)
            {
                return SaveLocalAsAdmin(setting);
            }

            if (setting.ConsulOptions == null && wasConsulMode)
                return Result.Fail("Consul 配置不能为空");

            try
            {
                // 当前处于 Consul 模式时写回当前集群使用的 key；首次启用 Consul 时使用本次提交的配置。
                // 同一集群地址/ServiceName 下允许管理员更新 Token，使用新 Token 完成本次写入。
                var writeOptions = wasConsulMode ? currentConsulOptions : setting.ConsulOptions;
                if (wasConsulMode && setting.ConsulOptions?.IsValid == true
                    && string.Equals(setting.ConsulOptions.ConsulUrl, currentConsulOptions.ConsulUrl, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(setting.ConsulOptions.ServiceName, currentConsulOptions.ServiceName, StringComparison.Ordinal))
                {
                    writeOptions = setting.ConsulOptions;
                }

                if (writeOptions?.IsValid != true)
                    return Result.Fail("Consul 配置无效，无法保存共享配置");

                // Consul 客户端按操作创建，确保写入的是 writeOptions 指定的集群，不会复用旧地址/Token。
                using var consulClient = CreateConsulClient(writeOptions);

                var consulKvKey = writeOptions.ServiceName + "/setting";

                // CAS 前从 leader 读取一致数据，避免用 follower 的旧 ModifyIndex 做 CAS。
                var currentKv = await consulClient.KV.Get(consulKvKey, new QueryOptions { Consistency = ConsistencyMode.Consistent }, cancellation);

                Setting remoteSetting = null;
                if (currentKv.Response?.Value != null && currentKv.Response.Value.Length > 0)
                {
                    try
                    {
                        remoteSetting = Encoding.UTF8.GetString(currentKv.Response.Value).ToObject<Setting>();
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Failed to deserialize Consul setting.");
                        return Result.Fail("Consul 中的配置无法解析，请检查配置中心");
                    }
                }

                var remoteRevision = remoteSetting?.Revision ?? 0;

                // Revision 不一致直接失败；远端为 0（旧版本/首次迁移）时，只有提交版本也为 0 才允许建立版本号。
                if (remoteSetting != null && setting.Revision != remoteRevision)
                {
                    if (setting.Revision < remoteRevision)
                    {
                        return Result.Fail($"配置版本已过期（Consul 当前版本 {remoteRevision}，提交版本 {setting.Revision}），请先加载远程 Consul 配置或等待节点同步后重试");
                    }

                    return Result.Fail($"提交的配置版本 {setting.Revision} 高于 Consul 当前版本 {remoteRevision}，本地配置可能已分叉，请先重新加载远程 Consul 配置或重启节点后再保存");
                }

                setting.Revision = Math.Max(remoteRevision, setting.Revision) + 1;

                var json = setting.ToJson(new Newtonsoft.Json.JsonSerializerSettings()
                {
                    NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore,
                    Formatting = Newtonsoft.Json.Formatting.Indented
                });
                var bytes = Encoding.UTF8.GetBytes(json);

                var kv = new KVPair(consulKvKey)
                {
                    Value = bytes,
                    ModifyIndex = currentKv.Response?.ModifyIndex ?? 0
                };

                // CAS：只有 Consul 中的配置仍是我们读取到的版本时才写入，防止并发保存互相覆盖。
                var casResult = await consulClient.KV.CAS(kv, cancellation);
                if (!casResult.Response)
                {
                    return Result.Fail("配置已被其他节点修改，请先加载远程 Consul 配置或等待节点同步后重试");
                }

                // 远程成功后，本地文件只作为缓存更新；失败也不影响共享配置的正确性。
                try
                {
                    UpsertLocal(setting);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Saved setting to Consul KV but failed to update local cache.");
                }

                Current = setting;

                Log.Information("Saved setting to Consul KV with CAS, revision: {Revision}.", setting.Revision);
                return Result.Ok();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save setting to Consul KV.");
                return Result.Fail("保存 Consul 配置失败，请检查 Consul 连接后重试");
            }
        }

        /// <summary>
        /// 单机模式保存：只写本地 data/mj.json，并校验 Revision。
        /// </summary>
        /// <param name="setting">管理员提交的新配置</param>
        /// <returns>保存结果</returns>
        private Result SaveLocalAsAdmin(Setting setting)
        {
            try
            {
                var currentRevision = Current?.Revision ?? 0;
                if (setting.Revision != currentRevision)
                {
                    return Result.Fail($"配置版本不一致（当前版本 {currentRevision}，提交版本 {setting.Revision}），请刷新页面后重试");
                }

                setting.Revision = currentRevision + 1;
                UpsertLocal(setting);
                Current = setting;
                Log.Information("Saved setting to local cache, revision: {Revision}.", setting.Revision);
                return Result.Ok();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save setting to local cache.");
                return Result.Fail("保存本地配置失败");
            }
        }

        /// <summary>
        /// 创建 Consul 客户端，统一处理 Token，避免把地址误写为 Token。
        /// </summary>
        /// <param name="options">Consul 连接配置</param>
        /// <returns>Consul 客户端</returns>
        private static ConsulClient CreateConsulClient(ConsulOptions options)
        {
            if (options?.IsValid != true)
                throw new ArgumentException("Consul 配置无效", nameof(options));

            return new ConsulClient(cfg =>
            {
                cfg.Address = new Uri(options.ConsulUrl);
                if (!string.IsNullOrWhiteSpace(options.ConsulToken))
                {
                    cfg.Token = options.ConsulToken;
                }
            });
        }

        /// <summary>
        /// 验证 consul 是否可以连接
        /// </summary>
        /// <param name="setting"></param>
        /// <param name="cancellation"></param>
        /// <returns></returns>
        public async Task<bool> IsConsulAvailableAsync(Setting setting, CancellationToken cancellation = default)
        {
            if (setting?.ConsulOptions?.IsValid != true)
                return false;
            try
            {
                using var consulClient = CreateConsulClient(setting.ConsulOptions);

                var status = await consulClient.Status.Leader(cancellation);
                return !string.IsNullOrWhiteSpace(status);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 将当前 Setting 原子写入本地 data/mj.json 缓存（覆盖）。
        /// 启用 Consul 时该文件只作为缓存/应急启动配置，不作为共享配置来源。
        /// </summary>
        private void UpsertLocal(Setting setting)
        {
            var json = setting.ToJson(new Newtonsoft.Json.JsonSerializerSettings()
            {
                NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore,
                Formatting = Newtonsoft.Json.Formatting.Indented
            });

            // 先写临时文件再原子替换，避免进程异常退出时留下半截 mj.json 导致应急启动配置丢失。
            var tempPath = _configPath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _configPath, true);
        }

        public void Dispose()
        {
            // Consul 客户端按操作创建并在 using 中释放，无需释放长期持有的资源。
        }
    }
}