#requires -Version 7.0
# 正式发布入口：五个平台均采用与本机验证相同的 NativeAOT 配方。
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
    [string]$Rid = 'win-x64',
    [string]$Version = '',
    [string]$ArtifactsRoot = '',
    [string]$PublishRoot = ''
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not $Version) {
    [xml]$props = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw
    $Version = [string]$props.Project.PropertyGroup.Version
}
if (-not $PublishRoot) { $PublishRoot = Join-Path $root "artifacts/publish/Vox.App-$Rid" }
& (Join-Path $PSScriptRoot 'publish-aot.ps1') -RID $Rid -Version $Version -ArtifactsRoot $ArtifactsRoot -PublishRoot $PublishRoot -Clean
& (Join-Path $PSScriptRoot 'verify_native_publish.ps1') -Rid $Rid -SourceDirectory $PublishRoot
