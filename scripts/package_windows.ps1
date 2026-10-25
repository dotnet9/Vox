# 构建 Windows 安装包：发布 win-x64 + Inno Setup 编译。
# 用法：scripts/package_windows.ps1 [-Version x.y.z]（缺省读 Directory.Build.props 的 Version）
[CmdletBinding()]
param(
    [string] $Version = ""
)

$ErrorActionPreference = "Stop"
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path

if ([string]::IsNullOrWhiteSpace($Version)) {
    $props = Join-Path $repositoryRoot "Directory.Build.props"
    $match = Select-String -LiteralPath $props -Pattern '<Version>([^<]+)</Version>' | Select-Object -First 1
    if ($match) { $Version = $match.Matches[0].Groups[1].Value }
}
if ([string]::IsNullOrWhiteSpace($Version)) { throw "Version not found (pass -Version or set <Version> in Directory.Build.props)." }
Write-Host "打包 win-x64 (Version=$Version)"

& (Join-Path $PSScriptRoot "publish.ps1") -Rid win-x64
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

$iscc = @(
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6/ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6/ISCC.exe"),
    (Join-Path $env:LOCALAPPDATA "Programs/Inno Setup 6/ISCC.exe")
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 (ISCC.exe) not found; install from https://jrsoftware.org/isinfo.php" }

$setup = Join-Path $PSScriptRoot "installer/Vox.Setup.iss"
$sourceDir = Join-Path $repositoryRoot "artifacts/publish/Vox.App-win-x64"
$outputDir = Join-Path $repositoryRoot "artifacts/release"
& $iscc "/DAppVersion=$Version" "/DSourceDir=$sourceDir" "/DOutputDir=$outputDir" $setup
if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }

$installer = Join-Path $outputDir "Vox-v$Version-win-x64-setup.exe"
if (-not (Test-Path $installer)) { throw "Installer missing: $installer" }
$sha = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
"$sha  Vox-v$Version-win-x64-setup.exe" | Set-Content -LiteralPath "$installer.sha256" -Encoding ascii
Write-Host "安装包 -> $installer"
