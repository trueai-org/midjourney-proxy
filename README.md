# Midjourney Proxy

**开发进行中，即将发布正式版，敬请期待！**

Midjourney api 的 C# 版本。

完全开源，不会存在部分开源或闭源的可能，欢迎 PR。

## 主要功能

- [x] 支持 Imagine 指令和相关动作 [V1/V2.../U1/U2.../R]
- [x] Imagine 时支持添加图片 base64，作为垫图
- [x] 支持 Blend (图片混合)、Describe (图生文) 指令
- [x] 支持任务实时进度
- [x] 支持中文 prompt 翻译，需配置百度翻译或 gpt
- [x] prompt 敏感词预检测，支持覆盖调整
- [x] user-token 连接 wss，可以获取错误信息和完整功能
- [x] 支持多账号配置，每个账号可设置对应的任务队列
- [x] 支持 Shorten(prompt分析) 指令
- [x] 支持焦点移动: Pan ⬅️ ➡️ ⬆️ ⬇️
- [x] 支持局部重绘: Vary (Region) 🖌
- [x] 支持几乎所有的关联按钮动作和 🎛️ Remix 模式
- [x] 账号池持久化，动态维护
- [x] 支持获取账号 /info、/settings信 息
- [ ] 账号 settings 设置
- [ ] 支持 niji bot 机器人
- [ ] 支持获取图片的 seed 值
- [ ] 支持 InsightFace 人脸替换机器人
- [ ] 内嵌管理后台页面
- [ ] 支持图片变焦: Zoom 🔍
- [ ] 支持局部重绘 

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

# 演示站点启动配置
docker pull registry.cn-guangzhou.aliyuncs.com/trueai-org/midjourney-proxy
docker run --name mjproxy -d --restart=always \
 -p 8081:8080 --user root \
 -v /root/mjproxy/appsettings.Production.json:/app/appsettings.Production.json:ro \
 -e TZ=Asia/Shanghai \
 -v /etc/localtime:/etc/localtime:ro \
 -v /etc/timezone:/etc/timezone:ro \
 registry.cn-guangzhou.aliyuncs.com/trueai-org/midjourney-proxy
```

## 相关文档
1. [API接口说明](./docs/api.md)

## 注意事项
1. 作图频繁等行为，可能会触发midjourney账号警告，请谨慎使用

## 其它
如果觉得这个项目对您有所帮助，请帮忙点个star