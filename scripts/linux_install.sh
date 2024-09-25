#!/bin/bash

# ================================
# MidJourney Proxy Manager Script
# ================================

# Define Colors
readonly RED='\033[0;31m'
readonly GREEN='\033[0;32m'
readonly YELLOW='\033[1;33m'
readonly BLUE='\033[0;34m'
readonly NC='\033[0m' # No Color

# Configuration Directory
readonly CONFIG_DIR="config_files"

# Supported Package Managers
declare -A PKG_MANAGERS=(
    ["apt-get"]="apt-get"
    ["yum"]="yum"
    ["dnf"]="dnf"
)

# GitHub API URL for latest release
readonly API_URL="https://api.github.com/repos/trueai-org/midjourney-proxy/releases/latest"

# Global Variables
PKG_MANAGER=""
LATEST_VERSION=""
LATEST_TAR_URL=""
INSTALLED_VERSIONS=()
CONFIG_FILES=()

# ================================
# Utility Functions
# ================================

# Print messages with colors
print_msg() {
    local color="$1"
    local message="$2"
    echo -e "${color}${message}${NC}"
}

# Exit script with error message
exit_with_error() {
    local message="$1"
    print_msg "${RED}" "$message"
    exit 1
}

# Detect Package Manager
detect_package_manager() {
    for manager in "${!PKG_MANAGERS[@]}"; do
        if command -v "$manager" &>/dev/null; then
            PKG_MANAGER="${PKG_MANAGERS[$manager]}"
            return
        fi
    done
    exit_with_error "No supported package manager found (apt-get, yum, dnf)."
}

# Update and Install Package
install_package() {
    local package="$1"
    print_msg "${YELLOW}" "$package is required but not installed. Installing $package..."
    sudo "$PKG_MANAGER" update -y && sudo "$PKG_MANAGER" install "$package" -y || exit_with_error "Failed to install $package. Please install it manually."
}

# Check and Install Dependencies
check_dependencies() {
    for dep in curl jq; do
        if ! command -v "$dep" &>/dev/null; then
            install_package "$dep"
        fi
    done
}

# Download File with Proxy Retry
download_file() {
    local url="$1"
    local output="$2"

    print_msg "${BLUE}" "Downloading $url..."
    if ! curl -L -o "$output" "$url"; then
        print_msg "${YELLOW}" "Failed to download $url. Switching to proxy and retrying..."
        local proxy_url="https://mirror.ghproxy.com/${url#https://}"
        if ! curl -L -o "$output" "$proxy_url"; then
            exit_with_error "Failed to download $url using proxy. Please check your network and try again."
        fi
    fi
}

# Initialize Configuration Directory
init_config_dir() {
    if [ ! -d "$CONFIG_DIR" ]; then
        mkdir -p "$CONFIG_DIR" || exit_with_error "Failed to create configuration directory: $CONFIG_DIR"
        print_msg "${GREEN}" "Configuration directory created: $CONFIG_DIR"
    fi
}

# List Configuration Files
list_config_files() {
    CONFIG_FILES=()
    echo -e "${BLUE}Configuration files:${NC}"
    local index=1
    for config_file in "$CONFIG_DIR"/*; do
        if [ -f "$config_file" ]; then
            CONFIG_FILES+=("$(basename "$config_file")")
            echo -e "  $index) ${CONFIG_FILES[-1]}"
            ((index++))
        fi
    done

    if [ "${#CONFIG_FILES[@]}" -eq 0 ]; then
        print_msg "${YELLOW}" "No configuration files found."
        return 1
    else
        return 0
    fi
}

# Get Configuration File by Number
get_config_file_by_number() {
    local num="$1"
    if (( num > 0 && num <= ${#CONFIG_FILES[@]} )); then
        echo "${CONFIG_FILES[$((num-1))]}"
    else
        echo ""
    fi
}

# List Installed Versions
list_installed_versions() {
    INSTALLED_VERSIONS=()
    for version_dir in v*; do
        if [ -d "$version_dir" ]; then
            INSTALLED_VERSIONS+=("$version_dir")
        fi
    done

    if [ "${#INSTALLED_VERSIONS[@]}" -eq 0 ]; then
        print_msg "${RED}" "No versions installed."
    else
        echo -e "${BLUE}Installed versions:${NC}"
        for version in "${INSTALLED_VERSIONS[@]}"; do
            echo -e "  $version"
        done
    fi
}

# Check Latest Version Installation
check_latest_version() {
    for version in "${INSTALLED_VERSIONS[@]}"; do
        if [ "$version" == "$LATEST_VERSION" ]; then
            print_msg "${GREEN}" "You already have the latest version installed: $LATEST_VERSION"
            return
        fi
    done
    print_msg "${YELLOW}" "A new version is available: $LATEST_VERSION."
}

# ================================
# Core Functions
# ================================

# Check CPU Architecture
check_architecture() {
    local arch
    arch=$(uname -m)
    case "$arch" in
        x86_64)
            echo "x64"
            ;;
        aarch64|arm64)
            echo "arm64"
            ;;
        *)
            exit_with_error "Unsupported architecture: $arch"
            ;;
    esac
}

# Get Latest Version Info from GitHub
get_latest_version_info() {
    local api_url="$API_URL"

    local response
    response=$(curl -s "$api_url") || {
        print_msg "${YELLOW}" "Failed to retrieve version info. Switching to proxy..."
        api_url="https://mirror.ghproxy.com/${API_URL#https://}"
        response=$(curl -s "$api_url") || exit_with_error "Failed to retrieve version info using proxy. Please check your network and try again."
    }

    LATEST_VERSION=$(echo "$response" | jq -r '.tag_name')
    DOWNLOAD_URL=$(echo "$response" | jq -r --arg ARCH "$ARCH" '.assets[] | select(.name | test("midjourney-proxy-linux-\($ARCH)")) | .browser_download_url')

    if [ -n "$LATEST_VERSION" ] && [ -n "$DOWNLOAD_URL" ]; then
        LATEST_TAR_URL="$DOWNLOAD_URL"
    else
        exit_with_error "Failed to retrieve the latest version information."
    fi
}

# Install Specified Version
install_version() {
    local version="$1"

    if [ -d "$version" ]; then
        print_msg "${YELLOW}" "Version $version is already installed. Please delete it first."
        return 1
    fi

    local specific_api_url="https://api.github.com/repos/trueai-org/midjourney-proxy/releases/tags/$version"
    local response
    response=$(curl -s "$specific_api_url") || {
        exit_with_error "Failed to fetch release information for version: $version"
    }

    local tar_url
    tar_url=$(echo "$response" | jq -r --arg ARCH "$ARCH" ".assets[] | select(.name | test(\"midjourney-proxy-linux-\($ARCH)-$version.tar.gz\")) | .browser_download_url")

    if [ -z "$tar_url" ]; then
        exit_with_error "No download link found for specified version: $version"
    fi

    # Create Temporary Directory
    local temp_dir
    temp_dir=$(mktemp -d) || exit_with_error "Failed to create temporary directory. Installation aborted."
    trap 'rm -rf "$temp_dir"' EXIT

    cd "$temp_dir" || exit_with_error "Failed to enter temporary directory."

    # Download Tarball
    download_file "$tar_url" "midjourney-proxy-linux-${ARCH}-${version}.tar.gz"

    # Extract Tarball
    tar -xzf "midjourney-proxy-linux-${ARCH}-${version}.tar.gz" || exit_with_error "Failed to extract tar file."

    # Determine Extracted Directory
    local extracted_dir
    extracted_dir=$(tar -tzf "midjourney-proxy-linux-${ARCH}-${version}.tar.gz" | head -1 | cut -f1 -d "/")

    if [ -d "$extracted_dir" ]; then
        mv "$extracted_dir" "$OLDPWD/$version" || exit_with_error "Failed to move extracted directory."
    else
        exit_with_error "Failed to find extracted directory. Installation might have failed."
    fi

    cd "$OLDPWD" || exit_with_error "Failed to return to original directory."

    print_msg "${GREEN}" "Installation completed for version $version."
    prompt_apply_config "$version"
}

# Prompt to Apply Configuration
prompt_apply_config() {
    local version="$1"
    if list_config_files; then
        read -rp "Do you want to apply a configuration file now? [y/N]: " apply_config
        apply_config=$(echo "$apply_config" | tr '[:upper:]' '[:lower:]')
        if [ "$apply_config" == "y" ]; then
            list_config_files
            read -rp "Select configuration file to apply by number: " num
            local selected_file
            selected_file=$(get_config_file_by_number "$num")
            if [ -n "$selected_file" ]; then
                cp "$CONFIG_DIR/$selected_file" "$version/appsettings.json" || print_msg "${YELLOW}" "Failed to apply configuration file."
                print_msg "${GREEN}" "Configuration file $selected_file applied to version $version."
            else
                print_msg "${RED}" "Invalid selection. No configuration file applied."
            fi
        else
            print_msg "${YELLOW}" "No configuration file applied. You can manually modify the configuration at $version/appsettings.json"
        fi
    else
        print_msg "${YELLOW}" "No configuration files found. You can manually modify the configuration at $version/appsettings.json"
    fi
}

# Install or Update to Latest Version
install_latest_version() {
    install_version "$LATEST_VERSION"
}

# Install a Specific Version
install_specific_version() {
    read -rp "Enter the version you want to install (e.g., v2.3.7): " version
    install_version "$version"
}

# Delete a Specific Version
delete_version() {
    read -rp "Enter the version you want to delete (e.g., v2.3.7): " version
    if [[ "$version" =~ ^v[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
        if [ -d "$version" ]; then
            read -rp "Are you sure you want to delete $version? [y/N]: " confirm
            confirm=$(echo "$confirm" | tr '[:upper:]' '[:lower:]')
            if [ "$confirm" == "y" ]; then
                rm -rf "$version" && print_msg "${GREEN}" "Version $version deleted." || print_msg "${RED}" "Failed to delete version $version."
            else
                print_msg "${YELLOW}" "Deletion canceled."
            fi
        else
            print_msg "${RED}" "Version $version is not installed."
        fi
    else
        print_msg "${RED}" "Invalid version format."
    fi
}

# Apply Configuration to a Specific Version
apply_config_to_version() {
    list_installed_versions
    read -rp "Enter the version to apply configuration to (e.g., v2.3.7): " version
    if [ -d "$version" ]; then
        if list_config_files; then
            read -rp "Select configuration file to apply by number: " num
            local selected_file
            selected_file=$(get_config_file_by_number "$num")
            if [ -n "$selected_file" ]; then
                cp "$CONFIG_DIR/$selected_file" "$version/appsettings.json" && print_msg "${GREEN}" "Configuration file applied to version: $version." || print_msg "${RED}" "Failed to apply configuration file."
            else
                print_msg "${RED}" "Invalid selection."
            fi
        fi
    else
        print_msg "${RED}" "Version directory not found: $version."
    fi
}

# Export Configuration from a Specified Version
export_config_file() {
    list_installed_versions
    read -rp "Enter the version to export configuration from (e.g., v2.3.7): " version
    if [ -d "$version" ]; then
        read -rp "Enter the name for the exported configuration file: " export_name
        export_name="${export_name%.json}"
        if [ -z "$export_name" ] || [ -e "$CONFIG_DIR/$export_name.json" ]; then
            print_msg "${RED}" "Invalid new name or file already exists."
        else
            cp "$version/appsettings.json" "$CONFIG_DIR/$export_name.json" && print_msg "${GREEN}" "Configuration file exported as: $export_name.json" || print_msg "${RED}" "Failed to export configuration file."
        fi
    else
        print_msg "${RED}" "Version directory not found: $version."
    fi
}

# Import Configuration from an Existing Version
import_config_from_existing() {
    if [ "${#INSTALLED_VERSIONS[@]}" -eq 0 ]; then
        print_msg "${RED}" "No installed versions available to import configuration."
        return 1
    fi

    echo -e "${BLUE}Available installed versions to import configuration from:${NC}"
    local index=1
    for version in "${INSTALLED_VERSIONS[@]}"; do
        echo -e "  $index) $version"
        ((index++))
    done

    read -rp "Select a version to import configuration from (by number): " num
    if (( num > 0 && num <= ${#INSTALLED_VERSIONS[@]} )); then
        local selected_version="${INSTALLED_VERSIONS[$((num-1))]}"
        if [ -f "$selected_version/appsettings.json" ]; then
            cp "$selected_version/appsettings.json" "$CONFIG_DIR/" && print_msg "${GREEN}" "Configuration from $selected_version imported."
        else
            print_msg "${RED}" "No configuration file found in $selected_version."
        fi
    else
        print_msg "${RED}" "Invalid selection. No configuration imported."
    fi
}

# ================================
# Main Script Execution
# ================================

main_menu() {
    local option
    while true; do
        echo
        list_installed_versions
        check_latest_version
        echo "Menu:"
        echo -e "1. ${GREEN}Install or update to the latest version ($LATEST_VERSION)${NC}"
        echo -e "2. ${GREEN}Install a specific version${NC}"
        echo -e "3. ${GREEN}Delete a specific version${NC}"
        echo -e "4. ${GREEN}Apply a configuration to a version${NC}"
        echo -e "5. ${GREEN}Export a configuration from a version${NC}"
        echo -e "6. ${GREEN}Import a configuration from an existing version${NC}"
        echo -e "7. ${GREEN}Exit${NC}"
        read -rp "Choose an option (1-7): " option

        case "$option" in
            1)
                install_latest_version
                ;;
            2)
                install_specific_version
                ;;
            3)
                delete_version
                ;;
            4)
                apply_config_to_version
                ;;
            5)
                export_config_file
                ;;
            6)
                import_config_from_existing
                ;;
            7)
                print_msg "${GREEN}" "Exiting."
                exit 0
                ;;
            *)
                print_msg "${RED}" "Invalid option. Please choose a number between 1 and 7."
                ;;
        esac
    done
}

# ================================
# Script Initialization
# ================================

main() {
    detect_package_manager
    check_dependencies
    init_config_dir
    ARCH=$(check_architecture)
    get_latest_version_info
    main_menu
}

main "$@"
