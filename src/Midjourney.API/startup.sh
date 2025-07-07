#!/bin/bash

# 复制升级文件（如果存在）
cp -rf /app/Upgrade/Extract/* /app/ 2>/dev/null || true

# 删除升级相关文件和目录
rm -rf /app/Upgrade/Extract /app/Upgrade/*.tar.gz /app/Upgrade/*.zip 2>/dev/null || true

# 启动应用程序
exec dotnet Midjourney.API.dll

