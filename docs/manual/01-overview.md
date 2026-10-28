# 01 产品概览

- **适用版本**：v12.1.6
- **适用读者**：所有首次接触本产品的开发者、运维人员与管理员
- **前置条件**：了解 HTTP API 与容器化部署的基本概念

本章从整体上介绍 Midjourney Proxy 是什么、可以对接哪些绘图后端、具备哪些能力，以及仓库的组成方式。读完本章后，读者应能判断自己的场景适合哪种运行形态，并知道后续应阅读哪些章节。

## 产品定位

Midjourney Proxy 是一个自托管的绘图代理服务。它把 Discord、悠船与 Midjourney 官网三种绘图后端的差异收敛到一套 HTTP API 后面：调用方只负责提交提示词、查询任务、读取结果，不需要分别处理登录态、频道绑定、消息解析和文件回收。

服务自带浏览器管理后台，用于维护账号池、查看任务、管理用户、维护违规词和修改系统配置。管理后台、绘图 API 与静态文件由同一个进程提供，容器内默认监听 8080 端口。

适合的场景：

- 团队内部统一接入绘图能力，不希望每个业务方各自维护账号与登录态。
- 需要把绘图任务纳入自己的账号池、并发、限额与审计体系。
- 需要在私有网络中部署，让数据与生成结果留在自己可控的存储里。

不适合的场景：

- 只是偶尔手动绘图；直接使用官方产品更简单。
- 没有任何可登录的绘图账号，或运行环境无法访问对应后端。

## 三种绘图后端

三类后端共用同一套提交与查询接口，差异主要在账号凭证、运行态指标和可用能力上。一个实例可以同时接入多类账号，由账号池统一调度。

### Discord 后端

通过 Discord 用户 Token 连接网关，在指定服务器与频道内与 Midjourney Bot、Niji Bot 交互。

- 必填凭证：`channelId`、`guildId`、`userToken`；`channelId` 在库内唯一。
- `privateChannelId`、`nijiBotChannelId` 用于接收 seed 私信，可以留空，启动时按配置自动获取。
- 能力最完整：绘图、变体、放大、混图、图生文、缩短、局部重绘、弹窗操作和视频。
- 运行态依赖 Discord 登录态与网络质量，可能触发 Cloudflare 人机验证，需要人工或验证器介入。

### 悠船后端

通过虚拟号与虚拟 SmsCode/密码登录悠船接口，不经过 Discord。

- 账号由 `loginAccount` 与 `loginPassword` 标识；纯数字的 `loginPassword` 按短信验证码处理，非纯数字按密码处理。
- `channelId`、`guildId` 由系统自动生成，不对应真实 Discord 频道，因此不会占用 Discord 频道唯一性。
- 支持 Excel 批量导入、慢速额度、套餐到期时间与快速时长等运行态指标。
- 适合批量接入、按慢速额度控量的场景。

### Midjourney 官网后端

直接调用 Midjourney 官网接口，凭证优先级为 Cookie 高于 UserToken，也可以使用账号、密码与 2FA 自动登录。

- `cookie` 适合已有浏览器登录态的场景；没有 Cookie 时使用 `userToken`。
- `loginAccount`、`loginPassword`、`login2fa` 可配合自动登录使用，登录结果通过回调写回账号。
- `channelId`、`guildId` 自动生成；账号固定开启 MJ 与 Niji 绘图，并关闭 shorten。

## 能力矩阵

### 接口能力

| 能力 | 入口 | 说明 |
| --- | --- | --- |
| 绘图 | `POST /mj/submit/imagine` | 提交提示词，返回任务 ID 或排队结果 |
| 按钮操作 | `POST /mj/submit/action` | 变体、放大、重绘等，按 `customId` 定位按钮 |
| 图生文 | `POST /mj/submit/describe` | 上传图片生成提示词 |
| 提示词缩短 | `POST /mj/submit/shorten` | 只返回缩短后的提示词 |
| 混图 | `POST /mj/submit/blend` | 多图混合，可指定方向比例 |
| 弹窗与编辑 | `POST /mj/submit/modal`、`/edit`、`/retexture` | 处理弹窗、局部编辑与重绘 |
| 视频 | `POST /mj/submit/video` | 需开启 `enableVideo` |
| 任务查询 | `GET /mj/task/{id}/fetch` | 查询状态、进度、图片与按钮 |
| 任务列表 | `GET /mj/task/list` | 返回最近 100 条，不支持条件/分页；按条件查询请使用 `list-by-ids` |
| 任务取消 | `POST /mj/task/{id}/cancel` | 未完成的任务可取消 |
| 换脸 | `POST /mj/insight-face/swap` | 基于外部换脸服务 |
| 视频换脸 | `POST /mj/insight-face/video-swap` | 基于外部换脸服务 |
| 速度前缀 | `/mj-fast`、`/mj-turbo`、`/mj-relax` | 加速前缀，会强制指定对应速度模式，不等价于 `/mj` |

### 管理能力

- 账号池：新增、编辑、启用、禁用、重连、同步信息与设置、删除。
- 批量导入：悠船账号 Excel 导入，支持预览、部分成功与失败原因回显。
- 任务管理：按用户、状态、时间与关键词查询任务，查看结果与失败原因。
- 用户管理：角色、状态、日限额、总限额、并发与队列。
- 违规词：维护词库，命中后按配置拦截、清理或限流。
- 系统设置：在线修改配置并广播到其他节点；高风险配置有明确的生效条件。
- 探针与日志：查看运行信息和日志级别，便于定位问题。

### 部署与运行能力

| 能力 | 说明 |
| --- | --- |
| 免安装包 | Windows、Linux、macOS 解压即可运行 |
| 容器化 | 官方镜像，提供 Compose 模板与一键升级脚本 |
| 数据库 | SQLite 默认，可切换 MySQL、PostgreSQL、SQL Server |
| 缓存与队列 | Redis 必配；提交链路强依赖 Redis，不可用时提交会直接失败；仅本地缓存可降级 |
| 图片存储 | 不存储、本地、OSS、COS、R2、S3 兼容存储 |
| 服务注册 | Consul 注册与共享配置，支持多节点 |
| 反向代理 | Nginx、Traefik、YARP 网关均可前置 |
| 访问控制 | 管理员令牌、访客开关、IP 限流、违规词限流 |
| 通知 | 任务回调、SMTP 邮件、预警通知 |
| 可观测 | 健康检查、分级日志、首页运行统计 |

## 运行形态

### 免安装包单机运行

下载对应平台的压缩包，解压后直接运行可执行文件。默认地址为 `http://localhost:8080`，数据、日志与静态文件都写在当前目录下。适合单机试用、内网小规模使用和 Windows IIS 托管。

### 容器化运行

使用官方镜像与宿主目录挂载启动，数据与日志落在宿主目录，容器可以随时删除重建。适合绝大多数生产部署；需要数据库或 Redis 时，可一并启动 Compose 中的依赖服务。

### 集群运行

多个实例共享同一套数据库与 Redis，通过 Consul 注册与发现，配置以 Consul KV 为期望状态源。任务、缓存与锁通过 Redis 协调，账号连接由分布式锁保证同一账号只在一个节点上运行。集群部署的注意事项见[部署](03-deployment.md)与[配置](04-configuration.md)。

## 仓库结构

### 解决方案与源码目录

```text
midjourney-proxy/
├── Midjourney.sln
├── src/
│   ├── Midjourney.API/            # Web 入口：控制器、中间件、管理端、静态站点
│   ├── Midjourney.Services/       # 任务调度、账号服务、消息处理、规则与通知
│   ├── Midjourney.Base/           # 模型、DTO、存储、数据库、工具与全局配置
│   ├── Midjourney.Tests/          # 单元与集成测试
│   ├── Midjourney.Captcha.API/    # 人机验证与登录辅助服务
│   └── Midjourney.YarpProxy/      # 基于 Consul 的 YARP 网关
├── scripts/                       # Docker Compose、升级与安装脚本
└── docs/                          # 使用文档与本手册
```

### 文档与脚本目录

| 路径 | 用途 |
| --- | --- |
| `docs/install.md` | 安装入口与历史安装说明 |
| `docs/api.md` | 接口说明 |
| `docs/manual/` | 本手册，按部署、配置、账号、接口、运维拆分 |
| `scripts/docker-compose.yml` | 主程序与 Redis、MySQL、MariaDB、PostgreSQL、SQL Server 的编排模板 |
| `scripts/docker-upgrade.sh` | 拉取镜像、重建容器的一键升级脚本 |
| `scripts/linux_install.sh` | Linux 交互式安装菜单 |
| `scripts/run_app.sh`、`scripts/run_app_osx.sh` | Linux 与 macOS 免安装包启动脚本 |
| `scripts/traefik/`、`scripts/yarp-proxy/` | 反向代理与网关示例 |

### 构建产物与数据目录

| 路径 | 说明 |
| --- | --- |
| `data/mj.json` | 运行配置；容器内为 `/app/data/mj.json` |
| `data/mj_sqlite.db` | 使用 SQLite 时的数据库文件，路径固定 |
| `logs/log.txt` | 按天滚动的主日志，单文件上限 10MB，保留 31 天 |
| `logs/error.txt` | 仅错误级别日志，滚动策略同上 |
| `wwwroot/` | 管理后台静态文件与图片访问根目录 |
| `wwwroot/attachments/` | 生成图片、合并图片与上传文件的落盘目录 |
| `wwwroot/ephemeral-attachments/` | 图生文等临时文件的落盘目录 |

后续建议按“首次部署 → 配置 → 账号 → 接口 → 运维”的顺序阅读：[快速开始](02-quick-start.md)、[部署](03-deployment.md)、[配置](04-configuration.md)、[账号管理](05-accounts.md)、[API 参考](06-api-reference.md)。
