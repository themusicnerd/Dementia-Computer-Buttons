#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [switch]$StartAtLogon,
    [switch]$SkipApplications
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$source = Join-Path $repository 'dist\DementiaComputerButtons'
$destination = Join-Path $env:ProgramFiles 'Dementia Computer Buttons'

if (-not (Test-Path -LiteralPath (Join-Path $source 'DementiaComputerButtons.exe'))) {
    throw "Published application not found. Run tools\Publish-DadConsole.ps1 first."
}

if (-not $SkipApplications) {
    $packages = @('VideoLAN.VLC', 'Zoom.Zoom', 'MicroSIP.MicroSIP', 'Spotify.Spotify', 'Google.Chrome')
    foreach ($package in $packages) {
        winget install --id $package --exact --source winget --silent `
            --accept-source-agreements --accept-package-agreements --disable-interactivity
        if ($LASTEXITCODE -notin 0, -1978335189) { throw "winget failed installing $package (exit $LASTEXITCODE)." }
    }
}

New-Item -ItemType Directory -Path $destination -Force | Out-Null
Copy-Item -Path (Join-Path $source '*') -Destination $destination -Recurse -Force

$shell = New-Object -ComObject WScript.Shell
$startMenu = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\Dementia Computer Buttons.lnk'
$shortcut = $shell.CreateShortcut($startMenu)
$shortcut.TargetPath = Join-Path $destination 'DementiaComputerButtons.exe'
$shortcut.WorkingDirectory = $destination
$shortcut.Description = 'Dementia Computer Buttons'
$shortcut.Save()

if ($StartAtLogon) {
    $startup = Join-Path ([Environment]::GetFolderPath('CommonStartup')) 'Dementia Computer Buttons.lnk'
    Copy-Item -LiteralPath $startMenu -Destination $startup -Force
}

$cp210 = Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue |
    Where-Object { $_.InstanceId -match 'VID_10C4&PID_EA60' }
if (-not $cp210) {
    Write-Warning 'The CP210x controller is not currently detected. Connect it and allow Windows Update to install the signed Silicon Labs VCP driver.'
}

Write-Host "Dementia Computer Buttons installed in $destination"
Write-Host 'Content settings are stored per user under %LOCALAPPDATA%\DementiaComputerButtons.'
