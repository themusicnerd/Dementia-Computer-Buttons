[CmdletBinding()]
param(
    [string]$Runtime = 'win-x64',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$output = Join-Path $repository 'dist\DementiaComputerButtons'

dotnet publish (Join-Path $repository 'src\DementiaComputerButtons\DementiaComputerButtons.csproj') `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    --output $output

if ($LASTEXITCODE -ne 0) { throw 'Dementia Computer Buttons publish failed.' }
Write-Host "Published Dementia Computer Buttons to $output"
