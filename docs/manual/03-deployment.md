# 03 部署

- **适用版本**：v12.1.6
- **适用读者**：负责环境搭建与发布上线的运维人员
- **前置条件**：熟悉 Docker/Compose、端口与卷挂载、反向代理配置

本章覆盖生产环境的部署方式与常见拓扑，包括容器编排、镜像来源与端口、需要持久化的目录、反向代理、服务注册与集群部署，以及升级与 Windows IIS 场景。读者应结合[配置](04-configuration.md)完成参数落地，并在上线前执行[运维](08-operations.md)中的检查项。

## Docker Compose 部署

### Compose 文件结构

`scripts/docker-compose.yml` 同时声明主程序与可选依赖。`docker compose up -d` 会启动主程序、Redis、MySQL 与 MariaDB；MySQL 与 MariaDB 同占 3306，必须显式选择其中一个服务，或先移除另一个。

| 服务 | 镜像 | 端口 | 说明 |
| --- | --- | --- | --- |
| `mjopen` | 官方镜像 | `8086:8080` | 主程序 |
| `mjopen-redis` | `redis:6.2` | `6379:6379` | 缓存、队列、分布式锁 |
| `mjopen-mysql` | `mysql:8.4` | `3306:3306` | 默认数据库选项 |
| `mjopen-mariadb` | `mariadb:11` | `3306:3306` | 与 MySQL 二选一；两者都没有 profile，直接启动会端口冲突 |
| `mjopen-postgres` | `postgres:18-alpine` | `5432:5432` | `profiles: postgres`，默认不启动 |
| `mjopen-sqlserver` | `mssql/server:2022-latest` | `1433:1433` | `profiles: sqlserver`，默认不启动 |

MySQL 与 MariaDB 都占用宿主 `3306` 且都没有 profile，直接 `docker compose up -d` 会端口冲突；启动时显式指定其中一个服务，或注释掉另一个。示例中的密码、库名与数据目录都是模板值，上线前必须替换并妥善保管。

### 环境变量与配置文件

| 变量 | 默认 | 说明 |
| --- | --- | --- |
| `TZ` | `Asia/Shanghai` | 容器时区，影响任务时间与日志 |
| `CONCURRENT` | 空 | 单节点最大并发；空表示按 CPU 计算，`0` 与 `-1` 均按 1280 处理 |
| `HOST_IP` | 空 | 指定宿主私网 IP，用于生成内网访问地址与注册信息 |
| `KESTREL_MIN_REQUEST_BODY_BYTES_PER_SECOND` | 240 | 请求体最小速率，单位字节/秒 |
| `KESTREL_SLOW_BUFFER_SECONDS` | 5 | 低速请求允许的缓冲时间，单位秒 |
| `KESTREL_MAX_REQUEST_BODY_SIZE` | 30 | 最大请求体，单位 MB |

`appsettings.json` 只保留 `urls`、`CONCURRENT`、Serilog 等启动项；`AdminToken`、`UserToken` 可通过配置或环境变量注入。业务配置全部在 `data/mj.json`。数据库、Redis、存储等敏感值不要写进 Compose 文件提交到版本库，应通过管理后台写入并在宿主机限制目录权限。

### 启动、停止与查看状态

```bash
cd /opt/midjourney-proxy/scripts
docker compose up -d mjopen mjopen-redis mjopen-mysql
docker compose ps
docker compose logs -f mjopen
docker compose restart mjopen
docker compose down
```

需要 PostgreSQL 或 SQL Server 时显式启用 profile 并指定服务：

```bash
docker compose --profile postgres up -d mjopen mjopen-redis mjopen-postgres
docker compose --profile sqlserver up -d mjopen mjopen-redis mjopen-sqlserver
```

`docker compose down` 只删除容器与网络，宿主目录中的数据仍然保留。确认不再需要历史数据时才删除 `/root/mjopen`。

## 镜像与端口

### 镜像来源与拉取方式

| 来源 | 地址 | 适用场景 |
| --- | --- | --- |
| 阿里云 | `registry.cn-guangzhou.aliyuncs.com/trueai-org/midjourney-proxy` | 国内网络 |
| GitHub | `ghcr.io/trueai-org/midjourney-proxy` | 海外或已配置代理 |
| Docker Hub | `trueaiorg/midjourney-proxy` | 通用 |

```bash
docker pull registry.cn-guangzhou.aliyuncs.com/trueai-org/midjourney-proxy:<VERSION>
```

生产环境固定版本标签，保留上一版本镜像用于回退。容器内已包含 .NET 运行时与管理后台静态文件，不需要在宿主机额外安装运行时。

### 端口映射与监听地址

容器内固定监听 `8080`；宿主端口按需映射，示例为 `8086`。应用读取 `urls` 配置（默认 `http://*:8080`），也可以用 `ASPNETCORE_URLS` 覆盖。只允许本机反向代理访问时，把宿主端口绑定到回环地址：

```bash
-p 127.0.0.1:8086:8080
```

容器内监听端口与宿主映射必须一致，改了一边不改另一边会导致外部无法访问。

### 时区与运行用户

- 传入 `-e TZ=Asia/Shanghai`，并挂载 `/etc/localtime:ro`，保证任务时间、日志时间与业务时间一致。
- 示例使用 `--user root`，避免挂载目录权限问题。改为非 root 用户时，必须提前把 `data`、`logs`、`attachments`、`ephemeral-attachments` 的属主改为对应 UID/GID。
- 时区不一致会造成“任务时间偏差数小时”“日限额提前或延后重置”，排查时间相关问题先核对容器内 `date`。

## 目录挂载

### 数据目录

`/app/data` 是唯一必须持久化的目录，包含：

- `mj.json`：运行配置，管理后台每次保存都会原子覆盖。
- `mj_sqlite.db`：使用 SQLite 时的数据库文件，路径固定，连接串写什么都无效。

升级、迁移和重建容器前先备份该目录。SQLite 场景不要多个容器同时挂载同一个文件。

### 日志目录

`/app/logs` 存放 `log.txt` 与 `error.txt`，按天滚动，单文件上限 10MB，保留 31 天。日志不持久化不会影响运行，但排障与审计通常需要保留，建议挂载到宿主目录并纳入清理策略。

### 附件目录

- `/app/wwwroot/attachments`：生成图片、合并图片与上传文件。
- `/app/wwwroot/ephemeral-attachments`：图生文等临时文件。

本地存储模式下这两个目录就是对外的图片源，必须持久化并保证反向代理能访问 `/attachments` 与 `/ephemeral-attachments` 路径。改用对象存储时，这两个目录只作为临时落盘，仍建议挂载以避免磁盘被临时文件占满。

### 挂载权限与常见错误

| 现象 | 常见原因 | 处理 |
| --- | --- | --- |
| 启动即报写入失败 | 宿主目录属主不是容器运行用户 | `chown -R <UID>:<GID>` 后重启 |
| 升级后配置丢失 | 未挂载 `/app/data`，或挂载到了临时目录 | 补挂载并恢复 `mj.json` |
| 图片 404 | 未挂载附件目录，或反向代理未转发静态路径 | 检查挂载与代理规则 |
| SQLite 报 database is locked | 多个容器共用同一个数据库文件，或放在网络文件系统 | 改为单实例，或切换 MySQL/PostgreSQL |
| 容器时间错误 | 未设置 `TZ` 或未挂载 `/etc/localtime` | 补齐时区配置 |

## 反向代理

### Nginx 反向代理

```nginx
server {
    listen 80;
    server_name mj.example.com;

    client_max_body_size 100m;

    location / {
        proxy_pass http://127.0.0.1:8086;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_read_timeout 600s;
        proxy_send_timeout 600s;
    }
}
```

`X-Forwarded-For` 必须传递，否则限流与审计看到的都是代理 IP。静态图片路径无需单独配置，交给同一个 `location /` 即可。

### Traefik 与 YARP 方案

`scripts/traefik/` 提供 Traefik 静态配置与启动脚本，通过 Consul 服务发现自动生成路由；`scripts/yarp-proxy/` 提供基于 Consul 的 YARP 网关镜像，容器内监听 `8080`，示例映射到宿主 `8083`，健康检查路径为 `/health`。

YARP 网关按轮询（RoundRobin）策略在 Consul 中的同名服务之间负载均衡，并暴露 `/debug/routes`、`/debug/headers` 便于核对路由与真实客户端 IP。生产环境应关闭 Traefik 的 `api.insecure` 或为其增加认证。

### HTTPS 与大文件上传

- 在反向代理层终止 TLS，后端继续使用 HTTP，减少证书维护成本。
- 代理的请求体上限要和 `KESTREL_MAX_REQUEST_BODY_SIZE`（默认 30MB）匹配；代理放行更大文件但应用拒绝时，调用方会收到 413。
- 上传大图或视频时同步调整 `proxy_read_timeout`、`proxy_send_timeout`，避免代理先超时。
- 若代理改写了 `Host` 或路径前缀，确认回调地址、静态资源路径和 CORS 仍然可用。

## Consul 服务注册

### 注册参数

| 配置项 | 默认值 | 说明 |
| --- | --- | --- |
| `consulOptions.enable` | `false` | 是否启用注册与共享配置 |
| `consulOptions.consulUrl` | `http://localhost:8500` | Consul 地址 |
| `consulOptions.consulToken` | 空 | 访问令牌，按需填写 |
| `consulOptions.serviceName` | `midjourney-proxy` | 服务名，同时作为 KV 前缀 |
| `consulOptions.servicePort` | `8080` | 注册端口，需与容器内监听一致 |
| `consulOptions.healthCheckUrl` | `/health` | 健康检查路径 |
| `consulOptions.healthCheckInterval` | `10s` | 检查间隔 |
| `consulOptions.healthCheckTimeout` | `5s` | 检查超时 |
| `consulOptions.deregisterCriticalServiceAfter` | `1m` | 严重异常后自动注销时间 |
| `consulOptions.enableVersionCheck` | `true` | 与注册中心版本比对，落后时触发更新检查 |

### 健康检查与服务发现

启用 Consul 后，实例启动完成会注册服务，停止时主动注销。配置以 Consul KV 的 `<serviceName>/setting` 为期望状态源，本地 `data/mj.json` 只作为缓存和 Consul 不可用时的应急启动配置；只有管理员显式保存才会写回 Consul。

反向代理或网关通过 Consul 获取健康实例列表。健康检查失败时实例会被摘除，恢复后自动重新加入。Consul 不可用时，运行中的节点保留内存配置继续运行，但不会回退读取本地文件；重启时使用本地缓存应急。

## 集群部署

### 多实例共享存储

- 数据库：所有节点连接同一个 MySQL/PostgreSQL/SQL Server，SQLite 只适合单机。
- Redis：所有节点使用同一个 Redis 与前缀；缺省前缀相同会共享键空间，混部不同环境时必须改 `prefix`。
- 图片：使用对象存储，或共享 NFS/云盘；不要依赖单节点本地磁盘，否则换节点后图片 404。
- 配置：以 Consul 为准，禁止只改某台机器的 `data/mj.json`。

### 共享队列与数据一致性

任务、运行态计数、缓存与锁都在 Redis 中；账号连接通过分布式锁保证同一账号只在一个节点运行。管理员保存配置后，当前节点立即应用，并通过 Redis 广播让其他节点重新加载配置。

数据库中的任务状态是最终事实，Redis 只保存运行态。节点异常退出后，未完成任务由存活节点按超时与队列状态回收；因此不要手工清理 Redis 中的队列键，除非确认没有运行中的任务。

### 负载均衡策略

- 普通轮询即可，不要求会话保持；请求落到任意节点都会共享同一套任务与配置。
- 健康检查使用 `/health`，检查间隔与超时不要小于 Consul 或反向代理的最小值。
- 扩容时先把新节点加入负载均衡，确认健康后再摘除旧节点；缩容时先摘流量，等待运行中的任务结束或超时。

## 在线升级与重启

### 升级前检查

1. 备份 `data/`（至少 `mj.json` 与数据库）与上一版本镜像标签。
2. 确认数据库连接正常、Redis 可 Ping、磁盘剩余空间足够存放新镜像与日志。
3. 阅读目标版本的变更说明，确认是否有配置项改名或行为变化。
4. 在维护窗口内操作，提前通知调用方可能出现的短暂中断。

### 滚动重启

```bash
docker pull registry.cn-guangzhou.aliyuncs.com/trueai-org/midjourney-proxy:<VERSION>
docker rm -f mjopen
docker run --name mjopen -d --restart=always \
  --network mjopen-network \
  -p 127.0.0.1:8086:8080 --user root \
  -v /root/mjopen/logs:/app/logs:rw \
  -v /root/mjopen/data:/app/data:rw \
  -v /root/mjopen/attachments:/app/wwwroot/attachments:rw \
  -v /root/mjopen/ephemeral-attachments:/app/wwwroot/ephemeral-attachments:rw \
  -e TZ=Asia/Shanghai \
  registry.cn-guangzhou.aliyuncs.com/trueai-org/midjourney-proxy:<VERSION>
```

多节点场景逐台执行：摘流量 → 停旧容器 → 启新容器 → 等 `/health` 通过 → 恢复流量。`scripts/docker-upgrade.sh` 会自动创建网络、拉取镜像、删除旧容器并按固定参数重建，适合单机升级。

### 失败回退

- 新版本启动失败时，用上一版本标签重新运行同一套挂载参数，`data/` 不要删除。
- 启动时会自动建表与补列，但不提供降级脚本；回退到旧版本前先确认旧版本能兼容新表结构，必要时恢复升级前的数据库备份。
- 回退后检查账号是否重新上线、任务队列是否积压、日志是否仍有新版本相关报错。

## Windows IIS 部署

### 应用程序池与运行时要求

- 安装 .NET 8 Hosting Bundle，并重启 IIS。
- 应用程序池的 .NET CLR 版本选择“无托管代码”，托管管道模式使用“集成”。
- 启用 32 位应用程序保持关闭，除非确实部署 32 位包。
- 确认 `AspNetCoreModuleV2` 已注册，否则站点会返回 500.30 或 502.5。

### 站点绑定与权限

把免安装包解压到站点物理路径，绑定域名与端口。应用程序池标识需要对以下目录有修改权限：

- `data`：写入 `mj.json` 与 SQLite 数据库。
- `logs`：写入滚动日志。
- `wwwroot/attachments`、`wwwroot/ephemeral-attachments`：写入生成图片与临时文件。

IIS 场景由 ASP.NET Core Module 注入监听端口，不要在 `appsettings.json` 中把 `urls` 写死为与站点冲突的端口。反向代理或 CDN 回源时仍需传递 `X-Forwarded-For`。

### 作为服务或计划任务运行

不使用 IIS 时，可以用系统自带的“任务计划程序”创建基本任务：触发器选择开机或登录时，操作指向 `Midjourney.API.exe`，并在设置中勾选“如果任务已在运行，则以下规则适用：请勿启动多个实例”。工作目录必须设置为解压目录，否则 `data/` 与 `logs/` 会落到系统目录。
