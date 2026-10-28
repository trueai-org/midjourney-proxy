# 02 快速开始

- **适用版本**：v12.1.6
- **适用读者**：第一次部署本服务的使用者
- **前置条件**：已准备好一台可运行 Docker 或 .NET 8 的主机，并开放所需端口

本章给出从零到可用的最短路径：选择免安装包或 Docker 启动服务，完成首次登录与必要的安全初始化，最后用一次最小请求确认服务可用。每一步只保留必要操作，详细参数与扩展场景请转到[部署](03-deployment.md)与[配置](04-configuration.md)章节。

## 免安装包启动

### Windows 免安装包

下载 `midjourney-proxy-win-x64.zip` 并解压到一个有写权限的目录，然后双击 `Midjourney.API.exe`，或在 PowerShell 中执行：

```powershell
.\Midjourney.API.exe
```

启动后浏览器访问 `http://localhost:8080`。首次启动会在当前目录创建 `data/`、`logs/` 与 `wwwroot/`；如果目录位于 `Program Files` 等受保护位置，请改用普通用户目录，避免写入失败。

### Linux 与 macOS 免安装包

Linux：

```bash
tar -xzf midjourney-proxy-linux-x64-<VERSION>.tar.gz
cd midjourney-proxy-linux-x64-<VERSION>
sh run_app.sh
```

macOS：

```bash
tar -xzf midjourney-proxy-osx-x64-<VERSION>.tar.gz
cd midjourney-proxy-osx-x64-<VERSION>
sh run_app_osx.sh
```

`run_app_osx.sh` 会先移除隔离属性并赋予执行权限；如果系统仍阻止运行，请在“系统设置 → 隐私与安全性”中允许该程序。

### 启动脚本与后台运行

两个脚本只启动同目录下的 `Midjourney.API`，不会改变工作目录。需要后台常驻时，先进入应用目录再用 `nohup` 启动：

```bash
cd /opt/midjourney-proxy
nohup ./run_app.sh > app.log 2>&1 &
```

进程重启后配置与数据仍然保留，因为它们都落在应用目录下的 `data/`；不要把 `data/` 放到临时目录，否则升级或重启会丢失配置和账号。

## Docker 启动

### 一键安装与升级脚本

推荐使用仓库提供的升级脚本，它会创建网络、拉取镜像、删除旧容器并重新启动：

```bash
wget -O docker-upgrade.sh https://raw.githubusercontent.com/trueai-org/midjourney-proxy/main/scripts/docker-upgrade.sh
bash docker-upgrade.sh
```

以后升级只需再次执行 `bash docker-upgrade.sh`。脚本默认把宿主 `8086` 映射到容器 `8080`，并把数据、日志与附件挂载到 `/root/mjopen` 下；首次执行前可以先编辑脚本中的端口、路径和镜像地址。Linux 也可以使用交互式安装脚本：

```bash
wget -N --no-check-certificate https://raw.githubusercontent.com/trueai-org/midjourney-proxy/main/scripts/linux_install.sh
chmod +x linux_install.sh
bash linux_install.sh
```

### 手动 docker run 启动

```bash
docker pull registry.cn-guangzhou.aliyuncs.com/trueai-org/midjourney-proxy:latest
docker run --name mjopen -d --restart=always \
  -p 8086:8080 --user root \
  -v /root/mjopen/logs:/app/logs:rw \
  -v /root/mjopen/data:/app/data:rw \
  -v /root/mjopen/attachments:/app/wwwroot/attachments:rw \
  -v /root/mjopen/ephemeral-attachments:/app/wwwroot/ephemeral-attachments:rw \
  -e TZ=Asia/Shanghai \
  -e CONCURRENT=10 \
  -v /etc/localtime:/etc/localtime:ro \
  registry.cn-guangzhou.aliyuncs.com/trueai-org/midjourney-proxy:latest
```

国内环境优先使用上面的阿里云镜像；也可以换成 `ghcr.io/trueai-org/midjourney-proxy` 或 `trueaiorg/midjourney-proxy`。生产环境建议固定版本标签，不要长期使用 `latest`。

### 目录挂载与端口映射

| 宿主目录 | 容器目录 | 用途 |
| --- | --- | --- |
| `/root/mjopen/data` | `/app/data` | 配置 `mj.json` 与 SQLite 数据库，必须持久化 |
| `/root/mjopen/logs` | `/app/logs` | 运行日志与错误日志 |
| `/root/mjopen/attachments` | `/app/wwwroot/attachments` | 生成图片、合并图片与上传文件 |
| `/root/mjopen/ephemeral-attachments` | `/app/wwwroot/ephemeral-attachments` | 图生文等临时文件 |

容器内固定监听 `8080`。宿主端口可自行调整，例如 `-p 9000:8080`；如果前面还有反向代理，建议只监听内网地址，例如 `-p 127.0.0.1:8086:8080`。

## 首次登录与安全初始化

### 默认管理员凭据与首次登录

首次启动会创建管理员账号，令牌取自 `AdminToken`。部署时必须在首次启动前注入强令牌；若未注入，首次登录后立即重置，并确认旧令牌失效。

在登录页输入令牌，或直接调用登录接口：

```bash
curl -X POST "http://<YOUR_HOST>:8086/mj/admin/login" \
  -H "Content-Type: application/json" \
  -d '"<YOUR_TOKEN>"'
```

成功后返回 `apiSecret`。管理后台会把它保存为 `mj-api-secret` 请求头；手工调用时也可以使用 `Authorization: <YOUR_TOKEN>`，两种方式等价。

### 修改默认管理员凭据

登录后立即在管理后台的用户管理中重置管理员令牌，并重新登录确认新令牌生效。管理员令牌存放在数据库中，`AdminToken` 配置只影响首次创建管理员时使用；之后修改配置文件不会覆盖数据库中的值。

### 关闭演示模式与开放注册

私有化部署必须完成以下三项，避免接口被匿名滥用：

| 配置项 | 目标值 | 作用 |
| --- | --- | --- |
| `isDemoMode` | `false` | 演示模式会跳过全站鉴权，任何调用方都能访问管理端与绘图接口 |
| `enableRegister` | `false` | 关闭自助注册 |
| `enableGuest` | `false` | 关闭访客访问，未携带令牌的请求返回 401 |

这些配置在管理后台的“系统设置”中保存。保存配置要求 Redis 可用且数据库可连接，因此请先完成[配置](04-configuration.md)中的 Redis 与数据库设置，再修改安全相关开关。

### 网络暴露面检查

上线前确认：

- 只对外开放 HTTP 端口（如 `8086`），数据库、Redis 与 Consul 端口不要暴露到公网。
- `enableSwagger` 保持 `false`；管理端 `/mj/admin/*` 必须携带管理员令牌，`/health` 是匿名健康检查。
- 反向代理层限制请求体大小与超时，避免大文件上传拖垮进程。

## 最小验证

### 健康检查与管理端连通性

```bash
curl -i "http://<YOUR_HOST>:8086/health"
```

正常时返回 HTTP 200 与包含 `status`、`timestamp`、`machineName`、`processId` 的 JSON。再用管理员令牌调用一次上文的管理端登录接口，返回 `code: 1` 且带有 `apiSecret` 即表示管理端连通。

### 账号上线验证

先接入一个 Discord 账号：

```bash
curl -X POST "http://<YOUR_HOST>:8086/mj/admin/account" \
  -H "Content-Type: application/json" \
  -H "Authorization: <YOUR_TOKEN>" \
  -d '{
    "channelId": "<CHANNEL_ID>",
    "guildId": "<GUILD_ID>",
    "userToken": "<YOUR_USER_TOKEN>",
    "enable": true
  }'
```

新增账号是异步启动：接口立即返回成功，后台再建立连接。等待数十秒后查询启用中的账号：

```bash
curl "http://<YOUR_HOST>:8086/mj/admin/accounts" \
  -H "Authorization: <YOUR_TOKEN>"
```

账号出现在列表中且 `running` 为 `true`，说明连接已建立。账号启动失败会被自动置为禁用并记录 `disabledReason`；默认账号列表只展示启用账号，排查时请切换筛选查看禁用项。

### 一次最小绘图请求验证

```bash
curl -X POST "http://<YOUR_HOST>:8086/mj/submit/imagine" \
  -H "Content-Type: application/json" \
  -H "Authorization: <YOUR_TOKEN>" \
  -d '{"prompt": "a red apple on a white table --ar 1:1"}'
```

`code` 为 `1` 表示已开始执行，`code` 为 `22` 表示已入队，`result` 是任务 ID。随后轮询任务：

```bash
curl "http://<YOUR_HOST>:8086/mj/task/<TASK_ID>/fetch" -H "Authorization: <YOUR_TOKEN>"
```

任务对象中 `status` 为 `SUCCESS` 表示完成，`imageUrl` 与 `imageUrls` 是结果地址。常见的 `status` 取值与失败原因见[API 参考](06-api-reference.md)与[故障排查](09-troubleshooting.md)。

### 结果文件可访问性验证

`imageStorageType` 为 `NONE` 时，结果地址直接指向上游 CDN，只要能访问外网即可。切换为 `LOCAL` 后，结果写入 `wwwroot/attachments/`，返回的地址形如 `/attachments/...`：

```bash
curl -I "http://<YOUR_HOST>:8086/attachments/<PATH>"
```

如果返回 404，依次检查 `imageStorageType` 是否为 `LOCAL`、附件目录是否正确挂载、`localStorage.customCdn` 是否指向可公网访问的地址；对象存储场景直接访问对应 CDN 地址验证。完成以上步骤后，即可进入[配置](04-configuration.md)细化数据库、Redis、存储与限流，并按[账号管理](05-accounts.md)接入更多账号。
