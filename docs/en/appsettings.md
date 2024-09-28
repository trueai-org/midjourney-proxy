## Parameter Configuration

- `appsettings.json` Default configuration
- `appsettings.Production.json` Production environment configuration
- `/app/data` Data directory, stores accounts, tasks, and other data
    - `/app/data/mj.db` Database file
- `/app/logs` Log directory
- `/app/wwwroot` Static file directory
    - `/app/wwwroot/attachments` Drawing file directory
    - `/app/wwwroot/ephemeral-attachments` Temporary image generation directory

#### More Parameter Descriptions

> No configuration file is required for deployment; please modify configurations through the GUI.

```json
{
  "Demo": null, // Website configuration for demo mode
  "UserToken": "", // User drawing token, can be used to access the drawing interface, not required to set
  "AdminToken": "", // Admin backend token, can be used to access the drawing interface and admin account features
  "mj": {
    "MongoDefaultConnectionString": null, // MongoDB connection string
    "MongoDefaultDatabase": null, // MongoDB database name
    "AccountChooseRule": "BestWaitIdle", // BestWaitIdle | Random | Weight | Polling = Best idle rule | Random | Weight | Polling
    "Discord": { // Discord configuration, can be null by default
      "GuildId": "125652671***", // Server ID
      "ChannelId": "12565267***", // Channel ID
      "PrivateChannelId": "1256495659***", // MJ private channel ID, used to receive seed values
      "NijiBotChannelId": "1261608644***", // NIJI private channel ID, used to receive seed values
      "UserToken": "MTI1NjQ5N***", // User token
      "BotToken": "MTI1NjUyODEy***", // Bot token
      "UserAgent": "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/112.0.0.0 Safari/537.36",
      "Enable": true, // Whether to start by default
      "CoreSize": 3, // Concurrency number
      "QueueSize": 10, // Queue number
      "MaxQueueSize": 100, // Maximum queue number
      "TimeoutMinutes": 5, // Task timeout in minutes
      "Mode": null, // RELAX | FAST | TURBO specifies the generation speed mode --fast, --relax, or --turbo parameter at the end.
      "Weight": 1 // Weight
    },
    "NgDiscord": { // NG Discord configuration, can be null by default
      "Server": "",
      "Cdn": "",
      "Wss": "",
      "ResumeWss": "",
      "UploadServer": "",
      "SaveToLocal": false, // Whether to enable saving images locally, if enabled, use the locally deployed address, can also configure CDN address
      "CustomCdn": "" // If not filled, and saving to local is enabled, defaults to root directory, it is recommended to fill in your own domain address
    },
    "Proxy": { // Proxy configuration, can be null by default
      "Host": "",
      "Port": 10809
    },
    "Accounts": [], // Account pool configuration
    "Openai": {
      "GptApiUrl": "https://goapi.gptnb.ai/v1/chat/completions", // your_gpt_api_url
      "GptApiKey": "", // your_gpt_api_key
      "Timeout": "00:00:30",
      "Model": "gpt-4o-mini",
      "MaxTokens": 2048,
      "Temperature": 0
    },
    "BaiduTranslate": { // Baidu translation configuration, can be null by default
      "Appid": "", // your_appid
      "AppSecret": "" // your_app_secret
    },
    "TranslateWay": "NULL", // NULL | GTP | BAIDU, translation configuration, default: NULL
    "ApiSecret": "", // your_api_secret
    "NotifyHook": "", // your_notify_hook, callback configuration
    "NotifyPoolSize": 10,
    "Smtp": {
      "Host": "smtp.mxhichina.com", // SMTP server information
      "Port": 465, // SMTP port, generally 587 or 465, depending on your SMTP server
      "EnableSsl": true, // Set according to your SMTP server requirements
      "FromName": "system", // Sender nickname
      "FromEmail": "system@***.org", // Sender email address
      "FromPassword": "", // Your email password or application-specific password
      "To": "" // Recipient
    },
    "CaptchaServer": "", // CF verification server address
    "CaptchaNotifyHook": "" // CF verification notification address (callback notification after verification passes, defaults to your current domain)
  },
  // IP/IP segment rate limiting configuration, can be used to limit the access frequency of a certain IP/IP segment
  // Triggering rate limiting will return a 429 status code
  // Blacklist will directly return a 403 status code
  // Black and white lists support IP and CIDR format IP segments, e.g.: 192.168.1.100, 192.168.1.0/24
  "IpRateLimiting": {
    "Enable": false,
    "Whitelist": [], // Permanent whitelist "127.0.0.1", "::1/10", "::1"
    "Blacklist": [], // Permanent blacklist
    // 0.0.0.0/32 single ip
    "IpRules": {
      // Limit all interfaces under mj/submit
      "*/mj/submit/*": {
        "3": 1, // At most 1 access every 3 seconds
        "60": 6, // At most 6 accesses every 60 seconds
        "600": 20, // At most 20 accesses every 600 seconds
        "3600": 60, // At most 60 accesses every 3600 seconds
        "86400": 120 // At most 120 accesses per day
      }
    },
    // 0.0.0.0/24 ip segment
    "Ip24Rules": {
      // Limit all interfaces under mj/submit
      "*/mj/submit/*": {
        "5": 10, // At most 10 accesses every 5 seconds
        "60": 30, // At most 30 accesses every 60 seconds
        "600": 100, // At most 100 accesses every 600 seconds
        "3600": 300, // At most 300 accesses every 3600 seconds
        "86400": 360 // At most 360 accesses per day
      }
    },
    // 0.0.0.0/16 ip segment
    "Ip16Rules": {}
  },
  // IP blacklist rate limiting configuration, automatically blocks IP after triggering, supports blocking time configuration
  // After triggering rate limiting, joining the blacklist will return a 403 status code
  // Black and white lists support IP and CIDR format IP segments, e.g.: 192.168.1.100, 192.168.1.0/24
  "IpBlackRateLimiting": {
    "Enable": false,
    "Whitelist": [], // Permanent whitelist "127.0.0.1", "::1/10", "::1"
    "Blacklist": [], // Permanent blacklist
    "BlockTime": 1440, // Blocking time, unit: minutes
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
  "urls": "http://*:8080" // Default port
}
```