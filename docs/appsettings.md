## 参数配置

- `appsettings.json` 默认配置
- `appsettings.Production.json` 生产环境配置
- `/app/data` 数据目录，存放账号、任务等数据
    - `/app/data/mj.db` 数据库文件
- `/app/logs` 日志目录
- `/app/wwwroot` 静态文件目录
    - `/app/wwwroot/attachments` 绘图文件目录
    - `/app/wwwroot/ephemeral-attachments` describe 生成图片目录

#### 更多参数说明

> 部署无需配置文件，修改配置请通过 GUI 修改。

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