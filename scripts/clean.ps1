# 清理全部 bin/obj 与 artifacts 输出
$ErrorActionPreference = "SilentlyContinue"
$root = Split-Path -Parent $PSScriptRoot

Get-ChildItem -Path $root -Include bin, obj -Recurse -Directory |
    Where-Object { $_.FullName -notmatch '\\\.git\\' } |
    Remove-Item -Recurse -Force

if (Test-Path (Join-Path $root "artifacts")) {
    Remove-Item (Join-Path $root "artifacts") -Recurse -Force
}

Write-Host "cleaned"
