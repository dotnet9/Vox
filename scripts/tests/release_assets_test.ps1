[CmdletBinding()]
param([string]$WorkRoot = '')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $WorkRoot) { $WorkRoot = Join-Path $repo '../../temp_files/vox-assets-contract' }
$work = Join-Path ([IO.Path]::GetFullPath($WorkRoot)) ([guid]::NewGuid().ToString('N'))
$packages = Join-Path $work 'packages'
New-Item -ItemType Directory -Path $packages -Force | Out-Null
$version = '9.8.7-preview.6'
foreach ($rid in @('win-x64','linux-x64','linux-arm64','osx-x64','osx-arm64')) {
    $extensions = switch -Wildcard ($rid) { 'win-*' { '-setup.exe'; '.zip' }; 'linux-*' { '.deb'; '.tar.gz' }; 'osx-*' { '.dmg'; '.zip' } }
    foreach ($ext in $extensions) {
        $name = "Vox-v$version-$rid$ext"
        $path = Join-Path $packages $name
        [IO.File]::WriteAllText($path, "controlled payload for $rid $ext")
        $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $name" | Set-Content -LiteralPath "$path.sha256" -Encoding ascii
    }
}
$verifier = Join-Path $repo 'scripts/verify_release_assets.ps1'
& $verifier -Version $version -PackageDirectory $packages
function Assert-Rejected([scriptblock]$Action, [string]$Expected) {
    try { & $Action } catch { if ($_.Exception.Message -match $Expected) { return }; throw }
    throw "Invalid payload was accepted; expected $Expected"
}
$target = Join-Path $packages "Vox-v$version-win-x64.zip"
$original = [IO.File]::ReadAllBytes($target)
[IO.File]::WriteAllText($target, 'corrupted archive')
Assert-Rejected { & $verifier -Version $version -PackageDirectory $packages } 'SHA256 mismatch'
[IO.File]::WriteAllBytes($target, $original)
Move-Item -LiteralPath $target -Destination "$target.missing"
Assert-Rejected { & $verifier -Version $version -PackageDirectory $packages } 'Missing or empty'
Move-Item -LiteralPath "$target.missing" -Destination $target
Assert-Rejected { & $verifier -Version '9.8.7' -PackageDirectory $packages } 'Missing or empty'
[IO.File]::WriteAllText((Join-Path $packages 'old-version.zip'), 'stale payload')
Assert-Rejected { & $verifier -Version $version -PackageDirectory $packages } 'Unexpected release files'

$native = Join-Path $work 'native'
New-Item -ItemType Directory -Path $native -Force | Out-Null
$bytes = [byte[]]::new(256)
$bytes[0] = 0x7f; $bytes[1] = 0x45; $bytes[2] = 0x4c; $bytes[3] = 0x46
$bytes[4] = 2; $bytes[5] = 1; $bytes[18] = 62
[IO.File]::WriteAllBytes((Join-Path $native 'Vox'), $bytes)
$nativeVerifier = Join-Path $repo 'scripts/verify_native_publish.ps1'
& $nativeVerifier -Rid linux-x64 -SourceDirectory $native
Assert-Rejected { & $nativeVerifier -Rid linux-arm64 -SourceDirectory $native } 'architecture does not match'
$bytes[18] = 183
[IO.File]::WriteAllBytes((Join-Path $native 'Vox'), $bytes)
& $nativeVerifier -Rid linux-arm64 -SourceDirectory $native
[IO.File]::WriteAllText((Join-Path $native 'Vox.dll'), 'managed companion')
Assert-Rejected { & $nativeVerifier -Rid linux-x64 -SourceDirectory $native } 'JIT payload'
Remove-Item -LiteralPath (Join-Path $native 'Vox.dll')
$bytes = [byte[]]::new(256)
$bytes[0] = 0xcf; $bytes[1] = 0xfa; $bytes[2] = 0xed; $bytes[3] = 0xfe
[BitConverter]::GetBytes([uint32]0x01000007).CopyTo($bytes, 4)
[IO.File]::WriteAllBytes((Join-Path $native 'Vox'), $bytes)
& $nativeVerifier -Rid osx-x64 -SourceDirectory $native
Assert-Rejected { & $nativeVerifier -Rid osx-arm64 -SourceDirectory $native } 'architecture does not match'
[BitConverter]::GetBytes([uint32]0x0100000c).CopyTo($bytes, 4)
[IO.File]::WriteAllBytes((Join-Path $native 'Vox'), $bytes)
& $nativeVerifier -Rid osx-arm64 -SourceDirectory $native
$bytes = [byte[]]::new(384)
$bytes[0] = 0x4d; $bytes[1] = 0x5a
[BitConverter]::GetBytes([int]64).CopyTo($bytes, 0x3c)
[BitConverter]::GetBytes([uint32]0x4550).CopyTo($bytes, 64)
[BitConverter]::GetBytes([uint16]0x8664).CopyTo($bytes, 68)
[BitConverter]::GetBytes([uint16]0x20b).CopyTo($bytes, 88)
[IO.File]::WriteAllBytes((Join-Path $native 'Vox.exe'), $bytes)
& $nativeVerifier -Rid win-x64 -SourceDirectory $native
$bytes[312] = 1
[IO.File]::WriteAllBytes((Join-Path $native 'Vox.exe'), $bytes)
Assert-Rejected { & $nativeVerifier -Rid win-x64 -SourceDirectory $native } 'CLR directory'
Write-Host 'Passed missing platform, corrupt checksum, wrong version, stale asset, wrong architecture and JIT payload regressions.'
