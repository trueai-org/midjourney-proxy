# 04 配置

- **适用版本**：v12.1.6
- **适用读者**：负责实例配置的管理员与运维人员
- **前置条件**：已完成基础部署，能访问管理后台与配置文件所在目录

本章说明配置从哪里来、优先级如何，以及各配置域的作用范围。所有示例只使用占位符，实际部署时应通过环境变量、配置文件或管理后台注入真实值，不要将其写入版本库。

## 配置来源与优先级

### 配置文件与环境变量

| 来源 | 位置 | 内容 | 优先级 |
| --- | --- | --- | --- |
| Consul KV | `<serviceName>/setting` | 完整运行配置 | 最高（启用且可连通时） |
| 本地配置文件 | `{cwd}/data/mj.json`，容器内 `/app/data/mj.json` | 完整运行配置的本地缓存 | 次高（Consul 不可用时的应急配置） |
| `appsettings.json` | 应用目录 | 启动参数与少量引导配置 | 最低 |

`data/mj.json` 是业务配置的唯一本地落盘位置，管理后台每次保存都会原子覆盖该文件。启用 Consul 后，Consul 是期望状态源，本地文件只作为缓存与应急启动配置；节点启动或运行时降级不会把本地配置写回 Consul。

`appsettings.json` 只保留 `urls`、`CONCURRENT`、Serilog 等启动项；`AdminToken`、`UserToken` 可通过配置或环境变量注入。业务配置全部在 `data/mj.json`。管理员与普通用户令牌只影响首次创建，之后以数据库为准；日志输出目标与滚动策略在当前版本中由代码固定。

常用环境变量：`TZ`、`ASPNETCORE_URLS`、`CONCURRENT`、`HOST_IP`、`KESTREL_MIN_REQUEST_BODY_BYTES_PER_SECOND`（默认 240）、`KESTREL_SLOW_BUFFER_SECONDS`（默认 5）、`KESTREL_MAX_REQUEST_BODY_SIZE`（默认 30，单位 MB）。它们分别控制时区、监听地址、节点并发、内网地址判断与上传限速。

其它常用全局开关：

| 配置项 | 默认值 | 说明 |
| --- | --- | --- |
| `accountChooseRule` | `BestWaitIdle` | 账号选择规则，启动时注册，修改后需重启 |
| `enableDiscordAppendSeed` / `enableFillOfficialCdn` | `true` / `false` | 自动追加 `--seed` / 补全官方 CDN 地址 |
| `enableAutoSyncInfoSetting` / `enableUpdateCheck` | `true` / `true` | 普通情况 3–6 小时同步账号信息，快速额度耗尽后 6–12 小时 / 每 6 小时检查更新 |
| `logEventLevel` | `Information` | 日志级别，保存后立即生效 |

Consul 默认关闭；启用时默认地址 `http://localhost:8500`、服务名 `midjourney-proxy`、注册端口 `8080`、健康检查 `/health`、检查间隔 10 秒、超时 5 秒，详细参数见[部署](03-deployment.md)。

### 命令行参数

命令行参数优先级高于环境变量与 `appsettings.json`。启动时可覆盖监听地址与并发：

```bash
dotnet Midjourney.API.dll --urls "http://0.0.0.0:9000" --CONCURRENT 20
```

`--CONCURRENT` 也可写成 `--concurrent`，配置键不区分大小写。业务配置不能通过命令行传入，必须写入 `data/mj.json` 或 Consul。

### 管理后台在线配置

管理后台保存配置调用 `POST /mj/admin/setting`，请求体是完整的 `Setting` 对象，必须携带当前 `revision`。保存前后会做以下校验：

1. Redis 可 Ping；`enableRedis` 为假或连接串为空时直接返回“必须配置 Redis 模式”。
2. 数据库可连接；连接失败时不保存。
3. `revision` 与当前版本一致；不一致时拒绝写入，避免旧页面覆盖新配置。

保存成功后当前节点立即应用，并通过 Redis 广播让其他节点重新加载。启用 Consul 且远端已有配置时使用 CAS 写入，提交版本落后或高于远端版本都会失败，需要刷新页面后重试。

### 配置生效方式与重启要求

| 生效方式 | 配置项 | 说明 |
| --- | --- | --- |
| 立即生效 | `logEventLevel`、`imageStorageType`、`translateWay`、`enableRedis`、存储参数 | 保存后当前节点立即应用，其他节点收到广播后应用 |
| 重启生效 | `databaseType`、`databaseConnectionString`、`accountChooseRule` | 数据库连接与选号规则在启动时初始化 |
| 重连生效 | 账号池参数（并发、队列、速度模式、工作时间）、代理参数 | 修改后需重连账号（或重启进程），队列容量等硬限制才完全生效 |
| 下次请求生效 | 限流规则、违规词规则、翻译开关 | 每次请求实时读取当前配置 |

代理参数需账号重连或重启后生效。

## 数据库配置

### 内置 SQLite

`databaseType` 默认是 `NONE`（0），启动时会按 SQLite 处理。SQLite 文件固定在 `{cwd}/data/mj_sqlite.db`，`databaseConnectionString` 写什么都无效。

| `databaseType` | 含义 | 启动行为 |
| --- | --- | --- |
| `0/1/2` | 历史枚举值 | 启动时按 SQLite 处理，仅用于兼容旧配置 |
| 3 `SQLite` | 默认 | 使用 `data/mj_sqlite.db` |
| 4 `MySQL` | 生产常用 | 使用 `databaseConnectionString` |
| 5 `PostgreSQL` | 生产可用 | 需先执行 `CREATE EXTENSION hstore;` |
| 6 `SQLServer` | 生产可用 | 使用 `databaseConnectionString` |

启动时会自动建表与补列，没有独立的迁移工具。SQLite 只适合单实例；多节点必须使用共享的 MySQL、PostgreSQL 或 SQL Server。

### 连接参数与重试

历史数据请先迁移到关系型数据库，再切换 `databaseType`。

连接串示例（值均为占位符）：

```text
MySQL:      Data Source=<DB_HOST>;Port=3306;User ID=<DB_USER>;Password=<YOUR_PASSWORD>;Initial Catalog=mj;SslMode=none;Min pool size=1
PostgreSQL: Host=<DB_HOST>;Port=5432;Username=<DB_USER>;Password=<YOUR_PASSWORD>;Database=mjopen;Pooling=true;Minimum Pool Size=1
SQLServer:  Data Source=<DB_HOST>;User Id=<DB_USER>;Password=<YOUR_PASSWORD>;Initial Catalog=mjopen;Encrypt=True;TrustServerCertificate=True;Pooling=true
```

启动连接失败会按 10 秒、60 秒、360 秒重试三次，仍失败则运行时降级为 SQLite。该降级只影响当前进程内存，不会写回配置；数据库恢复后必须重启才能重新连上原数据库。

### 历史数据迁移

需要迁移时设置 `isAutoMigrate=true`、`migrateDatabaseType` 与 `migrateDatabaseConnectionString`，启动时执行迁移，完成后关闭开关。目标数据库不能与当前数据库相同，否则保存配置时会被拒绝。迁移前备份源库与 `data/`，迁移期间停止写入；迁移只做正向复制，没有降级脚本，回退需要恢复备份。

任务表会按 `maxCount` 限制总量，默认 `500000`。超过上限且多出至少 1000 条时，后台按提交时间删除最早的任务，每次最多删 1000 条。日志与附件不受该限制，需要单独规划清理。

## Redis 配置

### 连接参数

`enableRedis` 出厂默认可能为 `false`，但生产环境必须开启；`redisConnectionString` 为空或连接失败时，启动会降级为内存模式，任务提交等强依赖 Redis 的路径会直接失败。示例连接串：

```text
<REDIS_HOST>:6379,password=<YOUR_PASSWORD>,defaultDatabase=1,prefix=mjopen:,poolsize=50,timeout=5000
```

`prefix` 用于隔离键空间，同一 Redis 上部署多套环境时必须使用不同前缀。生产环境必须配置 Redis：任务队列、运行态计数、缓存、分布式锁与节点广播都依赖它。

### 队列相关配置

Redis 承载任务队列与通知通道，但全局队列容量没有单独配置项，实际容量由账号池的 `coreSize`、`queueSize`、`relaxCoreSize`、`relaxQueueSize` 决定，详见[账号管理](05-accounts.md)。修改这些参数后，正在运行的账号需要重连才能让队列容量完全生效。

### 不可用时的降级行为

启动时会 `Ping` Redis；失败则自动把 `enableRedis` 置为 `false`，该状态只保存在内存，不会写回配置或 Consul。降级后：

- 缓存与分布式锁退化为单机实现，多节点之间不再协调。
- 任务队列与运行态计数无法跨节点共享，可能出现重复执行或统计偏差；任务提交等强依赖 Redis 的路径会直接失败。
- 管理后台保存配置会直接失败，因为保存前要求 Redis 可用。

Redis 恢复后重启进程即可重新启用；不要在降级状态下继续运行多节点集群。

## 存储配置

### 本地文件存储

| 配置项 | 默认值 | 说明 |
| --- | --- | --- |
| `imageStorageType` | `NONE` | `NONE`、`LOCAL`、`OSS`、`COS`、`R2`、`S3` |
| `enableSaveGeneratedImage` | `true` | 保存最终生成图片 |
| `enableSaveIntermediateImage` | `false` | 保存进度中间图，会显著增加存储量 |
| `enableConvertPromptLinkStorage` | `false` | 把提示词中的链接转存到配置的存储 |
| `enablePromptImageWebp` | `false` | 上传前把常见图片格式转为 WebP |

`LOCAL` 模式把文件写到 `{cwd}/wwwroot`，生成图片的键以 `attachments/` 开头，对外路径为 `/attachments/...`。`localStorage.customCdn` 是公网访问前缀（未配置时返回相对路径），`partnerCdn` 是合作商加速域名，`globalCustomCdn` 与 `globalImageStyle` 用于境外拉图与对应样式。

本地存储依赖单机磁盘，多节点部署必须使用共享存储或对象存储。

### 对象存储接入

| 类型 | 主要配置项 |
| --- | --- |
| `OSS` | `aliyunOss.bucketName`、`accessKeyId`、`accessKeySecret`、`endpoint`、`customCdn` |
| `COS` | `tencentCos.appId`、`secretId`、`secretKey`、`region`、`bucket`、`customCdn` |
| `R2` | `cloudflareR2.accountId`、`accessKey`、`secretKey`、`bucket`、`customCdn` |
| `S3` | `s3Storage.endpoint`、`accessKey`、`secretKey`、`bucket`、`region`、`useHttps`、`forcePathStyle` |

S3 兼容 MinIO 时通常需要 `forcePathStyle=true`。`expiredMinutes` 控制链接有效期，0 表示不使用过期时间；S3 还可开启 `enablePresignedUrl` 生成预签名链接。切换存储类型后，历史图片仍指向旧地址，需要自行迁移或保留旧存储可读。

### 自定义 CDN 与图片处理

| 配置项 | 默认值 | 说明 |
| --- | --- | --- |
| `enableFillOfficialCdn` | `false` | 完成时补全官方 CDN 地址 |
| `enableVideo` | `true` | 开启视频生成 |
| `enableVideoMerge` | `true` | 把视频帧合并为 WebP 预览 |
| `videoMergeQuality` | `75` | 合并质量，取值 0 至 100 |
| `enableYouChuanInternalDownload` | `false` | 悠船部署在云厂商同地域时走内网下载 |

各存储的 `imageStyle`、`thumbnailImageStyle`、`videoSnapshotStyle` 会拼接到返回地址上，用于缩略图与视频截帧。修改样式后只影响新返回的地址，已入库的地址不会自动更新。

## 代理配置

### 出站代理

`proxy.host` 为空表示不使用代理，`proxy.port` 为空时按 80 处理。代理用于访问 Discord 与下载远端图片，当前只支持无认证的 HTTP 代理。

`ngDiscord` 用于反代 Discord 固定域名：`server` 对应 `discord.com`，`cdn` 对应 `cdn.discordapp.com`，`wss` 对应 `gateway.discord.gg`，`resumeWss` 用于恢复连接，`uploadServer` 对应附件上传域名。

### 超时与重试

- 下载与代理 HTTP 客户端的超时在代码中固定为 10 分钟，没有独立配置项。
- 单任务超时由账号的 `timeoutMinutes` 控制，默认 5 分钟，见[账号管理](05-accounts.md)。
- 悠船可开启 `enableYouChuanRetry` 在失败后自动重试，`enableYouChuanRestartContinue` 在进程重启后继续未完成任务。
- 全局并发耗尽时任务会排队而不是立即失败，排队上限仍受账号队列约束。

## 翻译配置

### 翻译服务接入

| 配置项 | 默认值 | 说明 |
| --- | --- | --- |
| `translateWay` | `NULL` | `NULL` 不翻译、`BAIDU` 百度翻译、`GPT` GPT 翻译 |
| `baiduTranslate.appid` / `appSecret` | 空 | 百度翻译凭证 |
| `openai.gptApiUrl` / `gptApiKey` | 空 | 兼容 OpenAI 的接口地址与密钥 |
| `openai.model` | `gpt-4o-mini` | 使用的模型 |
| `openai.timeout` | `30s` | 单次翻译超时 |

`openai.maxTokens` 默认 `2048`，`openai.temperature` 默认 `0`。`translateWay` 为 `NULL` 时，即使翻译开关为真也不会翻译；选择 `BAIDU` 或 `GPT` 时，对应密钥必须非空，否则翻译服务不会初始化。

### 语言与启用范围

| 配置项 | 默认值 | 说明 |
| --- | --- | --- |
| `enableMjTranslate` | `true` | 是否翻译 Midjourney 提示词 |
| `enableNijiTranslate` | `true` | 是否翻译 Niji 提示词 |
| `enableConvertNijiToMj` | `false` | 把 Niji 任务转为 Midjourney 并追加 `--niji` |
| `enableConvertNijiToNijiBot` | `false` | 提示词含 `--niji` 时转为 Niji Bot 任务 |

翻译只处理提交时的提示词，不改变已入库任务的历史文本。转换开关会改变任务实际提交的 Bot 类型，排查“出图风格不一致”时先确认这两个开关。

## 通知配置

### 通知渠道

| 配置项 | 默认值 | 说明 |
| --- | --- | --- |
| `notifyHook` | 空 | 任务状态变更回调地址，为空不回调 |
| `notifyPoolSize` | `10` | 回调线程池大小 |
| `smtp.host` / `smtp.port` / `smtp.enableSsl` | 空 / `465` / `true` | SMTP 服务器、端口与 SSL |
| `smtp.fromName` / `fromEmail` / `fromPassword` | 空 | 发件人昵称、地址与密码或应用专用密码 |
| `smtp.to` | 空 | 默认收件人 |

SMTP 除发送通知外，也是自助注册的必要条件：`smtp.fromPassword` 为空时注册接口直接返回“注册已关闭”。

### 事件与触发条件

预警通知使用独立的 `alertNotify` 配置：

| 配置项 | 默认值 | 说明 |
| --- | --- | --- |
| `alertNotify.enable` | `false` | 是否启用预警 |
| `alertNotify.notifyHook` | 空 | 预警回调地址 |
| `alertNotify.cooldownMinutes` | `30` | 同一预警的冷却时间，单位分钟 |
| `alertNotify.fastRemaining` | `-1` | 快速剩余阈值，-1 不判断 |
| `alertNotify.relaxedRemaining` | `-1` | 慢速剩余阈值，-1 不判断 |
| `alertNotify.warningCount` | `0` | 警告数阈值 |
| `alertNotify.errorCount` | `0` | 错误数阈值 |
| `alertNotify.availableAccountCount` | `0` | 可用账号数阈值 |

任务回调按任务状态变化触发，回调内容与签名规则见[API 参考](06-api-reference.md)。回调失败不会回滚任务，但可能造成业务侧状态不同步，建议回调接口做幂等处理。

## 限流配置

### 用户级限流

| 配置项 | 默认值 | 说明 |
| --- | --- | --- |
| `ipRateLimiting.enable` | `false` | 启用 IP 限流，触发返回 429 |
| `ipRateLimiting.whitelist` | 空 | 白名单 IP 或 CIDR |
| `ipRateLimiting.blacklist` | 空 | 黑名单 IP 或 CIDR，直接返回 403 |
| `ipRateLimiting.ipRules` | 空 | 按路径模式限流；内层键为窗口秒数，值为窗口内最大请求数 |
| `ipRateLimiting.ip24Rules` | 空 | 同上，作用于 `/24` 网段 |
| `ipRateLimiting.ip16Rules` | 空 | 同上，作用于 `/16` 网段 |
| `ipBlackRateLimiting.enable` | `false` | 启用黑名单限流，触发返回 403 |
| `ipBlackRateLimiting.blockTime` | `1440` | 触发后的封锁时长，单位分钟 |

`ipRules` 的键是路径通配模式（如 `*/mj/*`），不是具体 IP；IP 与网段由白名单、黑名单控制。管理员、白名单用户与匿名接口不受限流影响，计数按路径模式分别累计。

### 全局并发限制

`CONCURRENT` 控制单节点同时处理的任务数：

- 未配置（`null`）：按 `CPU 核数 × 64` 计算，并夹在 1 至 1280 之间。
- `0` 与 `-1`：均按 1280 处理，不能用于停止任务。
- 正整数：按指定值处理，最大 1280。

后台执行器的队列容量为并发的 2 倍（最小 32、最大 1280），最大并发为并发的 1.2 倍（最小 32、最大 1280）。该限制是节点级的，集群总并发等于各节点之和。

### 违规词条触发后的限制

| 配置项 | 默认值 | 说明 |
| --- | --- | --- |
| `bannedLimiting.enable` | `false` | 是否启用违规词限流 |
| `bannedLimiting.rules` | 空 | 键为当日触发次数，值为封锁分钟数 |

触发后按登录用户 ID 或访客 IP 累计当日次数，命中规则后临时封锁；`rules` 的键是当日触发次数，值是封锁分钟数。违规词库本身在管理后台维护，命中后的拦截与清理行为由 `enableAutoCollectOfficialBannedWords`、`enableAutoClearUserBannedWords` 控制。

## 演示模式

### 演示模式的作用范围

`isDemoMode` 默认 `false`，`enableSwagger` 默认 `false`。开启演示模式后：

- 全站跳过鉴权，未携带令牌也能访问绘图接口。
- 管理员登录允许空令牌，直接返回空 `apiSecret`。
- Swagger 自动暴露，便于在线试用。

演示模式下的管理写操作大多会返回“演示模式，禁止操作”，但读接口与绘图接口仍然开放，不能作为生产环境的免登录方案。

### 开启与关闭的注意事项

- 只在公开演示环境短时开启，演示结束立即关闭。
- 关闭后所有调用方必须携带有效令牌，提前通知业务方。
- 演示模式与 `enableSwagger=false` 同时存在时，Swagger 仍会因演示模式而暴露。

## 注册与访客

### 用户注册开关

`enableRegister` 在首次启动生成默认配置时为 `true`，之后以保存值为准。注册还要求 SMTP 可用。新用户默认日上限 `registerUserDefaultDayLimit`、总上限 `registerUserDefaultTotalLimit`、并发 `registerUserDefaultCoreSize`、队列 `registerUserDefaultQueueSize` 均为 `-1`，表示不限制或使用全局默认。同一 IP 每天只能注册一个账号，私有化部署应关闭注册。

### 访客访问开关

`enableGuest` 在首次启动生成默认配置时为 `true`。开启后，未携带令牌的请求可以调用绘图接口；关闭后返回 401。访客默认额度由 `guestDefaultDayLimit`、`guestDefaultCoreSize`、`guestDefaultQueueSize` 控制，默认均为 `-1`。

### 私有化部署建议

| 配置项 | 建议值 |
| --- | --- |
| `isDemoMode` | `false` |
| `enableRegister` | `false` |
| `enableGuest` | `false` |
| `enableSwagger` | `false` |
| `ipRateLimiting.enable` | `true`，并按业务配置规则 |
| `enableRedis` | `true` |

同时设置强管理员令牌、限制管理端来源 IP，并定期检查账号池与任务列表中的异常调用。
