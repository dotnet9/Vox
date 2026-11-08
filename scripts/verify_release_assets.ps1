[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$PackageDirectory = ''
)
$ErrorActionPreference = 'Stop'
if (-not $PackageDirectory) { $PackageDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts/release' }
$directory = (Resolve-Path -LiteralPath $PackageDirectory).Path
$versionText = $Version.Trim().TrimStart('v', 'V')
$expected = @(
    "Vox-v$versionText-win-x64-setup.exe", "Vox-v$versionText-win-x64.zip",
    "Vox-v$versionText-linux-x64.deb", "Vox-v$versionText-linux-x64.tar.gz",
    "Vox-v$versionText-linux-arm64.deb", "Vox-v$versionText-linux-arm64.tar.gz",
    "Vox-v$versionText-osx-x64.dmg", "Vox-v$versionText-osx-x64.zip",
    "Vox-v$versionText-osx-arm64.dmg", "Vox-v$versionText-osx-arm64.zip"
)
$allowed = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($name in $expected) {
    $path = Join-Path $directory $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -eq 0) { throw "Missing or empty release artifact: $name" }
    $checksum = (Get-Content -LiteralPath "$path.sha256" -Raw).Trim()
    if ($checksum -notmatch ('^([a-fA-F0-9]{64})\s+\*?' + [regex]::Escape($name) + '$')) { throw "Invalid checksum target or format: $name" }
    $expectedHash = $Matches[1]
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $expectedHash) { throw "SHA256 mismatch: $name" }
    [void]$allowed.Add($name)
    [void]$allowed.Add("$name.sha256")
}
$unexpected = @(Get-ChildItem -LiteralPath $directory -File | Where-Object { -not $allowed.Contains($_.Name) })
if ($unexpected.Count) { throw "Unexpected release files: $($unexpected.Name -join ', ')" }
Write-Host 'Verified five installers, five portable archives, and all ten SHA256 checksums.'
