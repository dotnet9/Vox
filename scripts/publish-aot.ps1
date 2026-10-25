# Vox AOT publish (NativeAOT)
# usage: powershell -File scripts/publish-aot.ps1 [-RID win-x64] [-Clean]
# Params follow the verified Vex publish.ps1:
#   ILC single-threaded + workstation GC (avoids local .NET10 ilc crash),
#   full reflection metadata preservation (YamlDotNet), non-single-file layout.
param(
    [string]$RID = "win-x64",
    [switch]$Clean
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root (Join-Path "publish/aot" $RID)

if ($Clean -and (Test-Path $out)) { Remove-Item $out -Recurse -Force }

Write-Host "==> Vox AOT publish ($RID) -> $out" -ForegroundColor Cyan
dotnet publish (Join-Path $root "src/Vox.App/Vox.App.csproj") -m:1 `
    -c Release -r $RID --self-contained true `
    -p:PublishAot=true -p:PublishTrimmed=true -p:PublishSingleFile=false `
    -p:IlcGenerateCompleteTypeMetadata=true -p:IlcTrimMetadata=false `
    -p:IlcSingleThreaded=true -p:IlcUseServerGc=false `
    -p:StripSymbols=true `
    -o $out
if ($LASTEXITCODE -ne 0) { throw "AOT publish failed (exit $LASTEXITCODE)" }

Write-Host "==> done: $out" -ForegroundColor Green
Get-ChildItem $out | Format-Table Name, Length -AutoSize
