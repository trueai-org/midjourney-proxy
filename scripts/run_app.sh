#!/bin/bash

# 获取脚本所在目录
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# 应用程序名称
APP_NAME="Midjourney.API"

# 检查应用程序文件是否存在
if [ ! -f "$DIR/$APP_NAME" ]; then
  echo "错误：应用程序文件 $DIR/$APP_NAME 不存在。"
  exit 1
fi

# 赋予应用程序执行权限
chmod +x "$DIR/$APP_NAME"

# 执行应用程序
"$DIR/$APP_NAME"

# 检查应用程序退出状态
if [ $? -ne 0 ]; then
  echo "错误：应用程序执行失败。"
  exit 1
else
  echo "应用程序执行成功。"
fi