# 发布 Vox.App：默认 win-x64 自包含；-Aot 启用 NativeAOT（待全量 spike 通过后默认开启）
[CmdletBinding()]
param(
    [string]$Rid = "win-x64",
    [switch]$Aot
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "artifacts/publish/Vox.App-$Rid"

$args = @(
    "publish", "src/Vox.App/Vox.App.csproj",
    "-c", "Release",
    "-r", $Rid,
    "--self-contained", "true",
    "-o", $out
)
if ($Aot) { $args += "-p:PublishAot=true" }

Push-Location $root
try {
    dotnet @args
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Write-Host "Published -> $out"
}
finally {
    Pop-Location
}
