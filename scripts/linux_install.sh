#!/bin/bash

# 定义颜色
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# 配置文件存放目录
CONFIG_DIR="config_files"

# 检查包管理器类型
if command -v apt-get &> /dev/null; then
    PKG_MANAGER="apt-get"
elif command -v yum &> /dev/null; then
    PKG_MANAGER="yum"
elif command -v dnf &> /dev/null; then
    PKG_MANAGER="dnf"
else
    echo -e "${RED}No supported package manager found (apt-get, yum, dnf).${NC}"
    exit 1
fi

# 检查是否安装了 curl 和 jq
if ! command -v curl &> /dev/null; then
    echo -e "${YELLOW}curl is required but not installed. Installing curl...${NC}"
    sudo $PKG_MANAGER update && sudo $PKG_MANAGER install curl -y
fi

if ! command -v jq &> /dev/null; then
    echo -e "${YELLOW}jq is required but not installed. Installing jq...${NC}"
    sudo $PKG_MANAGER update && sudo $PKG_MANAGER install jq -y
fi

# 检查 curl 和 jq 是否成功安装
if ! command -v curl &> /dev/null; then
    echo -e "${RED}Failed to install curl. Please install it manually.${NC}"
    exit 1
fi

if ! command -v jq &> /dev/null; then
    echo -e "${RED}Failed to install jq. Please install it manually.${NC}"
    exit 1
fi

# 下载文件函数，包含镜像重试逻辑
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
            # 检查新名称是否为空或已存在
            if [ -z "$NEW_NAME" ] || [ -e "$CONFIG_DIR/$NEW_NAME" ]; then
                echo -e "${RED}Invalid new name or file already exists.${NC}"
            else
                mv "$CONFIG_DIR/$selected_file" "$CONFIG_DIR/$NEW_NAME"
                echo -e "${GREEN}Configuration file renamed to: $NEW_NAME${NC}"
            fi
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
            read -p "Are you sure you want to delete $selected_file? [y/N]: " CONFIRM
            CONFIRM=$(echo "$CONFIRM" | tr '[:upper:]' '[:lower:]')
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
    list_installed_versions
    read -p "Enter the version to export configuration from (e.g., v2.3.7): " VERSION
    if [ -d "$VERSION" ]; then
        read -p "Enter the name for the exported configuration file: " EXPORT_NAME
        # 删除用户输入的名字中的 `.json` 后缀
        EXPORT_NAME="${EXPORT_NAME%.json}"
        # 检查新名称是否为空或已存在
        if [ -z "$EXPORT_NAME" ] || [ -e "$CONFIG_DIR/$EXPORT_NAME.json" ]; then
            echo -e "${RED}Invalid new name or file already exists.${NC}"
        else
            cp "$VERSION/appsettings.json" "$CONFIG_DIR/$EXPORT_NAME.json"
            echo -e "${GREEN}Configuration file exported as: $EXPORT_NAME${NC}"
        fi
    else
        echo -e "${RED}Version directory not found: $VERSION${NC}"
    fi
}

# 提示应用配置文件
function prompt_apply_config() {
    echo -e "${GREEN}Installation completed for version $1.${NC}"
    if list_config_files; then
        read -p "Do you want to apply a configuration file now? [y/N]: " APPLY_CONFIG
        APPLY_CONFIG=$(echo "$APPLY_CONFIG" | tr '[:upper:]' '[:lower:]')
        if [ "$APPLY_CONFIG" == "y" ]; then
            list_config_files
            read -p "Select configuration file to apply by number: " num
            selected_file=$(get_config_file_by_number $num)
            if [ -n "$selected_file" ]; then
                if [ -e "$1/appsettings.json" ]; then
                    read -p "Target version already has an appsettings.json. Do you want to merge? (y/N): " MERGE_CONFIRM
                    if [[ "$MERGE_CONFIRM" =~ ^[yY]$ ]]; then
                        compare_and_merge_json "$1/appsettings.json" "$CONFIG_DIR/$selected_file"
                    else
                        echo -e "${YELLOW}Configuration not merged.${NC}"
                    fi
                else
                    cp "$CONFIG_DIR/$selected_file" "$1/appsettings.json"
                    echo -e "${GREEN}Configuration file $selected_file applied to version $1.${NC}"
                fi
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
                if [ -e "$VERSION/appsettings.json" ]; then
                    read -p "Target version already has an appsettings.json. Do you want to merge? (y/N): " MERGE_CONFIRM
                    if [[ "$MERGE_CONFIRM" =~ ^[yY]$ ]]; then
                        compare_and_merge_json "$VERSION/appsettings.json" "$CONFIG_DIR/$selected_file"
                    else
                        echo -e "${YELLOW}Configuration not merged.${NC}"
                    fi
                else
                    cp "$CONFIG_DIR/$selected_file" "$VERSION/appsettings.json"
                    echo -e "${GREEN}Configuration file applied to version: $VERSION${NC}"
                fi
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
    if [[ "$VERSION" =~ ^v[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
        if [ -d "$VERSION" ]; then
            rm -rf "$VERSION"
            echo -e "${GREEN}Version $VERSION deleted.${NC}"
        else
            echo -e "${RED}Version $VERSION is not installed.${NC}"
        fi
    else
        echo -e "${RED}Invalid version format.${NC}"
    fi
}

# compare_and_merge_json 函数
compare_and_merge_json() {
    local json1="$1"
    local json2="$2"
    local temp_file=$(mktemp)

    # 检查文件是否存在
    if [ ! -f "$json1" ] || [ ! -f "$json2" ]; then
        echo -e "${RED}One or both of the files do not exist.${NC}"
        return 1
    fi

    # 检查JSON格式是否正确
    if ! jq empty "$json1" > /dev/null 2>&1; then
        echo -e "${RED}First file is not a valid JSON${NC}"
        return 1
    fi

    if ! jq empty "$json2" > /dev/null 2>&1; then
        echo -e "${RED}Second file is not a valid JSON${NC}"
        return 1
    fi

    # 输出两者不同的项
    echo -e "${BLUE}Differences between the JSON files:${NC}"
    diff <(jq -S . "$json1") <(jq -S . "$json2")

    # 将相同项文件2覆盖文件1，并保存到临时文件中
    jq -s '.[0] * .[1]' "$json1" "$json2" > "$temp_file"

    # 获取文件的JSON结构
    local structure1=$(jq -S . "$json1" | jq '.. | objects | keys | select(length > 0) | unique' | jq -sc add | jq -S .)
    local structure2=$(jq -S . "$json2" | jq '.. | objects | keys | select(length > 0) | unique' | jq -sc add | jq -S .)

    # 计算缺失和新增的项
    local added_keys=$(jq -n --argfile a <(echo "$structure1") --argfile b <(echo "$structure2") \
        '($b - $a) // empty')

    # 让用户逐一输入新增项的值并保存
    if [ ! -z "$added_keys" ]; then
        echo -e "${YELLOW}Please input values for the following added keys:${NC}"
        for key in $(echo "$added_keys" | jq -r '.[]'); do
            read -p "Value for key \"$key\": " value
            temp_file=$(jq --arg key "$key" --arg value "$value" \
                'setpath([($key | split(".")[])]; $value)' "$temp_file")
        done
    fi

    # 保存合并后的文件
    echo "$temp_file" | jq . > "$json1"
    echo -e "${GREEN}File1 has been updated and saved.${NC}"
}

# 导入现有版本配置
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
        if [ -d "$selected_version" ]; then
            if [ -e "$selected_version/appsettings.json" ]; then
                if [ -e "$1/appsettings.json" ]; then
                    read -p "Target version already has an appsettings.json. Do you want to merge? (y/N): " MERGE_CONFIRM
                    if [[ "$MERGE_CONFIRM" =~ ^[yY]$ ]]; then
                        compare_and_merge_json "$1/appsettings.json" "$selected_version/appsettings.json"
                    else
                        echo -e "${YELLOW}Configuration not merged.${NC}"
                    fi
                else
                    cp "$selected_version/appsettings.json" "$1/appsettings.json"
                    echo -e "${GREEN}Configuration from $selected_version imported to version $1.${NC}"
                fi
            else
                echo -e "${RED}No appsettings.json in the selected version directory.${NC}"
            fi
        else
            echo -e "${RED}Directory not found, no configuration imported.${NC}"
        fi
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

    # 检查临时目录是否创建成功
    TEMP_DIR=$(mktemp -d)
    if [ ! -d "$TEMP_DIR" ]; then
        echo -e "${RED}Failed to create temporary directory. Installation aborted.${NC}"
        return 1
    fi
    cd $TEMP_DIR

    # 下载指定版本的 tar 文件
    download_file $TAR_URL "midjourney-proxy-linux-${ARCH}-$VERSION.tar.gz"

    # 解压 tar 文件
    tar -xzf "midjourney-proxy-linux-${ARCH}-$VERSION.tar.gz"

    # 检查解压出来的文件或目录名
    EXTRACTED_DIR=$(tar -tzf "midjourney-proxy-linux-${ARCH}-$VERSION.tar.gz" | head -1 | cut -f1 -d "/")

    if [ "$EXTRACTED_DIR" == "." ]; then
        mv "$TEMP_DIR" "$ORIGINAL_DIR/${VERSION}"
        cd $ORIGINAL_DIR
    elif [ -d "$EXTRACTED_DIR" ]; then
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
    read -p "Do you want to import configuration from an existing version? [y/N]: " IMPORT_CONFIG
    IMPORT_CONFIG=$(echo "$IMPORT_CONFIG" | tr '[:upper:]' '[:lower:]')
    if [ "$IMPORT_CONFIG" == "y" ]; then
        import_config_from_existing "$VERSION"
    else
        prompt_apply_config "$VERSION"
    fi

    return 0
}

# 启动程序版本
function start_program_version() {
    list_installed_versions
    read -p "Select a version to start (e.g., v2.3.7): " VERSION
    if [ -d "$VERSION" ]; then
        if [ -f "$VERSION/run_app.sh" ]; then
            echo -e "${GREEN}Starting version $VERSION...${NC}"
            # 以后台模式运行程序
            (cd "$VERSION" && nohup ./run_app.sh > "../$VERSION.log" 2>&1 < /dev/null &)
            echo -e "${GREEN}Program started for version $VERSION. Check logs in $VERSION.log.${NC}"
        else
            echo -e "${RED}run_app.sh not found in version directory $VERSION.${NC}"
        fi
    else
        echo -e "${RED}Version directory not found: $VERSION${NC}"
    fi
}

# 检查正在运行的版本函数
function check_running_version() {
    RUNNING_VERSION=""
    for version_dir in v*; do
        if [ -d "$version_dir" ]; then
            pid=$(pgrep -f "$version_dir/run_app.sh")
            if [ ! -z "$pid" ]; then
                RUNNING_VERSION=$version_dir
                echo -e "${GREEN}Running version: $RUNNING_VERSION (PID: $pid)${NC}"
                return
            fi
        fi
    done
    echo -e "${RED}No version is currently running.${NC}"
}

# 停止运行的版本
function stop_running_version() {
    if [ -z "$RUNNING_VERSION" ]; then
        echo -e "${RED}No version is currently running.${NC}"
        return
    fi

    pid=$(pgrep -f "$RUNNING_VERSION/run_app.sh")
    if [ ! -z "$pid" ]; then
        kill $pid
        echo -e "${GREEN}Stopped running version: $RUNNING_VERSION (PID: $pid)${NC}"
    else
        echo -e "${RED}Failed to find the running process for version: $RUNNING_VERSION${NC}"
    fi
}

# 检查是否安装最新版本
function is_latest_version_installed() {
    for version_dir in v*; do
        if [ "$version_dir" == "$LATEST_VERSION" ]; then
            return 0  # 已安装
        fi
    done
    return 1  # 未安装
}

# 找到本地最新版本
function find_local_latest_version() {
    local latest_version="0.0.0"
    for version_dir in v*; do
        version=$(echo $version_dir | sed 's/^v//')
        if dpkg --compare-versions "$version" "gt" "$latest_version"; then
            latest_version=$version
        fi
    done
    echo "v$latest_version"
}

# 删除版本并安装新版本
function delete_version_and_install() {
    VERSION=$1
    if [ -d "$VERSION" ]; then
        rm -rf "$VERSION"
        echo -e "${GREEN}Deleted version $VERSION.${NC}"
    fi
    install_version $VERSION
}

# 从一个版本复制配置到另一个版本
function copy_config_from_version() {
    SOURCE_VERSION=$1
    TARGET_VERSION=$2
    if [ -e "$SOURCE_VERSION/appsettings.json" ]; then
        cp "$SOURCE_VERSION/appsettings.json" "$TARGET_VERSION/appsettings.json"
        echo -e "${GREEN}Configuration copied from $SOURCE_VERSION to $TARGET_VERSION.${NC}"
    else
        echo -e "${RED}No configuration file found in $SOURCE_VERSION.${NC}"
    fi
}

# 安装或更新到最新版本
function install_or_update_latest_version() {
    if is_latest_version_installed; then
        echo -e "${GREEN}The latest version ($LATEST_VERSION) is already installed.${NC}"
        read -p "Do you want to reinstall it? All data will be lost. (y/N): " REINSTALL_CONFIRM
        if [[ "$REINSTALL_CONFIRM" =~ ^[yY]$ ]]; then
            delete_version_and_install $LATEST_VERSION
            prompt_apply_config $LATEST_VERSION
        else
            echo -e "${YELLOW}Reinstall cancelled.${NC}"
        fi
    else
        # 检查本地安装的版本
        local_latest_version=$(find_local_latest_version)
        if [ "$local_latest_version" != "v0.0.0" ]; then
            echo -e "${YELLOW}Local latest version found: $local_latest_version.${NC}"
            read -p "Do you want to perform a one-click update? (y/N): " ONE_CLICK_UPDATE
            if [[ "$ONE_CLICK_UPDATE" =~ ^[yY]$ ]]; then
                stop_running_version
                install_version $LATEST_VERSION
                import_config_from_existing "$LATEST_VERSION"
                start_program_version "$LATEST_VERSION"
            else
                echo -e "${BLUE}How do you want to configure the new version?${NC}"
                echo -e "1. Import configuration from local latest version ($local_latest_version)"
                echo -e "2. Select a different version to import configuration from"
                echo -e "3. Skip configuration for now"
                read -p "Choose an option (1/2/3): " CONFIG_OPTION
                install_version $LATEST_VERSION
                case $CONFIG_OPTION in
                    1)
                        copy_config_from_version "$local_latest_version" "$LATEST_VERSION"
                        ;;
                    2)
                        import_config_from_existing "$LATEST_VERSION"
                        ;;
                    3)
                        prompt_apply_config "$LATEST_VERSION"
                        ;;
                    *)
                        echo -e "${RED}Invalid option, no configuration applied.${NC}"
                        ;;
                esac
            fi
        else
            install_version $LATEST_VERSION
            prompt_apply_config $LATEST_VERSION
        fi
    fi
}


# 初始化配置目录
init_config_dir

# 获取最新版本信息
get_latest_version_info

until [ "$OPTION" == "7" ]; do
    echo
    check_running_version  # 调用检查正在运行的版本函数
    list_installed_versions
    check_latest_version
    echo "Menu:"
    echo -e "1. ${GREEN}Install or update to the latest version ($LATEST_VERSION)${NC}"
    echo -e "2. ${GREEN}Install a specific version${NC}"
    echo -e "3. ${GREEN}Delete a specific version${NC}"
    echo -e "4. ${GREEN}Manage configuration files${NC}"
    echo -e "5. ${GREEN}Start a specific version${NC}"
    echo -e "6. ${GREEN}Stop running version${NC}"  # 新增加的选项
    echo -e "7. ${GREEN}Exit${NC}"  # 更新退出选项的编号
    read -p "Choose an option (1/2/3/4/5/6/7): " OPTION

    case $OPTION in
        1)
            install_or_update_latest_version
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
            start_program_version
            ;;
        6)  stop_running_version
            ;;
        7)
            echo -e "${GREEN}Exiting.${NC}"
            ;;
        *)
            echo -e "${RED}Invalid option.${NC}"
            ;;
    esac
done