# Build and installation guide

## Supported target

The Windows application and MSI target 64-bit Windows 11. The published
application is self-contained, so a Dad Console PC does not need a separate
.NET runtime. Building from source requires the .NET 8 SDK.

Arduino CLI is optional for normal use but required for the in-application
controller firmware updater. It can be installed from the console's Required
applications section through the trusted winget package. If the AVR core is
missing, the updater asks Arduino CLI to install the official `arduino:avr`
platform before uploading.

## Build and test from source

From a PowerShell terminal in the repository root:

```powershell
dotnet restore DementiaComputerButtons.sln
dotnet build DementiaComputerButtons.sln --configuration Release
dotnet test DementiaComputerButtons.sln --configuration Release --no-build
```

Run against a connected controller:

```powershell
dotnet run --project src/DementiaComputerButtons/DementiaComputerButtons.csproj --configuration Debug
```

Run without hardware:

```powershell
$env:DCB_USE_MOCK = '1'
dotnet run --project src/DementiaComputerButtons/DementiaComputerButtons.csproj
```

## Create the portable application

```powershell
.\tools\Publish-DadConsole.ps1
```

Output is written to `dist\DementiaComputerButtons`. The executable is a
self-contained, single-file `win-x64` application. `appsettings.json` beside it
contains non-personal defaults only. Saved user configuration is kept under
`%LOCALAPPDATA%\DementiaComputerButtons`.

## Build the MSI

```powershell
.\tools\Build-Msi.ps1
```

The build uses the pinned `WixToolset.Sdk` 5.0.2 package from NuGet. No global
WiX installation is required. The resulting installer is:

```text
dist\DementiaComputerButtons-0.1.7-x64.msi
```

The MSI installs for all users under `Program Files\Dementia Computer Buttons`,
adds a common Start Menu shortcut, supports Windows Installer repair/uninstall,
and performs major upgrades. It does not bundle VLC, MicroSIP, Zoom, Chrome,
Spotify, Arduino CLI, or device drivers. Those remain separately maintained
dependencies and can be checked or installed from the engineering console.

The current development MSI is not code-signed. Windows may identify the
publisher as unknown until a trusted code-signing certificate is configured.

## GitHub update channel

On startup, the application checks the latest non-draft, non-prerelease GitHub
release at `themusicnerd/Dementia-Computer-Buttons`. The engineering console can
also run a manual check. An update is offered only when all of these conditions
are met:

- the release tag is a higher semantic version, such as `v0.2.0`;
- the release contains `DementiaComputerButtons-VERSION-x64.msi`;
- the release contains the matching `.msi.sha256` asset;
- both asset addresses use GitHub HTTPS URLs;
- the downloaded MSI matches the published SHA-256 value.

Selecting **Install update** downloads and verifies the MSI under
`%LOCALAPPDATA%\DementiaComputerButtons\updates`, then launches Windows Installer.
Windows displays the normal UAC prompt because the MSI installs for all users.
The updater never embeds a GitHub token and therefore uses a public GitHub
release channel.

## Install or uninstall the MSI

Use Explorer or an elevated PowerShell terminal:

```powershell
msiexec.exe /i .\dist\DementiaComputerButtons-0.1.7-x64.msi
```

For a diagnostic installation log:

```powershell
msiexec.exe /i .\dist\DementiaComputerButtons-0.1.7-x64.msi /l*v .\dcb-install.log
```

Uninstall through Windows **Settings > Apps > Installed apps**, or by invoking
the same MSI with `/x`.

## Start automatically with Windows

In the engineering console, enable **Start Dementia Computer Buttons
automatically after Windows sign-in**, then select **Save console settings**.
The application creates a per-user entry under the standard Windows Run key.
Turning the option off and saving removes that entry. On each launch, an enabled
setting refreshes the registered executable path so MSI upgrades remain valid.

## Firmware build and upload

Install Arduino CLI from its official distribution, then run:

```powershell
arduino-cli core update-index
arduino-cli core install arduino:avr
arduino-cli lib install "Adafruit NeoPixel"
arduino-cli compile --fqbn arduino:avr:uno firmware/keyestudio-max
arduino-cli board list
arduino-cli upload --fqbn arduino:avr:uno --port COM3 firmware/keyestudio-max
```

Replace `COM3` with the currently detected controller port. The Windows
application itself never assumes or stores a COM port number as controller
identity.

## Release checklist

1. Update the application and MSI version together.
2. Run the Release build and all automated tests.
3. Compile the firmware for `arduino:avr:uno`.
4. Run `tools\Build-Msi.ps1`.
5. Inspect the MSI ProductName, ProductVersion, file table, and shortcut table.
6. Test install, upgrade, launch, automatic startup, repair, and uninstall on a
   disposable Windows 11 machine.
7. Attach the MSI to the matching GitHub release.
