# Installation

## Docker Version

!> Note: Make sure the file mapping and paths are correct ⚠⚠

```bash
# Alibaba Cloud Mirror (Recommended for users in China)
docker pull registry.cn-guangzhou.aliyuncs.com/trueai-org/midjourney-proxy

# Example configuration for demo site

# 1. Download and rename the configuration file (example configuration)
# Note: Version 3.x does not require configuration file
wget -O /root/mjopen/appsettings.Production.json https://raw.githubusercontent.com/trueai-org/midjourney-proxy/main/src/Midjourney.API/appsettings.json

# Or use curl to download and rename the configuration file (example configuration)
# Note: Version 3.x does not require configuration file
curl -o /root/mjopen/appsettings.Production.json https://raw.githubusercontent.com/trueai-org/midjourney-proxy/main/src/Midjourney.API/appsettings.json

# 2. Stop and remove the old Docker container
docker stop mjopen && docker rm mjopen

# 3. Start a new Docker container
# Note: Version 3.x does not require configuration file
docker run -m 1g --name mjopen -d --restart=always \
 -p 8086:8080 --user root \
 -v /root/mjopen/logs:/app/logs:rw \
 -v /root/mjopen/data:/app/data:rw \
 -v /root/mjopen/attachments:/app/wwwroot/attachments:rw \
 -v /root/mjopen/ephemeral-attachments:/app/wwwroot/ephemeral-attachments:rw \
 -v /root/mjopen/appsettings.Production.json:/app/appsettings.Production.json:ro \
 -e TZ=Asia/Shanghai \
 -v /etc/localtime:/etc/localtime:ro \
 -v /etc/timezone:/etc/timezone:ro \
 registry.cn-guangzhou.aliyuncs.com/trueai-org/midjourney-proxy

# Configuration example for production environment
docker run --name mjopen -d --restart=always \
 -p 8086:8080 --user root \
 -v /root/mjopen/logs:/app/logs:rw \
 -v /root/mjopen/data:/app/data:rw \
 -v /root/mjopen/attachments:/app/wwwroot/attachments:rw \
 -v /root/mjopen/ephemeral-attachments:/app/wwwroot/ephemeral-attachments:rw \
 -v /root/mjopen/appsettings.Production.json:/app/appsettings.Production.json:ro \
 -e TZ=Asia/Shanghai \
 -v /etc/localtime:/etc/localtime:ro \
 -v /etc/timezone:/etc/timezone:ro \
 registry.cn-guangzhou.aliyuncs.com/trueai-org/midjourney-proxy

# GitHub Mirror
docker pull ghcr.io/trueai-org/midjourney-proxy
docker run --name mjopen -d --restart=always \
 -p 8086:8080 --user root \
 -v /root/mjopen/logs:/app/logs:rw \
 -v /root/mjopen/data:/app/data:rw \
 -v /root/mjopen/attachments:/app/wwwroot/attachments:rw \
 -v /root/mjopen/ephemeral-attachments:/app/wwwroot/ephemeral-attachments:rw \
 -v /root/mjopen/appsettings.Production.json:/app/appsettings.Production.json:ro \
 -e TZ=Asia/Shanghai \
 -v /etc/localtime:/etc/localtime:ro \
 -v /etc/timezone:/etc/timezone:ro \
 ghcr.io/trueai-org/midjourney-proxy

# DockerHub Mirror
docker pull trueaiorg/midjourney-proxy
docker run --name mjopen -d --restart=always \
 -p 8086:8080 --user root \
 -v /root/mjopen/logs:/app/logs:rw \
 -v /root/mjopen/data:/app/data:rw \
 -v /root/mjopen/attachments:/app/wwwroot/attachments:rw \
 -v /root/mjopen/ephemeral-attachments:/app/wwwroot/ephemeral-attachments:rw \
 -v /root/mjopen/appsettings.Production.json:/app/appsettings.Production.json:ro \
 -e TZ=Asia/Shanghai \
 -v /etc/localtime:/etc/localtime:ro \
 -v /etc/timezone:/etc/timezone:ro \
 trueaiorg/midjourney-proxy
```

## Windows Version

```bash
a. Download the latest Windows version from https://github.com/trueai-org/midjourney-proxy/releases, for example: midjourney-proxy-win-x64.zip
b. Extract and run Midjourney.API.exe
c. Open the website at http://localhost:8080
d. Optionally, deploy to IIS by adding the site in IIS, deploying the folder, and setting the application pool to 'No Managed Code'; start the site.
e. Optionally, use the built-in 'Task Scheduler' to create a basic task for the .exe program, and select 'Do not start a new instance' to ensure only one task runs.
```

## Linux Version

```bash
a. Download the latest Linux version from https://github.com/trueai-org/midjourney-proxy/releases, for example: midjourney-proxy-linux-x64.zip
b. Extract to the current directory: tar -xzf midjourney-proxy-linux-x64-<VERSION>.tar.gz
c. Execute: run_app.sh
c. Start Option 1: sh run_app.sh
d. Start Option 2: chmod +x run_app.sh && ./run_app.sh
```

## macOS Version

```bash
a. Download the latest macOS version from https://github.com/trueai-org/midjourney-proxy/releases, for example: midjourney-proxy-osx-x64.zip
b. Extract to the current directory: tar -xzf midjourney-proxy-osx-x64-<VERSION>.tar.gz
c. Execute: run_app_osx.sh
c. Start Option 1: sh run_app_osx.sh
d. Start Option 2: chmod +x run_app_osx.sh && ./run_app_osx.sh
```

## One-click Installation Script for Linux (❤ Thanks to [@dbccccccc](https://github.com/dbccccccc))

```bash
# Method 1
wget -N --no-check-certificate https://raw.githubusercontent.com/trueai-org/midjourney-proxy/main/scripts/linux_install.sh && chmod +x linux_install.sh && bash linux_install.sh

# Method 2
curl -o linux_install.sh https://raw.githubusercontent.com/trueai-org/midjourney-proxy/main/scripts/linux_install.sh && chmod +x linux_install.sh && bash linux_install.sh
```