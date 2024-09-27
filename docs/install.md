## 安装与使用

> 提示：如果您是私有化部署，请务必关闭演示模式、关闭注册、关闭访客功能，避免 API 被滥用。

> 提示：Windows 平台直接下载启动即可，详情参考下方说明。

### 快速启动

> Docker 版本

- [Bilibili Midjourney API Docker 部署视频教程](https://www.bilibili.com/video/BV1NQpQezEu4/)
- [抖音 Midjourney API Docker 部署视频教程](https://v.douyin.com/irvnDGfo/)

注意：一定确认映射文件和路径不要出错⚠

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

# 1.下载并重命名配置文件（示例配置）
# 提示：3.x 版本无需配置文件
# wget -O /root/mjopen/appsettings.Production.json https://raw.githubusercontent.com/trueai-org/midjourney-proxy/main/src/Midjourney.API/appsettings.json
# curl -o /root/mjopen/appsettings.Production.json https://raw.githubusercontent.com/trueai-org/midjourney-proxy/main/src/Midjourney.API/appsettings.json

# 2.停止并移除旧的 Docker 容器
docker stop mjopen && docker rm mjopen

# 3.启动容器
docker run --name mjopen -d --restart=always \
 -p 8086:8080 --user root \
 -v /root/mjopen/logs:/app/logs:rw \
 -v /root/mjopen/data:/app/data:rw \
 -v /root/mjopen/attachments:/app/wwwroot/attachments:rw \
 -v /root/mjopen/ephemeral-attachments:/app/wwwroot/ephemeral-attachments:rw \
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

[更多配置参数文档](./docs/appsettings.md)

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

#### 默认用户

- 首次启动站点，默认管理员 token 为：`admin`，登录后请重置 `token`

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

#### MongoDB 配置

> 如果你的任务量未来可能超过 10 万，推荐 Docker 部署 MongoDB。

> 注意：
> 1.切换 MongoDB 历史任务可选择自动迁移。
> 2.关于 IP 的填写方式有多种，内网 IP、外网 IP、容器互通等方式。

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

#### 换脸配置

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

#### Banned prompt 限流配置

- 当日触发触发 `Banned prompt detected` n 次后，封锁用户的时长（分钟）配置（白名单用户除外）。

```json
{
  "enable": true,
  "rules": {
    "1": 60,
    "2": 120,
    "3": 600
  }
}
```

## CloudFlare 人机验证

免费自动过人机验证，CloudFlare 自动验证配置示例。

- `CaptchaServer` 验证器地址
- `CaptchaNotifyHook` 验证完成通知回调，默认为你的域名

```json
"CaptchaServer": "http://47.76.110.222:8081",
"CaptchaNotifyHook": "https://ai.trueai.org"
```

## CloudFlare 验证器

仅支持 Windows 部署（并且支持 TLS 1.3，系统要求 Windows11 或 Windows Server 2022），由于 CloudFlare 验证器需要使用到 Chrome 浏览器，所以需要在 Windows 环境下部署，而在 Linux 环境下部署会依赖很多库，所以暂时不支持 Linux 部署。

注意：自行部署需提供 2captcha.com 的 API Key，否则无法使用，价格：1000次/9元，官网：<https://2captcha.cn/p/cloudflare-turnstile>

提示：首次启动会下载 Chrome 浏览器，会比较慢，请耐心等待。

> `appsettings.json` 配置参考

```json
{
  "Demo": null, // 网站配置为演示模式
  "Captcha": {
    "Concurrent": 1, // 并发数
    "Headless": true, // chrome 是否后台运行
    "TwoCaptchaKey": "" // 2captcha.com 的 API Key
  },
  "urls": "http://*:8081" // 默认端口
}
```

## 机器人 Token（可选配置）

本项目利用 Discord 机器人 Token 连接 wss，可以获取错误信息和完整功能，确保消息的高可用性等问题。

[机器人 Token 配置教程](./docs/api.md)

## 作图频繁预防警告

- 任务间隔 30~180 秒，执行前间隔 3.6 秒以上
- 每日最大 200 张
- 每日工作时间，建议 9：10~22：50
- 如果有多个账号，则建议开启垂直领域功能，每个账号只做某一类作品