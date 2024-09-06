# Midjourney API

**English** | [中文](README.md)

Proxying Midjourney's Discord channel to draw images via API, supporting one-click face-swapping for images and videos, as a public welfare project offering free image generation interfaces.

🦄 Total drawings generated through this project: over 2 million images 🐂, with a daily output of over 100,000 images. Thank you for your support!

⭐ If you think this project is useful, please consider giving it a `Star`, much appreciated!

## Discussion Group

If you have any questions about usage or deployment, feel free to join the discussion group for collective problem-solving.

If you have drawing requests, feel free to contact the group admins. Some members generate over a million images daily!

[Midjourney Public Welfare Group](https://qm.qq.com/q/k88clCkyMS) (QQ Group: 565908696)

<img src="./docs/screenshots/565908696.png" alt="Welcome" width="360"/>

## Key Features

- [x] Supports the Imagine command and related actions [V1/V2.../U1/U2.../R]
- [x] Supports adding base64 images as overlays during Imagine
- [x] Supports Blend (image merging), Describe (image-to-text), and Shorten (prompt analysis) commands
- [x] Supports real-time task progress
- [x] Supports Chinese prompt translation via Baidu or GPT translation
- [x] Pre-check for sensitive words in prompts with customizable overrides
- [x] User-token connection to WSS to retrieve errors and full functionality
- [x] Supports prompt analysis with Shorten
- [x] Supports focus movement: Pan ⬅️➡⬆️⬇️
- [x] Supports partial re-drawing: Vary (Region) 🖌
- [x] Supports all related button actions
- [x] Supports image zooming, customizable Zoom 🔍
- [x] Retrieves image seed values
- [x] Supports account-specified generation speed modes: RELAX | FAST | TURBO
- [x] Supports multi-account configuration with individual task queues, selectable modes: BestWaitIdle | Random | Weight | Polling
- [x] Persistent account pool with dynamic maintenance
- [x] Retrieves account /info and /settings information
- [x] Account settings configuration
- [x] Supports both niji・journey Bot and Midjourney Bot
- [x] Secure zlib-stream compression transmission <https://discord.com/developers/docs/topics/gateway>
- [x] Embedded MJ management interface with multi-language support <https://github.com/trueai-org/midjourney-proxy-webui>
- [x] Account management for adding, deleting, editing, and querying accounts
- [x] Detailed account information queries and synchronization operations
- [x] Account concurrency settings
- [x] MJ account task queries
- [x] Full-featured drawing test page
- [x] Compatible with mainstream drawing clients and API calls
- [x] Adds parent task information to tasks
- [x] 🎛️ Remix mode and auto-submission in Remix mode
- [x] Built-in local image saving and CDN acceleration
- [x] Auto-mark unread messages when drawing to reduce notification overload
- [x] Supports PicReader and Picread commands for regenerating images, including batch operations without needing fast mode
- [x] Supports BOOKMARK and other commands
- [x] Draw with specified instances or filter accounts based on speed, including remix mode filtering (see Swagger `accountFilter` field for details)
- [x] Reverse generate system task info based on job id or image
- [x] Configures account sorting, parallelism, queue numbers, maximum queue limits, task execution intervals, etc.
- [x] Customizable client path specification, default example: https://{BASE_URL}/mj/submit/imagine; /mj-turbo/mj is turbo mode, /mj-relax/mj is relax mode, /mj-fast/mj is fast mode, /mj does not specify mode
- [x] Cloudflare manual human verification with account auto-lock when triggered, verification via GUI or email notification
- [x] Cloudflare automatic human verification with a server-side configuration (automatic verifier supports Windows deployment only)
- [x] Configurable work schedules, recommended breaks of 8-10 hours to avoid warnings when drawing non-stop for 24 hours, e.g., `09:10-23:55, 13:00-08:10`
- [x] Built-in IP rate limiting, IP block rate limiting, blacklist, whitelist, auto-blacklist features
- [x] Daily drawing limit support; once the limit is exceeded, new drawing tasks are disabled, but variation and redraws are still possible
- [x] Enable registration and guest access
- [x] Visual configuration features
- [x] Independent Swagger documentation activation
- [x] Bot token configuration is optional, usage is still available without bot configuration
- [x] Optimized command and status progress display
- [x] Idle mode configuration to reduce high-frequency operations; idle mode disables new drawings but still allows other commands, customizable across multiple time periods
- [x] Vertical account categorization support, account-specific work types (e.g., only landscapes, only portraits)
- [x] Supports shared or sub-channels for drawing, even if an account is banned; continue drawing with a sub-channel of a banned account by saving a permanent invite link, supporting batch modifications
- [x] Multi-database support for local databases or MongoDB; for more than 100,000 tasks, MongoDB is recommended (default keeps 1 million records), with automatic data migration support
- [x] Supports one-click migration of `mjplus` or other services to this service, including accounts, tasks, etc.
- [x] Built-in banned word management with multi-term grouping
- [x] Non-official links in prompts are automatically converted to official ones, allowing for domestic or custom reference links to avoid verification issues
- [x] Supports automatic switch to slow mode when fast mode duration is exhausted, with customizable activation, and auto-reverts when fast mode time is purchased or renewed
- [x] Supports image storage on Aliyun OSS, with customizable CDN, custom styles, and thumbnails (recommended to use OSS for faster loading)
- [x] Supports Shorten analysis for prompt-based image regeneration
- [x] Supports image face-swapping, please comply with legal regulations
- [x] Supports video face-swapping, please comply with legal regulations
- [x] Supports automatic switch to slow mode (when fast time is consumed) and auto-switch to fast mode (when fast time is available)

## Online Preview

The public interface uses slow mode. The interface is free to use, and the account pool is provided by sponsors. Please use it wisely.

- Management Panel: <https://ai.trueai.org>
- Account password: `none`
- Public Interface: <https://ai.trueai.org/mj>
- API Documentation: <https://ai.trueai.org/swagger>
- API Key: `none`
- Cloudflare Auto Verification Server: <http://47.76.110.222:8081>
- Cloudflare Auto Verification Server Docs: <http://47.76.110.222:8081/swagger>

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