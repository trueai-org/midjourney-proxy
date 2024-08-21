## 机器人 Token（可选配置）

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