# Midjourney Proxy

**完全开源，不会存在部分开源或部分闭源，欢迎 PR。**

代理 Midjourney 的 Discord 频道，实现 API 形式调用 AI 绘图，公益项目，全球所有 AI 模型免费使用。

功能最全、最安全、占用内存最小的 Midjourney Proxy 开源项目~~

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
- [x] 支持多账号配置，每个账号可设置对应的任务队列
- [x] 账号池持久化，动态维护
- [x] 支持获取账号 /info、/settings 信息
- [x] 账号 settings 设置
- [x] zstd-stream 安全压缩传输 <https://discord.com/developers/docs/topics/gateway>
- [x] 内嵌管理后台页面 <https://github.com/trueai-org/midjourney-proxy-webui>

## 公益接口（免费）

公益接口为慢速模式，请合理使用。

- 绘图公益接口地址：<https://ai.googlec.cc/mj-relax>
- 绘图公益接口密钥：`无需密钥或输入任意值`

## 客户端推荐

- **ChatGPT Web Midjourney Proxy**: <https://github.com/Dooy/chatgpt-web-midjourney-proxy> 
  - 打开网站 <https://vercel.ddaiai.com> -> 设置 -> MJ 绘画接口地址 -> <https://ai.googlec.cc/mj-relax>

- **GoAmzAI**: <https://github.com/Licoy/GoAmzAI>
  -	打开后台 -> 绘画管理 -> 新增 -> MJ 绘画接口地址 -> <https://ai.googlec.cc/mj-relax/mj>

## 配置项
- mj.accounts: 参考 [账号池配置](./docs/config.md#%E8%B4%A6%E5%8F%B7%E6%B1%A0%E9%85%8D%E7%BD%AE%E5%8F%82%E8%80%83)
- mj.task-store.type: 任务存储方式，默认in_memory(内存\重启后丢失)，可选redis
- mj.task-store.timeout: 任务存储过期时间，过期后删除，默认30天
- mj.api-secret: 接口密钥，为空不启用鉴权；调用接口时需要加请求头 mj-api-secret
- mj.translate-way: 中文prompt翻译成英文的方式，可选null(默认)、baidu、gpt
- 更多配置查看 [配置项](./docs/config.md)

## 安装与使用

### 快速启动

> Docker 版本

```bash
# 阿里云镜像（推荐国内使用）
docker pull registry.cn-guangzhou.aliyuncs.com/trueai-org/midjourney-proxy
docker run --name mjproxy -d --restart=always \
 -p 8081:8080 --user root \
 -e TZ=Asia/Shanghai \
 -v /etc/localtime:/etc/localtime:ro \
 -v /etc/timezone:/etc/timezone:ro \
 registry.cn-guangzhou.aliyuncs.com/trueai-org/midjourney-proxy

# 公益站点启动配置示例
docker pull registry.cn-guangzhou.aliyuncs.com/trueai-org/midjourney-proxy
docker run --name mjproxy -d --restart=always \
 -p 8081:8080 --user root \
 -v /root/mjproxy/logs:/app/logs:rw \
 -v /root/mjproxy/data:/app/data:rw \
 -v /root/mjproxy/appsettings.Production.json:/app/appsettings.Production.json:ro \
 -e TZ=Asia/Shanghai \
 -v /etc/localtime:/etc/localtime:ro \
 -v /etc/timezone:/etc/timezone:ro \
 registry.cn-guangzhou.aliyuncs.com/trueai-org/midjourney-proxy

# 生产环境启动配置示例
docker pull registry.cn-guangzhou.aliyuncs.com/trueai-org/midjourney-proxy
docker run --name mjapi -d --restart=always \
 -p 8082:8080 --user root \
 -v /root/mjapi/logs:/app/logs:rw \
 -v /root/mjapi/data:/app/data:rw \
 -v /root/mjapi/appsettings.Production.json:/app/appsettings.Production.json:ro \
 -e TZ=Asia/Shanghai \
 -v /etc/localtime:/etc/localtime:ro \
 -v /etc/timezone:/etc/timezone:ro \
 registry.cn-guangzhou.aliyuncs.com/trueai-org/midjourney-proxy
```

> 配置使用

启动 docker 后，配置代理为 `http://ip:8081/mj` 即可

![示例](./docs/screenshots/chrome_EDzoztHa2b.png)

## 机器人 Token（必须配置）

本项目利用 Discord 机器人 Token 连接 wss，可以获取错误信息和完整功能。

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