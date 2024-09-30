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
    elif command -v yum &>/dev/null; then
        PKG_MANAGER="yum"
    elif command -v apt-get &>/dev/null; then
        PKG_MANAGER="apt-get"
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
        apt-get update -y
        apt-get install "$package" -y || exit_with_error "安装 $package 失败。"
        ;;
    yum)
        yum makecache -y
        yum install "$package" -y || exit_with_error "安装 $package 失败。"
        ;;
    dnf)
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
    echo "正在检查并安装依赖"
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
        ;;
    aarch64)
        ARCH="arm64"
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
    else
        ask_acceleration
    fi
}

check_docker_installed() {
    if ! command -v docker &>/dev/null; then
        docker_installed=false
    else
        docker_installed=true
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
                print_msg "${RED}" "Docker 安装失败，请检查错误信息并重试。"
                exit 1
            fi
        else
            if curl -fsSL https://get.docker.com | bash -s docker; then
                docker_try=true
                install_docker
            else
                print_msg "${RED}" "Docker 安装失败，请检查错误信息并重试。"
                exit 1
            fi
        fi
    else
        docker_installed=true
        print_msg "${GREEN}" "Docker 已安装。"
    fi
}

run_docker_container() {
    print_msg "${BLUE}" "正在拉取最新的 Docker 镜像..."
    docker pull ${IMAGE_NAME} || exit_with_error "拉取 Docker 镜像失败。"

    # 停止并删除现有容器
    if [ "$(docker ps -q -f name=${CONTAINER_NAME})" ]; then
        print_msg "${BLUE}" "停止现有容器 ${CONTAINER_NAME}..."
        docker stop ${CONTAINER_NAME}
        docker rm ${CONTAINER_NAME}
    fi

    # 运行容器
    print_msg "${BLUE}" "启动 Docker 容器..."
    docker run --name ${CONTAINER_NAME} -d --restart=always \
        -p 8086:8080 --user root \
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

    print_msg "${GREEN}" "Docker 容器 ${CONTAINER_NAME} 启动成功，请注意端口配置。"
    print_msg "${GREEN}" "访问地址: http://$private_ip:8086 或 http://$public_ip:8086"
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

update_docker_container() {
    print_msg "${BLUE}" "正在更新..."
    run_docker_container
}

check_docker_status() {
    if [ "$docker_installed" = false ]; then
        print_msg "${YELLOW}" "Docker 未安装。"
        # docker_installed=false
    else
        # docker_installed=true
        if [ "$(docker ps -q -f name=${CONTAINER_NAME})" ]; then
            print_msg "${GREEN}" "容器 ${CONTAINER_NAME} 正在运行。"
        else
            print_msg "${YELLOW}" "容器 ${CONTAINER_NAME} 未在运行。"
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

    response=$(curl -s "$API_URL") || exit_with_error "获取最新版本信息失败。"
    LATEST_VERSION=$(echo "$response" | jq -r '.tag_name')
    DOWNLOAD_URL=$(echo "$response" | jq -r --arg ARCH "$ARCH" '.assets[] | select(.name | test("midjourney-proxy-linux-\($ARCH)")) | .browser_download_url')

    if [ -z "$LATEST_VERSION" ] || [ -z "$DOWNLOAD_URL" ]; then
        exit_with_error "无法获取最新版本的信息。"
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
    response=$(curl -s "$specific_api_url") || exit_with_error "获取版本 $version 的信息失败。"

    local tar_url
    tar_url=$(echo "$response" | jq -r --arg ARCH "$ARCH" '.assets[] | select(.name | test("midjourney-proxy-linux-\($ARCH)-$version.tar.gz")) | .browser_download_url')

    if [ -z "$tar_url" ]; then
        exit_with_error "找不到指定版本的下载链接：$version"
    fi

    # 创建临时目录
    local temp_dir
    temp_dir=$(mktemp -d) || exit_with_error "创建临时目录失败。"
    trap 'rm -rf "$temp_dir"' EXIT

    cd "$temp_dir" || exit_with_error "进入临时目录失败。"

    # 下载压缩包
    download_file "$tar_url" "midjourney-proxy-linux-${ARCH}-${version}.tar.gz"

    # 解压
    tar -xzf "midjourney-proxy-linux-${ARCH}-${version}.tar.gz" || exit_with_error "解压文件失败。"

    # 获取解压后的目录
    local extracted_dir
    extracted_dir=$(tar -tzf "midjourney-proxy-linux-${ARCH}-${version}.tar.gz" | head -1 | cut -f1 -d "/")

    if [ -d "$extracted_dir" ]; then
        mv "$extracted_dir" "$OLDPWD/$version" || exit_with_error "移动解压目录失败。"
    else
        exit_with_error "未找到解压目录。可能安装失败。"
    fi

    cd "$OLDPWD" || exit_with_error "返回原目录失败。"

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
            exit_with_error "下载失败，请检查网络连接。"
        fi
    else
        print_msg "${BLUE}" "正在下载 $url..."
        if ! curl -L -o "$output" "$url"; then
            exit_with_error "下载失败，请检查网络连接。"
        fi
    fi
}

# ================================
# 主菜单
# ================================

main_menu() {
    while true; do
        echo
        check_docker_status
        list_installed_versions
        echo -e "1. ${GREEN}Docker版本（推荐，仅支持x64）${NC}"
        echo -e "2. ${GREEN}Linux版本（支持x64和arm64）${NC}"
        echo -e "3. ${GREEN}退出${NC}"
        read -rp "请选择 (1-3)： " choice

        case "$choice" in
        1)
            if [ "$ARCH" != "x64" ]; then
                print_msg "${RED}" "Docker版本仅支持x64架构。"
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
        echo -e "2. ${GREEN}启动容器${NC}"
        echo -e "3. ${GREEN}停止容器${NC}"
        echo -e "4. ${GREEN}更新容器${NC}"
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
                stop_docker_container
            else
                print_msg "${RED}" "Docker 未安装，无法停止容器。"
            fi
            ;;
        4)
            if [ "$docker_installed" = true ]; then
                update_docker_container
            else
                print_msg "${RED}" "Docker 未安装，无法更新容器。"
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
        echo -e "${BLUE}Linux版本菜单:${NC}"
        echo -e "1. ${GREEN}安装最新版本${NC}"
        echo -e "2. ${GREEN}安装指定版本${NC}"
        echo -e "3. ${GREEN}删除指定版本${NC}"
        echo -e "4. ${GREEN}返回主菜单${NC}"
        read -rp "请选择 (1-4)： " option

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
            break
            ;;
        *)
            print_msg "${RED}" "无效选项，请输入1到4之间的数字。"
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
    load_config
    check_docker_installed
    main_menu
}

main "$@"