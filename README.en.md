# Midjourney Proxy

**中文** | [English](README.en.md)

A proxy for the Midjourney Discord channel, allowing AI drawing through API calls. This is a public welfare project providing drawing APIs for free.

It is entirely open source, with no parts being closed, and contributions via PR are welcome.

The most comprehensive, secure, and memory-efficient (100MB+) Midjourney Proxy API.

We are very grateful for the help and support from everyone, and for the many great sponsors who have contributed to this project. Thank you very much!

## Community Group

Since the documentation is not very comprehensive at the moment, you may encounter issues with usage and deployment. Feel free to join our community group for discussions and problem-solving.

[Midjourney Public Welfare Group](https://qm.qq.com/q/k88clCkyMS) (QQ Group: 565908696)

<img src="./docs/screenshots/565908696.png" alt="welcome" width="360"/>

## Main Features

- [x] Supports Imagine commands and related actions [V1/V2.../U1/U2.../R]
- [x] Allows adding image base64 as a base map when using Imagine
- [x] Supports Blend (image mixing) and Describe (text from image) commands
- [x] Real-time task progress supported
- [x] Chinese prompt translation with Baidu Translate configuration
- [x] Pre-detection of sensitive words in prompts, with support for customization
- [x] User-token connection to wss to retrieve error messages and complete features
- [x] Supports Shorten (prompt analysis) command
- [x] Supports pan movements: Pan ⬅️➡⬆️⬇️
- [x] Partial redo supported: Vary (Region) 🖌
- [x] Supports nearly all associated button actions
- [x] Image zoom and custom zoom with Zoom 🔍
- [x] Fetch seed values of images
- [x] Account-specific generation speed modes: RELAX | FAST | TURBO 
- [x] Multi-account configuration, with corresponding task queue settings and selection modes like BestWaitIdle | Random | Weight | Polling
- [x] Persistent account pool with dynamic maintenance
- [x] Retrieve account /info, /settings information
- [x] Account settings management
- [x] Support for both Niji・Journey Bot and Midjourney Bot
- [x] Secure zlib-stream transmission <https://discord.com/developers/docs/topics/gateway>
- [x] Built-in MJ management backend with multilingual support <https://github.com/trueai-org/midjourney-proxy-webui>
- [x] CRUD operations for MJ accounts
- [x] Detailed query and synchronization of MJ account information
- [x] Concurrent queue settings for MJ accounts
- [x] MJ account settings management
- [x] Task querying for MJ
- [x] Comprehensive drawing test page
- [x] Compatibility with major drawing clients and API calls
- [x] Adding parent task information to tasks
- [x] 🎛️ Remix mode and auto-submission in Remix mode 
- [x] Built-in image saving to local storage with CDN acceleration
- [x] Simulated read on unviewed messages when drawing counts exceed a threshold
- [x] Support for PicReader and Picread commands in image-to-text regeneration, including batch commands, without fast mode
- [x] Support for BOOKMARK and other commands
- [x] Allows drawing on specified instances and filtering accounts based on speed or `remix` mode; see Swagger's `accountFilter`
- [x] Reverse task information creation from job id or image
- [x] Configuration for account sorting, parallelism, queue limits, max queue length, task interval, etc.
- [x] Client path mode specification, default URL example: https://{BASE_URL}/mj/submit/imagine, /mj-turbo/mj is turbo mode, /mj-relax/mj is relax mode, /mj-fast/mj is fast mode, /mj without mode specification
- [x] Manual human verification with CloudFlare, with auto account lock on trigger and both GUI and email options for verification
- [x] Auto human verification with CloudFlare; configure verification server address (auto-verifier supports Windows deployment only)
- [x] Work schedule configuration to avoid triggering warnings from continuous 24-hour activities, suggested 8~10 hour rest; e.g., `09:10-23:55, 13:00-08:10`
- [x] Built-in IP throttling, IP range throttling, blacklist, whitelist, auto-blacklist, etc.
- [x] Daily drawing limit support with auto-stop upon reaching limit
- [x] Enable registration and guest access
- [x] Visual configuration features
- [x] Separate activation of Swagger documentation
- [x] Optional bot token configuration, operational without a bot
- [x] Optimization for command and status progress display
- [x] Configuration for idle times, with accounts switching between relax modes to avoid high frequency operations (no new drawings in this mode; other commands can still be executed, configurable with multiple time slots and strategies)
- [x] Vertical account classification support, with keyword configuration for accounts to focus on specific types of work (e.g., landscapes only, portraits only)
- [ ] Allow shared drawing channels or subchannels, continuing drawings even if an account is banned; convert banned account channels into subchannels of active accounts by saving permanent invite links and subchannel links, with batch modification support; enter invite links or shared channel addresses directly with auto-channel joining. Or transfer ownership to achieve this.
- [ ] Call for a video tutorial
- [ ] One-click migration support from `mjplus` or other services to this service

## Online Preview

A public API is available in a slow mode. It allows free access with account pools sponsored by contributors. Please use it responsibly.

- Admin Dashboard: <https://ai.trueai.org>
- Username and Password: `None`
- Public API: <https://ai.trueai.org/mj>
- API Documentation: <https://ai.trueai.org/swagger>
- API Key: `None`
- CF Auto-validation Server Address: <http://47.76.110.222:8081>
- CF Auto-validation Server Documentation: <http://47.76.110.222:8081/swagger>

> Example of CF Auto-validation configuration (for free automatic CAPTCHA bypass)

```json
"CaptchaServer": "http://47.76.110.222:8081",
"CaptchaNotifyHook": "https://ai.trueai.org" // Callback notification, defaults to the current domain
```

## Preview Screenshots

![Welcome](./docs/screenshots/ui1.png)

![Account](./docs/screenshots/ui2.png)

![Tasks](./docs/screenshots/ui3.png)

![Testing](./docs/screenshots/ui4.png)

![Logs](./docs/screenshots/ui5.png)

![API](./docs/screenshots/uiswagger.png)

## Recommended Clients

- **ChatGPT-Midjourney**: <https://github.com/Licoy/ChatGPT-Midjourney>
  - Easily set up your own ChatGPT+StabilityAI+Midjourney web service -> <https://chat-gpt-midjourney-96vk.vercel.app/#/mj>
  - Visit the website -> Settings -> Custom API -> Model (Midjourney) -> API Address -> <https://ai.trueai.org/mj>

- **ChatGPT Web Midjourney Proxy**: <https://github.com/Dooy/chatgpt-web-midjourney-proxy> 
  - Visit <https://vercel.ddaiai.com> -> Settings -> MJ Drawing API Address -> <https://ai.trueai.org>

- **GoAmzAI**: <https://github.com/Licoy/GoAmzAI>
  - Access backend -> Drawing Management -> Add -> MJ Drawing API Address -> <https://ai.trueai.org/mj>

## Installation and Usage

### Quick Start

> Docker Version

**Note: Ensure the file mapping and paths are correct⚠⚠**

```bash
# Aliyun Mirror (Recommended for users in China)
docker pull registry.cn-guangzhou.aliyuncs.com/trueai-org/midjourney-proxy

# Example Configuration for Public Demonstration Site

# 1. Download and rename the configuration file (example config)
# Note: Configuration file not needed for version 3.x
wget -O /root/mjopen/appsettings.Production.json https://raw.githubusercontent.com/trueai-org/midjourney-proxy/main/src/Midjourney.API/appsettings.json

# Or use curl to download and rename the configuration file (example config)
# Note: Configuration file not needed for version 3.x
curl -o /root/mjopen/appsettings.Production.json https://raw.githubusercontent.com/trueai-org/midjourney-proxy/main/src/Midjourney.API/appsettings.json

# 2. Stop and remove old Docker container
docker stop mjopen && docker rm mjopen

# 3. Start new Docker container
# Note: Configuration file not needed for version 3.x
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

# Example Configuration for Production Environment
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

# GitHub Mirror
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

# DockerHub Mirror
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

> Windows Version

```bash
a. Download the latest portable version for Windows from https://github.com/trueai-org/midjourney-proxy/releases, such as midjourney-proxy-win-x64.zip
b. Extract and run Midjourney.API.exe
c. Open http://localhost:8080 in your browser
d. Deploy to IIS (optional), add the site in IIS and deploy the folder, set the application pool to `No Managed Code`, then start the site.
e. Use the built-in `Task Scheduler` (optional), create a basic task, choose the `.exe` program, and make sure to select `Do not start a new instance` to ensure only one task runs.
```

> Linux Version

```bash
a. Download the latest portable version for Linux from https://github.com/trueai-org/midjourney-proxy/releases, such as midjourney-proxy-linux-x64.zip 
b. Extract to the current directory: tar -xzf midjourney-proxy-linux-x64-<VERSION>.tar.gz
c. Run: run_app.sh
c. Start Option 1: sh run_app.sh
d. Start Option 2: chmod +x run_app.sh && ./run_app.sh
```

> macOS Version

```bash
a. Download the latest portable version for macOS from https://github.com/trueai-org/midjourney-proxy/releases, such as midjourney-proxy-osx-x64.zip 
b. Extract to the current directory: tar -xzf midjourney-proxy-osx-x64-<VERSION>.tar.gz
c. Run: run_app_osx.sh
c. Start Option 1: sh run_app_osx.sh
d. Start Option 2: chmod +x run_app_osx.sh && ./run_app_osx.sh
```

> One-click Installation Script for Linux (❤Thanks to [@dbccccccc](https://github.com/dbccccccc))

```bash
# Method 1
wget -N --no-check-certificate https://raw.githubusercontent.com/trueai-org/midjourney-proxy/main/scripts/linux_install.sh && chmod +x linux_install.sh && bash linux_install.sh

# Method 2
curl -o linux_install.sh https://raw.githubusercontent.com/trueai-org/midjourney-proxy/main/scripts/linux_install.sh && chmod +x linux_install.sh && bash linux_install.sh
```

## Parameter Configuration

- `appsettings.json` Default Configuration
- `appsettings.Production.json` Production Environment Configuration
- `/app/data` Data Directory where accounts, tasks, etc., are stored
    - `/app/data/mj.db` Database file
- `/app/logs` Log Directory
- `/app/wwwroot` Static Files Directory
    - `/app/wwwroot/attachments` Directory for drawing files
    - `/app/wwwroot/ephemeral-attachments` Directory for images generated by 'describe'

#### Role Description

- `Regular User`: Can only access drawing API, cannot log into the backend.
- `Administrator`: Can log into the backend, view tasks, configurations, etc.

#### Default User Information

- When starting the site, if `AdminToken` is not previously set, the default admin token is: `admin`

> For version 3.x, this configuration is not needed. Please use the GUI for configuration changes.

```json
{
  "Demo": null, // Site configured in demo mode
  "UserToken": "", // User drawing token for accessing drawing API, optional
  "AdminToken": "", // Admin backend token for accessing drawing API and admin features
  "mj": {
    "AccountChooseRule": "BestWaitIdle", // BestWaitIdle | Random | Weight | Polling = best idle | random | weighted | polling
    "Discord": { // Discord configuration, can be null by default
      "GuildId": "125652671***", // Server ID
      "ChannelId": "12565267***", // Channel ID
      "PrivateChannelId": "1256495659***", // MJ direct message channel ID for receiving seed values
      "NijiBotChannelId": "1261608644***", // NIJI direct message channel ID for receiving seed values
      "UserToken": "MTI1NjQ5N***", // User token
      "BotToken": "MTI1NjUyODEy***", // Bot token
      "UserAgent": "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/112.0.0.0 Safari/537.36",
      "Enable": true, // Default enable
      "CoreSize": 3, // Concurrency number
      "QueueSize": 10, // Queue size
      "MaxQueueSize": 100, // Maximum queue size
      "TimeoutMinutes": 5, // Task timeout in minutes
      "Mode": null, // RELAX | FAST | TURBO specify generation speed mode --fast, --relax, or --turbo parameter at the end.
      "Weight": 1 // Weight
    },
    "NgDiscord": { // NG Discord configuration, can be null by default
      "Server": "",
      "Cdn": "",
      "Wss": "",
      "ResumeWss": "",
      "UploadServer": "",
      "SaveToLocal": false, // Enable local image saving, use local deployment address if enabled or set a custom CDN address
      "CustomCdn": "" // Defaults to root if not set and local save is enabled, recommended to set your domain
    },
    "Proxy": { // Proxy configuration, can be null by default
      "Host": "",
      "Port": 10809
    },
    "Accounts": [], // Account pool configuration
    "BaiduTranslate": { // Baidu Translation configuration, can be null by default
      "Appid": "", // your_appid
      "AppSecret": "" // your_app_secret
    },
    "TranslateWay": "NULL", // NULL | GTP | BAIDU, translation configuration, default: NULL
    "ApiSecret": "", // your_api_secret
    "NotifyHook": "", // your_notify_hook, callback configuration
    "NotifyPoolSize": 10,
    "Smtp": {
      "Host": "smtp.mxhichina.com", // SMTP server information
      "Port": 465, // SMTP port, usually 587 or 465 depending on your server
      "EnableSsl": true, // Set according to your SMTP server's requirements
      "FromName": "system", // Sender's nickname
      "FromEmail": "system@***.org", // Sender's email address
      "FromPassword": "", // Your email password or app-specific password
      "To": "" // Recipient
    },
    "CaptchaServer": "", // CF verification server address
    "CaptchaNotifyHook": "" // CF verification notification address (callback notification after verification, default is your current domain)
  },
  // IP/IP range rate limiting configuration, can be used to limit access frequency
  // Will return 429 status code upon triggering rate limit
  // Blacklist directly returns 403 status code
  // Support for IP and CIDR format for black/whitelist, e.g., 192.168.1.100, 192.168.1.0/24
  "IpRateLimiting": {
    "Enable": false,
    "Whitelist": [], // Permanent whitelist "127.0.0.1", "::1/10", "::1"
    "Blacklist": [], // Permanent blacklist
    // 0.0.0.0/32 single IP
    "IpRules": {
      // Restriction on mj/submit API
      "*/mj/submit/*": {
        "3": 1, // Maximum 1 access every 3 seconds
        "60": 6, // Maximum 6 accesses every 60 seconds
        "600": 20, // Maximum 20 accesses every 600 seconds
        "3600": 60, // Maximum 60 accesses every 3600 seconds
        "86400": 120 // Maximum 120 accesses daily
      }
    },
    // 0.0.0.0/24 IP segment
    "IpRangeRules": {
      // Restriction on mj/submit API
      "*/mj/submit/*": {
        "5": 10, // Maximum 10 accesses every 5 seconds
        "60": 30, // Maximum 30 accesses every 60 seconds
        "600": 100, // Maximum 100 accesses every 600 seconds
        "3600": 300, // Maximum 300 accesses every 3600 seconds
        "86400": 360 // Maximum 360 accesses daily
      }
    },
    // 0.0.0.0/24 IP segment
    "Ip24Rules": {},
    // 0.0.0.0/16 IP segment
    "Ip16Rules": {}
  },
  // IP blacklist rate limiting configuration, automatically blocks IP upon triggering limits
  // Will return 403 status code when added to blacklist
  // Support for IP and CIDR format for black/whitelist, e.g., 192.168.1.100, 192.168.1.0/24
  "IpBlackRateLimiting": {
    "Enable": false,
    "Whitelist": [], // Permanent whitelist "127.0.0.1", "::1/10", "::1"
    "Blacklist": [], // Permanent blacklist
    "BlockTime": 1440, // Block time in minutes
    "IpRules": {
      "*/mj/*": {
        "1": 30,
        "60": 900
      }
    },
    "IpRangeRules": {
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
  "urls": "http://*:8080" // Default port
}
```

## Deployment of CloudFlare Validator

Support is limited to Windows deployment (and requires TLS 1.3, thus Windows 11 or Windows Server 2022). Since the CloudFlare validator utilizes the Chrome browser, it necessitates deployment in a Windows environment. Deploying on Linux is not supported at this time due to heavy library dependencies.

Note: You must provide an API Key for 2captcha.com to use this otherwise it won't function. Pricing: 1000 requests for 9 RMB. Official website: <https://2captcha.cn/p/cloudflare-turnstile>

Tip: The first launch will download the Chrome browser, which may take some time, so please be patient.

> `appsettings.json` Configuration Example

```json
{
  "Demo": null, // Site configuration for demo mode
  "Captcha": {
    "Headless": true, // whether chrome runs in the background
    "TwoCaptchaKey": "" // API Key for 2captcha.com
  },
  "urls": "http://*:8081" // Default port
}

```

## Bot Token (Optional Configuration)

This project utilizes a Discord Bot Token to connect to wss, allowing for error reporting and full functionality, ensuring message reliability, among other features.

```
1. Create an application
https://discord.com/developers/applications

2. Configure application permissions (ensure read message permissions, refer to screenshots)
[Bot] Settings -> Enable all

3. Add application to server (refer to screenshots)

client_id can be found on application details page, labelled as APPLICATION ID

https://discord.com/oauth2/authorize?client_id=xxx&permissions=8&scope=bot

4. Copy or reset the Bot Token in the configuration file
```

Configure application permissions (ensure read message permissions, refer to screenshot)

![Configure application permissions](./docs/screenshots/gjODn5Nplq.png)

Add application to server (refer to screenshot)

![Add application to server](./docs/screenshots/ItiWgaWIaX.png)

## Related Documentation
1. [API Interface Documentation](./docs/api.md)

## Roadmap

- [ ] Optimize alerts for full tasks and queues
- [ ] Optimize issues with concurrent queues for shared accounts
- [ ] Built-in lexicon management for batch modifications
- [ ] Integration with official drawing API support
- [ ] Add statistics panel, drawing statistics, visitor statistics
- [ ] Built-in user system with registration, management, rate limiting, and max attempts
- [ ] Integration of GPT translation
- [ ] Enhance final prompt with translation and Chinese display support
- [ ] Separate proxy support for individual accounts
- [ ] Multi-database support including MySQL, Sqlite, SqlServer, MongoDB, PostgeSQL, Redis, etc.
- [ ] Payment integration support for WeChat, Alipay, and custom drawing pricing strategies
- [ ] Announcement feature
- [ ] Text-from-image seed value handling
- [ ] Automatically read private messages
- [ ] Multi-account group and pagination support
- [ ] Upon service restart, any unstarted tasks will join the execution queue

## Support and Sponsorship

- If you find this project helpful, please give it a star ⭐
- You can also contribute by providing temporarily unused public drawing accounts (sponsoring 1 slow queue) to support the development of this project 😀