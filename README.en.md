# Midjourney Proxy

[中文](README.md) | **English**

Proxy for Midjourney's Discord channels, enabling API-based AI drawing calls, a public benefit project offering free use of drawing APIs.

Fully open-source without any partially open or closed source components, pull requests are welcome.

The most feature-rich, secure, and memory-efficient (100MB+) Midjourney Proxy API~~

## Key Features

- [x] Supports Imagine commands and related actions [V1/V2.../U1/U2.../R]
- [x] Supports adding images in base64 format during Imagine calls
- [x] Supports Blend (image mixing) and Describe (image-to-text) commands
- [x] Real-time progress tracking of tasks
- [x] Chinese prompt translation, requires Baidu Translate configuration
- [x] Pre-check for sensitive words in prompts, with support for adjustments
- [x] User-token connection via wss, enabling retrieval of error messages and full functionality
- [x] Supports Shorten (prompt analysis) command
- [x] Supports movement focus: Pan ⬅️➡⬆️⬇️
- [x] Supports local redraw: Vary (Region) 🖌
- [x] Supports almost all related button actions and 🎛️ Remix mode
- [x] Image zoom customization: Zoom 🔍
- [x] Retrieval of image seed values
- [x] Speed mode settings per account: RELAX | FAST | TURBO
- [x] Multi-account configuration, each with its task queue, supports account selection modes: BestWaitIdle | Random | Weight | Polling
- [x] Persistent account pool, dynamically maintained
- [x] Access to account /info, /settings
- [x] Account settings adjustments
- [x] Supports niji・journey Bot and Midjourney Bot
- [x] zlib-stream for secure transmission <https://discord.com/developers/docs/topics/gateway>
- [x] Embedded MJ management webpage, multi-language support <https://github.com/trueai-org/midjourney-proxy-webui>
- [x] MJ account management functionalities: add, delete, modify, and sync
- [x] Concurrent queue settings for MJ accounts
- [x] MJ account settings configuration
- [x] MJ task query support
- [x] Comprehensive drawing test page available
- [x] Compatible with mainstream drawing clients and API calls in the market.

## Online Preview

Public interface is in slow mode, free to use, supported by sponsor-provided account pools, please use responsibly.

- Admin panel: <https://ai.trueai.org>
- Username & password: `none`
- Public interface: <https://ai.trueai.org/mj>
- API documentation: <https://ai.trueai.org/swagger>
- API key: `none`

## Preview Screenshots

![Welcome](./docs/screenshots/ui1.png)

![Account](./docs/screenshots/ui2.png)

![Tasks](./docs/screenshots/ui3.png)

![Test](./docs/screenshots/ui4.png)

![Logs](./docs/screenshots/ui5.png)

![API](./docs/screenshots/uiswagger.png)

## Recommended Clients

- **ChatGPT Web Midjourney Proxy**: <https://github.com/Dooy/chatgpt-web-midjourney-proxy> 
  - Visit <https://vercel.ddaiai.com> -> Settings -> MJ Drawing Interface URL -> <https://ai.trueai.org>

- **GoAmzAI**: <https://github.com/Licoy/GoAmzAI>
  - Open backend -> Drawing Management -> Add -> MJ Drawing Interface URL -> <https://ai.trueai.org/mj>

## Installation and Usage

### Quick Start

> Docker version

```bash
# Aliyun image (recommended for use in China)
docker pull registry.cn-guangzhou.aliyuncs.com/trueai-org/midjourney-proxy

# Public demo site startup configuration example

# 1. Download and rename configuration file (example configuration)
wget -O /root/mjopen/appsettings.Production.json https://raw.githubusercontent.com/trueai-org/midjourney-proxy/main/src/Midjourney.API/appsettings.json

# Or use curl to download and rename configuration file (example configuration)
curl -o /root/mjopen/appsettings.Production.json https://raw.githubusercontent.com/trueai-org/midjourney-proxy/main/src/Midjourney.API/appsettings.json

# 2. Stop and remove old Docker containers
docker stop mjopen && docker rm mjopen

# 3. Start a new Docker container
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

# Production environment startup configuration example
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

> Windows version

```bash
a. Download the latest no-install version for Windows from https://github.com/trueai-org/midjourney-proxy/releases, e.g., midjourney-proxy-win-x64.zip
b. Unzip and run Midjourney.API.exe
c. Open http://localhost:8080
d. Optional: Deploy to IIS, add the site to IIS, deploy the folder to IIS, configure the application pool to `No Managed Code`, and start the site.
e. Optional: Use the built-in `Task Scheduler` to create a basic task, select the `.exe` program, and ensure `Do not start multiple instances` is selected to keep only one task running.
```

> Linux version

```bash
a. Download the latest no-install version for Linux from https://github.com/trueai-org/midjourney-proxy/releases, e.g., midjourney-proxy-linux-x64.zip
b. Extract to the current directory: tar -xzf midjourney-proxy-linux-x64-<VERSION>.tar.gz
c. Execute: run_app.sh
c. Startup method 1: sh run_app.sh
d. Startup method 2: chmod +x run_app.sh && ./run_app.sh
```

> macOS version

```bash
a. Download the latest no-install version for macOS from https://github.com/trueai-org/midjourney-proxy/releases, e.g., midjourney-proxy-osx-x64.zip
b. Extract to the current directory: tar -xzf midjourney-proxy-osx-x64-<VERSION>.tar.gz
c. Execute: run_app_osx.sh
c. Startup method 1: sh run_app_osx.sh
d. Startup method 2: chmod +x run_app_osx.sh && ./run_app_osx.sh
```

## Configuration Parameters

- `appsettings.json` default configuration
- `appsettings.Production.json` production environment configuration
- `/app/data` data directory, stores accounts, tasks, etc.
    - `/app/data/mj.db` database file
- `/app/logs` log directory

```json
{
  "Demo": null, // Set the website to demo mode
  "UserToken": "", // User drawing token, can access drawing interface, optional
  "AdminToken": "", // Admin backend token, can access drawing interface and admin accounts
  "mj": {
    "AccountChooseRule": "BestWaitIdle", // BestWaitIdle | Random | Weight | Polling = Best wait idle rule | Random | Weight | Polling
    "Discord": { // Discord settings, default can be null
      "GuildId": "125652671***", // Server ID
      "ChannelId": "12565267***", // Channel ID
      "PrivateChannelId": "1256495659***", // MJ private channel ID, for receiving seed values
      "NijiBotChannelId": "1261608644***", // NIJI private channel ID, for receiving seed values
      "UserToken": "MTI1NjQ5N***", // User token
      "BotToken": "MTI1NjUyODEy***", // Bot token
      "UserAgent": "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/112.0.0.0 Safari/537.36",
      "Enable": true, // Whether to enable by default
      "CoreSize": 3, // Concurrent number
      "QueueSize": 10, // Queue size
      "MaxQueueSize": 100, // Maximum queue size
      "

TimeoutMinutes": 5, // Task timeout in minutes
      "Mode": null, // RELAX | FAST | TURBO specify generation speed mode --fast, --relax, or --turbo parameter at the end.
      "Weight": 1 // Weight
    },
    "NgDiscord": { // NG Discord settings, default can be null
      "Server": "",
      "Cdn": "",
      "Wss": "",
      "ResumeWss": "",
      "UploadServer": ""
    },
    "Proxy": { // Proxy settings, default can be null
      "Host": "",
      "Port": 10809
    },
    "Accounts": [], // Account pool configuration
    "BaiduTranslate": { // Baidu Translate settings, default can be null
      "Appid": "", // your_appid
      "AppSecret": "" // your_app_secret
    },
    "TranslateWay": "NULL", // NULL | GTP | BAIDU, Translation settings, default: NULL
    "ApiSecret": "", // your_api_secret
    "NotifyHook": "", // your_notify_hook, callback settings
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
  "urls": "http://*:8080" // default port
}
```

## Bot Token (Required)

This project uses a Discord bot token to connect via wss, retrieving error messages and full functionality, ensuring high availability of messages.

```
1. Create an application
https://discord.com/developers/applications

2. Set application permissions (ensure read content permission, refer to screenshot)
[Bot] Settings -> Enable all

3. Add application to channel server (refer to screenshot)

client_id can be found on the application details page, it is the APPLICATION ID

https://discord.com/oauth2/authorize?client_id=xxx&permissions=8&scope=bot

4. Copy or reset Bot Token to configuration file
```

Set application permissions (ensure read content permission, refer to screenshot)

![Set application permissions](./docs/screenshots/gjODn5Nplq.png)

Add application to channel server (refer to screenshot)

![Add application to channel server](./docs/screenshots/ItiWgaWIaX.png)

## Related Documentation
1. [API Documentation](./docs/api.md)

## Support and Sponsorship

- If you find this project helpful, please give it a Star⭐
- You can also provide temporarily idle drawing accounts for public benefit (sponsor slow mode) to support the development of this project😀

## Friendly Links

- https://github.com/dolfies/discord.py-self
- https://github.com/bao-io/midjourney-sdk
- https://github.com/webman-php/midjourney-proxy
- https://github.com/novicezk/midjourney-proxy
- https://github.com/litter-coder/midjourney-proxy-plus
- https://github.com/Dooy/chatgpt-web-midjourney-proxy
- https://github.com/erictik/midjourney-api
- https://github.com/yokonsan/midjourney-api
- https://github.com/imagineapi/imagineapi

## Useful Links
- Open AI official website: <https://openai.com>
- Discord official website: <https://discord.com>
- Discord Developer Platform: <https://discord.com/developers/applications>
- Wenxin Yiyang official website: <https://cloud.baidu.com/product/wenxinworkshop>
- Xinghuo Model official website: <https://xinghuo.xfyun.cn/>
- Api2d official website: <https://api2d.com/>
- OpenAI-SB official website: <https://www.openai-sb.com>
- Baota official website: <https://bt.cn>
- Alibaba Cloud official website: <https://aliyun.com>
- Tencent Cloud official website: <https://cloud.tencent.com/>
- SMS Bao official website: <http://www.smsbao.com/>
- OneAPI project: <https://github.com/songquanpeng/one-api>
```