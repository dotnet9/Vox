@echo off
rem 打包 Windows 安装包：package_win_x64.bat（版本读 Directory.Build.props）
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\package_windows.ps1" %*
exit /b %ERRORLEVEL%
