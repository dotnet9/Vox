[CmdletBinding()]
param([string]$WorkRoot = '', [string]$InnoSetupCompiler = '')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $WorkRoot) { $WorkRoot = Join-Path $repo '../../temp_files/vox-setup-contract' }
$work = Join-Path ([IO.Path]::GetFullPath($WorkRoot)) ([guid]::NewGuid().ToString('N'))
$isolated = Join-Path $work 'isolated source with spaces'
$payload = Join-Path $work 'payload with spaces'
$output = Join-Path $work 'output'
New-Item -ItemType Directory -Path (Join-Path $isolated 'scripts'),(Join-Path $isolated 'docs'),$payload,$output -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'scripts/installer') -Destination (Join-Path $isolated 'scripts') -Recurse
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE'),(Join-Path $repo 'logo.ico') -Destination $isolated
Copy-Item -LiteralPath (Join-Path $repo 'docs/更新日志.md') -Destination (Join-Path $isolated 'docs')
[IO.File]::WriteAllText((Join-Path $payload 'Vox.exe'), 'MZ controlled installer fixture; never executed')
$id = [guid]::NewGuid().ToString().ToUpperInvariant()
$slug = 'VoxInstallerTest' + $id.Replace('-', '')
$label = "Vox Installer Test $id"
$entry = Get-Content -LiteralPath (Join-Path $repo 'scripts/Vox.iss') -Raw
# 只改隔离副本的身份和文件扩展名，真实 Vox、Markdown 默认程序与用户配置不参与测试。
$entry = $entry.Replace('0D240AA3-DEBB-4F65-BEE5-CD2FDEE72227', $id).
    Replace('AppName=Vox', "AppName=$label").
    Replace('Vox.Markdown', "$slug.Markdown").
    Replace('Applications\Vox.exe', "Applications\$slug.exe").
    Replace('OpenWithVox', "OpenWith$slug").
    Replace('Name: "{autoprograms}\Vox"', ('Name: "{autoprograms}\' + $label + '"')).
    Replace('Name: "{autodesktop}\Vox"', ('Name: "{autodesktop}\' + $label + '"'))
foreach ($ext in @('md','markdown','mdown')) {
    $entry = $entry.Replace(('".' + $ext + '"'), ('".' + $slug + '-' + $ext + '"')).
        Replace(('Classes\.' + $ext + '\'), ('Classes\.' + $slug + '-' + $ext + '\')).
        Replace(('SystemFileAssociations\.' + $ext + '\'), ('SystemFileAssociations\.' + $slug + '-' + $ext + '\'))
}
if ($entry.Contains('Vox.Markdown') -or $entry.Contains('Applications\Vox.exe') -or $entry.Contains('OpenWithVox"') -or $entry.Contains('{cm:OpenWithVox}') -or $entry -match '(?:Classes|SystemFileAssociations)\\\.(?:md|markdown|mdown)\\') { throw 'Fixture identity was not fully isolated.' }
$definition = Join-Path $isolated 'scripts/Vox.iss'
[IO.File]::WriteAllText($definition, $entry)
if (-not $InnoSetupCompiler) {
    $InnoSetupCompiler = @(
        if (${env:ProgramFiles(x86)}) { Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe' }
        if ($env:ProgramFiles) { Join-Path $env:ProgramFiles 'Inno Setup 6/ISCC.exe' }
        if ($env:LOCALAPPDATA) { Join-Path $env:LOCALAPPDATA 'Programs/Inno Setup 6/ISCC.exe' }
    ) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}
if (-not $InnoSetupCompiler) { throw 'Inno Setup compiler is required for the installation regression.' }
& $InnoSetupCompiler '/Q' '/DAppVersion=9.9.9-preview.1' "/DSourceDir=$payload" "/DOutputDir=$output" $definition
if ($LASTEXITCODE -ne 0) { throw 'Isolated product installer compilation failed.' }
$setup = Join-Path $output 'Vox-v9.9.9-preview.1-win-x64-setup.exe'
$desktop = Join-Path ([Environment]::GetFolderPath('Desktop')) "$label.lnk"
$program = Join-Path ([Environment]::GetFolderPath('Programs')) "$label.lnk"
$registry = "HKCU:\Software\Classes\$slug.Markdown"
$shell = New-Object -ComObject WScript.Shell
function Invoke-Installer([string]$File, [string[]]$Arguments) {
    $process = Start-Process -FilePath $File -ArgumentList $Arguments -PassThru -WindowStyle Hidden
    try {
        if (-not $process.WaitForExit(60000)) { $process.Kill(); throw 'Fixture installation timed out.' }
        if ($process.ExitCode -ne 0) { throw "Fixture installation failed: $($process.ExitCode)" }
    }
    finally { $process.Dispose() }
}
foreach ($enabled in @($true, $false)) {
    $install = Join-Path $work "installed app $enabled"
    $tasks = if ($enabled) { 'desktopicon,markdownassociation' } else { '!desktopicon,!markdownassociation' }
    Invoke-Installer $setup @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-','/LANG=english',('/DIR="' + $install + '"'),("/TASKS=$tasks"),('/LOG="' + (Join-Path $work "install-$enabled.log") + '"'))
    try {
        if (-not (Test-Path -LiteralPath $program)) { throw 'Start menu shortcut was not installed.' }
        $link = $shell.CreateShortcut($program)
        if ($link.TargetPath -ne (Join-Path $install 'Vox.exe') -or $link.WorkingDirectory -ne $install) { throw 'Start menu shortcut points at the wrong application.' }
        if ((Test-Path -LiteralPath $desktop) -ne $enabled) { throw 'Desktop shortcut does not follow the selected task.' }
        if ((Test-Path -LiteralPath $registry) -ne $enabled) { throw 'Markdown candidate registration does not follow the selected task.' }
        if ($enabled) {
            $command = (Get-Item -LiteralPath "$registry\shell\open\command").GetValue('')
            if ($command -ne ('"' + (Join-Path $install 'Vox.exe') + '" "%1"')) { throw 'Markdown command quoting is incorrect.' }
            $icon = (Get-Item -LiteralPath "$registry\DefaultIcon").GetValue('')
            if ($icon -ne ('"' + (Join-Path $install 'Vox.exe') + '",0')) { throw 'Markdown icon registration is incorrect.' }
            foreach ($ext in @('md','markdown','mdown')) {
                $key = Get-Item -LiteralPath "HKCU:\Software\Classes\.$slug-$ext\OpenWithProgids"
                if ($key.GetValueNames() -notcontains "$slug.Markdown") { throw 'An extension opening candidate is missing.' }
            }
        }
    }
    finally {
        $uninstaller = Get-ChildItem -LiteralPath $install -Filter 'unins*.exe' -File | Select-Object -First 1
        if (-not $uninstaller) { throw 'Fixture uninstaller was not produced.' }
        Invoke-Installer $uninstaller.FullName @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART')
    }
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while (((Test-Path -LiteralPath $program) -or (Test-Path -LiteralPath $desktop) -or (Test-Path -LiteralPath $registry)) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
    if ((Test-Path -LiteralPath $program) -or (Test-Path -LiteralPath $desktop) -or (Test-Path -LiteralPath $registry)) { throw 'Uninstall left a fixture shortcut or application registration.' }
}
Write-Host "Passed isolated install/uninstall, optional desktop shortcut, Markdown candidates and icon registration: $work"
