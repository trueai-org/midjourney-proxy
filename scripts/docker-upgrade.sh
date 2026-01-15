#!/bin/bash

# 定义变量
IMAGE_NAME="registry.cn-guangzhou.aliyuncs.com/trueai-org/midjourney-proxy"
CONTAINER_NAME="mjopen"
NETWORK_NAME="mjopen-network"

echo "开始更新 ${CONTAINER_NAME} 容器..."

# 创建网络（已存在则忽略）
docker network create ${NETWORK_NAME} 2>/dev/null || true

# 拉取最新镜像
echo "拉取最新的镜像 ${IMAGE_NAME}..."
docker pull ${IMAGE_NAME} || { echo "拉取镜像失败"; exit 1; }

# 强制停止并移除容器（如果存在）
docker rm -f ${CONTAINER_NAME} 2>/dev/null || true

# 运行新的容器
echo "启动新的容器 ${CONTAINER_NAME}..."
docker run --name ${CONTAINER_NAME} -d --restart=always \
 --network ${NETWORK_NAME} \
 -p 8086:8080 --user root \
 -v /root/mjopen/logs:/app/logs:rw \
 -v /root/mjopen/data:/app/data:rw \
 -v /root/mjopen/attachments:/app/wwwroot/attachments:rw \
 -v /root/mjopen/ephemeral-attachments:/app/wwwroot/ephemeral-attachments:rw \
 -e TZ=Asia/Shanghai \
 -v /etc/localtime:/etc/localtime:ro \
 ${IMAGE_NAME} || { echo "启动容器失败"; exit 1; }

echo "容器 ${CONTAINER_NAME} 更新成功！"