@echo off
chcp 65001 >nul

:: 设置源目录变量
set "SOURCE_DIR=%~1"
if "%SOURCE_DIR%"=="" set "SOURCE_DIR=upgrade\extract"

echo 应用程序升级脚本开始执行...
echo 源目录: %SOURCE_DIR%
echo 等待应用程序完全退出...

:: 等待3秒确保程序退出
timeout /t 3 /nobreak >nul

:: 检查进程是否还在运行，最多等待30秒
set /a count=0
:check_process
tasklist /FI "IMAGENAME eq Midjourney.API.exe" 2>NUL | find /I /N "Midjourney.API.exe">NUL
if "%ERRORLEVEL%"=="0" (
    echo 应用程序仍在运行，继续等待...
    timeout /t 2 /nobreak >nul
    set /a count+=2
    if %count% LSS 30 goto check_process
    echo 强制继续执行升级...
)

echo 开始复制新文件...
echo 从 %SOURCE_DIR% 复制到当前目录

:: 检查源目录是否存在
if not exist "%SOURCE_DIR%" (
    echo 错误: 源目录不存在: %SOURCE_DIR%
    pause
    exit /b 1
)

:: 使用 xcopy 复制文件，覆盖现有文件
xcopy "%SOURCE_DIR%\*" ".\" /E /Y /I /Q

if %errorlevel% equ 0 (
    echo 文件复制完成
) else (
    echo 文件复制失败，错误代码: %errorlevel%
    pause
    exit /b 1
)

:: 清理解压目录
if exist "%SOURCE_DIR%" (
    echo 清理临时文件...
    rmdir /s /q "%SOURCE_DIR%" 2>nul
)

:: 清理升级包文件
if exist "upgrade\*.zip" del /q "upgrade\*.zip" 2>nul
if exist "upgrade\*.tar.gz" del /q "upgrade\*.tar.gz" 2>nul

:: echo 重启应用程序...
:: start "" "Midjourney.API.exe"

echo 升级完成！程序已重启
timeout /t 2 /nobreak >nul

exit /b 0