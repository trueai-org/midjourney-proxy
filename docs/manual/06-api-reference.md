# 06 API 参考

- **适用版本**：v12.1.6
- **适用读者**：需要对接绘图接口的开发者
- **前置条件**：已有一个可用实例与管理端凭据，了解 HTTP/JSON 与异步任务模型

本章描述对外接口的鉴权方式、路径约定、提交与查询接口、任务对象结构、回调机制以及错误与限流表现。示例中的地址与凭据均为占位符：`<BASE_URL>` 为不带结尾斜杠的服务根地址，`<YOUR_API_SECRET>` 为管理员或用户令牌，`<TASK_ID>` 为提交接口返回的任务 ID。

## 鉴权与路径前缀

### 接口路径前缀

提交、任务、换脸三组接口都提供四套路径前缀，用于直接指定绘图速度：

| 前缀 | 作用 | 示例 |
| --- | --- | --- |
| `/mj/...` | 默认，速度由账号过滤或提示词决定 | `/mj/submit/imagine` |
| `/mj-fast/mj/...` | 强制快速模式 | `/mj-fast/mj/submit/imagine` |
| `/mj-turbo/mj/...` | 强制 Turbo 模式 | `/mj-turbo/mj/submit/imagine` |
| `/mj-relax/mj/...` | 强制慢速模式 | `/mj-relax/mj/submit/imagine` |

三组接口分别是 `/mj/submit`、`/mj/task`、`/mj/insight-face`，上表以提交接口为例。速度优先级从高到低为：路径前缀 → 请求体 `accountFilter.modes`（按数组顺序）→ 提示词中的 `--fast`、`--relax`、`--turbo`；最终任务的 `mode` 取合并去重后的第一个模式。

`/mj/home`、`/mj/profile`、`/Health`、`/health`、`/mj/admin/*` 没有速度前缀变体。反向代理必须保留 `/mj` 前缀，也不能把 `/mj-fast`、`/mj-turbo`、`/mj-relax` 重写掉。

### 辅助接口

| 接口 | 鉴权 | 说明 |
| --- | --- | --- |
| `GET /Health` | 匿名 | 返回 `Status`、`Timestamp`、`MachineName`、`ProcessId`（JSON 默认 camelCase 输出） |
| `GET /health` | 匿名 | MapHealthChecks 端点，与 `/Health` 并存，返回健康检查文本 |
| `GET /mj/home` | 匿名 | 返回 `Result<HomeDto>`，含今日/昨日/总绘图、可用次数、Top IP、系统信息、实时并发与队列 |
| `GET /mj/profile` | 仅管理员 | 官网个性化配置；功能未开启时返回 403 |
| `POST /mj/insight-face/swap` | 需登录或开启访客 | 图片换脸；需开启换脸且换脸服务自身的 Token 配置非空 |
| `POST /mj/insight-face/video-swap` | 需登录 + 白名单 | 视频换脸（multipart/form-data）；同样需要换脸服务已启用，非白名单用户不可用 |

### 请求头与凭据传递

服务按以下顺序读取第一个非空令牌：`Authorization` → `mj-api-secret` → `x-api-key` → `api-key`。头名不区分大小写，值可以带 `Bearer ` 前缀，服务会自动去掉。推荐使用 `Authorization`，兼容 OpenAI 风格的客户端通常也支持 `x-api-key`。

匿名接口只有 `/mj/home`、`/health`、`/debug`（管理端登录、注册、当前用户等少量接口也允许匿名，见第七章）。其余接口在未携带令牌且未开启访客时由中间件直接拦截：

| 场景 | HTTP | 响应体 |
| --- | --- | --- |
| 未带令牌且未开启访客 | 401 | 纯文本 `Authorization header missing.` |
| 访问 `/mj/admin/*` 但不是管理员 | 401 | 纯文本 `账号无权限` |
| 用户被禁用 | 401 | 纯文本 `账号已禁用` |
| 用户不可用 | 403 | 纯文本 `账号不可用` |

这些拦截响应不是 JSON，不要按 `Result` 解析。演示模式跳过鉴权，但写操作是否可用仍取决于具体控制器。

### 管理模式与普通模式的区别

| 维度 | 普通模式 | 管理模式 |
| --- | --- | --- |
| 典型路径 | `/mj/submit/*`、`/mj/task/*`、`/mj/insight-face/*` | `/mj/admin/*` |
| 令牌角色 | 用户令牌；开启访客时可无令牌 | 必须是 `ADMIN` |
| 限额 | 受用户或 IP 的日次数、总次数、并发、队列限制 | 管理员不受 IP 限流；用户限额仍取决于账号配额 |
| 返回结构 | 提交返回 `SubmitResultVO`，查询返回裸对象或数组 | 多为 `Result`，分页列表使用 `StandardTableResult` |
| 使用方 | 绘图客户端、业务后端 | 管理后台、运维脚本 |

管理端分页接口统一返回 `StandardTableResult<T>`（`list` 与 `pagination`）；需要附加统计信息时使用 `StandardTableResult<T, TStat>`，统计结果位于 `extendData`。

## 提交接口

提交类接口统一使用 `POST` 与 JSON 请求体。所有提交接口都支持公共字段 `state`（自定义状态，≤255 字符）与 `notifyHook`（回调地址，≤2000 字符）。`accountFilter` 仅 `imagine`、`blend`、`edit`、`retexture`、`describe`、`shorten`、`upload-discord-images` 支持；`action`、`modal`、`video` 不支持。超长时提交失败：`/imagine` 会返回 `code: 4` 的 `SubmitResultVO`，其他入口可能由全局过滤器返回 HTTP 200 的 `Result`。

### 绘图提交

**`POST /mj/submit/imagine`**

| 字段 | 必填 | 说明 |
| --- | --- | --- |
| `prompt` | 是 | 提示词，不能为空或纯空白 |
| `base64Array` | 否 | 垫图数组，元素为 data URL 或 `http(s)` 图片 URL |
| `botType` | 否 | `mj`/`MID_JOURNEY`/`niji`/`NIJI_JOURNEY`，缺省按 Midjourney |
| `accountFilter` | 否 | 选号过滤，见“提交参数与约束” |
| `state` / `notifyHook` | 否 | 自定义状态 / 本次任务的回调地址 |

```json
{
  "prompt": "a cat sitting on a windowsill --ar 3:2",
  "botType": "mj",
  "base64Array": ["data:image/png;base64,<BASE64_DATA>", "https://example.com/reference.png"],
  "state": "order-1001",
  "notifyHook": "https://example.com/mj/callback",
  "accountFilter": { "instanceId": "<CHANNEL_ID>", "modes": ["FAST"], "remix": false, "remark": "vip" }
}
```

```json
{
  "code": 1,
  "description": "提交成功",
  "result": "<TASK_ID>",
  "properties": { "discordInstanceId": "<CHANNEL_ID>", "discordChannelId": "<CHANNEL_ID>" }
}
```

排队时返回 `code: 22`，`description` 说明前面还有多少任务，`properties.numberOfQueues` 为排队数量，`result` 仍是任务 ID。开启 Niji 自动转换时，Midjourney 提示词包含 `--niji` 会切到 Niji 账号；提示词包含 `--video` 且视频已启用时，动作会识别为 `VIDEO`。垫图 URL 直接复用；base64 在请求线程只解码，WebP 转换与上传由后台工作线程完成。

**`POST /mj/submit/blend`**：`base64Array` 为 2~5 张图片；`dimensions` 取 `PORTRAIT`（2:3）、`SQUARE`（1:1，默认）、`LANDSCAPE`（3:2）。数量越界或显式传 `null` 的 `dimensions` 返回 `code: 4`。

**`POST /mj/submit/edit` 与 `/mj/submit/edits`**：两个路径完全同义，推荐只用 `/edit`。`image` 支持 `http(s)` URL 或 base64（纯 base64 会自动补 PNG 前缀），`prompt` 必填；接口固定使用 Midjourney 机器人类型。

**`POST /mj/submit/retexture`**：字段与 `/edit` 相同（`image` + `prompt`），动作记录为 `RETEXTURE`，同样固定使用 Midjourney 机器人类型。

**`POST /mj/submit/video`**

| 字段 | 必填 | 说明 |
| --- | --- | --- |
| `prompt` | 是 | 视频提示词 |
| `motion` | 否 | `low`（默认）或 `high` |
| `image` / `endImage` | 扩展时可为空 | 首帧 / 尾帧图片，支持 URL 或 base64 |
| `loop` | 否 | 是否循环，追加 `--end loop` |
| `action` | 否 | 固定为 `extend` 表示视频扩展 |
| `taskId` / `index` | 扩展时必填 | 父任务 ID（必须已成功）/ 视频索引（从 0 开始，内部转 1 基） |
| `videoType` | 否 | `vid_1.1_i2v_480`（标清，默认）或 `vid_1.1_i2v_720`（高清） |
| `batchSize` | 否 | `1`、`2`、`4`，缺省 4；4 不会追加 `--bs` |

使用前必须开启视频功能，否则返回 `code: 4`、`description: 视频功能未启用`。视频接口没有 `accountFilter`；扩展模式要求账号开启 Remix，高清视频要求账号具备高清能力，否则可能返回“无可用的账号实例”。

**`POST /mj/submit/upload-discord-images`**：请求体只需 `base64Array`（可附带 `accountFilter`）。成功时 `result` 是图片 URL 字符串数组，而非单个任务 ID；任一图片上传或发送失败会返回对应错误码，已上传成功的图片不回滚。

### 变体与操作提交

**`POST /mj/submit/action`**

| 字段 | 必填 | 说明 |
| --- | --- | --- |
| `taskId` | 是 | 父任务 ID，父任务必须已成功 |
| `customId` | 是 | 按钮动作标识，决定 U/V、变焦、平移、重绘等动作 |
| `enableRemix` | 否 | 强制使用 Remix；账号未开启 Remix 时返回参数错误 |
| `tools` | 否 | 工具列表；父任务不存在时可携带 `get_parent_task` 尝试拉取第三方父任务；另接受 `strong`（变化强度兼容字段，主要依据 `customId` 判定动作） |

| `customId` 前缀 | 动作 |
| --- | --- |
| `MJ::JOB::upsample::` / `MJ::JOB::upsample_` | 放大（`UPSCALE`）/ 高清放大（`UPSCALE_HD`） |
| `MJ::JOB::variation::` / `high_variation` / `low_variation` | 变体（`VARIATION`） |
| `MJ::JOB::reroll::` | 重新生成，按父任务类型决定是否允许 |
| `MJ::Outpaint::` / `MJ::CustomZoom::` | 变焦（`ZOOM`） |
| `MJ::Inpaint::` | 局部重绘（记录为 `VARIATION`） |
| `MJ::JOB::pan_` | 平移（`PAN`） |
| `MJ::JOB::animate_` | 视频（`VIDEO`） |

需要二次确认的动作返回 `code: 21`：

```json
{ "code": 21, "description": "Waiting for window confirm", "result": "<TASK_ID>", "properties": { "finalPrompt": "a cat --ar 3:2", "remix": true } }
```

此时任务处于 `MODAL`，客户端必须调用 `/mj/submit/modal` 补交提示词，或调用 `/mj/task/{id}/cancel` 取消。未知 `customId` 返回 `code: 4`、`description: 未知的 Action 类型`；父任务不存在返回 HTTP 404 与 `code: 3`。

**`POST /mj/submit/modal`**：`taskId` 必填；`prompt` 为空时优先继承父任务的 `prompt`，再回退到最终提示词，仍为空返回 `code: 4`；`maskBase64` 为局部重绘蒙版，**当前版本未使用该字段，蒙版不会生效**，接入方不要依赖它做精确区域控制。成功返回 `code: 1` 或 `22`。

### 图生文提交

**`POST /mj/submit/describe`**：`base64` 与 `link` 二选一，`link` 必须以 `http://` 或 `https://` 开头；`language` 为 `zh` 或 `zh_cn` 时，任务完成后尝试把结果翻译为中文（是否真正翻译取决于是否配置翻译服务）；支持 `botType` 与 `accountFilter`。

**`POST /mj/submit/shorten`**：使用与 `/imagine` 相同的 DTO，但只读取 `prompt`、`botType`、`state`、`notifyHook`、`accountFilter`。完成后缩短后的提示词出现在任务对象中。

### 提交参数与约束

`accountFilter` 字段：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `instanceId` | string | 只使用指定实例/频道 ID |
| `modes` | string[] | `RELAX`、`FAST`、`TURBO`，按数组顺序优先 |
| `remix` / `nijiRemix` | bool? | 只选择对应 Remix 状态匹配的账号 |
| `remixAutoConsidered` | bool? | 过滤时是否把 Remix 自动提交视为未开启 Remix |
| `remark` | string | 按账号备注过滤 |

`accountFilter` 仅 imagine、blend、edit、retexture、describe、shorten、upload-discord-images 支持；action、modal、video 不支持。

公共约束：

- `state` 超过 255 字符、`notifyHook` 超过 2000 字符会直接失败；
- 未开启 Redis 时提交返回“服务暂不可用，请联系管理员”；
- 用户或访客受日绘图次数、总绘图次数、并发数、队列数限制，触发时返回 `Result` 形式的业务错误；
- 提交接口没有幂等键，同一请求重复发送会创建多个任务，客户端应自行去重；
- base64 请求体受服务端最大请求体限制（Kestrel 可通过环境变量调整），大图优先传 URL。

## 任务接口

任务接口用于查询提交后的执行状态。查询类接口没有归属校验，拿到任务 ID 即可读取（取消接口另有管理员限制），部署时应通过鉴权、访客开关和反向代理限制访问范围。

### 单任务查询

**`GET /mj/task/{id}/fetch`** 成功时直接返回裸 `TaskInfo`，不包 `Result`：

```json
{
  "id": "<TASK_ID>", "action": "IMAGINE", "status": "SUCCESS", "progress": "100%",
  "imageUrl": "https://cdn.example.com/result.png",
  "thumbnailUrl": "https://cdn.example.com/result_640.webp",
  "submitTime": 1761696000000, "finishTime": 1761696060000, "failReason": null
}
```

任务不存在时返回 **HTTP 204**（不是 404，也不是空对象），客户端应停止轮询并标记为已清理。

**`POST /mj/task/{id}/cancel`** 成功返回 HTTP 200 空响应。实现上只有 `ADMIN` 能通过匿名判定；普通用户即使拥有该任务，也可能收到 HTTP 400 纯文本“演示模式，禁止操作”，请把它当作运维接口使用。

**`GET /mj/task/{id}/image-seed`** 返回 `SubmitResultVO`，`result` 为 seed 字符串。任务不存在返回 404；缺少消息 hash 返回 400；有 hash 但没有 seed 时会尝试重新获取。

### 任务列表查询

**`GET /mj/task/list`** 返回全库最近 100 条任务（按提交时间倒序），不区分用户，也不支持分页或过滤，适合本地调试，不建议在生产环境频繁调用。

**`POST /mj/task/list-by-condition` 与 `POST /mj/task/list-by-ids`** 完全同义，请求体只使用 `ids`：

```json
{ "ids": ["<TASK_ID>", "<TASK_ID>"] }
```

`ids` 为空或全部为空白时返回空数组；返回顺序不保证与请求顺序一致，客户端应按 `id` 自行映射。该接口只按 ID 查询，不会按状态、时间或用户过滤。

### 任务分页与过滤

对外任务接口没有分页参数。需要分页或按动作/状态/时间/用户筛选时，使用管理端 `/mj/admin/tasks`（需要 `ADMIN` 令牌，返回 `StandardTableResult<TaskInfo, TaskInfoQueryStatResult>`，页大小上限 100），或由业务侧把任务 ID 与订单表关联后，用 `/mj/task/list-by-ids` 批量补齐状态。

轮询建议：提交成功后立即记录任务 ID；每 2~5 秒调用一次 `/fetch` 或批量 `/list-by-ids`；遇到 `SUCCESS`、`FAILURE`、`CANCEL` 或 204 停止；有回调时以回调为准，轮询只做兜底对账。

## 任务对象

### 字段说明

`TaskInfo` 使用 camelCase，枚举按字符串输出（如 `SUCCESS`、`IMAGINE`）。主要字段：

| 字段 | 说明 |
| --- | --- |
| `id` / `parentId` | 任务 ID / 父任务 ID（U/V、变体、扩展等操作有值） |
| `botType` / `realBotType` | 期望的机器人类型 / 实际机器人类型 |
| `userId` / `isWhite` | 提交用户 ID（访客为空）/ 提交时是否白名单 |
| `nonce` / `messageId` / `messageIds` | 提交随机串 / 上游消息 ID / 各阶段消息 ID 列表 |
| `instanceId` / `subInstanceId` | 账号实例或频道 ID / 子频道 ID |
| `action` / `status` | 任务动作 / 任务状态，见下文 |
| `prompt` / `promptEn` / `promptFull` | 提示词 / 英文提示词 / 上游返回的完整提示词 |
| `description` / `language` / `state` | 任务描述 / 语言参数 / 提交时的自定义状态 |
| `submitTime` / `startTime` / `finishTime` | 提交、开始、完成时间，均为 Unix 毫秒 |
| `imageUrl` / `baseImageUrl` / `thumbnailUrl` | 主结果图或视频 / 垫图 / 缩略图 |
| `imageUrls` / `videoUrls` | 图片、视频列表，元素为 `{ url, thumbnail }` |
| `progress` / `failReason` / `useTime` | 进度字符串 / 失败原因 / 耗时（如 `12.34 s`，未完成为 `-`） |
| `isThirdParty` / `isPartner` / `partnerTaskId` / `isOfficial` / `officialTaskId` | 是否外部任务 / 是否悠船任务 / 悠船任务 ID / 是否官网任务 / 官网任务 ID |
| `buttons` / `seed` / `clientIp` / `jobId` | 可执行按钮（含 `customId`、`label`、`emoji`、`style`、`type`）/ seed / 提交方 IP / 图片 ID 或 hash |
| `mode` / `requestMode` / `accountFilter` | 最终速度 / 期望速度 / 选号过滤快照 |
| `url` / `proxyUrl` / `height` / `width` / `size` / `contentType` / `videoUrl` / `videoType` | 上游原始内容信息与视频地址、分辨率类型 |
| `version` / `properties` | 解析出的版本（如 `v 6.1`）/ 扩展属性字典 |

### 状态取值与含义

| 状态 | 数值 | 含义 |
| --- | --- | --- |
| `NOT_START` | 0 | 已创建，尚未开始 |
| `SUBMITTED` | 1 | 已提交到上游，等待执行 |
| `IN_PROGRESS` | 3 | 执行中，`progress` 会更新 |
| `FAILURE` | 4 | 失败，查看 `failReason` |
| `SUCCESS` | 5 | 成功 |
| `MODAL` | 6 | 等待弹窗确认，需调用 `/mj/submit/modal` |
| `CANCEL` | 7 | 已取消 |

`SUCCESS`、`FAILURE`、`CANCEL` 为终态。`action` 的主要取值：

| 动作 | 含义 | 备注 |
| --- | --- | --- |
| `IMAGINE` / `UPSCALE` / `VARIATION` | 文生图 / 放大 / 变体（含局部重绘） | 常用 |
| `DESCRIBE` / `BLEND` / `SHORTEN` | 图生文 / 多图混合 / 提示词简化 | 常用 |
| `ACTION` / `PAN` / `ZOOM` | 通用动作 / 平移 / 变焦 | `PAN`、`ZOOM` 常用 |
| `SWAP_FACE` / `SWAP_VIDEO_FACE` | 图片换脸 / 视频换脸 | 需开启换脸；视频换脸还要求白名单 |
| `VIDEO` / `EDIT` / `RETEXTURE` / `UPSCALE_HD` | 视频 / 编辑 / 转绘 / 高清放大 | 常用 |

历史动作仅用于读取既有数据，不应在新任务中使用。

### 进度与结果字段

- `progress`：上游进度字符串，可能为 `0%`、`50%`、`100%` 或空；回调去重会解析其中的百分比。
- `imageUrls` / `videoUrls`：多图、视频任务的结果列表，元素固定为 `url` 与 `thumbnail`；单图任务可能只有 `imageUrl`/`thumbnailUrl`。
- `buttons`：成功后可执行的按钮。客户端把 `customId` 原样传给 `/mj/submit/action`，不要自行拼接。
- `useTime` / `seed`：耗时（仅在开始与完成时间都有值时计算，保留两位小数）/ 通过 `/mj/task/{id}/image-seed` 获取或任务完成时写入。
- `properties`：扩展属性字典，常见键为 `notifyHook`、`nonce`、`finalPrompt`、`discordInstanceId`、`messageHash`，由服务内部维护，客户端只读。

## 回调

### 回调地址配置

任务级 `notifyHook` 优先；请求未传时使用实例全局配置的回调地址。地址长度上限 2000 字符，超长会在提交阶段失败。回调地址必须是服务端可访问的 HTTP(S) 地址，建议使用内网地址或带签名的公网地址。

### 回调载荷结构

回调使用 `POST`、`Content-Type: application/json`，请求体是完整的 `TaskInfo`，字段为 camelCase，枚举为字符串：

```json
{
  "id": "<TASK_ID>", "status": "IN_PROGRESS", "progress": "50%", "action": "IMAGINE",
  "prompt": "a cat sitting on a windowsill --ar 3:2",
  "submitTime": 1761696000000, "startTime": 1761696010000, "finishTime": null, "failReason": null,
  "properties": { "notifyHook": "https://example.com/mj/callback", "nonce": "<NONCE>", "finalPrompt": "a cat sitting on a windowsill --ar 3:2", "discordInstanceId": "<CHANNEL_ID>" }
}
```

完成后同一任务还会推送 `status: SUCCESS`、`progress: 100%` 与结果地址。接收方应返回 2xx；非 2xx 只会被记录日志，不会改变任务状态。

### 投递重试与幂等处理

投递规则：同一任务只有状态或进度“更高”时才推送，相同状态且进度不增加会被忽略；`SUCCESS` 或 `FAILURE` 推送后，服务会清理该任务的内存状态；回调没有自动重试，网络失败、接收方超时或非 2xx 都可能丢失该次通知；回调并发受通知线程池限制，池大小有上限，接收方不要做耗时处理。

因此接收方必须幂等：以 `id + status + progress` 作为去重键，重复通知只处理一次；对 `SUCCESS` 只落库一次结果。回调丢失时用 `/mj/task/{id}/fetch` 对账。不要在回调里同步调用耗时下游，先落库再异步处理。

## 错误与限流

### 常见错误码

提交接口返回 `SubmitResultVO`，业务码如下：

| code | 含义 | 说明 |
| --- | --- | --- |
| 1 | 成功 | 任务已受理 |
| 3 | 未找到 | 关联任务、实例或资源不存在 |
| 4 | 校验错误 | 参数缺失、格式错误、状态不满足 |
| 9 | 系统异常 | 上游或内部执行失败 |
| 21 | 需要二次确认 | Remix、局部重绘、自定义变焦等，任务进入 `MODAL` |
| 22 | 排队中 | 已受理但未开始，`properties.numberOfQueues` 为排队数 |
| 23 / 24 | 队列已满 / 敏感词 | 仅定义，当前版本不会返回 |

HTTP 层表现（业务校验失败仍可能返回 HTTP 200 + `code: 4`）：

| HTTP | 响应体 | 典型场景 |
| --- | --- | --- |
| 200 | `SubmitResultVO` | 提交成功或业务校验失败 |
| 200 | `Result`（`success:false`） | 次数/并发/队列限制、部分提交入口的 `state`/`notifyHook` 超长、模型绑定失败 |
| 204 | 空 | `/fetch` 任务不存在 |
| 404 | `SubmitResultVO` | action/modal/video 扩展的父任务不存在、image-seed 任务不存在 |
| 401 / 403 / 429 | 纯文本或空 | 鉴权失败、角色不足、账号禁用/不可用、触发 IP 限流 |
| 500 | `Result`（`message: 操作失败`） | 未捕获异常 |

业务校验失败时返回 `{ "code": 4, "description": "prompt不能为空", "result": null, "properties": {} }`；触发用户限额时由全局异常过滤器返回 `{ "success": false, "code": -1, "message": "用户今日绘图次数已达上限" }`，HTTP 仍为 200。

### 限流触发条件

- 限流只作用于非匿名接口，`/mj/home`、`/health`、`/debug` 不参与；
- 白名单用户与管理员不参与限流；
- 规则按路径模式匹配（支持前缀、后缀、包含通配），分别统计单 IP、/24 段、/16 段；
- 超出普通限流返回 HTTP 429，响应体为空；命中黑名单或黑名单规则返回 HTTP 403，响应体为空。限流响应没有 JSON 错误体，客户端不要等待 `description`。

### 重试建议

| 场景 | 建议 |
| --- | --- |
| `code: 22` | 已受理，不要重复提交；按 `result` 轮询任务 |
| `code: 21` | 不要重试原请求；调用 `/mj/submit/modal` 或取消 |
| `code: 4` | 修正参数后再试，盲目重试只会继续失败 |
| `code: 9` | 可指数退避重试 2~3 次，并记录任务 ID |
| HTTP 429 | 按指数退避降低频率，避免固定间隔冲击 |
| HTTP 5xx | 提交可能已落库，先用业务侧 `state` 去重再重试 |
| 回调失败 | 不依赖回调重试，改用 `/fetch` 对账 |

提交接口不是幂等的。需要“最多一次”语义时，客户端应在订单表中记录 `state` 或业务单号，收到成功响应后立即落库，重试前先查询是否已有任务 ID。

## 客户端接入

### 请求示例与调用顺序

最小调用链：提交 → 拿任务 ID → 轮询或等回调 → 执行按钮动作。

```bash
curl -X POST '<BASE_URL>/mj/submit/imagine' \
  -H 'Authorization: Bearer <YOUR_API_SECRET>' \
  -H 'Content-Type: application/json' \
  -d '{"prompt":"a cat --ar 3:2","state":"order-1001"}'

curl '<BASE_URL>/mj/task/<TASK_ID>/fetch' \
  -H 'Authorization: Bearer <YOUR_API_SECRET>'
```

| 客户端 | 配置位置 | 地址填写 | 说明 |
| --- | --- | --- | --- |
| ChatAny | 设置 → 自定义接口 → Midjourney | `<BASE_URL>/mj` | 密钥填 `<YOUR_API_SECRET>`；对应本服务的 `/mj/submit/*`、`/mj/task/*` |
| ChatGPT Web Midjourney Proxy | 设置 → MJ 绘画接口地址 | `<BASE_URL>` | 客户端通常自行拼接 `/mj`；若出现 `/mj/mj` 重复，改为只填根地址或按客户端版本文档调整 |
| GoAmzAI | 后台 MJ 渠道/接口配置 | `<BASE_URL>/mj` | 字段名以实际版本为准，先用 curl 验证提交与查询链路 |

不同客户端字段名可能不同，判断标准只有一个：最终请求是否落在 `<BASE_URL>/mj/submit/imagine` 与 `<BASE_URL>/mj/task/{id}/fetch`。反向代理不要剥掉 `/mj` 前缀。

### 图片与文件地址处理

- 垫图、混合图、编辑图、图生文都支持 `data:image/...;base64,...` 或 `http(s)` URL；显式写完整 data URL 更稳妥，大图优先传 URL 以避免请求体超限。
- 带 query 或无后缀的图片 URL 会先 HEAD 探测 Content-Type 推断后缀，超时 5 秒。
- 开启垫图转 WebP 后，上传图片可能被转为 WebP；局部重绘蒙版始终保留 PNG，但当前 `maskBase64` 未生效。
- `/blend` 必须是 2~5 张；`/upload-discord-images` 返回的是图片 URL 数组。
- 任务结果可能被本地存储或对象存储改写域名，客户端应直接使用返回的 `imageUrl`/`imageUrls`，不要自行拼接 CDN 路径；视频结果可能在 `videoUrl`、`videoUrls` 或 `imageUrl` 中，三者都要兼容。

### 接入自检清单

- [ ] 用 `<BASE_URL>/health` 或 `<BASE_URL>/Health` 确认服务可达；
- [ ] 不带令牌请求 `/mj/task/list`，确认是否按预期返回 401 纯文本；
- [ ] 带令牌提交一次 `/mj/submit/imagine`，确认返回 `code: 1` 或 `22`，并用 `result` 调用 `/fetch` 拿到状态；
- [ ] 故意提交空 `prompt` 和请求不存在的任务，确认前者为 HTTP 200 + `code: 4`，后者为 204；
- [ ] 若使用速度前缀，确认 `/mj-fast/mj/submit/imagine` 与 `/mj/submit/imagine` 都能通；
- [ ] 若使用回调，确认接收端返回 2xx，并按 `id + status + progress` 去重；
- [ ] 若使用按钮动作，确认能处理 `code: 21` 并调用 `/modal`；
- [ ] 确认日志与前端没有记录完整令牌、base64 图片或用户隐私数据。
