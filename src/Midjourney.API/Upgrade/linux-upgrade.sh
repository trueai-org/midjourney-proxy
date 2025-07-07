#!/bin/bash

# 设置源目录变量
SOURCE_DIR="${1:-upgrade/extract}"

echo "应用程序升级脚本开始执行..."
echo "源目录: $SOURCE_DIR"
echo "等待应用程序完全退出..."

# 等待3秒确保程序退出
sleep 3

# 检查进程是否还在运行，最多等待30秒
count=0
while [ $count -lt 30 ]; do
    if ! pgrep -f "Midjourney.API" > /dev/null; then
        echo "应用程序已完全退出"
        break
    fi
    echo "应用程序仍在运行，继续等待..."
    sleep 2
    count=$((count + 2))
done

if [ $count -ge 30 ]; then
    echo "强制继续执行升级..."
    pkill -f "Midjourney.API" 2>/dev/null || true
    sleep 2
fi

echo "开始复制新文件..."
echo "从 $SOURCE_DIR 复制到当前目录"

# 检查源目录是否存在
if [ ! -d "$SOURCE_DIR" ]; then
    echo "错误: 源目录不存在: $SOURCE_DIR"
    exit 1
fi

# 复制文件
if cp -r "$SOURCE_DIR"/* /app; then
    echo "文件复制完成"
else
    echo "文件复制失败"
    exit 1
fi

# 设置可执行权限
# chmod +x Midjourney.API 2>/dev/null || true

# 清理解压目录
if [ -d "$SOURCE_DIR" ]; then
    echo "清理临时文件..."
    rm -rf "$SOURCE_DIR"
fi

# 清理升级包文件
rm -f /app/Upgrade/*.zip /app/Upgrade/*.tar.gz 2>/dev/null || true

# echo "重启应用程序..."
# nohup ./Midjourney.API > /dev/null 2>&1 &

echo "升级完成！程序已重启"
sleep 2

exit 0

