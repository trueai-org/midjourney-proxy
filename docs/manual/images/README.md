# 管理后台截图

本目录保存 `midjourney-proxy v12.1.6` 管理后台的手册截图：9 张来自受控演示实例（演示模式）**https://ai.trueai.org**，`10-swagger.png` 来自**同版本本地实例**（源码已修复，见「Swagger 截图来源」）；所有图片均为 **1366×768**。

## 截图清单

| 文件名 | 页面 | 路由 | 尺寸 | 采集日期 | 采集地址 | alt 文案 |
| --- | --- | --- | --- | --- | --- | --- |
| `01-login.png` | 登录页 | `/#/user/login` | 1366×768 | 2026-10-28 | https://ai.trueai.org | 登录页 |
| `02-welcome.png` | 欢迎页 / 运行概览 | `/#/welcome` | 1366×768 | 2026-10-28 | https://ai.trueai.org | 欢迎页概览 |
| `03-accounts.png` | 账号管理 | `/#/account` | 1366×768 | 2026-10-28 | https://ai.trueai.org | 账号管理列表 |
| `04-tasks.png` | 任务查询 | `/#/task` | 1366×768 | 2026-10-28 | https://ai.trueai.org | 任务查询列表 |
| `05-draw-test.png` | 绘画测试 | `/#/draw-test` | 1366×768 | 2026-10-28 | https://ai.trueai.org | 绘画测试页 |
| `06-users.png` | 用户管理 | `/#/user-list` | 1366×768 | 2026-10-28 | https://ai.trueai.org | 用户管理列表 |
| `07-banned-words.png` | 违规词条 | `/#/banned-word` | 1366×768 | 2026-10-28 | https://ai.trueai.org | 违规词条列表 |
| `08-settings.png` | 系统设置 / 其他设置 | `/#/setting` | 1366×768 | 2026-10-28 | https://ai.trueai.org | 系统设置-其他设置 |
| `09-probe.png` | 日志查看 | `/#/probe` | 1366×768 | 2026-10-28 | https://ai.trueai.org | 日志查看页 |
| `10-swagger.png` | Swagger 接口文档 | `/swagger/index.html` | 1366×768 | 2026-10-28 | 本地同版本实例（源码已修复） | Swagger 接口文档 |

> 视口固定 **1366×768**、`deviceScaleFactor=1`、`mobile=false`，**只截当前视口**：不拼接整页、不做滚动拼接，10 张图片的宽高都精确为 1366×768。
> 宽表（账号管理、任务查询）在 1366 宽度下需要横向滚动，因此画面内不会出现备注、描述等靠右的敏感列。

## 脱敏说明

线上后端已对账号 `userToken`/`cookie`（返回 `****`）、用户 `name`/`email`/`phone`/`token`（返回 `***`）、设置连接串（返回 `****`）做了脱敏；脚本在截图前还会再执行一轮前端脱敏：

| 页面 | 处理内容 |
| --- | --- |
| 欢迎页 | 整卡隐藏「程序信息」「系统信息」「服务器信息」（含程序目录、主机名、私有 IP）；隐藏「今日绘图 TOP 10」区块；节点切换里的内网 IP 替换为 `***` |
| 账号管理 | 「服务器ID」「频道ID」按**前 6 位 + `...` + 后 4 位**部分脱敏（如 `177914...4204`，保留示意价值）；「备注」列整列替换为 `***`，列宽保持不变 |
| 任务查询 | 「实例ID」按前 6 位 + `...` + 后 4 位部分脱敏；「描述」「提示词」「Prompt」列替换为 `***`；表格内缩略图替换为灰色占位块并保留列宽 |
| 绘画测试 | 隐藏「最近任务」面板（可能含真实提示词与图片），只保留提交表单与参数区 |
| 用户管理 | 表头含「邮箱」「手机」「最后登录 IP」「注册 IP」「IP」的列替换为 `***` |
| 违规词条 | 表头含「名称」「描述」的列替换为 `***`，保留行数、列宽与「违规词」数量 |
| 系统设置 | 只保留「其他设置」分组（隐藏更新状态、账号设置、存储与集成、翻译与消息、验证与风控、绘图配置、Discord 配置、悠船配置、私人定制），不会出现数据库/Redis/SMTP/第三方 Key/敏感信息 |
| 日志查看 | 日志文本替换为「演示模式，日志内容已隐藏」，避免出现本机路径、IP、主机名 |
| 接口文档 | 隐藏信息区里的文档地址（含本地端口），只保留标题与接口列表 |
| 全部页面 | 可见文本中的邮箱、手机号、IPv4、长 Token/访问密钥、连接串键值对统一替换为 `***` / `****`（`v12.1.6.0` 这类版本号不会被误替换） |

截图前还会按站点自身逻辑写入“今日不再提醒”，避免全局公告弹窗遮挡页面。

## Swagger 截图来源

`10-swagger.png` 取自**同版本（v12.1.6）本地实例**：本地源码已修复 Swagger 文档生成问题，`/swagger/v1/swagger.json` 返回 200，页面可正常渲染接口列表。

线上演示站 `https://ai.trueai.org` 尚未部署最新修复时，`/swagger/v1/swagger.json` 仍可能返回 **HTTP 500**，Swagger UI 只会显示 `Failed to load API definition / Fetch error`；部署更新后自动恢复。这属于部署版本差异，不是当前源码的已知限制。

本地采集方式（临时运行目录放在仓库外，用完即删，仓库内不保留临时脚本与运行产物）：

1. 构建 `src/Midjourney.API/Midjourney.API.csproj`，在临时目录写入启用演示模式与 Swagger 的 `data/mj.json`，并复制发布目录的 `wwwroot`；
2. 以 `ASPNETCORE_URLS=http://127.0.0.1:8098` 启动本地实例，确认 `/swagger/v1/swagger.json` 返回 200；
3. 用同一套脚本采集：`BASE_URL=http://127.0.0.1:8098`、`PAGES=10-swagger.png`；
4. 采集完成后停止进程并删除临时目录，释放调试端口。

线上部署更新后，可直接用默认线上地址重跑脚本替换该图；脚本检测到线上仍是报错页时会跳过并保留现有文件，不会把报错页写进手册。

## 重新生成

```powershell
# 默认：线上 https://ai.trueai.org，输出到 docs/manual/images，视口 1366×768
node scripts/docs/capture-screenshots.mjs

# 可选：覆盖线上地址、Chrome 路径、输出目录
$env:BASE_URL='https://ai.trueai.org'; node scripts/docs/capture-screenshots.mjs
$env:CHROME_PATH='<YOUR_CHROME_PATH>'; node scripts/docs/capture-screenshots.mjs
$env:OUT_DIR='<YOUR_OUTPUT_DIR>'; node scripts/docs/capture-screenshots.mjs

# 可选：只补采指定页面（逗号分隔）
$env:PAGES='03-accounts.png,04-tasks.png'; node scripts/docs/capture-screenshots.mjs
$env:PAGES='10-swagger.png'; $env:BASE_URL='http://127.0.0.1:8098'; node scripts/docs/capture-screenshots.mjs
```

## 依赖与校验

- `Node 22`：运行 `capture-screenshots.mjs`；仅复用 `midjourney-proxy-webui/node_modules/ws`，不额外安装 npm 依赖。
- 本机 Chrome：默认使用系统 Chrome，可用 `CHROME_PATH=<YOUR_CHROME_PATH>` 覆盖。
- 访问线上需要网络；**不需要**本地 .NET、Python、Redis 或演示数据（仅在补采 Swagger 时才临时启动本地实例）。脚本自行启动 headless Chrome（调试端口 `9223`，临时 profile 用完即删）。
- 脚本对每张 PNG 读取前 24 字节校验 IHDR 宽高必须为 `1366×768`，不符直接报错。
- 人工复核：逐张确认非空白、非报错，且没有真实 Token、手机号、邮箱、IP、连接串或敏感内容。
