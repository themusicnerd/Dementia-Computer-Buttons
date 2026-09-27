[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot

& (Join-Path $PSScriptRoot 'Publish-DadConsole.ps1') -Configuration $Configuration
if ($LASTEXITCODE -ne 0) { throw 'Application publish failed.' }

$project = Join-Path $repository 'installer\DementiaComputerButtons.Installer.wixproj'
dotnet build $project --configuration $Configuration
if ($LASTEXITCODE -ne 0) { throw 'MSI build failed.' }

$built = Join-Path $repository "installer\bin\x64\$Configuration\DementiaComputerButtons-0.1.3-x64.msi"
if (-not (Test-Path -LiteralPath $built)) {
    $built = Get-ChildItem (Join-Path $repository 'installer\bin') -Filter '*.msi' -Recurse |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $built -or -not (Test-Path -LiteralPath $built)) { throw 'WiX completed without producing an MSI.' }

$destination = Join-Path $repository 'dist\DementiaComputerButtons-0.1.3-x64.msi'
Copy-Item -LiteralPath $built -Destination $destination -Force
$checksum = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumPath = $destination + '.sha256'
Set-Content -LiteralPath $checksumPath -Encoding ascii -Value "$checksum  $(Split-Path -Leaf $destination)"
Write-Host "Built MSI installer at $destination"
Write-Host "Built checksum at $checksumPath"
