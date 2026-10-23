@echo off
rem 仓库级构建入口：build.cmd [-Test]
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\build.ps1" %*
exit /b %ERRORLEVEL%
