# Midjourney Proxy

## 介绍

代理 Midjourney 的 Discord 频道，实现 API 形式调用 AI 绘图，公益项目，提供绘图 API 免费使用。

完全开源，不会存在部分开源或部分闭源，欢迎 PR。

功能最全、最安全、占用内存最小（100MB+）的 Midjourney Proxy API ~~

## 交流群

由于目前文档不是很完善，使用上和部署上可能会有问题，欢迎加入交流群，一起讨论和解决问题。

[Midjourney公益群](https://qm.qq.com/q/k88clCkyMS)（QQ群：565908696）

<img src="https://vip.123pan.cn/1816233029/8626602" alt="欢迎" width="360"/>

## 在线预览

公益接口为慢速模式，接口免费调用，账号池由赞助者提供，请大家合理使用。

- 管理后台：<https://ai.trueai.org>
- 账号密码：`无`
- 公益接口：<https://ai.trueai.org/mj>
- 接口文档：<https://ai.trueai.org/swagger>
- 接口密钥：`无`
- CF 自动验证服务器地址：<http://47.76.110.222:8081>
- CF 自动验证服务器文档：<http://47.76.110.222:8081/swagger>

> CF 自动验证配置示例（免费自动过人机验证）

```json
"CaptchaServer": "http://47.76.110.222:8081",
"CaptchaNotifyHook": "https://ai.trueai.org" // 通知回调，默认为当前域名
```

## 预览截图

![欢迎](./docs/screenshots/ui1.png)

![账号](./docs/screenshots/ui2.png)

![任务](./docs/screenshots/ui3.png)

![测试](./docs/screenshots/ui4.png)

![日志](./docs/screenshots/ui5.png)

![接口](./docs/screenshots/uiswagger.png)