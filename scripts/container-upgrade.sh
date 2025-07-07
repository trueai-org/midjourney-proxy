#!/bin/bash

# 容器内安全升级脚本
# 此脚本设计为在容器内部执行，会触发外部主机的升级操作

echo "=== MidJourney Proxy 容器在线升级 ==="
echo "开始执行容器升级流程..."

# 定义变量
IMAGE_NAME="registry.cn-guangzhou.aliyuncs.com/trueai-org/midjourney-proxy"
CONTAINER_NAME="mjopen"
UPGRADE_DIR="/tmp/upgrade"
LOG_FILE="$UPGRADE_DIR/upgrade.log"

# 创建升级目录
mkdir -p "$UPGRADE_DIR"

# 记录日志函数
log() {
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] $1" | tee -a "$LOG_FILE"
}

log "升级开始..."

# 检查是否在容器中运行
if [ ! -f /.dockerenv ] && [ ! -f /proc/1/cgroup ] || ! grep -q "docker\|containerd" /proc/1/cgroup 2>/dev/null; then
    log "错误: 此脚本只能在Docker容器内运行"
    exit 1
fi

# 下载最新的升级脚本
log "下载最新的升级脚本..."
SCRIPT_URL="https://raw.githubusercontent.com/trueai-org/midjourney-proxy/main/scripts/docker-upgrade.sh"
TEMP_SCRIPT="$UPGRADE_DIR/docker-upgrade.sh"

# 尝试使用curl下载
if command -v curl > /dev/null; then
    if curl -fsSL "$SCRIPT_URL" -o "$TEMP_SCRIPT" --connect-timeout 30 --max-time 60; then
        log "使用curl成功下载升级脚本"
    else
        log "curl下载失败，尝试使用wget..."
        if command -v wget > /dev/null; then
            if wget -q --timeout=60 --tries=3 "$SCRIPT_URL" -O "$TEMP_SCRIPT"; then
                log "使用wget成功下载升级脚本"
            else
                log "错误: wget下载也失败了"
                exit 1
            fi
        else
            log "错误: 无法找到curl或wget命令"
            exit 1
        fi
    fi
elif command -v wget > /dev/null; then
    if wget -q --timeout=60 --tries=3 "$SCRIPT_URL" -O "$TEMP_SCRIPT"; then
        log "使用wget成功下载升级脚本"
    else
        log "错误: 下载升级脚本失败"
        exit 1
    fi
else
    log "错误: 无法找到curl或wget命令"
    exit 1
fi

# 验证下载的脚本
if [ ! -f "$TEMP_SCRIPT" ] || [ ! -s "$TEMP_SCRIPT" ]; then
    log "错误: 下载的升级脚本为空或不存在"
    exit 1
fi

# 设置脚本执行权限
chmod +x "$TEMP_SCRIPT"

log "升级脚本下载完成，准备执行升级..."

# 创建一个外部执行脚本，用于在容器外执行升级
EXTERNAL_SCRIPT="$UPGRADE_DIR/external-upgrade.sh"
cat > "$EXTERNAL_SCRIPT" << 'EOF'
#!/bin/bash

# 外部升级执行脚本
echo "等待容器准备就绪..."
sleep 5

# 执行升级
UPGRADE_SCRIPT="/tmp/upgrade/docker-upgrade.sh"
if [ -f "$UPGRADE_SCRIPT" ]; then
    echo "开始执行升级脚本..."
    bash "$UPGRADE_SCRIPT"
    echo "升级脚本执行完成"
else
    echo "错误: 找不到升级脚本"
    exit 1
fi
EOF

chmod +x "$EXTERNAL_SCRIPT"

# 尝试将升级脚本复制到主机可访问的位置
# 如果挂载了/app/data目录，我们可以利用它
if [ -d "/app/data" ] && [ -w "/app/data" ]; then
    log "复制升级脚本到数据目录..."
    cp "$TEMP_SCRIPT" "/app/data/docker-upgrade.sh"
    cp "$EXTERNAL_SCRIPT" "/app/data/external-upgrade.sh"
    chmod +x "/app/data/docker-upgrade.sh"
    chmod +x "/app/data/external-upgrade.sh"
    
    log "升级脚本已复制到 /app/data/ 目录"
    log "请在主机上执行以下命令完成升级："
    log "  cd /root/mjopen/data && bash docker-upgrade.sh"
fi

# 在后台启动升级进程，延迟执行以确保API响应能够返回
log "启动后台升级进程..."

# 创建一个延迟执行的脚本
DELAYED_SCRIPT="$UPGRADE_DIR/delayed-upgrade.sh"
cat > "$DELAYED_SCRIPT" << EOF
#!/bin/bash
sleep 10
echo "开始执行延迟升级..." >> "$LOG_FILE"
if bash "$TEMP_SCRIPT" >> "$LOG_FILE" 2>&1; then
    echo "升级执行完成" >> "$LOG_FILE"
else
    echo "升级执行失败" >> "$LOG_FILE"
fi
EOF

chmod +x "$DELAYED_SCRIPT"

# 在后台执行延迟升级
nohup bash "$DELAYED_SCRIPT" >/dev/null 2>&1 &

log "升级命令已在后台启动，容器将在10秒后开始升级流程"
log "升级日志将保存在: $LOG_FILE"

echo "=== 升级流程启动成功 ==="
echo "容器将在数秒后重启以完成升级"

exit 0