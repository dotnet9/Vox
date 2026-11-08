@echo off
rem 打包 Windows 安装包：package_win_x64.bat（版本读 Directory.Build.props）
where pwsh >nul 2>nul
if errorlevel 1 (
    echo PowerShell 7 is required. Install it and retry.
    exit /b 1
)
pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\package_windows.ps1" %*
exit /b %ERRORLEVEL%
