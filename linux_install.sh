#!/bin/bash

# 定义颜色
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# 配置文件存放目录
CONFIG_DIR="config_files"

# 检查是否安装了 curl 和 jq
if ! command -v curl &> /dev/null; then
    echo -e "${YELLOW}curl is required but not installed. Installing curl...${NC}"
    sudo apt-get update && sudo apt-get install curl -y
fi

if ! command -v jq &> /dev/null; then
    echo -e "${YELLOW}jq is required but not installed. Installing jq...${NC}"
    sudo apt-get update && sudo apt-get install jq -y
fi

# 检查 CPU 架构
ARCH=$(uname -m)
if [ "$ARCH" == "x86_64" ]; then
    ARCH="x64"
elif [[ "$ARCH" == "aarch64" || "$ARCH" == "arm64" ]]; then
    ARCH="arm64"
else
    echo -e "${RED}Unsupported architecture: $ARCH${NC}"
    exit 1
fi

# GitHub API URL for latest release
API_URL="https://api.github.com/repos/trueai-org/midjourney-proxy/releases/latest"


# 使用 curl 下载文件
function download_file() {
    local url=$1
    local output=$2

    echo -e "${BLUE}Downloading $url...${NC}"
    curl -L -o "$output" "$url"
    if [ $? -ne 0 ]; then
        echo -e "${RED}Failed to download $url.${NC}"
        echo -e "${YELLOW}Switching to proxy and retrying...${NC}"
        url="https://mirror.ghproxy.com/${url#https://}"
        curl -L -o "$output" "$url"
        if [ $? -ne 0 ]; then
            echo -e "${RED}Failed to download $url using proxy. Please check your network and try again.${NC}"
            exit 1
        fi
    fi
}

# 获取最新版本的下载链接
function get_latest_version_info() {
    local api_url=$API_URL

    # 使用 curl 获取响应
    function fetch_version_info() {
        curl -s $api_url
    }

    # 尝试获取初始版本信息
    RESPONSE=$(fetch_version_info)
    if [ $? -ne 0 ]; then
        echo -e "${YELLOW}Failed to retrieve version info. Switching to proxy...${NC}"
        api_url="https://mirror.ghproxy.com/${API_URL#https://}"
        RESPONSE=$(fetch_version_info)
        if [ $? -ne 0 ]; then
            echo -e "${RED}Failed to retrieve version info using proxy. Please check your network and try again.${NC}"
            exit 1
        fi
    fi

    LATEST_VERSION=$(echo $RESPONSE | jq -r '.tag_name')
    DOWNLOAD_URL=$(echo $RESPONSE | jq -r ".assets[] | select(.name | test(\"midjourney-proxy-linux-${ARCH}\")) | .browser_download_url")

    if [ -n "$LATEST_VERSION" ] && [ -n "$DOWNLOAD_URL" ]; then
        LATEST_TAR_URL=$DOWNLOAD_URL
    else
        echo -e "${RED}Failed to retrieve the latest version information.${NC}"
        exit 1
    fi
}


# 初始化配置文件存放目录
function init_config_dir() {
    if [ ! -d "$CONFIG_DIR" ]; then
        mkdir -p "$CONFIG_DIR"
        echo -e "${GREEN}Configuration directory created: $CONFIG_DIR${NC}"
    fi
}

# 列出配置文件
function list_config_files() {
    CONFIG_FILES=()
    echo -e "${BLUE}Configuration files:${NC}"
    local index=1
    for config_file in "$CONFIG_DIR"/*; do
        if [ -f "$config_file" ]; then
            CONFIG_FILES+=("$(basename "$config_file")")
            echo -e "  $index) ${CONFIG_FILES[-1]}"
            index=$((index + 1))
        fi
    done

    if [ ${#CONFIG_FILES[@]} -eq 0 ]; then
        echo -e "${YELLOW}No configuration files found.${NC}"
        return 1
    else
        return 0
    fi
}

# 根据用户选择的编号获取配置文件名称
function get_config_file_by_number() {
    local num=$1
    if (( num > 0 && num <= ${#CONFIG_FILES[@]} )); then
        echo "${CONFIG_FILES[$((num-1))]}"
    else
        echo ""
    fi
}

# 重命名配置文件
function rename_config_file() {
    if list_config_files; then
        read -p "Select configuration file to rename by number: " num
        selected_file=$(get_config_file_by_number $num)
        if [ -n "$selected_file" ]; then
            read -p "Enter the new configuration file name: " NEW_NAME
            mv "$CONFIG_DIR/$selected_file" "$CONFIG_DIR/$NEW_NAME"
            echo -e "${GREEN}Configuration file renamed to: $NEW_NAME${NC}"
        else
            echo -e "${RED}Invalid selection.${NC}"
        fi
    fi
}

# 删除配置文件
function delete_config_file() {
    if list_config_files; then
        read -p "Select configuration file to delete by number: " num
        selected_file=$(get_config_file_by_number $num)
        if [ -n "$selected_file" ];then
            read -p "Are you sure you want to delete $selected_file? (y/n): " CONFIRM
            if [ "$CONFIRM" == "y" ]; then
                rm "$CONFIG_DIR/$selected_file"
                echo -e "${GREEN}Configuration file deleted: $selected_file${NC}"
            else
                echo -e "${YELLOW}Deletion canceled.${NC}"
            fi
        else
            echo -e "${RED}Invalid selection.${NC}"
        fi
    fi
}

# 从指定版本导出配置文件
function export_config_file() {
    read -p "Enter the version to export configuration from (e.g., v2.3.7): " VERSION
    if [ -d "$VERSION" ]; then
        read -p "Enter the name for the exported configuration file: " EXPORT_NAME
        cp "$VERSION/appsettings.json" "$CONFIG_DIR/$EXPORT_NAME"
        echo -e "${GREEN}Configuration file exported as: $EXPORT_NAME${NC}"
    else
        echo -e "${RED}Version directory not found: $VERSION${NC}"
    fi
}

# 注意：此函数用于在安装版本后应用配置文件
function prompt_apply_config() {
    echo -e "${GREEN}Installation completed for version $1.${NC}"
    if list_config_files; then
        read -p "Do you want to apply a configuration file now? (y/n): " APPLY_CONFIG
        if [ "$APPLY_CONFIG" == "y" ];then
            list_config_files
            read -p "Select configuration file to apply by number: " num
            selected_file=$(get_config_file_by_number $num)
            if [ -n "$selected_file" ]; then
                cp "$CONFIG_DIR/$selected_file" "$1/appsettings.json"
                echo -e "${GREEN}Configuration file $selected_file applied to version $1.${NC}"
            else
                echo -e "${RED}Invalid selection. No configuration file applied.${NC}"
            fi
        else
            echo -e "${YELLOW}No configuration file applied. You can manually modify the configuration at $1/appsettings.json${NC}"
        fi
    else
        echo -e "${YELLOW}No configuration files found. You can manually modify the configuration at $1/appsettings.json${NC}"
    fi
}

# 将配置文件应用到指定版本
function apply_config_to_version() {
    read -p "Enter the version to apply configuration to (e.g., v2.3.7): " VERSION
    if [ -d "$VERSION" ]; then
        if list_config_files; then
            read -p "Select configuration file to apply by number: " num
            selected_file=$(get_config_file_by_number $num)
            if [ -n "$selected_file" ]; then
                cp "$CONFIG_DIR/$selected_file" "$VERSION/appsettings.json"
                echo -e "${GREEN}Configuration file applied to version: $VERSION${NC}"
            else
                echo -e "${RED}Invalid selection.${NC}"
            fi
        fi
    else
        echo -e "${RED}Version directory not found: $VERSION${NC}"
    fi
}

# 检查已安装的版本
function list_installed_versions() {
    INSTALLED_VERSIONS=()
    for version_dir in v*; do
        if [ -d "$version_dir" ]; then
            version=$(basename $version_dir)
            INSTALLED_VERSIONS+=($version)
        fi
    done

    if [ ${#INSTALLED_VERSIONS[@]} -eq 0 ]; then
        echo -e "${RED}No versions installed.${NC}"
    else
        echo -e "${BLUE}Installed versions:${NC}"
        for version in "${INSTALLED_VERSIONS[@]}"; do
            echo -e "  $version${NC}"
        done
    fi
}

# 检查最新版本是否已安装
function check_latest_version() {
    for version in "${INSTALLED_VERSIONS[@]}"; do
        if [ "$version" == "$LATEST_VERSION" ]; then
            echo -e "${GREEN}You already have the latest version installed: $LATEST_VERSION${NC}"
            return
        fi
    done
    echo -e "${YELLOW}A new version is available: $LATEST_VERSION.${NC}"
}

# 删除指定版本
function delete_version() {
    read -p "Enter the version you want to delete (e.g., v2.3.7): " VERSION
    if [ -d "$VERSION" ]; then
        rm -rf "$VERSION"
        echo -e "${GREEN}Version $VERSION deleted.${NC}"
    else
        echo -e "${RED}Version $VERSION is not installed.${NC}"
    fi
}

# 新增函数：导入现有版本配置
function import_config_from_existing() {
    if [ ${#INSTALLED_VERSIONS[@]} -eq 0 ]; then
        echo -e "${RED}No installed versions available to import configuration.${NC}"
        return 1
    fi

    echo -e "${BLUE}Available installed versions to import configuration from:${NC}"
    local index=1
    for version in "${INSTALLED_VERSIONS[@]}"; do
        echo -e "  $index) $version"
        index=$((index + 1))
    done

    read -p "Select a version to import configuration from (by number): " num
    if (( num > 0 && num <= ${#INSTALLED_VERSIONS[@]} )); then
        selected_version="${INSTALLED_VERSIONS[$((num-1))]}"
        cp "$selected_version/appsettings.json" "$1/appsettings.json"
        echo -e "${GREEN}Configuration from $selected_version imported to version $1.${NC}"
    else
        echo -e "${RED}Invalid selection. No configuration imported.${NC}"
    fi
}

# 安装指定版本
function install_version() {
    VERSION=$1
    if [ -d "$VERSION" ]; then
        echo -e "${YELLOW}Version $VERSION is already installed. Please delete it first.${NC}"
        return 1
    fi

    SPECIFIC_API_URL="https://api.github.com/repos/trueai-org/midjourney-proxy/releases/tags/$VERSION"
    RESPONSE=$(curl -s $SPECIFIC_API_URL)
    TAR_URL=$(echo $RESPONSE | jq -r ".assets[] | select(.name | test(\"midjourney-proxy-linux-${ARCH}-$VERSION.tar.gz\")) | .browser_download_url")

    if [ -z "$TAR_URL" ]; then
        echo -e "${RED}No download link found for specified version: $VERSION${NC}"
        return 1
    fi

    # 记录当前目录
    ORIGINAL_DIR=$(pwd)

    # 创建临时目录
    TEMP_DIR=$(mktemp -d)
    cd $TEMP_DIR

    # 下载指定版本的 tar 文件
    download_file $TAR_URL "midjourney-proxy-linux-${ARCH}-$VERSION.tar.gz"

    # 解压 tar 文件
    tar -xzf "midjourney-proxy-linux-${ARCH}-$VERSION.tar.gz"

    # 检查解压出来的文件或目录名
    EXTRACTED_DIR=$(tar -tzf "midjourney-proxy-linux-${ARCH}-$VERSION.tar.gz" | head -1 | cut -f1 -d "/")

    if [ "$EXTRACTED_DIR" == "." ]; then
        cd /
        mv "$TEMP_DIR" "$ORIGINAL_DIR/${VERSION}"
        cd $ORIGINAL_DIR
    fi

    if [ -d "$EXTRACTED_DIR" ]; then
        # 移动解压目录到目标位置
        mv "$EXTRACTED_DIR" "$ORIGINAL_DIR/${VERSION}"
        cd $ORIGINAL_DIR
    else
        echo -e "${RED}Failed to find extracted directory. Installation might have failed.${NC}"
    fi

    # 清理下载文件和临时目录
    cd $ORIGINAL_DIR
    rm -rf $TEMP_DIR

    # 新增：提示用户选择是否从现有版本导入配置文件
    echo -e "${GREEN}Installation completed for version $VERSION.${NC}"
    read -p "Do you want to import configuration from an existing version? (y/n): " IMPORT_CONFIG
    if [ "$IMPORT_CONFIG" == "y" ]; then
        import_config_from_existing "$VERSION"
    else
        prompt_apply_config "$VERSION"
    fi

    return 0
}

# 获取最新版本信息
get_latest_version_info

# 初始化配置目录
init_config_dir

until [ "$OPTION" == "5" ]; do
    echo
    list_installed_versions
    check_latest_version
    echo "Menu:"
    echo -e "1. ${GREEN}Install or update to the latest version ($LATEST_VERSION)${NC}"
    echo -e "2. ${GREEN}Install a specific version${NC}"
    echo -e "3. ${GREEN}Delete a specific version${NC}"
    echo -e "4. ${GREEN}Manage configuration files${NC}"
    echo -e "5. ${GREEN}Exit${NC}"
    read -p "Choose an option (1/2/3/4/5): " OPTION

    case $OPTION in
        1)
            install_version $LATEST_VERSION
            ;;
        2)
            read -p "Enter the version you want to install (e.g., v2.3.7): " VERSION
            install_version $VERSION
            ;;
        3)
            delete_version
            ;;
        4)
            CONFIG_OPTION=""
            until [ "$CONFIG_OPTION" == "6" ]; do
                echo "Configuration File Management:"
                echo -e "1. ${GREEN}List configuration files${NC}"
                echo -e "2. ${GREEN}Rename a configuration file${NC}"
                echo -e "3. ${GREEN}Delete a configuration file${NC}"
                echo -e "4. ${GREEN}Export configuration from version${NC}"
                echo -e "5. ${GREEN}Apply configuration to version${NC}"
                echo -e "6. ${GREEN}Back to main menu${NC}"
                read -p "Choose an option (1/2/3/4/5/6): " CONFIG_OPTION

                case $CONFIG_OPTION in
                    1)
                        list_config_files
                        ;;
                    2)
                        rename_config_file
                        ;;
                    3)
                        delete_config_file
                        ;;
                    4)
                        export_config_file
                        ;;
                    5)
                        apply_config_to_version
                        ;;
                    6)
                        ;;
                    *)
                        echo -e "${RED}Invalid option.${NC}"
                        ;;
                esac
            done
            ;;
        5)
            echo -e "${GREEN}Exiting.${NC}"
            ;;
        *)
            echo -e "${RED}Invalid option.${NC}"
            ;;
    esac
done
