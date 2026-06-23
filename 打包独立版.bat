@echo off
chcp 936 >nul
setlocal
rem ===== 一键打包 Chrome多开生成器 独立版 (自包含单文件, 客户零安装) =====
rem 产物: 同目录 Chrome多开生成器.exe (约 60MB), 任何 64 位 Windows 双击即用, 无需安装 .NET。
rem 前提: 本机已装 .NET SDK。

cd /d "%~dp0"

rem 1) 定位 dotnet: 先查 PATH, 再退回默认安装目录 (兼容各种环境)
set "DOTNET="
for %%D in (dotnet.exe) do if not defined DOTNET set "DOTNET=%%~$PATH:D"
if not defined DOTNET if exist "%ProgramFiles%\dotnet\dotnet.exe" set "DOTNET=%ProgramFiles%\dotnet\dotnet.exe"
if not defined DOTNET if exist "C:\Program Files\dotnet\dotnet.exe" set "DOTNET=C:\Program Files\dotnet\dotnet.exe"
if not defined DOTNET (
    echo [错误] 未找到 dotnet 命令。请先安装 .NET SDK: https://dotnet.microsoft.com/download
    pause
    exit /b 1
)

rem 2) 缺图标就用 Windows 自带的 .NET Framework 编译器现绘 app.ico
if exist "%~dp0app.ico" goto :build
echo 未发现 app.ico, 正在生成应用图标 ...
set "CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo [错误] 缺少 app.ico 且找不到 csc.exe, 无法生成图标。
    pause
    exit /b 1
)
"%CSC%" /nologo /target:exe /optimize+ /out:"%~dp0MakeIcon.exe" /reference:System.Drawing.dll "%~dp0MakeIcon.cs"
"%~dp0MakeIcon.exe" "%~dp0app.ico"
del "%~dp0MakeIcon.exe" >nul 2>nul

:build
rem 3) 自包含单文件发布, 直接输出到本目录 (覆盖旧的 exe)
echo 正在打包独立版 (首次会联网下载运行时, 请耐心等待) ...
"%DOTNET%" publish "%~dp0pack\pack.csproj" -c Release -o "."
if errorlevel 1 (
    echo.
    echo [失败] 打包出错, 请查看上方信息。
    pause
    exit /b 1
)

rem 4) 清理中间产物 (保留 pack\pack.csproj 供下次打包)
rmdir /s /q "%~dp0pack\obj" >nul 2>nul
rmdir /s /q "%~dp0pack\bin" >nul 2>nul

echo.
echo [成功] 已生成独立版: %~dp0Chrome多开生成器.exe
echo 把这一个 exe 发给客户即可, 任何 64 位 Windows 双击运行, 无需安装任何东西。
pause
