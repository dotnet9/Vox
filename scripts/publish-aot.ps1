# Vox AOT publish (NativeAOT)
#requires -Version 7.0
# usage: pwsh -File scripts/publish-aot.ps1 [-RID win-x64] [-Clean]
# 与正式发布共用的 NativeAOT 配方；publish.ps1 负责统一分发目录。
param(
    [ValidateSet('win-x64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
    [string]$RID = "win-x64",
    [switch]$Clean,
    [string]$ArtifactsRoot = "",
    [string]$PublishRoot = "",
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$out = if ($PublishRoot) { [IO.Path]::GetFullPath($PublishRoot) }
    else { Join-Path $root (Join-Path "publish/aot" $RID) }
$buildArgs = @()
if ($ArtifactsRoot) {
    $buildArgs += "-p:ArtifactsPath=$([IO.Path]::GetFullPath($ArtifactsRoot))"
    $buildArgs += "-p:UseArtifactsOutput=true"
}

if ($Version) { $buildArgs += "-p:Version=$Version" }
# macOS 与 Vex 一致，不请求压缩调试段；打包时排除 dSYM。
$buildArgs += if ($RID.StartsWith('osx-')) { '-p:StripSymbols=false' } else { '-p:StripSymbols=true' }

if ($Clean -and (Test-Path -LiteralPath $out)) {
    $resolvedOut = (Resolve-Path -LiteralPath $out).Path
    if ($resolvedOut -ne [IO.Path]::GetFullPath($out) -or
        $resolvedOut -eq $root -or $resolvedOut -eq [IO.Path]::GetPathRoot($resolvedOut) -or
        $root.StartsWith($resolvedOut.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean outside the selected publish directory: $resolvedOut"
    }
    Remove-Item -LiteralPath $resolvedOut -Recurse -Force
}

Write-Host "==> Vox AOT publish ($RID) -> $out" -ForegroundColor Cyan
dotnet publish (Join-Path $root "src/Vox.App/Vox.App.csproj") -m:1 `
    -c Release -r $RID --self-contained true `
    -p:PublishAot=true -p:PublishTrimmed=true -p:PublishSingleFile=false `
    -p:IlcGenerateCompleteTypeMetadata=true -p:IlcTrimMetadata=false `
    -p:IlcSingleThreaded=true -p:IlcUseServerGc=false `
    @buildArgs -o $out
if ($LASTEXITCODE -ne 0) { throw "AOT publish failed (exit $LASTEXITCODE)" }

Write-Host "==> done: $out" -ForegroundColor Green
