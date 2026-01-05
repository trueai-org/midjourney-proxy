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
using Midjourney.Infrastructure.Services;
using Serilog;

namespace Midjourney.Infrastructure
{
    /// <summary>
    /// 系统配置存储（单例）。
    /// 启动时：先读取本地 LiteDB（data/mj.db）里的 Consul 连接配置（如果有），检查能否连接 Consul。
    /// 如果能连接，则从 Consul 的 KV 中加载远程配置并覆盖本地配置。
    /// 保存时：同时写到本地 LiteDB 与远程 Consul KV。
    /// </summary>
    public class SettingHelper : IDisposable
    {
        private readonly string _configPath;

        private ConsulClient _consulClient;

        /// <summary>
        /// 单例实例（要先调用 InitializeAsync）
        /// </summary>
        public static SettingHelper Instance { get; private set; }

        /// <summary>
        /// 当前生效配置缓存
        /// </summary>
        public Setting Current { get; private set; }

        private SettingHelper()
        {
            _configPath = Path.Combine(Directory.GetCurrentDirectory(), Path.Combine("data", "mj.json"));

            // 确保目录存在
            var dir = Path.GetDirectoryName(_configPath);
            Directory.CreateDirectory(dir);
        }

        /// <summary>
        /// 启动初始化（程序启动时调用一次）。
        /// 该方法会：
        /// 1) 从本地 SQLite / LiteDB 读取配置；
        /// 2) 如果本地配置包含 Consul 连接信息，尝试连接 Consul；
        /// 3) 如果 Consul 可用，尝试从远程 KV 加载设置并覆盖本地（然后保存到本地以保持同步）。
        /// </summary>
        /// <param name="logger">可选的 ILogger 实例</param>
        /// <param name="cancellation">取消令牌</param>
        public static async Task InitializeAsync()
        {
            if (Instance != null)
                return;

            var inst = new SettingHelper();

            await inst.LoadAsync();

            Instance = inst;
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
                TranslateHelper.Initialize(new GPTTranslateService());
            }
            else if (setting.TranslateWay == TranslateWay.BAIDU && !string.IsNullOrWhiteSpace(setting.BaiduTranslate?.AppSecret))
            {
                TranslateHelper.Initialize(new BaiduTranslateService());
            }
            else
            {
                TranslateHelper.Initialize(null);
            }

            // 缓存 / Redis / Reids 锁
            if (setting.IsValidRedis)
            {
                var csredis = new CSRedisClient(setting.RedisConnectionString);
                AdaptiveLock.Initialization(csredis);
                AdaptiveCache.Initialization(csredis);
            }
        }

        /// <summary>
        /// 加载远程配置，如果失败则加载本地配置
        /// </summary>
        /// <param name="cancellation"></param>
        /// <returns></returns>
        public async Task LoadAsync(CancellationToken cancellation = default)
        {
            try
            {
                Setting setting = null;
                var localJson = File.Exists(_configPath) ? await File.ReadAllTextAsync(_configPath, cancellation) : null;
                if (!string.IsNullOrWhiteSpace(localJson))
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

                // 如果本地中没有配置，则从 litedb 中加载
                if (setting == null)
                {
                    // 判断是否存在旧版 litedb 文件
                    var litedbPath = Path.Combine(Directory.GetCurrentDirectory(), Path.Combine("data", "mj.db"));
                    if (File.Exists(litedbPath))
                    {
                        var liteDb = new LiteDBRepository<Setting>("data/mj.db");

                        setting = liteDb.Get(Constants.DEFAULT_SETTING_ID);

                        // 写入本地
                        if (setting != null)
                        {
                            await File.WriteAllTextAsync(_configPath, setting.ToJson(new Newtonsoft.Json.JsonSerializerSettings()
                            {
                                NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore,
                                Formatting = Newtonsoft.Json.Formatting.Indented
                            }), cancellation);
                        }
                    }
                }

                if (setting == null)
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
                    await File.WriteAllTextAsync(_configPath, setting.ToJson(new Newtonsoft.Json.JsonSerializerSettings()
                    {
                        NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore,
                        Formatting = Newtonsoft.Json.Formatting.Indented
                    }), cancellation);
                }

                // 检查本地是否包含 Consul 连接配置
                if (setting.ConsulOptions?.IsValid == true)
                {
                    try
                    {
                        var consulConfigAddress = setting.ConsulOptions.ConsulUrl;
                        var consulToken = setting.ConsulOptions.ConsulToken;
                        var consulKvKey = setting.ConsulOptions.ServiceName + "/setting";

                        _consulClient = new ConsulClient(cfg =>
                        {
                            cfg.Address = new Uri(consulConfigAddress);
                            if (!string.IsNullOrWhiteSpace(consulToken))
                            {
                                cfg.Token = consulToken;
                            }
                        });

                        // 测试连接（获取 leader）
                        var status = await _consulClient.Status.Leader(cancellation);
                        if (!string.IsNullOrWhiteSpace(status))
                        {
                            Log.Information($"Connected to Consul at {consulConfigAddress}, leader: {status}.");

                            // 3) 尝试从 KV 加载远程配置
                            var kv = await _consulClient.KV.Get(consulKvKey, cancellation);
                            if (kv.Response != null && kv.Response.Value != null && kv.Response.Value.Length > 0)
                            {
                                var json = Encoding.UTF8.GetString(kv.Response.Value);
                                try
                                {
                                    var remoteSetting = json.ToObject<Setting>();
                                    if (remoteSetting != null)
                                    {
                                        // 更新当前设置并保存到本地（覆盖）
                                        Current = remoteSetting;

                                        UpsertLocal(Current);

                                        GlobalConfiguration.Setting = Current;

                                        Log.Information("Loaded setting from Consul KV and persisted to local LiteDB.");

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
                                Log.Information("No remote setting found in Consul KV.");
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

                        // keep using local Current
                        _consulClient = null;
                    }
                }
                else
                {
                    Log.Information("Local setting does not contain Consul connection info, skip consul load.");
                }

                // 如果没有从远程加载成功，则使用本地配置
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
        public static async Task<Setting> LoadFromConsulAsync(ConsulOptions consul, CancellationToken cancellation = default)
        {
            try
            {
                if (consul == null || !consul.IsValid)
                {
                    return null;
                }

                var consulConfigAddress = consul.ConsulUrl;
                var consulToken = consul.ConsulToken;
                var consulKvKey = consul.ServiceName + "/setting";

                var consulClient = new ConsulClient(cfg =>
                {
                    cfg.Address = new Uri(consulConfigAddress);
                    if (!string.IsNullOrWhiteSpace(consulToken))
                    {
                        cfg.Token = consulToken;
                    }
                });

                // 测试连接（获取 leader）
                var status = await consulClient.Status.Leader(cancellation);
                if (!string.IsNullOrWhiteSpace(status))
                {
                    Log.Information($"Connected to Consul at {consulConfigAddress}, leader: {status}.");

                    // 3) 尝试从 KV 加载远程配置
                    var kv = await consulClient.KV.Get(consulKvKey, cancellation);
                    if (kv.Response != null && kv.Response.Value != null && kv.Response.Value.Length > 0)
                    {
                        var json = Encoding.UTF8.GetString(kv.Response.Value);
                        try
                        {
                            var remoteSetting = json.ToObject<Setting>();
                            if (remoteSetting != null)
                            {
                                return remoteSetting;
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

            return null;
        }

        /// <summary>
        /// 同时保存到本地 LiteDB 与远程 Consul（如果可用）。
        /// </summary>
        /// <param name="setting"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public async Task SaveAsync(Setting setting)
        {
            if (setting == null)
                throw new ArgumentNullException(nameof(setting));

            // 本地写入（同步）
            try
            {
                UpsertLocal(setting);
                Current = setting;
                Log.Information("Saved setting to local LiteDB.");
            }
            catch (Exception ex)
            {
                Log.Information(ex, "Failed to save setting to local LiteDB.");
            }

            // 2) 远程写入（如果 consul client 可用 或 setting 本身包含 consul info）
            try
            {
                // 如果当前没有 _consulClient，但是配置里有 consul 信息，则尝试新建一个客户端
                if (_consulClient == null && setting.ConsulOptions?.IsValid == true)
                {
                    try
                    {
                        _consulClient = new ConsulClient(cfg =>
                        {
                            cfg.Address = new Uri(setting.ConsulOptions.ConsulUrl);

                            if (!string.IsNullOrWhiteSpace(setting.ConsulOptions.ConsulToken))
                            {
                                cfg.Token = setting.ConsulOptions.ConsulUrl;
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        Log.Information(ex, "Failed to create ConsulClient from provided setting. Skipping remote save.");
                        _consulClient = null;
                    }
                }

                if (_consulClient != null)
                {
                    // 格式化
                    var json = setting.ToJson(new Newtonsoft.Json.JsonSerializerSettings()
                    {
                        NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore,
                        Formatting = Newtonsoft.Json.Formatting.Indented
                    });

                    var bytes = Encoding.UTF8.GetBytes(json);

                    var consulKvKey = setting.ConsulOptions.ServiceName + "/setting";
                    var kv = new KVPair(consulKvKey) { Value = bytes };
                    var p = await _consulClient.KV.Put(kv).ConfigureAwait(false);
                    if (p.StatusCode == System.Net.HttpStatusCode.OK)
                    {
                        Log.Information("Saved setting to Consul KV.");
                    }
                    else
                    {
                        Log.Information("Consul KV put returned status {status}", p.StatusCode);
                    }
                }
                else
                {
                    Log.Information("Skipping remote save because no Consul client is available.");
                }
            }
            catch (Exception ex)
            {
                Log.Information(ex, "Failed to save setting to Consul KV.");
            }
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
                using var consulClient = new ConsulClient(cfg =>
                {
                    cfg.Address = new Uri(setting.ConsulOptions.ConsulUrl);
                    if (!string.IsNullOrWhiteSpace(setting.ConsulOptions.ConsulToken))
                    {
                        cfg.Token = setting.ConsulOptions.ConsulToken;
                    }
                });

                var status = await consulClient.Status.Leader(cancellation);
                return !string.IsNullOrWhiteSpace(status);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 直接将当前 Setting 写入本地 LiteDB（覆盖/插入）。
        /// </summary>
        private void UpsertLocal(Setting setting)
        {
            // 写入本地 json 文件
            File.WriteAllText(_configPath, setting.ToJson(new Newtonsoft.Json.JsonSerializerSettings()
            {
                NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore,
                Formatting = Newtonsoft.Json.Formatting.Indented
            }));
        }

        public void Dispose()
        {
            _consulClient?.Dispose();
        }
    }
}