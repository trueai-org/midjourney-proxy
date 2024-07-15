# Midjourney Proxy

**中文** | [English](README.en.md)

代理 Midjourney 的 Discord 频道，实现 API 形式调用 AI 绘图，公益项目，提供绘图 API 免费使用。

完全开源，不会存在部分开源或部分闭源，欢迎 PR。

功能最全、最安全、占用内存最小（100MB+）的 Midjourney Proxy API ~~

## 主要功能

- [x] 支持 Imagine 指令和相关动作 [V1/V2.../U1/U2.../R]
- [x] Imagine 时支持添加图片 base64，作为垫图
- [x] 支持 Blend (图片混合)、Describe (图生文) 指令
- [x] 支持任务实时进度
- [x] 支持中文 prompt 翻译，需配置百度翻译
- [x] prompt 敏感词预检测，支持覆盖调整
- [x] user-token 连接 wss，可以获取错误信息和完整功能
- [x] 支持 Shorten(prompt分析) 指令
- [x] 支持焦点移动：Pan ⬅️➡⬆️⬇️
- [x] 支持局部重绘：Vary (Region) 🖌
- [x] 支持几乎所有的关联按钮动作和 🎛️ Remix 模式
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
- [x] 兼容支持市面上主流绘图客户端和 API 调用。

## 在线预览

公益接口为慢速模式，接口免费调用，账号池由赞助者提供，请大家合理使用。

- 管理后台：<https://ai.trueai.org>
- 账号密码：`无`
- 公益接口：<https://ai.trueai.org/mj>
- 接口文档：<https://ai.trueai.org/swagger>
- 接口密钥：`无`

## 预览截图

![欢迎](./docs/screenshots/ui1.png)

![账号](./docs/screenshots/ui2.png)

![任务](./docs/screenshots/ui3.png)

![测试](./docs/screenshots/ui4.png)

![日志](./docs/screenshots/ui5.png)

![接口](./docs/screenshots/uiswagger.png)

## 客户端推荐

- **ChatGPT Web Midjourney Proxy**: <https://github.com/Dooy/chatgpt-web-midjourney-proxy> 
  - 打开网站 <https://vercel.ddaiai.com> -> 设置 -> MJ 绘画接口地址 -> <https://ai.trueai.org>

- **GoAmzAI**: <https://github.com/Licoy/GoAmzAI>
  -	打开后台 -> 绘画管理 -> 新增 -> MJ 绘画接口地址 -> <https://ai.trueai.org/mj>

## 安装与使用

### 快速启动

> Docker 版本

```bash
# 阿里云镜像（推荐国内使用）
docker pull registry.cn-guangzhou.aliyuncs.com/trueai-org/midjourney-proxy

# 公益演示站点启动配置示例
docker stop mjopen && docker rm mjopen
docker run --name mjopen -d --restart=always \
 -e DEMO=true \
 -p 8086:8080 --user root \
 -v /root/mjopen/logs:/app/logs:rw \
 -v /root/mjopen/data:/app/data:rw \
 -v /root/mjopen/appsettings.Production.json:/app/appsettings.Production.json:ro \
 -e TZ=Asia/Shanghai \
 -v /etc/localtime:/etc/localtime:ro \
 -v /etc/timezone:/etc/timezone:ro \
 registry.cn-guangzhou.aliyuncs.com/trueai-org/midjourney-proxy

# 生产环境启动配置示例
docker run --name mjproxy -d --restart=always \
 -p 8088:8080 --user root \
 -v /root/mjproxy/logs:/app/logs:rw \
 -v /root/mjproxy/data:/app/data:rw \
 -v /root/mjproxy/appsettings.Production.json:/app/appsettings.Production.json:ro \
 -e TZ=Asia/Shanghai \
 -v /etc/localtime:/etc/localtime:ro \
 -v /etc/timezone:/etc/timezone:ro \
 registry.cn-guangzhou.aliyuncs.com/trueai-org/midjourney-proxy
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

## 参数配置

- `appsettings.json` 默认配置
- `appsettings.Production.json` 生产环境配置
- `/app/data` 数据目录，存放账号、任务等数据
    - `/app/data/mj.db` 数据库文件
- `/app/logs` 日志目录

```json
{
  "Demo": null, // 网站配置为演示模式
  "UserToken": "", // 用户绘画令牌 token，可以用来访问绘画接口，可以不用设定
  "AdminToken": "", // 管理后台令牌 token，可以用来访问绘画接口和管理员账号等功能
  "mj": {
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
      "UploadServer": ""
    },
    "Proxy": { // 代理配置，默认可以为 null
      "Host": "",
      "Port": 10809
    },
    "Accounts": [], // 账号池配置
    "BaiduTranslate": { // 百度翻译配置，默认可以为 null
      "Appid": "", // your_appid
      "AppSecret": "" // your_app_secret
    },
    "TranslateWay": "NULL", // NULL | GTP | BAIDU, 翻译配置, 默认: NULL
    "ApiSecret": "", // your_api_secret
    "NotifyHook": "", // your_notify_hook, 回调配置
    "NotifyPoolSize": 10
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

> 配置使用

启动 docker 后，配置代理为 `http://127.0.0.1:8086/mj` 即可

![示例](./docs/screenshots/chrome_EDzoztHa2b.png)

## 机器人 Token（必须配置）

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

## 相关文档
1. [API接口说明](./docs/api.md)

## 支持与赞助

- 如果觉得这个项目对您有所帮助，请帮忙点个 Star⭐
- 您也可以提供暂时不用的绘画公益账号，支持此项目的发展😀

## 友情链接

- https://github.com/dolfies/discord.py-self
- https://github.com/bao-io/midjourney-sdk
- https://github.com/webman-php/midjourney-proxy
- https://github.com/novicezk/midjourney-proxy
- https://github.com/litter-coder/midjourney-proxy-plus
- https://github.com/Dooy/chatgpt-web-midjourney-proxy
- https://github.com/erictik/midjourney-api
- https://github.com/yokonsan/midjourney-api
- https://github.com/imagineapi/imagineapi

## 常用链接
- Open AI官网: <https://openai.com>
- Discord官网: <https://discord.com>
- Discord开放平台: <https://discord.com/developers/applications>
- 文心一言官网: <https://cloud.baidu.com/product/wenxinworkshop>
- 星火模型官网: <https://xinghuo.xfyun.cn/>
- Api2d官网: <https://api2d.com/>
- OpenAI-SB官网: <https://www.openai-sb.com>
- 宝塔官网: <https://bt.cn>
- 阿里云官网: <https://aliyun.com>
- 腾讯云官网: <https://cloud.tencent.com/>
- 短信宝官网: <http://www.smsbao.com/>
- OneAPI项目: <https://github.com/songquanpeng/one-api>