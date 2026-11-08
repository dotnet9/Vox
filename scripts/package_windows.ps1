#requires -Version 7.0
# 构建 Windows NativeAOT 安装包与便携版，支持复用同一份已验证发布产物。
[CmdletBinding()]
param(
    [string]$Version = '',
    [string]$SourceDirectory = '',
    [string]$OutputDirectory = '',
    [string]$WorkRoot = '',
    [string]$InnoSetupCompiler = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $Version) {
    [xml]$props = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw
    $Version = [string]$props.Project.PropertyGroup.Version
}
if (-not $InnoSetupCompiler) {
    $candidates = @(
        if (${env:ProgramFiles(x86)}) { Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe' }
        if ($env:ProgramFiles) { Join-Path $env:ProgramFiles 'Inno Setup 6/ISCC.exe' }
        if ($env:LOCALAPPDATA) { Join-Path $env:LOCALAPPDATA 'Programs/Inno Setup 6/ISCC.exe' }
    )
    $InnoSetupCompiler = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}
if (-not $InnoSetupCompiler) { throw 'Inno Setup 6 is required; pass -InnoSetupCompiler if installed elsewhere.' }
if (-not $WorkRoot) { $WorkRoot = Join-Path $root '../../temp_files/vox-windows' }
$runRoot = Join-Path ([IO.Path]::GetFullPath($WorkRoot)) ('builds/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root 'artifacts/release' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
if (-not $SourceDirectory) {
    $SourceDirectory = Join-Path $runRoot 'publish'
    & (Join-Path $PSScriptRoot 'publish.ps1') -Rid win-x64 -Version $Version -ArtifactsRoot (Join-Path $runRoot 'artifacts') -PublishRoot $SourceDirectory
}
$SourceDirectory = (Resolve-Path -LiteralPath $SourceDirectory).Path
& (Join-Path $PSScriptRoot 'verify_native_publish.ps1') -Rid win-x64 -SourceDirectory $SourceDirectory -VerificationDirectory (Join-Path $runRoot 'runtime-verification')
& $InnoSetupCompiler '/Q' "/DAppVersion=$Version" "/DSourceDir=$SourceDirectory" "/DOutputDir=$OutputDirectory" (Join-Path $PSScriptRoot 'Vox.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }
$installer = Join-Path $OutputDirectory "Vox-v$Version-win-x64-setup.exe"
if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) { throw "Installer missing: $installer" }
$zip = Join-Path $OutputDirectory "Vox-v$Version-win-x64.zip"
$payload = @(Get-ChildItem -LiteralPath $SourceDirectory -Force | Where-Object { $_.Extension -notin '.pdb', '.dbg' })
Compress-Archive -LiteralPath $payload.FullName -DestinationPath $zip -Force
foreach ($path in @($installer, $zip)) {
    $sha = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    "$sha  $([IO.Path]::GetFileName($path))" | Set-Content -LiteralPath "$path.sha256" -Encoding ascii
    Write-Host "Package: $path"
}
