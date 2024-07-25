## 获取discord配置参数

### 1. 获取用户Token
进入频道，打开network，刷新页面，找到 `messages` 的请求，这里的 authorization 即用户Token，后续设置到 `mj.discord.user-token`

![User Token](img_8.png)

### 2. 获取服务器ID、频道ID

频道的url里取出 服务器ID、频道ID，后续设置到配置项
![Guild Channel ID](img_9.png)


### 如何捕获事件：INTERACTION_IFRAME_MODAL_CREATE

> 参考

https://github.com/dolfies/discord.py-self/discussions/573

> 注意修改 command.ts 为：https://discord.com/api/v9/users/@me/application-command-index

https://github.com/bao-io/midjourney-sdk
https://www.npmjs.com/package/midjourney-sdk

在处理 WebSocket 连接和会话管理时，捕获特定的前端事件，如 `INTERACTION_IFRAME_MODAL_CREATE`，可以是一个挑战。以下教程基于实际对话，解释了如何正确捕获这一事件。


#### 环境设定
- 技术栈：TypeScript
- 应用场景：WebSocket 连接管理

#### 步骤概述

1. **创建 WebSocket 连接**：
    确保在创建 WebSocket 连接时捕获并保存 `session_id`。这个 `session_id` 是后续所有交互的关键。

2. **捕获 `session_id`**：
    在建立 WebSocket 连接时，通常会从服务器接收到一个类型为 `READY` 的消息，该消息包含了 `session_id`。

3. **使用 `session_id` 发送请求**：
    在发送请求以创建交互式 iframe 模态框时，必须使用从 WebSocket 连接中获得的 `session_id`。

4. **处理和监听事件**：
    使用该 `session_id` 发送数据后，系统应能够正确触发 `INTERACTION_IFRAME_MODAL_CREATE` 事件。

#### 代码示例

```typescript
// 假设 websocket 已经连接并且是可用的状态
websocket.onmessage = function(event) {
    let data = JSON.parse(event.data);
    let type = data.type;

    // 当服务器发送 READY 类型的消息时，保存 session_id
    if (type === 'READY') {
        this.opts.session_id = data.session_id;
    }
};

// 使用保存的 session_id 发送请求
function sendInteractionRequest() {
    let request = {
        type: 'INTERACTION_IFRAME_MODAL_CREATE',
        session_id: this.opts.session_id
    };
    websocket.send(JSON.stringify(request));
}

// 监听事件
websocket.onmessage = function(event) {
    let data = JSON.parse(event.data);
    if (data.type === 'INTERACTION_IFRAME_MODAL_CREATE') {
        console.log('Modal create interaction triggered successfully.');
    }
};
```

#### 常见问题解决

- **问题**: `INTERACTION_IFRAME_MODAL_CREATE` 事件不触发。
  - **解决方案**: 确保发送的 `session_id` 是在建立 WebSocket 连接时接收的那个。如果 `session_id` 错误，该事件不会被正确触发。

通过以上步骤和示例代码，您应该能够有效地捕获并处理 `INTERACTION_IFRAME_MODAL_CREATE` 事件。这需要确保您正确管理和使用 `session_id`，以保证数据的一致性和事件的正确触发。

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