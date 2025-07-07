#!/bin/bash

# 创建数据目录
mkdir -p /opt/traefik/data
mkdir -p /opt/traefik/config

# 设置权限
chmod 600 /opt/traefik/data/acme.json 2>/dev/null || touch /opt/traefik/data/acme.json && chmod 600 /opt/traefik/data/acme.json

# 启动 Traefik
docker run -d \
  --name traefik \
  --restart unless-stopped \
  -p 80:80 \
  -p 443:443 \
  -p 8080:8080 \
  -v /var/run/docker.sock:/var/run/docker.sock:ro \
  -v /opt/traefik/config/traefik.yml:/etc/traefik/traefik.yml:ro \
  -v /opt/traefik/config/dynamic.yml:/etc/traefik/dynamic.yml:ro \
  -v /opt/traefik/data:/data \
  --network bridge \
  traefik:v3.1

echo "Traefik 已启动！"
echo "仪表板地址: http://localhost:8080"
echo "Consul 地址: http://192.168.3.241:8500"