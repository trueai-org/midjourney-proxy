# Midjourney Proxy

**中文** | [English](README.en.md)

代理 Midjourney 的 Discord 频道，通过 API 绘图，支持图片、视频一键换脸，公益项目，提供免费 API 绘图。

如果觉得项目不错，欢迎帮助点个 `Star`，万分谢谢！

## 交流群

如果使用上和部署上有什么疑问，欢迎加入交流群，一起讨论和解决问题。

[Midjourney公益群](https://qm.qq.com/q/k88clCkyMS)（QQ群：565908696）

<img src="./docs/screenshots/565908696.png" alt="欢迎" width="360"/>

## 主要功能

- [x] 支持 Imagine 指令和相关动作 [V1/V2.../U1/U2.../R]
- [x] Imagine 时支持添加图片 base64，作为垫图
- [x] 支持 Blend (图片混合)、Describe (图生文) 指令、Shorten (提示词分析) 指令
- [x] 支持任务实时进度
- [x] 支持中文 prompt 翻译，需配置百度翻译、GPT 翻译
- [x] prompt 敏感词预检测，支持覆盖调整
- [x] user-token 连接 wss，可以获取错误信息和完整功能
- [x] 支持 Shorten(prompt分析) 指令
- [x] 支持焦点移动：Pan ⬅️➡⬆️⬇️
- [x] 支持局部重绘：Vary (Region) 🖌
- [x] 支持所有的关联按钮动作
- [x] 支持图片变焦，自定义变焦 Zoom 🔍
- [x] 支持获取图片的 seed 值
- [x] 支持账号指定生成速度模式 RELAX | FAST | TURBO 
- [x] 支持多账号配置，每个账号可设置对应的任务队列，支持账号选择模式 BestWaitIdle | Random | Weight | Polling
- [x] 账号池持久化，动态维护
- [x] 支持获取账号 /info、/settings 信息
- [x] 账号 settings 设置
- [x] 支持 niji・journey Bot 和 Midjourney Bot
- [x] zlib-stream 安全压缩传输 <https://discord.com/developers/docs/topics/gateway>
- [x] 内嵌MJ管理后台页面，支持多语言 <https://github.com/trueai-org/midjourney-proxy-webui>
- [x] 支持MJ账号的增删改查功能
- [x] 支持MJ账号的详细信息查询和账号同步操作
- [x] 支持MJ账号的并发队列设置
- [x] 支持MJ的账号settings设置
- [x] 支持MJ的任务查询
- [x] 提供功能齐全的绘图测试页面
- [x] 兼容支持市面上主流绘图客户端和 API 调用
- [x] 任务增加父级任务信息等
- [x] 🎛️ Remix 模式和 Remix 模式自动提交
- [x] 内置图片保存到本地、内置 CDN 加速
- [x] 绘图时当未读消息过多时，自动模拟读未读消息
- [x] 图生文之再生图 PicReader、Picread 指令支持，以及批量再生图指令支持（无需 fast 模式）
- [x] 支持 BOOKMARK 等指令
- [x] 支持指定实例绘图，支持过滤指定速度的账号绘图，支持过滤 `remix` 模式账号绘图等，详情参考 Swagger `accountFilter` 字段
- [x] 逆向根据 job id 或 图片生成系统任务信息
- [x] 支持账号排序、并行数、队列数、最大队列数、任务执行间隔等配置
- [x] 支持客户端路径指定模式，默认地址例子 https://{BASE_URL}/mj/submit/imagine, /mj-turbo/mj 是 turbo mode, /mj-relax/mj 是 relax mode, /mj-fast/mj 是 fast mode, /mj 不指定模式
- [x] CloudFlare 手动真人验证，触发后自动锁定账号，通过 GUI 直接验证或通过邮件通知验证
- [x] CloudFlare 自动真人验证，配置验证服务器地址（自动验证器仅支持 Windows 部署）
- [x] 支持工作时间段配置，连续 24 小时不间断绘图可能会触发警告，建议休息 8~10 小时，示例：`09:10-23:55, 13:00-08:10`
- [x] 内置 IP 限流、IP 段限流、黑名单、白名单、自动黑名单等功能
- [x] 单日绘图上限支持，超出上限后不在进行新的绘图任务，仍可以进行变化、重绘等操作
- [x] 开启注册、开启访客
- [x] 可视化配置功能
- [x] 支持 Swagger 文档独立开启
- [x] 配置机器人 Token 可选配置，不配置机器人也可以使用
- [x] 优化指令和状态进度显示
- [x] 摸鱼时间配置，账号增加咸鱼模式/放松模式，避免高频作业（此模式下不可创建新的绘图，仍可以执行其他命令，可以配置为多个时间段等策略）
- [x] 账号垂直分类支持，账号支持词条配置，每个账号只做某一类作品，例如：只做风景、只做人物
- [x] 允许共享频道或子频道绘画，即便账号被封，也可以继续之前的绘画，将被封的账号频道作为正常账号的子频道即可，保存永久邀请链接，和子频道链接，支持批量修改。
- [x] 多数据库支持本地数据库、MongoDB 等，如果你的任务数据超过 10万条，则建议使用 MongoDB 存储任务（默认保留 100万条记录），支持数据自动迁移。
- [x] 支持 `mjplus` 或其他服务一键迁移到本服务，支持迁移账号、任务等
- [x] 内置违禁词管理，支持多词条分组
- [x] prompt 中非官方链接自动转为官方链接，允许国内或自定义参考链接，以避免触发验证等问题。
- [x] 支持快速模式时长用完时，自动切换到慢速模式，可自定义开启，当购买快速时长或到期续订时将会自动恢复。
- [x] 支持图片存储到阿里云 OSS，支持自定义 CDN，支持自定义样式，支持缩略图（推荐使用 OSS，与源站分离，加载更快）
- [x] 支持 Shorten 分析 Prompt 之再生图指令
- [x] 支持图片换脸，请遵守相关法律法规，不得用于违法用途。
- [x] 支持视频换脸，请遵守相关法律法规，不得用于违法用途。

## 在线预览

公益接口为慢速模式，接口免费调用，账号池由赞助者提供，请大家合理使用。

- 管理后台：<https://ai.trueai.org>
- 账号密码：`无`
- 公益接口：<https://ai.trueai.org/mj>
- 接口文档：<https://ai.trueai.org/swagger>
- 接口密钥：`无`
- CloudFlare 自动验证服务器地址：<http://47.76.110.222:8081>
- CloudFlare 自动验证服务器文档：<http://47.76.110.222:8081/swagger>

> CloudFlare 自动验证配置示例（免费自动过人机验证）

```json
"CaptchaServer": "http://47.76.110.222:8081", // 自动验证器地址
"CaptchaNotifyHook": "https://ai.trueai.org" // 验证完成通知回调，默认为你的域名
```

## 预览截图

![欢迎](./docs/screenshots/ui1.png)

![账号](./docs/screenshots/ui2.png)

![任务](./docs/screenshots/ui3.png)

![测试](./docs/screenshots/ui4.png)

![日志](./docs/screenshots/ui5.png)

![接口](./docs/screenshots/uiswagger.png)

## 客户端推荐

- **ChatGPT-Midjourney**: <https://github.com/Licoy/ChatGPT-Midjourney>
  - 一键拥有你自己的 ChatGPT+StabilityAI+Midjourney 网页服务 -> <https://aidemo.xiazai.zip/#/mj>
  - 打开网站 -> 设置 -> 自定义接口 -> 模型(Midjourney) -> 接口地址 -> <https://ai.trueai.org/mj>

- **ChatGPT Web Midjourney Proxy**: <https://github.com/Dooy/chatgpt-web-midjourney-proxy> 
  - 打开网站 <https://vercel.ddaiai.com> -> 设置 -> MJ 绘画接口地址 -> <https://ai.trueai.org>

- **GoAmzAI**: <https://github.com/Licoy/GoAmzAI>
  -	打开后台 -> 绘画管理 -> 新增 -> MJ 绘画接口地址 -> <https://ai.trueai.org/mj>

## 教程

- [Bilibili Midjourney API Docker 部署视频教程](https://www.bilibili.com/video/BV1NQpQezEu4/)
- [抖音 Midjourney API Docker 部署视频教程](https://www.douyin.com/video/7405107738868501771)

> 提示：Windows 平台直接下载启动即可，详情参考下方说明。

## 安装与使用

### 快速启动

> Docker 版本

**注意：一定确认映射文件和路径不要出错⚠⚠**

- [Bilibili Midjourney API Docker 部署视频教程](https://www.bilibili.com/video/BV1NQpQezEu4/)
- [抖音 Midjourney API Docker 部署视频教程](https://www.douyin.com/video/7405107738868501771)

```bash
# 自动安装并启动
# 推荐使用一键升级脚本
# 1.首次下载（下载后可以编辑此脚本，进行自定义配置，例如：路径、端口、内存等配置，默认8086端口）
wget -O docker-upgrade.sh https://raw.githubusercontent.com/trueai-org/midjourney-proxy/main/scripts/docker-upgrade.sh && bash docker-upgrade.sh

# 2.更新升级（以后升级只需要执行此脚本即可）
sh docker-upgrade.sh
```

```bash
# 手动安装并启动
# 阿里云镜像（推荐国内使用）
docker pull registry.cn-guangzhou.aliyuncs.com/trueai-org/midjourney-proxy

# 公益演示站点启动配置示例

# 1.下载并重命名配置文件（示例配置）
# 提示：3.x 版本无需配置文件
wget -O /root/mjopen/appsettings.Production.json https://raw.githubusercontent.com/trueai-org/midjourney-proxy/main/src/Midjourney.API/appsettings.json

# 或使用 curl 下载并重命名配置文件（示例配置）
# 提示：3.x 版本无需配置文件
curl -o /root/mjopen/appsettings.Production.json https://raw.githubusercontent.com/trueai-org/midjourney-proxy/main/src/Midjourney.API/appsettings.json

# 2.停止并移除旧的 Docker 容器
docker stop mjopen && docker rm mjopen

# 3.启动新的 Docker 容器
# 提示：3.x 版本无需配置文件
docker run -m 1g --name mjopen -d --restart=always \
 -p 8086:8080 --user root \
 -v /root/mjopen/logs:/app/logs:rw \
 -v /root/mjopen/data:/app/data:rw \
 -v /root/mjopen/attachments:/app/wwwroot/attachments:rw \
 -v /root/mjopen/ephemeral-attachments:/app/wwwroot/ephemeral-attachments:rw \
 -v /root/mjopen/appsettings.Production.json:/app/appsettings.Production.json:ro \
 -e TZ=Asia/Shanghai \
 -v /etc/localtime:/etc/localtime:ro \
 -v /etc/timezone:/etc/timezone:ro \
 registry.cn-guangzhou.aliyuncs.com/trueai-org/midjourney-proxy

# 生产环境启动配置示例
docker run --name mjopen -d --restart=always \
 -p 8086:8080 --user root \
 -v /root/mjopen/logs:/app/logs:rw \
 -v /root/mjopen/data:/app/data:rw \
 -v /root/mjopen/attachments:/app/wwwroot/attachments:rw \
 -v /root/mjopen/ephemeral-attachments:/app/wwwroot/ephemeral-attachments:rw \
 -v /root/mjopen/appsettings.Production.json:/app/appsettings.Production.json:ro \
 -e TZ=Asia/Shanghai \
 -v /etc/localtime:/etc/localtime:ro \
 -v /etc/timezone:/etc/timezone:ro \
 registry.cn-guangzhou.aliyuncs.com/trueai-org/midjourney-proxy

# GitHub 镜像
docker pull ghcr.io/trueai-org/midjourney-proxy
docker run --name mjopen -d --restart=always \
 -p 8086:8080 --user root \
 -v /root/mjopen/logs:/app/logs:rw \
 -v /root/mjopen/data:/app/data:rw \
 -v /root/mjopen/attachments:/app/wwwroot/attachments:rw \
 -v /root/mjopen/ephemeral-attachments:/app/wwwroot/ephemeral-attachments:rw \
 -v /root/mjopen/appsettings.Production.json:/app/appsettings.Production.json:ro \
 -e TZ=Asia/Shanghai \
 -v /etc/localtime:/etc/localtime:ro \
 -v /etc/timezone:/etc/timezone:ro \
 ghcr.io/trueai-org/midjourney-proxy

# DockerHub 镜像
docker pull trueaiorg/midjourney-proxy
docker run --name mjopen -d --restart=always \
 -p 8086:8080 --user root \
 -v /root/mjopen/logs:/app/logs:rw \
 -v /root/mjopen/data:/app/data:rw \
 -v /root/mjopen/attachments:/app/wwwroot/attachments:rw \
 -v /root/mjopen/ephemeral-attachments:/app/wwwroot/ephemeral-attachments:rw \
 -v /root/mjopen/appsettings.Production.json:/app/appsettings.Production.json:ro \
 -e TZ=Asia/Shanghai \
 -v /etc/localtime:/etc/localtime:ro \
 -v /etc/timezone:/etc/timezone:ro \
 trueaiorg/midjourney-proxy
```

> Windows 版本

```bash
a. 通过 https://github.com/trueai-org/midjourney-proxy/releases 下载 windows 最新免安装版，例如：midjourney-proxy-win-x64.zip
b. 解压并执行 Midjourney.API.exe
c. 打开网站 http://localhost:8080
d. 部署到 IIS（可选），在 IIS 添加网站，将文件夹部署到 IIS，配置应用程序池为`无托管代码`，启动网站。
e. 使用系统自带的 `任务计划程序`（可选），创建基本任务，选择 `.exe` 程序即可，请选择`请勿启动多个实例`，保证只有一个任务执行即可。
```

> Linux 版本

```bash
a. 通过 https://github.com/trueai-org/midjourney-proxy/releases 下载 linux 最新免安装版，例如：midjourney-proxy-linux-x64.zip
b. 解压到当前目录: tar -xzf midjourney-proxy-linux-x64-<VERSION>.tar.gz
c. 执行: run_app.sh
c. 启动方式1: sh run_app.sh
d. 启动方式2: chmod +x run_app.sh && ./run_app.sh
```

> macOS 版本

```bash
a. 通过 https://github.com/trueai-org/midjourney-proxy/releases 下载 macOS 最新免安装版，例如：midjourney-proxy-osx-x64.zip
b. 解压到当前目录: tar -xzf midjourney-proxy-osx-x64-<VERSION>.tar.gz
c. 执行: run_app_osx.sh
c. 启动方式1: sh run_app_osx.sh
d. 启动方式2: chmod +x run_app_osx.sh && ./run_app_osx.sh
```

> Linux 一键安装脚本（❤感谢 [@dbccccccc](https://github.com/dbccccccc)）

```bash
# 方式1
wget -N --no-check-certificate https://raw.githubusercontent.com/trueai-org/midjourney-proxy/main/scripts/linux_install.sh && chmod +x linux_install.sh && bash linux_install.sh

# 方式2
curl -o linux_install.sh https://raw.githubusercontent.com/trueai-org/midjourney-proxy/main/scripts/linux_install.sh && chmod +x linux_install.sh && bash linux_install.sh
```

## 参数配置

- `appsettings.json` 默认配置
- `appsettings.Production.json` 生产环境配置
- `/app/data` 数据目录，存放账号、任务等数据
    - `/app/data/mj.db` 数据库文件
- `/app/logs` 日志目录
- `/app/wwwroot` 静态文件目录
    - `/app/wwwroot/attachments` 绘图文件目录
    - `/app/wwwroot/ephemeral-attachments` describe 生成图片目录

#### 角色说明

- `普通用户`：只可用于绘图接口，无法登录后台。
- `管理员`：可以登录后台，可以查看任务、配置等。

#### 默认用户说明

- 启动站点，如果之前没有设置过 `AdminToken`，则默认管理员 token 为：`admin`

> 3.x 版本，无需此配置，修改配置请通过 GUI 修改

```json
{
  "Demo": null, // 网站配置为演示模式
  "UserToken": "", // 用户绘画令牌 token，可以用来访问绘画接口，可以不用设定
  "AdminToken": "", // 管理后台令牌 token，可以用来访问绘画接口和管理员账号等功能
  "mj": {
    "MongoDefaultConnectionString": null, // MongoDB 连接字符串
    "MongoDefaultDatabase": null, // MongoDB 数据库名称
    "AccountChooseRule": "BestWaitIdle", // BestWaitIdle | Random | Weight | Polling = 最佳空闲规则 | 随机 | 权重 | 轮询
    "Discord": { // Discord 配置，默认可以为 null
      "GuildId": "125652671***", // 服务器 ID
      "ChannelId": "12565267***", // 频道 ID
      "PrivateChannelId": "1256495659***", // MJ 私信频道 ID，用来接受 seed 值
      "NijiBotChannelId": "1261608644***", // NIJI 私信频道 ID，用来接受 seed 值
      "UserToken": "MTI1NjQ5N***", // 用户 token
      "BotToken": "MTI1NjUyODEy***", // 机器人 token
      "UserAgent": "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/112.0.0.0 Safari/537.36",
      "Enable": true, // 是否默认启动
      "CoreSize": 3, // 并发数
      "QueueSize": 10, // 队列数
      "MaxQueueSize": 100, // 最大队列数
      "TimeoutMinutes": 5, // 任务超时分钟数
      "Mode": null, // RELAX | FAST | TURBO 指定生成速度模式 --fast, --relax, or --turbo parameter at the end.
      "Weight": 1 // 权重
    },
    "NgDiscord": { // NG Discord 配置，默认可以为 null
      "Server": "",
      "Cdn": "",
      "Wss": "",
      "ResumeWss": "",
      "UploadServer": "",
      "SaveToLocal": false, // 是否开启图片保存到本地，如果开启则使用本地部署的地址，也可以同时配置 CDN 地址
      "CustomCdn": "" // 如果不填写，并且开启了保存到本地，则默认为根目录，建议填写自己的域名地址
    },
    "Proxy": { // 代理配置，默认可以为 null
      "Host": "",
      "Port": 10809
    },
    "Accounts": [], // 账号池配置
    "Openai": {
      "GptApiUrl": "https://goapi.gptnb.ai/v1/chat/completions", // your_gpt_api_url
      "GptApiKey": "", // your_gpt_api_key
      "Timeout": "00:00:30",
      "Model": "gpt-4o-mini",
      "MaxTokens": 2048,
      "Temperature": 0
    },
    "BaiduTranslate": { // 百度翻译配置，默认可以为 null
      "Appid": "", // your_appid
      "AppSecret": "" // your_app_secret
    },
    "TranslateWay": "NULL", // NULL | GTP | BAIDU, 翻译配置, 默认: NULL
    "ApiSecret": "", // your_api_secret
    "NotifyHook": "", // your_notify_hook, 回调配置
    "NotifyPoolSize": 10,
    "Smtp": {
      "Host": "smtp.mxhichina.com", // SMTP服务器信息
      "Port": 465, // SMTP端口，一般为587或465，具体依据你的SMTP服务器而定
      "EnableSsl": true, // 根据你的SMTP服务器要求设置
      "FromName": "system", // 发件人昵称
      "FromEmail": "system@***.org", // 发件人邮箱地址
      "FromPassword": "", // 你的邮箱密码或应用专用密码
      "To": "" // 收件人
    },
    "CaptchaServer": "", // CF 验证服务器地址
    "CaptchaNotifyHook": "" // CF 验证通知地址（验证通过后的回调通知，默认就是你的当前域名）
  },
  // IP/IP 段 限流配置，可以用来限制某个 IP/IP 段 的访问频率
  // 触发限流后会返回 429 状态码
  // 黑名单直接返回 403 状态码
  // 黑白名、白名单支持 IP 和 CIDR 格式 IP 段，例如：192.168.1.100、192.168.1.0/24
  "IpRateLimiting": {
    "Enable": false,
    "Whitelist": [], // 永久白名单 "127.0.0.1", "::1/10", "::1"
    "Blacklist": [], // 永久黑名单
    // 0.0.0.0/32 单个 ip
    "IpRules": {
      // 限制 mj/submit 接口下的所有接口
      "*/mj/submit/*": {
        "3": 1, // 每 3 秒 最多访问 1 次
        "60": 6, // 每 60 秒最多访问 6 次
        "600": 20, // 每 600 秒最多访问 20 次
        "3600": 60, // 每 3600 秒最多访问 60 次
        "86400": 120 // 每天最多访问 120 次
      }
    },
    // 0.0.0.0/24 ip 段
    "Ip24Rules": {
      // 限制 mj/submit 接口下的所有接口
      "*/mj/submit/*": {
        "5": 10, // 每 5 秒 最多访问 10 次
        "60": 30, // 每 60 秒最多访问 30 次
        "600": 100, // 每 600 秒最多访问 100 次
        "3600": 300, // 每 3600 秒最多访问 300 次
        "86400": 360 // 每天最多访问 360 次
      }
    },
    // 0.0.0.0/16 ip 段
    "Ip16Rules": {}
  },
  // IP 黑名单限流配置，触发后自动封锁 IP，支持封锁时间配置
  // 触发限流后，加入黑名单会返回 403 状态码
  // 黑白名、白名单支持 IP 和 CIDR 格式 IP 段，例如：192.168.1.100、192.168.1.0/24
  "IpBlackRateLimiting": {
    "Enable": false,
    "Whitelist": [], // 永久白名单 "127.0.0.1", "::1/10", "::1"
    "Blacklist": [], // 永久黑名单
    "BlockTime": 1440, // 封锁时间，单位：分钟
    "IpRules": {
      "*/mj/*": {
        "1": 30,
        "60": 900
      }
    },
    "Ip24Rules": {
      "*/mj/*": {
        "1": 90,
        "60": 3000
      }
    }
  },
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Default": "Warning",
        "System": "Warning",
        "Microsoft": "Warning"
      }
    },
    "WriteTo": [
      {
        "Name": "File",
        "Args": {
          "path": "logs/log.txt",
          "rollingInterval": "Day",
          "fileSizeLimitBytes": null,
          "rollOnFileSizeLimit": false,
          "retainedFileCountLimit": 31
        }
      },
      {
        "Name": "Console"
      }
    ]
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "urls": "http://*:8080" // 默认端口
}
```

#### 阿里云 OSS 配置项

```json
{
  "enable": true,
  "bucketName": "mjopen",
  "region": null,
  "accessKeyId": "LTAIa***",
  "accessKeySecret": "QGqO7***",
  "endpoint": "oss-cn-hongkong-internal.aliyuncs.com",
  "customCdn": "https://mjcdn.googlec.cc",
  "imageStyle": "x-oss-process=style/webp",
  "thumbnailImageStyle": "x-oss-process=style/w200"
}
```

## CloudFlare 验证器部署

仅支持 Windows 部署（并且支持 TLS 1.3，系统要求 Windows11 或 Windows Server 2022），由于 CloudFlare 验证器需要使用到 Chrome 浏览器，所以需要在 Windows 环境下部署，而在 Linux 环境下部署会依赖很多库，所以暂时不支持 Linux 部署。

注意：自行部署需提供 2captcha.com 的 API Key，否则无法使用，价格：1000次/9元，官网：<https://2captcha.cn/p/cloudflare-turnstile>

提示：首次启动会下载 Chrome 浏览器，会比较慢，请耐心等待。

> `appsettings.json` 配置参考

```json
{
  "Demo": null, // 网站配置为演示模式
  "Captcha": {
    "Headless": true, // chrome 是否后台运行
    "TwoCaptchaKey": "" // 2captcha.com 的 API Key
  },
  "urls": "http://*:8081" // 默认端口
}

```

## 机器人 Token（可选配置）

本项目利用 Discord 机器人 Token 连接 wss，可以获取错误信息和完整功能，确保消息的高可用性等问题。

```
1. 创建应用
https://discord.com/developers/applications

2. 设置应用权限（确保拥有读取内容权限，参考截图）
[Bot] 设置 -> 全部开启

3. 添加应用到频道服务器（参考截图）

client_id 可以在应用详情页找到，为 APPLICATION ID

https://discord.com/oauth2/authorize?client_id=xxx&permissions=8&scope=bot

4. 复制或重置 Bot Token 到配置文件
```

设置应用权限（确保拥有读取内容权限，参考截图）

![设置应用权限](./docs/screenshots/gjODn5Nplq.png)

添加应用到频道服务器（参考截图）

![添加应用到频道服务器](./docs/screenshots/ItiWgaWIaX.png)

## MongoDB 配置

> 如果你的任务量未来可能超过 10 万，推荐 Docker 部署 MongoDB。

> 注意：切换 MongoDB 历史任务可选择自动迁移。

1. 启动容器 `xxx` 为你的密码
2. 打开系统设置 -> 输入 MongoDB 连接字符串 `mongodb://mongoadmin:xxx@ip` 即可
3. 填写 MongoDB 数据库名称 -> `mj` -> 保存
4. 重启服务

```bash
# 启动容器
docker run -d \
  --name mjopen-mongo \
  -p 27017:27017 \
  -v /root/mjopen/mongo/data:/data/db \
  --restart always \
  -e MONGO_INITDB_ROOT_USERNAME=mongoadmin \
  -e MONGO_INITDB_ROOT_PASSWORD=xxx \
  mongo

# 创建数据库（也可以通过 BT 创建数据库）（可选）
```

## 换脸配置

- 打开官网注册并复制 Token: https://replicate.com/codeplugtech/face-swap

```json
{
  "token": "****",
  "enableFaceSwap": true,
  "faceSwapVersion": "278a81e7ebb22db98bcba54de985d22cc1abeead2754eb1f2af717247be69b34",
  "faceSwapCoreSize": 3,
  "faceSwapQueueSize": 10,
  "faceSwapTimeoutMinutes": 10,
  "enableVideoFaceSwap": true,
  "videoFaceSwapVersion": "104b4a39315349db50880757bc8c1c996c5309e3aa11286b0a3c84dab81fd440",
  "videoFaceSwapCoreSize": 3,
  "videoFaceSwapQueueSize": 10,
  "videoFaceSwapTimeoutMinutes": 30,
  "maxFileSize": 10485760,
  "webhook": null,
  "webhookEventsFilter": []
}
```

## 相关文档

1. [API接口说明](./docs/api.md)

## 作图频繁预防警告

- 任务间隔 36~120 秒，执行前间隔 3.6 秒以上
- 每日最大 200 张
- 每日工作时间，建议 9：10~22：50
- 如果有多个账号，则建议开启垂直领域功能，每个账号只做某一类作品

## 路线图

- [ ] 支持腾讯云存储等
- [ ] 支持通过 openai 分析 prompt 词条，然后分配到领域账号，更加智能。通过 shorten 分析 prompt 词条，并分配到领域。
- [ ] 接入官网绘图 API 支持
- [ ] 最终提示词增加翻译中文显示支持
- [ ] 账号支持单独的代理
- [ ] 多数据库支持 MySQL、Sqlite、SqlServer、PostgeSQL、Redis 等
- [ ] 支付接入支持、支持微信、支付宝，支持绘图定价策略等
- [ ] 增加公告功能
- [ ] 图生文 seed 值处理
- [ ] 自动读私信消息
- [ ] 多账号分组支持
- [ ] 服务重启后，如果有未启动的任务，则加入到执行的队列中
- [ ] 子频道自动化支持，可直接输入邀请链接，或共享频道地址，系统自动加入频道转换。或者通过转交所有权实现。
- [ ] 通过 discord www.picsi.ai 换脸支持

## 支持与赞助

- 如果觉得这个项目对您有所帮助，请帮忙点个 Star⭐
- 您也可以提供暂时空闲的绘画公益账号（赞助 1 个慢速队列），支持此项目的发展😀

## 赞助商

非常感谢赞助商和群友的帮助和支持，万分感谢！

<a href="https://goapi.gptnb.ai"><img src="https://img.stqu.me/images/2023/06/26/favicon.png" style="width: 60px;"></a>
<a href="https://d.goamzai.com" target="_blank"><img src="https://d.goamzai.com/logo.png" style="width: 60px;"></a>
<a href="https://api.ephone.ai" target="_blank"><img src="https://api.iowen.cn/favicon/supernormal.com.png" style="width: 60px;"></a>
<a href="https://api.mjdjourney.cn" target="_blank"><img src="https://cdn.optiai.cn/file/upload/2024/08/05/1820477746069901312.png" style="width: 60px;"></a>

## 安全协议

> 由于部分开源作者被请去喝茶，使用本项目不得用于违法犯罪用途。

- 请务必遵守国家法律，任何用于违法犯罪的行为将由使用者自行承担。
- 本项目遵循 GPL 协议，允许个人和商业用途，但必须经作者允许且保留版权信息。
- 请遵守当地国家法律法规，不得用于违法用途。
- 请勿用于非法用途。