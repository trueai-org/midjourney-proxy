# 05 账号管理

- **适用版本**：v12.1.6
- **适用读者**：负责账号接入与容量管理的管理员
- **前置条件**：已了解账号对应后端的登录方式，并能访问管理后台

本章说明三类绘图账号的接入方式，以及账号池、并发队列、速度模式、垂直领域与工作时间等调度策略。也会覆盖 Cloudflare 人机验证、二次验证、批量导入和账号同步/重连等日常维护动作。

## 三种账号接入

### Discord 账号接入

Discord 账号通过用户 Token 连接网关，必填字段：

| 字段 | 说明 |
| --- | --- |
| `channelId` | 频道 ID，库内唯一；重复添加会被拒绝 |
| `guildId` | 服务器 ID |
| `userToken` | Discord 用户 Token |
| `enable` | 是否启用，建议新增时设为 `true` |
| `enableMj` / `enableNiji` | 是否允许 Midjourney / Niji 绘图，默认均为 `true` |

`privateChannelId`、`nijiBotChannelId` 用于接收 seed 私信，可以留空；开启 `enableAutoGetPrivateId` 后，启动时会自动获取。`isDraft` 为真时，该账号所有含提示词的操作都会追加 `--draft`。

新增接口为 `POST /mj/admin/account`，账号在后台异步启动。启动失败会把 `enable` 置为 `false`，并把原因写入 `disabledReason`。

### 悠船账号接入

悠船账号不使用 Discord 频道，核心字段是：

| 字段 | 说明 |
| --- | --- |
| `isYouChuan` | 必须为 `true` |
| `loginAccount` | 虚拟号 |
| `loginPassword` | 虚拟 SmsCode 或登录密码；纯数字按短信验证码处理，非纯数字按密码处理 |
| `enable` | 新增时默认 `true` |

`channelId`、`guildId` 由系统自动生成，不使用用户填写的值；账号固定开启 MJ 与 Niji 绘图，并关闭 shorten。悠船支持 Excel 批量导入，见本章“Excel 批量导入”。

悠船账号还提供慢速相关运行态：`youChuanFastRemaining` 表示快速剩余秒数，`youChuanRelaxDailyLimit` 表示慢速每日上限，`youChuanExpire` 表示套餐到期时间。`youChuanEnablePreferRelax` 开启后，有慢速额度时优先消耗慢速。

### Midjourney 官网账号接入

官网账号使用 `isOfficial=true`，凭证优先级为 Cookie 高于 UserToken：

| 字段 | 说明 |
| --- | --- |
| `cookie` | 浏览器登录态，优先使用 |
| `userToken` | 没有 Cookie 时使用 |
| `loginAccount` / `loginPassword` / `login2fa` | 账号、密码与 2FA 密钥，用于自动登录 |
| `officialEnablePersonalize` | 是否允许该账号使用个性化参数 |

与悠船一样，`channelId`、`guildId` 自动生成，账号固定开启 MJ 与 Niji 绘图并关闭 shorten。自动登录可通过 `POST /mj/admin/account-login/{id}` 触发，结果由回调写回账号。

### 账号字段与状态含义

| 字段 | 含义 |
| --- | --- |
| `enable` | 是否启用；为空或 `false` 时不参与派单 |
| `lock` | 暂时锁定，通常由人机验证触发；验证完成后清除 |
| `disabledReason` | 最近一次初始化或运行失败的原因 |
| `fastExhausted` | 快速额度是否已用完 |
| `running` | 当前节点是否已建立连接，仅用于显示 |
| `runningCount` / `queueCount` | 运行中任务数与排队任务数，仅用于显示 |
| `todayDraw` | `慢速 / 快速 / 极速` 的今日绘图计数 |
| `infoUpdated` | 最近一次同步账号信息的时间 |
| `loginMessage` | 自动登录的结果说明 |
| `isYouChuan` / `isOfficial` | 账号类型标记，二者都为假时按 Discord 账号处理 |

其它账号默认值：

| 字段 | 默认值 | 说明 |
| --- | --- | --- |
| `weight` / `sort` | `0` / `0` | 权重与列表排序 |
| `mode` / `allowModes` | `null` / `[]` | 固定速度模式与允许模式 |
| `enableMj` / `enableNiji` | `true` / `true` | 是否允许 MJ / Niji 绘图 |
| `isBlend` / `isDescribe` / `isShorten` | `true` / `true` / `true` | 功能开关 |
| `isDraft` / `remixAutoSubmit` | `false` / `false` | 草稿模式与 Remix 自动提交 |
| `isVerticalDomain` | `false` | 是否启用垂直领域 |
| `workTime` / `fishingTime` | 空 | 工作时间与摸鱼时间，空表示不限 |

`remark` 用于人工备注，`weight` 与 `sort` 影响调度和列表顺序，见下一节。

## 账号池与选号

### 账号启用与禁用

| 操作 | 接口 | 说明 |
| --- | --- | --- |
| 新增 | `POST /mj/admin/account` | 异步启动，失败自动禁用 |
| 批量启用 | `POST /mj/admin/accounts-enable` | 只把 `enable` 置为 `true`，由每分钟巡检启动，最长约 1 分钟 |
| 禁用或重连 | `PUT /mj/admin/account-reconnect/{id}` | 提交完整账号对象，`enable=false` 即禁用 |
| 同步信息 | `POST /mj/admin/account-sync/{id}` | 强制执行 info 与 setting |
| 删除 | `DELETE /mj/admin/account/{id}` | 释放连接并删除记录 |

禁用只改变调度状态，不会清空历史任务。重连会先释放旧连接再启动新连接，正在执行的任务可能失败，操作前确认账号空闲。

### 选号策略

`accountChooseRule` 在启动时注册，修改后必须重启：

| 取值 | 行为 |
| --- | --- |
| `BestWaitIdle` | 默认；按队列利用率分组，选择利用率最低的组，组内随机 |
| `Random` | 在可用账号中随机选择 |
| `Weight` | 按 `weight` 加权随机；权重全为 0 时结果不再随机，会固定落到列表末个账号 |
| `Polling` | 按节点内顺序轮询 |
| `AccountFill` | 优先打满最早创建的账号的并发，再比较可用并发与可用队列 |

派单前会先过滤：账号已启用、Bot 类型匹配、速度模式被 `allowModes` 允许、处于可接单时间、未超过日限额、队列未满。没有可用账号时提交返回“无可用的账号实例”。

### 账号分组与权重

系统没有独立的分组实体，常用做法是：

- 用 `remark` 标记业务归属，后台可按备注筛选。
- 用 `verticalDomainIds` 给账号打领域标签。
- 用 `weight` 控制 `Weight` 规则下的分流比例，建议使用正整数。
- 用 `sort` 调整账号列表展示顺序。
- Discord 账号可用 `subChannels` 配置多个子频道，扩展单账号的可用频道。

提交接口的 `accountFilter.instanceId` 可以指定只使用某个账号，适合灰度或专用通道；不要把它当作常规分流手段。

## 并发与队列

### 账号并发上限

| 字段 | 默认值 | 上限 | 说明 |
| --- | --- | --- | --- |
| `coreSize` | `3` | `12` | 快速并发数 |
| `queueSize` | `10` | `100` | 快速等待队列长度 |
| `maxQueueSize` | `100` | 无 | 等待队列最大长度字段，调度主要按 `queueSize` 判断 |
| `relaxCoreSize` | `3` | `12` | 悠船慢速并发数 |
| `relaxQueueSize` | `10` | `100` | 悠船慢速等待队列长度 |
| `timeoutMinutes` | `5` | 无 | 单任务超时，超时后标记失败 |
| `interval` | `1.2` | 无 | 任务执行前间隔，单位秒 |
| `afterIntervalMin` / `afterIntervalMax` | `1.2` / `1.2` | 无 | 任务执行后间隔区间，单位秒 |

新增账号时 `coreSize`、`relaxCoreSize` 超过 12 会被截断到 12，`queueSize`、`relaxQueueSize` 超过 100 会被截断到 100，`interval` 小于 0 会被置为 0。

### 队列容量与排队行为

账号并发已满但队列未满时，任务进入该账号的等待队列；队列长度达到 `queueSize`（悠船慢速为 `relaxQueueSize`）后不再接收新任务。提交结果有三种：

- `code=1`：立即执行，描述为“提交成功”。
- `code=22`：已入队，描述为“排队中，前面还有 N 个任务”，并返回 `numberOfQueues`。
- `code=9`：失败，描述为“提交失败，队列已满，请稍后重试”。

全局 `CONCURRENT` 也会限制节点同时处理的任务数，队列已入队但全局并发耗尽时，任务会等待而不是重复提交。

### 队列满时的表现

队列满时任务不会落库为待执行，接口直接返回失败，调用方应按指数退避重试。持续队列满通常意味着账号并发设置过低、账号数量不足或上游响应变慢，先看账号列表的 `runningCount` 与 `queueCount`，再考虑扩容或调整 `coreSize`。

## 速度模式

### 快速与慢速模式

| 模式 | 含义 | 路由前缀 |
| --- | --- | --- |
| `FAST` | 快速模式，消耗快速额度 | `/mj-fast` |
| `RELAX` | 慢速模式，消耗慢速额度 | `/mj-relax` |
| `TURBO` | 极速模式，消耗快速额度 | `/mj-turbo` |

不写前缀时使用 `/mj`，由账号状态与请求参数共同决定。账号的 `mode` 为固定模式时会覆盖请求中的速度参数；`allowModes` 非空时，只有列表内的模式可用，提交前会移除不被允许的速度参数。

### 模式切换时机

- 快速可用次数不足时，账号不会被选中执行快速或极速任务；评估阈值约为快速大于 6、极速大于 12。
- 开启 `enableAutoSetRelax` 且账号不在 RELAX 模式时，快速额度用完会把 `allowModes` 收敛为仅 `RELAX`，并把 `coreSize` 重置为 3。
- 悠船按 `youChuanFastRemaining` 判断快速是否可用，按 `youChuanRelaxDailyLimit` 与重置日期判断慢速是否可用。
- 修改固定模式、允许模式或额度字段后，重连账号可立即生效；队列容量等硬限制需在账号启动或重连后完全生效。

## 垂直领域

### 垂直领域的含义

垂直领域用于给账号打标签，表示该账号只服务某一类作品。账号上有 `isVerticalDomain` 与 `verticalDomainIds`，全局配置里也有同名开关，用于统一展示与筛选。

### 配置方式与注意事项

在账号编辑中开启 `isVerticalDomain`，并填入一个或多个领域 ID。当前提交链路只按账号、模式、Bot 类型与时间过滤，不按领域 ID 过滤；因此领域标签主要用于运营归类与后台筛选，不要把它当作硬隔离手段。需要严格隔离时，用 `accountFilter.instanceId` 指定账号，或为不同业务部署独立实例。

## 工作时间

### 工作时间段配置

`workTime` 与 `fishingTime` 使用 `HH:mm-HH:mm`，多个时间段用逗号分隔，例如 `09:10-12:00,13:30-22:50`。时间段支持跨午夜，例如 `23:00-02:00`。留空表示不限制。

### 非工作时间的行为

- 不在 `workTime` 内时，账号不接收任何新任务。
- 在 `fishingTime` 内时，账号只接收变化类任务（变体、视频拓展、弹窗等），不接收新绘图任务。
- 既不在 `workTime` 也不在 `fishingTime` 内时，连变化类任务也不接收。

时间判断使用服务器本地时间，部署时必须把容器或主机的时区设置正确，否则工作时段会整体偏移。

## Cloudflare 人机验证

### 验证方式与依赖

Discord 账号触发 Cloudflare 验证时，账号会被置为 `lock=true`，并写入验证链接。处理方式有两种：

1. 人工打开账号的 `cfUrl` 完成验证，验证通过后回调 `/mj/admin/account-cf-notify` 清除锁。
2. 配置自动验证，由系统自动完成。

Discord 账号的自动验证使用 `CaptchaServer` 与 `CaptchaNotifyHook`，由独立的验证器服务完成挑战；官网账号的自动验证使用 `enableRiskControlAutoCaptcha`、`twoCaptchaKey`/`YesCaptchaKey` 与 Redis，相关配置缺一不可。验证链接过期时，可调用 `GET /mj/admin/account-cf/{id}?refresh=true` 重新生成。

### 验证器部署限制

验证器是独立的 `Midjourney.Captcha.API` 服务，默认监听 `8081`，通过浏览器自动化完成挑战。自动验证/自动登录器目前仅支持 Windows 11 / Windows Server 2022+，Linux 部署无法运行该组件。首次启动会下载浏览器，耗时较长。

配置时把 `captchaServer` 指向验证器地址，`captchaNotifyHook` 指向本服务地址，`captchaNotifySecret` 用于校验回调来源。

### 验证失败的处理

- 账号长期 `lock=true` 时不参与派单，先确认 `cfUrl` 是否可打开、验证器是否在线、回调密钥是否一致。
- 验证链接 10 分钟内有效，过期后需要重新生成；重新生成会覆盖账号上的旧链接。
- 使用代理时确认代理能访问验证页面与回调地址。
- 同一账号频繁触发验证时，降低并发与执行频率，或更换网络出口。
- 自动验证连续失败会消耗第三方验证额度，必要时先关闭自动验证改为人工处理。

## 双重验证与登录态维护

### 二次验证码处理

`login2fa` 保存 2FA 密钥。自动登录时由服务生成动态验证码，不需要人工输入。触发登录：

```bash
curl -X POST "http://<YOUR_HOST>:8086/mj/admin/account-login/<ACCOUNT_ID>" \
  -H "Authorization: <YOUR_TOKEN>"
```

登录结果通过 `/mj/admin/account-login-notify` 回调写回，超过 10 分钟视为超时。`loginMessage` 会记录成功或失败原因；登录成功后按登录前的启用状态恢复账号。

### 登录态失效与刷新

- `enableAutoLogin` 用于 Discord 账号在 Token 失效时自动登录；悠船与官网走各自的登录流程。
- WSS 断开后会自动重连，重连期间账号不接新任务，已入队任务保留。
- `enableAutoSyncInfoSetting` 开启后，普通情况每 3–6 小时随机同步一次账号信息与设置；快速额度耗尽后改为 6–12 小时。
- `enableAutoGetPrivateId` 开启后，启动时自动补全私信频道 ID。
- `enableAutoVerifyAccount` 开启后，启动时验证账号可用性，失败会禁用并记录原因。

## Excel 批量导入

### 模板与表头要求

仅悠船账号支持 Excel 导入，入口为 `POST /mj/admin/account-import/youchuan/preview`（预览）与 `POST /mj/admin/account-import/youchuan`（导入）。

解析器在前 20 行内寻找表头，表头必须同时包含“虚拟号”和“虚拟 SmsCode/密码”两类列。常见别名均被识别，例如“虚拟号码”“手机号”“账号”对应虚拟号，“SmsCode”“短信验证码”“登录密码”“密码”对应密码。可选的“套餐名称”“套餐周期”会写入备注。

### 文件格式与大小限制

| 限制 | 值 |
| --- | --- |
| 文件格式 | `.xlsx`，不支持 `.xls` 与加密工作簿 |
| 文件大小 | 不超过 20MB |
| 数据行数 | 不超过 2000 行，空行不计入 |
| 单个压缩包条目解压后 | 不超过 30MB |

虚拟号会去掉空格、横线与 `+`，并去掉常见的 `0086`/`86` 前缀；密码列如果是数值或科学计数法，会还原为整数字符串。纯数字密码按悠船短信验证码流程登录。

### 重复账号的处理

- 文件内重复虚拟号只保留第一行，后续行标记为“文件内虚拟号重复”且不可勾选。
- 与库内已有悠船账号重复时执行更新：覆盖虚拟号、密码、并发、队列、权重、日限额与超时，保留 `enable`、`channelId`、`userToken` 等运行态字段。
- 新账号自动生成 `channelId`、`guildId`，默认启用，并在后台逐个启动，避免并发登录触发风控。

### 导入结果与失败排查

导入按行处理，允许部分成功，返回新增、更新、失败计数与错误列表。错误信息包含 Excel 行号，不包含 SmsCode 或密码。

导入参数默认值：`coreSize=12`、`queueSize=48`、`relaxCoreSize=3`、`relaxQueueSize=12`、`weight=1`、`timeoutMinutes=20`，日限额为 `-1`。常见失败原因：

| 现象 | 处理 |
| --- | --- |
| 未识别到模板 | 确认表头包含“虚拟号”和“虚拟 SmsCode/密码” |
| 虚拟号格式不正确 | 检查是否少于 5 位或混入非数字字符 |
| 单行失败但其他行成功 | 按错误列表逐行修正后重新导入，重复行会走更新 |
| 更新后队列容量未变 | 队列容量在下次启动或重连后完全生效 |

## 账号同步与重连

### 同步账号信息

`POST /mj/admin/account-sync/{id}` 会强制执行 info 与 setting，刷新订阅、额度、当前模式与按钮。同步失败返回“同步失败，请稍后重试”，账号本身不会被禁用；先确认连接是否正常，再重试。

日常可依赖 `enableAutoSyncInfoSetting` 自动同步；手动同步适合调整套餐、切换模式或排查额度显示不一致时使用。

### 重连运行中的实例

`PUT /mj/admin/account-reconnect/{id}` 提交完整账号对象，服务会：

1. 以库中的 `channelId`、`guildId` 为准，忽略请求体中的同名字段。
2. 悠船与官网账号强制 `enableMj=true`、`enableNiji=true`、`isShorten=false`。
3. 在 `enable` 或 `userToken` 变化时清理缓存。
4. 释放旧连接并启动新连接。

重连会中断该账号正在执行的任务，建议在任务空闲时操作。账号编辑统一通过该接口提交，即使只改备注、权重等非连接字段，服务也会释放并重建连接。

### 变更何时完全生效

| 变更 | 生效方式 |
| --- | --- |
| 账号启用、禁用、凭证、并发、队列、权重 | 重连后生效 |
| Excel 导入的凭证与池参数 | 数据库立即生效；运行中实例同步更新凭证与池参数，但不强制重连 |
| 队列容量等 Redis 硬限制 | 下次启动或重连后完全生效 |
| 批量启用 | 每分钟巡检启动，最长约 1 分钟 |
| 账号选择规则 | 重启进程后生效 |
| 数据库类型、全局并发 | 重启进程后生效 |
