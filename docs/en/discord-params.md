## Getting Discord Configuration Parameters

### 1. Obtaining User Token
Enter the channel, open the network tab, refresh the page, and find the request for `messages`. The authorization here is the user token, which should be set to `mj.discord.user-token` later.

![User Token](../img_8.png)

### 2. Obtaining Server ID and Channel ID

Extract the server ID and channel ID from the channel's URL, and set them in the configuration options later.
![Guild Channel ID](../img_9.png)

### How to Capture the Event: INTERACTION_IFRAME_MODAL_CREATE

> Reference

https://github.com/dolfies/discord.py-self/discussions/573

> Note to modify command.ts to: https://discord.com/api/v9/users/@me/application-command-index

https://github.com/bao-io/midjourney-sdk
https://www.npmjs.com/package/midjourney-sdk

Capturing specific frontend events, such as `INTERACTION_IFRAME_MODAL_CREATE`, while handling WebSocket connections and session management can be a challenge. The following tutorial explains how to correctly capture this event based on actual conversations.


#### Environment Setup
- Technology Stack: TypeScript
- Application Scenario: WebSocket Connection Management

#### Overview of Steps

1. **Create WebSocket Connection**:
    Ensure to capture and save the `session_id` when creating the WebSocket connection. This `session_id` is crucial for all subsequent interactions.

2. **Capture `session_id`**:
    When establishing the WebSocket connection, you will typically receive a message of type `READY` from the server, which contains the `session_id`.

3. **Use `session_id` to Send Requests**:
    When sending a request to create an interactive iframe modal, you must use the `session_id` obtained from the WebSocket connection.

4. **Handle and Listen for Events**:
    After sending data with the `session_id`, the system should be able to correctly trigger the `INTERACTION_IFRAME_MODAL_CREATE` event.

#### Example Code

```typescript
// Assuming the websocket is already connected and in a usable state
websocket.onmessage = function(event) {
    let data = JSON.parse(event.data);
    let type = data.type;

    // When the server sends a READY type message, save the session_id
    if (type === 'READY') {
        this.opts.session_id = data.session_id;
    }
};

// Use the saved session_id to send a request
function sendInteractionRequest() {
    let request = {
        type: 'INTERACTION_IFRAME_MODAL_CREATE',
        session_id: this.opts.session_id
    };
    websocket.send(JSON.stringify(request));
}

// Listen for events
websocket.onmessage = function(event) {
    let data = JSON.parse(event.data);
    if (data.type === 'INTERACTION_IFRAME_MODAL_CREATE') {
        console.log('Modal create interaction triggered successfully.');
    }
};
```

#### Troubleshooting

- **Issue**: The `INTERACTION_IFRAME_MODAL_CREATE` event is not triggered.
  - **Solution**: Ensure that the `session_id` being sent is the one received when establishing the WebSocket connection. If the `session_id` is incorrect, the event will not be triggered properly.

By following the above steps and example code, you should be able to effectively capture and handle the `INTERACTION_IFRAME_MODAL_CREATE` event. This requires ensuring that you manage and use the `session_id` correctly to maintain data consistency and trigger the event properly.

## Related Links

- https://github.com/dolfies/discord.py-self
- https://github.com/bao-io/midjourney-sdk
- https://github.com/webman-php/midjourney-proxy
- https://github.com/novicezk/midjourney-proxy
- https://github.com/litter-coder/midjourney-proxy-plus
- https://github.com/Dooy/chatgpt-web-midjourney-proxy
- https://github.com/erictik/midjourney-api
- https://github.com/yokonsan/midjourney-api
- https://github.com/imagineapi/imagineapi

## Useful Links
- OpenAI Official Site: <https://openai.com>
- Discord Official Site: <https://discord.com>
- Discord Developer Portal: <https://discord.com/developers/applications>
- Wenxin Yiyan Official Site: <https://cloud.baidu.com/product/wenxinworkshop>
- Xinghuo Model Official Site: <https://xinghuo.xfyun.cn/>
- Api2d Official Site: <https://api2d.com/>
- OpenAI-SB Official Site: <https://www.openai-sb.com>
- Baota Official Site: <https://bt.cn>
- Alibaba Cloud Official Site: <https://aliyun.com>
- Tencent Cloud Official Site: <https://cloud.tencent.com/>
- SMS Bao Official Site: <http://www.smsbao.com/>
- OneAPI Project: <https://github.com/songquanpeng/one-api>