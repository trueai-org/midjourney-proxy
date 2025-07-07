# Docker 容器在线升级功能

## 概述

此功能允许管理员通过 API 接口直接升级运行中的 Docker 容器到最新版本，无需手动执行升级脚本。

## 功能特性

- 🔒 **安全性**: 仅管理员用户可以执行升级操作
- 🐳 **环境检测**: 自动检测是否在 Docker 容器中运行
- 📥 **自动下载**: 自动下载最新的升级脚本
- 🔄 **平滑升级**: 后台执行升级，避免中断正在进行的API请求
- 📝 **详细日志**: 完整的升级过程日志记录

## API 接口

### 升级端点

```
POST /mj/admin/upgrade
```

**请求头:**
- `Authorization: Bearer {admin_token}` 或 `Authorization: {admin_token}`

**响应示例:**

成功:
```json
{
  "code": 1,
  "description": "升级命令已执行，容器将在数秒后重启",
  "result": null,
  "properties": {}
}
```

失败:
```json
{
  "code": 0,
  "description": "此功能仅在Docker容器中可用",
  "result": null,
  "properties": {}
}
```

## 升级流程

1. **权限验证**: 检查用户是否为管理员
2. **环境检测**: 验证是否在 Docker 容器中运行
3. **脚本下载**: 从 GitHub 下载最新的升级脚本
4. **执行升级**: 在后台执行升级脚本
5. **容器重启**: 自动拉取最新镜像并重启容器

## 使用方法

### 通过 cURL

```bash
curl -X POST \
  -H "Authorization: your_admin_token" \
  http://your-domain:8086/mj/admin/upgrade
```

### 通过 JavaScript

```javascript
fetch('/mj/admin/upgrade', {
  method: 'POST',
  headers: {
    'Authorization': 'your_admin_token',
    'Content-Type': 'application/json'
  }
})
.then(response => response.json())
.then(data => console.log('升级结果:', data));
```

## 前置条件

1. **管理员权限**: 必须使用管理员令牌
2. **Docker 环境**: 必须在 Docker 容器中运行
3. **网络连接**: 需要访问 GitHub 以下载升级脚本
4. **文件权限**: 容器需要有创建临时文件的权限

## 安全考虑

- ✅ 只有管理员用户可以触发升级
- ✅ 只在 Docker 容器环境中工作
- ✅ 使用 HTTPS 下载升级脚本
- ✅ 升级脚本在临时目录中执行
- ✅ 完整的操作日志记录

## 故障排除

### 常见错误

1. **"账号无权限"**: 检查是否使用了正确的管理员令牌
2. **"此功能仅在Docker容器中可用"**: 确保应用运行在 Docker 容器中
3. **"下载升级脚本失败"**: 检查网络连接和 GitHub 访问
4. **"升级失败"**: 查看应用日志获取详细错误信息

### 日志查看

升级过程的详细日志会记录在应用的标准日志中，可以通过以下方式查看:

```bash
# 查看容器日志
docker logs mjopen

# 查看升级临时日志
docker exec mjopen cat /tmp/upgrade/upgrade.log
```

## 注意事项

- 升级过程中会短暂中断服务（通常 10-30 秒）
- 升级会保留所有数据和配置（通过 Docker 卷挂载）
- 建议在低峰时期执行升级操作
- 升级前建议备份重要数据

## 技术实现

升级功能通过以下组件实现:

1. **AdminController.UpgradeContainer()**: API 接口入口
2. **container-upgrade.sh**: 容器内升级脚本
3. **docker-upgrade.sh**: Docker 升级脚本（从现有脚本库重用）

升级脚本会自动:
- 拉取最新的 Docker 镜像
- 停止当前容器
- 启动新容器并保持所有配置和数据卷不变