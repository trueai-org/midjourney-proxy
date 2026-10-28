# 文档维护与 AI 生成规则

- **适用版本**：v12.1.6
- **适用读者**：手册维护者、自动化脚本作者、参与文档工作的 AI 助手
- **前置条件**：已阅读本文件与[维护指南](MAINTENANCE.md)，并了解仓库目录结构

本文件规定 `docs/manual` 手册的定位、目录职责、生成与修改规则、定期维护节奏，以及 AI 参与文档工作时的执行步骤和禁止事项。与[维护指南](MAINTENANCE.md)冲突时，以更严格、更安全的要求为准。

## 1. 适用范围与文档定位

- 手册面向四类读者：部署者、管理员、接口开发者、运维人员。
- 正文以当前版本 **v12.1.6** 为准；版本号来源与升级流程见[维护指南](MAINTENANCE.md)。
- 手册只描述产品的部署、配置、使用与运维方式，不涉及内部许可校验实现、安全算法或设备标识细节。
- 手册不是源码注释的替代品：涉及行为、默认值、字段名时，必须能追溯到源码或脚本。

## 2. 目录职责

| 路径 | 职责 | 维护方式 |
| --- | --- | --- |
| `index.md` | 中文手册首页：版本、读者、阅读路径、章节导航、快速链接 | 手工维护 |
| `01-overview.md` ~ `11-faq-glossary.md` | 正文十一章，按章节顺序编号 | 手工维护 |
| `en/README.md` | 英文入口页与章节索引 | 手工维护 |
| `index.json` | 结构化索引，供检索工具使用 | 手工维护，需与正文同步 |
| `images/` | 管理后台截图与截图说明 `README.md` | PNG 由脚本生成，说明手工维护 |
| `appendix/` | 提交归档与发布归档（`commits.md`、`releases.md` 及对应 JSON） | 由脚本生成，禁止手改 |
| `MAINTENANCE.md` | 版本口径、升级步骤、截图规范与提交边界 | 手工维护 |
| `AGENTS.md` | 本文件：文档维护与 AI 生成规则 | 手工维护 |

生成文件与手工文件的边界：

- `appendix/*` 由 `scripts/docs/generate-release-archive.mjs` 生成，任何情况下都不要手工编辑。
- `images/*.png` 由 `scripts/docs/capture-screenshots.mjs` 生成，禁止手工替换或修图。
- `index.json` 是手工文件，但新增/删除章节、调整顺序时必须同步；字段为 `id`、`title`、`path`、`lang`、`tags`、`summary`、`order`、`version`、`lastUpdated`。

## 3. 生成规则

### 3.1 正文

- 先读源码、配置模型与脚本，再写正文；禁止凭记忆或经验臆造。
- 版本号、配置项、接口路径、默认值、错误码必须能在仓库中找到依据。
- 不写“可能”“大概”之类的模糊结论；无法确认的内容不写，或在维护记录中标注待确认。

### 3.2 归档

```bash
node scripts/docs/generate-release-archive.mjs
```

- 生成 `appendix/commits.md`、`appendix/commits.json`、`appendix/releases.md`、`appendix/releases.json`。
- 禁止手改 `appendix/*`；条目规模由校验脚本检查。

### 3.3 截图

```powershell
# 默认：线上 https://ai.trueai.org，输出 docs/manual/images，视口 1366×768
node scripts/docs/capture-screenshots.mjs

# 只补采指定页面（逗号分隔）
$env:PAGES='10-swagger.png'; $env:BASE_URL='http://127.0.0.1:8098'; node scripts/docs/capture-screenshots.mjs
```

- 默认从线上演示站采集，固定 **1366×768**、`deviceScaleFactor=1`、只截当前视口。
- Swagger 截图可用本地同版本实例采集，采集前确认 `/swagger/v1/swagger.json` 返回 200；临时运行目录放在仓库外，用完删除。
- 禁止手改 PNG；脱敏逻辑与采集地址说明见[截图说明](images/README.md)。

### 3.4 索引

- 新增或删除章节时，同步更新 `index.json`、`index.md` 的章节导航与 `en/README.md` 的索引表格。
- `order` 必须从 0 开始连续、唯一且按文件顺序递增。
- 每次内容变更同步 `version` 与 `lastUpdated`。

## 4. 修改规则

- 先读本文件与[维护指南](MAINTENANCE.md)，只改与任务相关的 Markdown 或文档脚本。
- 不改源码，不改生成文件；确需调整生成逻辑时，改 `scripts/docs/*.mjs` 后重新生成。
- 不删除历史归档，不修改归档条目。

### 4.1 红线

- 不得写入内部许可校验实现、安全算法、设备标识细节或内部许可服务地址。
- 不得写入真实 token、密码、AK/SK、Cookie、邮箱、手机号；凭据统一用 `<YOUR_...>` 或 `***` 占位。
- 不展示已废弃功能：LiteDB、MongoDB、Ocelot、旧 `mj` 配置段、`data/mj.db`、旧接口与旧动作等；历史归档除外，且需保留“不代表当前支持范围”的提示。
- 公开演示地址按实际地址展示，不要改回占位符：验证器 `http://47.76.110.222:8081`、MinIO 示例 `192.168.3.241`、图片示例 UUID `4872b862-7aeb-4346-a87d-16e077a2935d`。
- 保留 `127.0.0.1`、`localhost`、`0.0.0.0` 等本地或监听地址，不要替换。

## 5. 定期生成规则

| 周期 | 动作 |
| --- | --- |
| 每次版本发布 / 子模块更新 | 重新生成归档、重新采集截图、更新版本号与 `lastUpdated`、运行校验 |
| 每月 | 检查链接、旧功能残留、敏感信息、截图是否与当前 UI 一致 |
| 每季度 | 全量复核章节与源码一致性，抽样 3 篇逐行核对 |

## 6. AI 维护规则

AI 参与文档工作时，按以下顺序执行：

1. 读本文件、[维护指南](MAINTENANCE.md)与 `index.json`。
2. 确认当前版本与目标章节，明确本次改动范围。
3. 用 codegraph 或 grep 查源码事实，不凭记忆、不猜测。
4. 只改相关 Markdown 或文档脚本，不碰源码与生成文件。
5. 运行 `node scripts/docs/verify-docs.mjs`。
6. 执行红线与敏感信息检索，确认没有真实凭据、邮箱、手机号或内部实现细节。
7. 报告改动清单、校验结果与未解决项。
8. 不提交、不 push，除非用户明确要求。

AI 禁止事项：

- 不编造接口、配置项、默认值或错误码。
- 不写入真实凭据、Cookie、邮箱、手机号或内部服务地址。
- 不展示已废弃功能，不删除历史归档。
- 不修改源码，不手工编辑 `appendix/*` 与 `images/*.png`。
- 不把公开演示地址改回占位符。

## 7. 校验命令与通过标准

```bash
node scripts/docs/generate-release-archive.mjs
node scripts/docs/capture-screenshots.mjs
node scripts/docs/verify-docs.mjs
```

通过标准：

- 三条命令退出码均为 0，无脚本异常。
- `verify-docs.mjs` 报告 `[失败] 0 项` 且结论为“全部硬性检查通过”。
- 归档条目达到脚本下限：`commits.md` 不少于 1451 条，`releases.md` 版本表不少于 631 行。
- `docs/manual/images` 下 10 张 PNG 均为 1366×768，且与 `07-admin-console.md` 引用一致。

## 8. 变更记录

| 日期 | 版本 | 变更 | 校验 |
| --- | --- | --- | --- |
| 2026-10-28 | v12.1.6 | 新增文档维护与 AI 生成规则；Swagger 截图改为本地同版本实例；将地址类占位符替换为公开演示地址 | `node scripts/docs/verify-docs.mjs` 0 失败 |
