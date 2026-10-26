# 打包 Vox Linux .deb 安装包（移植自 Vex 同名脚本的精简版）。
# 用法：scripts/package_linux_deb.ps1 -RuntimeIdentifier linux-x64 -Version 0.1.0-preview.2
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("linux-x64", "linux-arm64")]
    [string] $RuntimeIdentifier,

    [Parameter(Mandatory = $true)]
    [string] $Version
)

$ErrorActionPreference = "Stop"
$scriptRoot = (Resolve-Path -LiteralPath $PSScriptRoot).Path
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $scriptRoot "..")).Path

$SourceDirectory = Join-Path $repositoryRoot "artifacts/publish/Vox.App-$RuntimeIdentifier"
$OutputDirectory = Join-Path $repositoryRoot "artifacts/release"

$sourcePath = (Resolve-Path -LiteralPath $SourceDirectory).Path
$outputPath = [System.IO.Path]::GetFullPath($OutputDirectory)
$cleanVersion = $Version.Trim().TrimStart('v', 'V').Split('+')[0]
if ($cleanVersion -notmatch '^\d+(\.\d+){1,3}$') {
    throw "Version must be numeric (for example 0.1.0): $Version"
}

$architecture = if ($RuntimeIdentifier -eq "linux-arm64") { "arm64" } else { "amd64" }
$packageName = "Vox-v$cleanVersion-$RuntimeIdentifier.deb"
$packagePath = Join-Path $outputPath $packageName
$stageRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("Vox-deb-" + [Guid]::NewGuid().ToString("N"))
$appPath = Join-Path $stageRoot "usr/lib/vox"
$binPath = Join-Path $stageRoot "usr/bin"
$desktopPath = Join-Path $stageRoot "usr/share/applications"
$iconPath = Join-Path $stageRoot "usr/share/icons/hicolor/256x256/apps"
$controlPath = Join-Path $stageRoot "DEBIAN"

New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
New-Item -ItemType Directory -Path $appPath, $binPath, $desktopPath, $iconPath, $controlPath -Force | Out-Null

try {
    Copy-Item -LiteralPath (Join-Path $repositoryRoot "logo.png") -Destination (Join-Path $iconPath "vox.png")

    # 调试符号不进入安装器；其余发布产物（含 I18n 资源）全部入库。
    Get-ChildItem -LiteralPath $sourcePath -Recurse -File -Force | ForEach-Object {
        $relative = [IO.Path]::GetRelativePath($sourcePath, $_.FullName)
        if ($_.Extension -in '.pdb', '.dbg') { return }
        $destination = Join-Path $appPath $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $destination -Force
    }

    $executable = Join-Path $appPath "Vox"
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        throw "Published executable was not produced: $executable"
    }
    & chmod 755 $executable
    if ($LASTEXITCODE -ne 0) { throw '无法设置程序启动权限。' }

    [IO.File]::WriteAllText((Join-Path $binPath 'vox'), "#!/bin/sh`n" + 'exec /usr/lib/vox/Vox "$@"' + "`n")
    & chmod 755 (Join-Path $binPath 'vox')
    if ($LASTEXITCODE -ne 0) { throw '无法设置启动入口权限。' }

    @(
        "[Desktop Entry]",
        "Type=Application",
        "Name=Vox",
        "Comment=Vox content workspace",
        "Exec=vox %f",
        "Icon=vox",
        "MimeType=text/markdown;text/x-markdown;",
        "Terminal=false",
        "Categories=Office;TextEditor;"
    ) | Set-Content -LiteralPath (Join-Path $desktopPath "vox.desktop") -Encoding ascii

    $installedSize = [Math]::Ceiling(((Get-ChildItem -LiteralPath (Join-Path $stageRoot 'usr') -Recurse -File | Measure-Object Length -Sum).Sum) / 1KB)
    @(
        "Package: vox",
        "Version: $cleanVersion",
        "Section: text",
        "Priority: optional",
        "Architecture: $architecture",
        "Installed-Size: $installedSize",
        "Maintainer: Dotnet9 <1012434131@qq.com>",
        "Depends: libc6, libx11-6, libxrandr2, libxrender1, libxi6, libfontconfig1, libfreetype6, libglib2.0-0",
        "Description: Vox content workspace",
        " A writing environment and content repository governance desktop app built with Avalonia."
    ) | Set-Content -LiteralPath (Join-Path $controlPath "control") -Encoding ascii
    & chmod 644 (Join-Path $controlPath 'control')

    if (Test-Path -LiteralPath $packagePath) {
        Remove-Item -LiteralPath $packagePath -Force
    }
    & dpkg-deb --build --root-owner-group $stageRoot $packagePath
    if ($LASTEXITCODE -ne 0) { throw "dpkg-deb failed" }
    Write-Host "deb -> $packagePath"
}
finally {
    if (Test-Path -LiteralPath $stageRoot) {
        Remove-Item -LiteralPath $stageRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
