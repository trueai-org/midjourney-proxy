#!/bin/bash

# 定义一些变量
IMAGE_NAME="registry.cn-guangzhou.aliyuncs.com/trueai-org/midjourney-proxy"
CONTAINER_NAME="mjopen"

# 打印信息
echo "开始更新 ${CONTAINER_NAME} 容器..."

# 拉取最新镜像
echo "拉取最新的镜像 ${IMAGE_NAME}..."
docker pull ${IMAGE_NAME}
if [ $? -ne 0 ]; then
    echo "拉取镜像失败，请检查网络连接或镜像地址是否正确。"
    exit 1
fi

# 停止并移除现有容器
if [ "$(docker ps -q -f name=${CONTAINER_NAME})" ]; then
    echo "停止现有的容器 ${CONTAINER_NAME}..."
    docker stop ${CONTAINER_NAME}
    if [ $? -ne 0 ]; then
        echo "停止容器失败，请手动检查。"
        exit 1
    fi
fi

if [ "$(docker ps -aq -f status=exited -f name=${CONTAINER_NAME})" ]; then
    echo "移除现有的容器 ${CONTAINER_NAME}..."
    docker rm ${CONTAINER_NAME}
    if [ $? -ne 0 ]; then
        echo "移除容器失败，请手动检查。"
        exit 1
    fi
fi

# 运行新的容器
echo "启动新的容器 ${CONTAINER_NAME}..."
docker run --name ${CONTAINER_NAME} -d --restart=always \
 -p 8086:8080 --user root \
 -v /root/mjopen/logs:/app/logs:rw \
 -v /root/mjopen/data:/app/data:rw \
 -v /root/mjopen/attachments:/app/wwwroot/attachments:rw \
 -v /root/mjopen/ephemeral-attachments:/app/wwwroot/ephemeral-attachments:rw \
 -e TZ=Asia/Shanghai \
 -v /etc/localtime:/etc/localtime:ro \
 -v /etc/timezone:/etc/timezone:ro \
 ${IMAGE_NAME}
if [ $? -ne 0 ]; then
    echo "启动新的容器失败，请手动检查。"
    exit 1
fi

echo "容器 ${CONTAINER_NAME} 更新并启动成功！"

