#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('win-x64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')][string]$Rid,
    [Parameter(Mandatory)][string]$SourceDirectory,
    [string]$VerificationDirectory = ''
)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $SourceDirectory).Path
foreach ($name in @('Vox.dll', 'Vox.runtimeconfig.json', 'coreclr.dll', 'libcoreclr.so', 'libcoreclr.dylib')) {
    if (Test-Path -LiteralPath (Join-Path $source $name)) { throw "JIT payload must not be distributed as NativeAOT: $name" }
}
$name = if ($Rid -eq 'win-x64') { 'Vox.exe' } else { 'Vox' }
$executable = Join-Path $source $name
$reader = [IO.BinaryReader]::new([IO.File]::OpenRead($executable))
try {
    switch -Wildcard ($Rid) {
        'win-*' {
            if ($reader.ReadUInt16() -ne 0x5a4d) { throw 'Missing Windows executable signature.' }
            $reader.BaseStream.Position = 0x3c
            $pe = $reader.ReadInt32()
            $reader.BaseStream.Position = $pe
            if ($reader.ReadUInt32() -ne 0x4550 -or $reader.ReadUInt16() -ne 0x8664) { throw 'Expected an x64 Windows PE.' }
            $reader.BaseStream.Position = $pe + 24
            if ($reader.ReadUInt16() -ne 0x20b) { throw 'Expected a PE32+ executable.' }
            $reader.BaseStream.Position = $pe + 24 + 112 + 14 * 8
            if ($reader.ReadUInt64() -ne 0) { throw 'Managed CLR directory remains in the executable.' }
        }
        'linux-*' {
            if ($reader.ReadUInt32() -ne 0x464c457f -or $reader.ReadByte() -ne 2 -or $reader.ReadByte() -ne 1) { throw 'Expected a little-endian 64-bit ELF executable.' }
            $reader.BaseStream.Position = 18
            $machine = if ($Rid -eq 'linux-x64') { 62 } else { 183 }
            if ($reader.ReadUInt16() -ne $machine) { throw "ELF architecture does not match $Rid." }
        }
        'osx-*' {
            if ($reader.ReadUInt32() -ne 4277009103) { throw 'Expected a 64-bit Mach-O executable.' }
            $cpu = if ($Rid -eq 'osx-x64') { 0x01000007 } else { 0x0100000c }
            if ($reader.ReadUInt32() -ne $cpu) { throw "Mach-O architecture does not match $Rid." }
        }
    }
}
finally { $reader.Dispose() }
Write-Host "Native payload and architecture verified: $Rid"
if ($VerificationDirectory) {
    if ($Rid -ne 'win-x64') { throw 'Window verification requires the Windows host.' }
    $start = [Diagnostics.ProcessStartInfo]::new($executable)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WorkingDirectory = $source
    $start.ArgumentList.Add('--verify-runtime')
    $start.ArgumentList.Add([IO.Path]::GetFullPath($VerificationDirectory))
    $process = [Diagnostics.Process]::Start($start)
    try {
        if (-not $process.WaitForExit(120000)) { $process.Kill(); throw 'Native runtime verification timed out.' }
        if ($process.ExitCode -ne 0) { throw "Native runtime verification failed: $($process.ExitCode)" }
        $evidence = Get-Content -LiteralPath (Join-Path $VerificationDirectory 'runtime-verification.txt') -Raw
        if ($evidence -notmatch '^NativeAOT runtime verification passed;') { throw 'Native runtime evidence is missing.' }
        Write-Host $evidence.Trim()
    }
    finally { $process.Dispose() }
}
