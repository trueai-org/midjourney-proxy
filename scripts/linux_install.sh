#!/bin/bash

# =====================================
# MidJourney Proxy Installer
# =====================================

# 定义颜色
readonly RED='\033[0;31m'
readonly GREEN='\033[0;32m'
readonly YELLOW='\033[1;33m'
readonly BLUE='\033[0;34m'
readonly NC='\033[0m' # 无颜色

# 全局变量
ARCH=""
PKG_MANAGER=""
docker_try=false
docker_installed=false
USE_ACCELERATION=false
CONFIG_FILE="./installer_config"
CONTAINER_NAME="mjopen"
IMAGE_NAME="registry.cn-guangzhou.aliyuncs.com/trueai-org/midjourney-proxy"

# ================================
# 工具函数
# ================================

# 启动提示信息
start_info() {
    echo -e "${BLUE}MidJourney Proxy 安装脚本${NC}"
    echo -e "${BLUE}正在执行启动前检查...${NC}"
}

# 彩色打印消息
print_msg() {
    local color="$1"
    local message="$2"
    echo -e "${color}${message}${NC}"
}

# 打印错误并退出
exit_with_error() {
    local message="$1"
    print_msg "${RED}" "$message"
    exit 1
}

# 检测包管理器
detect_package_manager() {
    if command -v dnf &>/dev/null; then
        PKG_MANAGER="dnf"
        print_msg "${GREEN}" "检测到dnf包管理器"
    elif command -v yum &>/dev/null; then
        PKG_MANAGER="yum"
        print_msg "${GREEN}" "检测到yum包管理器"
    elif command -v apt-get &>/dev/null; then
        PKG_MANAGER="apt-get"
        print_msg "${GREEN}" "检测到apt-get包管理器"
    else
        exit_with_error "不支持的Linux发行版。"
    fi
}

# 安装必要的软件包
install_package() {
    local package="$1"
    print_msg "${YELLOW}" "正在安装 $package..."
    case "$PKG_MANAGER" in
    apt-get)
        print_msg "${BLUE}" "正在更新apt-get..."
        apt-get update -y
        apt-get install "$package" -y || exit_with_error "安装 $package 失败。"
        ;;
    yum)
        yum makecache -y
        yum install "$package" -y || exit_with_error "安装 $package 失败。"
        ;;
    dnf)
        print_msg "${BLUE}" "正在更新dnf..."
        dnf makecache -y
        dnf install "$package" -y || exit_with_error "安装 $package 失败。"
        ;;
    *)
        exit_with_error "未知的包管理器。"
        ;;
    esac
}

# 检查并安装依赖
check_dependencies() {
    print_msg "${BLUE}" "正在检查并安装依赖..."
    for dep in curl jq; do
        if ! command -v "$dep" &>/dev/null; then
            install_package "$dep"
        fi
    done
}

# 检查CPU架构
check_architecture() {
    local arch
    arch=$(uname -m)
    case "$arch" in
    x86_64)
        ARCH="x64"
        print_msg "${GREEN}" "检测到x64架构"
        ;;
    aarch64)
        ARCH="arm64"
        print_msg "${GREEN}" "检测到arm64架构"
        ;;
    *)
        exit_with_error "不支持的架构: $arch"
        ;;
    esac
}

# 读取配置并应用加速
load_config() {
    if [ -f "$CONFIG_FILE" ]; then
        source "$CONFIG_FILE"
        print_msg "${GREEN}" "已加载配置文件"
    else
        print_msg "${YELLOW}" "未发现配置文件"
        ask_acceleration
    fi
}

# 创建运行版本配置文件
create_running_versions_file() {
    local file="running_versions.conf"
    # 如果文件不存在，则创建一个空文件
    if [ ! -f "$file" ]; then
        touch "$file"
        # print_msg "${GREEN}" "已创建运行版本配置文件：$file"
    fi
}

check_docker_installed() {
    if ! command -v docker &>/dev/null; then
        docker_installed=false
        print_msg "${YELLOW}" "未安装Docker"
    else
        docker_installed=true
        print_msg "${GREEN}" "已安装Docker"
    fi
}

# 询问是否启用加速
ask_acceleration() {
    while true; do
        read -rp "是否启用加速（解决国内无法连接 Docker 和 GitHub 的问题）？[Y/n]: " choice
        choice=$(echo "$choice" | tr '[:upper:]' '[:lower:]')
        if [[ "$choice" == "y" || "$choice" == "" ]]; then
            USE_ACCELERATION=true
            save_config
            break
        elif [[ "$choice" == "n" ]]; then
            USE_ACCELERATION=false
            save_config
            break
        else
            print_msg "${YELLOW}" "请输入 Y 或 N。"
        fi
    done
}

# 保存配置
save_config() {
    echo "USE_ACCELERATION=$USE_ACCELERATION" >"$CONFIG_FILE"
    print_msg "${GREEN}" "已保存配置文件"
}

# ================================
# Docker操作函数
# ================================

install_docker() {
    if ! command -v docker &>/dev/null; then
        if [ "$docker_try" = true ]; then
            exit_with_error "已使用Docker官方脚本安装但无法启用，请手动检查。"
        fi
        print_msg "${YELLOW}" "Docker 未安装，正在使用官方脚本安装 Docker..."
        if $USE_ACCELERATION; then
            if curl -fsSL https://get.docker.com | bash -s docker --mirror Aliyun; then
                docker_try=true
                install_docker
            else
                exit_with_error "Docker 安装失败，请检查错误信息并重试。"
            fi
        else
            if curl -fsSL https://get.docker.com | bash -s docker; then
                docker_try=true
                install_docker
            else
                exit_with_error "Docker 安装失败，请检查错误信息并重试。"
            fi
        fi
    else
        docker_installed=true
        print_msg "${GREEN}" "Docker 已安装，无需进行操作。"
    fi
}

run_docker_container() {
    print_msg "${BLUE}" "正在拉取最新的 Docker 镜像..."
    docker pull ${IMAGE_NAME} || exit_with_error "拉取 Docker 镜像失败。"

    # 停止并删除现有容器
    if [ "$(docker ps -q -f name=${CONTAINER_NAME})" ]; then
        print_msg "${BLUE}" "停止现有的容器 ${CONTAINER_NAME}..."
        docker stop ${CONTAINER_NAME}
        if [ $? -ne 0 ]; then
            print_msg "${RED}" "停止容器失败，请手动检查。"
            return 1
        fi
    fi

    if [ "$(docker ps -aq -f status=exited -f name=${CONTAINER_NAME})" ]; then
        print_msg "${BLUE}" "移除现有的容器 ${CONTAINER_NAME}..."
        docker rm ${CONTAINER_NAME}
        if [ $? -ne 0 ]; then
            print_msg "${RED}" "移除容器失败，请手动检查。"
            return 1
        fi
    fi

    # 提示用户输入端口
    read -rp "请设定外部端口（输入enter选择默认，默认8086）: " external_port
    external_port=${external_port:-8086}  # 使用默认端口8086

    print_msg "${BLUE}" "启动 Docker 容器..."
    docker run --name ${CONTAINER_NAME} -d --restart=always \
        -p $external_port:8080 --user root \
        -v /root/mjopen/logs:/app/logs:rw \
        -v /root/mjopen/data:/app/data:rw \
        -v /root/mjopen/attachments:/app/wwwroot/attachments:rw \
        -v /root/mjopen/ephemeral-attachments:/app/wwwroot/ephemeral-attachments:rw \
        -e TZ=Asia/Shanghai \
        -v /etc/localtime:/etc/localtime:ro \
        -v /etc/timezone:/etc/timezone:ro \
        ${IMAGE_NAME} || exit_with_error "启动 Docker 容器失败。"

    # 获取本机公网和内网IP
    local public_ip
    local private_ip
    public_ip=$(curl -s ifconfig.me)
    private_ip=$(hostname -I | awk '{print $1}')

    print_msg "${GREEN}" "Docker 容器 ${CONTAINER_NAME} 已成功启动，请确认端口设置："
    print_msg "${GREEN}" "内网地址: http://$private_ip:8080"
    print_msg "${GREEN}" "外网地址: http://$public_ip:$external_port"
}

start_docker_container() {
    if [ "$(docker ps -q -f name=${CONTAINER_NAME})" ]; then
        print_msg "${YELLOW}" "容器 ${CONTAINER_NAME} 已在运行。"
    else
        docker start ${CONTAINER_NAME} && print_msg "${GREEN}" "容器 ${CONTAINER_NAME} 已启动。" || print_msg "${RED}" "启动容器失败。"
    fi
}

stop_docker_container() {
    if [ "$(docker ps -q -f name=${CONTAINER_NAME})" ]; then
        docker stop ${CONTAINER_NAME} && print_msg "${GREEN}" "容器 ${CONTAINER_NAME} 已停止。" || print_msg "${RED}" "停止容器失败。"
    else
        print_msg "${YELLOW}" "容器 ${CONTAINER_NAME} 未在运行。"
    fi
}

check_docker_status() {
    if [ "$docker_installed" = false ]; then
        print_msg "${YELLOW}" "Docker 未安装。"
    else
        if [ "$(docker ps -q -f name=${CONTAINER_NAME})" ]; then
            print_msg "${GREEN}" "容器 ${CONTAINER_NAME} 正在运行。"
        else
            print_msg "${YELLOW}" "Docker 已安装，容器未启动。"
        fi
    fi
}

# ================================
# Linux版本安装函数
# ================================

# 全局变量
LATEST_VERSION=""
DOWNLOAD_URL=""
INSTALLED_VERSIONS=()

# 从GitHub获取最新版本信息
get_latest_version_info() {
    local API_URL="https://api.github.com/repos/trueai-org/midjourney-proxy/releases/latest"
    local response

    if $USE_ACCELERATION; then
        API_URL="https://ghproxy.com/$API_URL"
    fi

    response=$(curl -s "$API_URL")
    if [ $? -ne 0 ]; then
        print_msg "${RED}" "获取最新版本信息失败。"
        return
    fi

    LATEST_VERSION=$(echo "$response" | jq -r '.tag_name')
    DOWNLOAD_URL=$(echo "$response" | jq -r --arg ARCH "$ARCH" '.assets[] | select(.name | test("midjourney-proxy-linux-\($ARCH)")) | .browser_download_url')

    if [ -z "$LATEST_VERSION" ] || [ -z "$DOWNLOAD_URL" ]; then
        print_msg "${RED}" "获取最新版本信息失败。"
        return
    fi
}

# 列出已安装的版本
list_installed_versions() {
    INSTALLED_VERSIONS=()
    for version_dir in v*; do
        if [ -d "$version_dir" ]; then
            INSTALLED_VERSIONS+=("$version_dir")
        fi
    done

    if [ "${#INSTALLED_VERSIONS[@]}" -gt 0 ]; then
        echo -e "${BLUE}已安装的linux版本:${NC}"
        for version in "${INSTALLED_VERSIONS[@]}"; do
            echo -e "  $version"
        done
    fi
}

# 安装指定版本
install_version() {
    local version="$1"

    if [ -d "$version" ]; then
        print_msg "${YELLOW}" "版本 $version 已安装。"
        return 1
    fi

    local specific_api_url="https://api.github.com/repos/trueai-org/midjourney-proxy/releases/tags/$version"
    if $USE_ACCELERATION; then
        specific_api_url="https://ghproxy.com/$specific_api_url"
    fi
    local response
    response=$(curl -s "$specific_api_url")
    if [ $? -ne 0 ]; then
        print_msg "${RED}" "获取版本 $version 的信息失败。"
        return
    fi

    local tar_url
    tar_url=$(echo "$response" | jq -r --arg ARCH "$ARCH" --arg VERSION "$version" '
        .assets[]
        | select(.name | test("midjourney-proxy-linux-" + $ARCH + "-" + $VERSION + "\\.tar\\.gz"))
        | .browser_download_url
    ')

    if [ -z "$tar_url" ]; then
        print_msg "${RED}" "找不到指定版本的下载链接：$version"
        return
    fi

    # 创建临时目录
    local temp_dir
    temp_dir=$(mktemp -d)
    if [ ! -d "$temp_dir" ]; then
        print_msg "${RED}" "创建临时目录失败。"
        return
    fi
    trap 'rm -rf "$temp_dir"' EXIT

    cd "$temp_dir" || { print_msg "${RED}" "进入临时目录失败。"; return; }

    # 下载压缩包
    download_file "$tar_url" "midjourney-proxy-linux-${ARCH}-${version}.tar.gz" || {
        print_msg "${RED}" "下载失败。"
        return
    }

    # 创建目标目录
    mkdir -p "$OLDPWD/$version"

    # 解压到目标目录
    if ! tar -xzf "midjourney-proxy-linux-${ARCH}-${version}.tar.gz" -C "$OLDPWD/$version"; then
        print_msg "${RED}" "解压文件失败。"
        return
    fi

    cd "$OLDPWD" || { print_msg "${RED}" "返回原目录失败。"; return; }

    print_msg "${GREEN}" "版本 $version 安装完成。"
}

# 删除指定版本
delete_version() {
    read -rp "请输入要删除的版本（例如：v2.3.7）： " version
    if [[ "$version" =~ ^v[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
        if [ -d "$version" ]; then
            read -rp "确定要删除 $version 吗？ [y/N]: " confirm
            confirm=$(echo "$confirm" | tr '[:upper:]' '[:lower:]')
            if [ "$confirm" == "y" ]; then
                rm -rf "$version" && print_msg "${GREEN}" "版本 $version 已删除。" || print_msg "${RED}" "删除版本 $version 失败。"
            else
                print_msg "${YELLOW}" "已取消删除。"
            fi
        else
            print_msg "${RED}" "版本 $version 未安装。"
        fi
    else
        print_msg "${RED}" "无效的版本格式。"
    fi
}

# 下载文件，支持使用加速
download_file() {
    local url="$1"
    local output="$2"

    if $USE_ACCELERATION; then
        local proxy_url="https://ghproxy.com/${url#https://}"
        print_msg "${BLUE}" "正在使用加速下载 $proxy_url..."
        if ! curl -L -o "$output" "$proxy_url"; then
            print_msg "${RED}" "下载失败，请检查网络连接。"
            return 1
        fi
    else
        print_msg "${BLUE}" "正在下载 $url..."
        if ! curl -L -o "$output" "$url"; then
            print_msg "${RED}" "下载失败，请检查网络连接。"
            return 1
        fi
    fi
}

start_version() {
    local version="$1"

    if [ ! -d "$version" ]; then
        print_msg "${RED}" "版本 $version 未安装。"
        return 1
    fi

    local settings_file=""
    if [ -f "$version/appsettings.Production.json" ]; then
        settings_file="$version/appsettings.Production.json"
    elif [ -f "$version/appsettings.json" ]; then
        cp "$version/appsettings.json" "$version/appsettings.Production.json"
        if [ ! -f "$version/appsettings.Production.json" ]; then
            print_msg "${RED}" "复制配置文件失败。"
            return 1
        fi
        settings_file="$version/appsettings.Production.json"
    else
        print_msg "${RED}" "未找到配置文件。"
        return 1
    fi

    # 创建一个临时文件来存储无注释的 JSON
    local temp_json
    temp_json=$(mktemp)

    # 移除注释（行注释和行内注释）
    sed -E 's,([^:])(//.*),\1,g' "$settings_file" > "$temp_json"

    local urls
    urls=$(jq -r '.urls' "$temp_json")
    rm -f "$temp_json"  # 删除临时文件

    # 获取本机公网和内网IP
    local public_ip
    local private_ip
    public_ip=$(curl -s ifconfig.me)
    private_ip=$(hostname -I | awk '{print $1}')

    local flag=true

    if [ -z "$urls" ] || [ "$urls" == "null" ]; then
        print_msg "${YELLOW}" "未在配置文件中找到 'urls' 字段。"
        flag=false
    fi

    cd "$version" || { print_msg "${RED}" "无法进入目录 $version"; return 1; }
    chmod +x ./run_app.sh
    nohup ./run_app.sh > "../$version.log" 2>&1 &

    local pid=$!
    cd - > /dev/null || return 1

    # 等待3秒
    sleep 3

    # 检查进程是否仍在运行
    if ps -p "$pid" > /dev/null 2>&1; then
        # 将版本号和 PID 写入运行版本配置文件
        echo "$version:$pid" >> "running_versions.conf"
        print_msg "${GREEN}" "版本 $version 已启动，PID: $pid。"

        if $flag; then
            local private_url="${urls//\*/$private_ip}"
            local public_url="${urls//\*/$public_ip}"
            print_msg "${GREEN}" "内网地址: $private_url"
            print_msg "${GREEN}" "外网地址: $public_url"
        fi
    else
        print_msg "${RED}" "版本 $version 启动失败，请检查日志文件：$version.log"
        return 1
    fi
}

stop_version() {
    local version="$1"

    if [ ! -s "running_versions.conf" ]; then
        print_msg "${YELLOW}" "版本 $version 未在运行。"
        return 1
    fi

    local pid
    pid=$(awk -F":" -v ver="$version" '$1 == ver {print $2}' "running_versions.conf")
    if [ -z "$pid" ]; then
        print_msg "${YELLOW}" "版本 $version 未在运行。"
        return 1
    fi

    if kill "$pid" > /dev/null 2>&1; then
        print_msg "${GREEN}" "已停止版本 $version，PID: $pid"
        grep -vE "^$version:$pid$|^$" "running_versions.conf" > "running_versions.tmp" && mv "running_versions.tmp" "running_versions.conf"
    else
        print_msg "${RED}" "停止版本 $version 失败，可能进程已不存在。"
        grep -vE "^$version:$pid$|^$" "running_versions.conf" > "running_versions.tmp" && mv "running_versions.tmp" "running_versions.conf"
    fi
}

list_running_versions() {
    if [ ! -s "running_versions.conf" ]; then
        print_msg "${YELLOW}" "没有正在运行的版本。"
        return
    fi

    local running_versions=()
    local updated_entries=()
    while IFS=":" read -r version pid; do
        if [[ -n "$version" && -n "$pid" ]] && ps -p "$pid" > /dev/null 2>&1; then
            running_versions+=("$version (PID: $pid)")
            updated_entries+=("$version:$pid")
        else
            if [[ -n "$version" ]]; then
                print_msg "${YELLOW}" "版本 $version (PID: $pid) 已停止，移除记录。"
            fi
        fi
    done < "running_versions.conf"

    printf "%s\n" "${updated_entries[@]}" > "running_versions.conf"

    if [ "${#running_versions[@]}" -gt 0 ]; then
        print_msg "${GREEN}" "正在运行的版本："
        for entry in "${running_versions[@]}"; do
            echo "  $entry"
        done
    else
        print_msg "${YELLOW}" "没有正在运行的版本。"
    fi
}

update_version() {
    local version="$1"

    if [ ! -d "$version" ]; then
        print_msg "${RED}" "版本 $version 未安装，无法更新。"
        return 1
    fi

    stop_version "$version"

    print_msg "${BLUE}" "正在更新版本 $version ..."

    # 获取最新版本信息
    get_latest_version_info
    if [ -z "$LATEST_VERSION" ] || [ -z "$DOWNLOAD_URL" ]; then
        print_msg "${RED}" "获取最新版本信息失败。"
        return 1
    fi

    # 检查版本是否需要更新
    if [ "$version" == "$LATEST_VERSION" ]; then
        print_msg "${GREEN}" "版本 $version 已是最新，无需更新。"
        return 0
    fi

    # 在更新前备份配置文件
    if [ ! -f "$version/appsettings.Production.json" ]; then
        if [ -f "$version/appsettings.json" ]; then
            cp "$version/appsettings.json" "$version/appsettings.Production.json"
            print_msg "${GREEN}" "已将 appsettings.json 复制为 appsettings.Production.json"
        else
            print_msg "${YELLOW}" "警告：未发现 appsettings.json"
        fi
    fi

    # 下载最新版本的安装包
    local temp_dir
    temp_dir=$(mktemp -d)
    if [ ! -d "$temp_dir" ]; then
        print_msg "${RED}" "创建临时目录失败。"
        return 1
    fi
    trap 'rm -rf "$temp_dir"' EXIT

    cd "$temp_dir" || { print_msg "${RED}" "进入临时目录失败。"; return 1; }

    # 下载最新版本的压缩包
    print_msg "${BLUE}" "正在下载最新版本的安装包..."
    download_file "$DOWNLOAD_URL" "midjourney-proxy-linux-${ARCH}-${LATEST_VERSION}.tar.gz" || {
        print_msg "${RED}" "下载失败。"
        return 1
    }

    # 解压到版本目录，覆盖安装
    if ! tar -xzf "midjourney-proxy-linux-${ARCH}-${LATEST_VERSION}.tar.gz" -C "$OLDPWD/$version"; then
        print_msg "${RED}" "解压文件失败。"
        return 1
    fi

    if ! cd "$OLDPWD"; then
        exit_with_error "在安装更新时返回原目录失败。"
    fi

    # 更新成功后重命名目录
    if ! mv "$version" "${LATEST_VERSION}"; then
        print_msg "${RED}" "重命名目录失败。"
        return 1
    fi

    print_msg "${GREEN}" "版本 $version 已更新到最新版本 $LATEST_VERSION 并重命名目录。"
}

# ================================
# 主菜单
# ================================

main_menu() {
    while true; do
        echo
        echo -e "${BLUE}Midjourney Proxy 安装脚本${NC}"
        check_docker_status
        list_installed_versions
        list_running_versions
        echo -e "1. ${GREEN}Docker版本（推荐，仅支持x64）${NC}"
        echo -e "2. ${GREEN}Linux版本（支持x64和arm64）${NC}"
        echo -e "3. ${GREEN}退出${NC}"
        read -rp "请选择 (1-3)： " choice

        case "$choice" in
        1)
            if [ "$ARCH" != "x64" ]; then
                print_msg "${RED}" "Docker版本目前仅支持x64架构。"
            else
                docker_submenu
            fi
            ;;
        2)
            linux_menu
            ;;
        3)
            print_msg "${GREEN}" "退出。"
            exit 0
            ;;
        *)
            print_msg "${RED}" "无效选项，请输入1到3之间的数字。"
            ;;
        esac
    done
}

docker_submenu() {
    while true; do
        echo
        check_docker_status
        echo -e "${BLUE}Docker 菜单:${NC}"
        echo -e "1. ${GREEN}安装 Docker${NC}"
        echo -e "2. ${GREEN}安装或更新并启动容器${NC}"
        echo -e "3. ${GREEN}启动但不更新容器${NC}"
        echo -e "4. ${GREEN}停止容器${NC}"
        echo -e "5. ${GREEN}返回主菜单${NC}"
        read -rp "请选择 (1-5)： " option

        case "$option" in
        1)
            if [ "$docker_installed" = false ]; then
                install_docker
            else
                print_msg "${YELLOW}" "Docker 已安装。"
            fi
            ;;
        2)
            if [ "$docker_installed" = true ]; then
                run_docker_container
            else
                print_msg "${RED}" "Docker 未安装，请先安装 Docker。"
            fi
            ;;
        3)
            if [ "$docker_installed" = true ]; then
                start_docker_container
            else
                print_msg "${RED}" "Docker 未安装，请先安装 Docker。"
            fi
            ;;
        4)
            if [ "$docker_installed" = true ]; then
                if [ "$(docker ps -q -f name=${CONTAINER_NAME})" ]; then
                    stop_docker_container
                else
                    print_msg "${YELLOW}" "容器 $CONTAINER_NAME 未在运行。"
                fi
            else
                print_msg "${RED}" "Docker 未安装，请先安装 Docker。"
            fi
            ;;
        5)
            break
            ;;
        *)
            print_msg "${RED}" "无效选项，请输入1到5之间的数字。"
            ;;
        esac
    done
}

linux_menu() {
    while true; do
        echo
        list_installed_versions
        list_running_versions
        echo -e "${BLUE}Linux版本菜单:${NC}"
        echo -e "1. ${GREEN}安装最新版本${NC}"
        echo -e "2. ${GREEN}安装指定版本${NC}"
        echo -e "3. ${GREEN}删除指定版本${NC}"
        echo -e "4. ${GREEN}启动指定版本${NC}"
        echo -e "5. ${GREEN}停止指定版本${NC}"
        echo -e "6. ${GREEN}更新已安装的版本${NC}"
        echo -e "7. ${GREEN}返回主菜单${NC}"
        read -rp "请选择 (1-7)： " option

        case "$option" in
        1)
            get_latest_version_info
            install_version "$LATEST_VERSION"
            ;;
        2)
            read -rp "请输入要安装的版本（例如：v2.3.7）： " version
            install_version "$version"
            ;;
        3)
            delete_version
            ;;
        4)
            read -rp "请输入要启动的版本号： " version
            start_version "$version"
            ;;
        5)
            read -rp "请输入要停止的版本号： " version
            stop_version "$version"
            ;;
        6)
            read -rp "请输入要更新的版本号： " version
            update_version "$version"
            ;;
        7)
            break
            ;;
        *)
            print_msg "${RED}" "无效选项，请输入1到7之间的数字。"
            ;;
        esac
    done
}

# ================================
# 脚本初始化
# ================================

main() {
    detect_package_manager
    check_dependencies
    check_architecture
    create_running_versions_file
    load_config
    check_docker_installed
    main_menu
}

main "$@"