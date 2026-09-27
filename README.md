# Dementia Computer Buttons

A reliability-first Windows 11 accessibility controller that makes a PC behave
more like an appliance. A WPF application owns the true system state and drives
an Arduino Uno-compatible Keyestudio/Jaycar KS0501 controller. Physical button
lights therefore describe what Windows is actually doing, rather than merely
echoing the last switch press.

The current phase provides an engineering diagnostics application, production-
shaped serial transport, onboard hardware firmware, Windows master-volume
control, a central application state model, automated tests, and extension
points for VLC, browser content, Zoom, MicroSIP and future IR hardware.

## Repository layout

- `src/DementiaComputerButtons.Core`: protocol, state, configuration, service
  interfaces, serial discovery/reconnect, button routing and mock controller.
- `src/DementiaComputerButtons`: .NET 8 WPF diagnostics UI and Windows service
  implementations.
- `firmware/keyestudio-max`: maintainable ATmega328P firmware.
- `firmware/board-identification`: retained minimal first-flash proof sketch.
- `tools/ControllerDiagnostic`: headless discovery/handshake integration check.
- `tests/DementiaComputerButtons.Tests`: parser, state, routing,
  configuration, reconnect, malformed-input and heartbeat tests.
- `installer`: WiX source for the versioned x64 MSI package.
- `docs`: architecture, protocol, hardware, testing and future IR design.

## Prerequisites

- Windows 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Arduino CLI](https://arduino.github.io/arduino-cli/latest/installation/)
- Arduino AVR Boards core and Adafruit NeoPixel library
- Silicon Labs CP210x VCP driver for the KS0501 USB bridge

The development computer currently has .NET SDK 8.0.425, Arduino CLI 1.5.1,
Arduino AVR core 1.8.8 and Adafruit NeoPixel 1.15.5 installed.

## Build and run the Windows application

```powershell
dotnet restore DementiaComputerButtons.sln
dotnet build DementiaComputerButtons.sln --configuration Debug
dotnet run --project src/DementiaComputerButtons/DementiaComputerButtons.csproj
```

Run without hardware by setting `DCB_USE_MOCK=1` in that terminal first. Logs
are JSON Lines files under
`%LOCALAPPDATA%\DementiaComputerButtons\logs`.

The window includes a clickable 3×3 preview of the physical panel. Its **Content
setup** section saves a YouTube video/live-stream URL, lets the operator browse
for a local VLC video, and chooses whether Stop/Home closes VLC. Settings are
stored under `%LOCALAPPDATA%\DementiaComputerButtons`, outside the executable.
Minimizing the window hides it from the taskbar and leaves a Dementia Computer Buttons icon in
the notification area beside the clock; double-click that icon to restore it.

Contact 1 and Contact 2 can each independently use a Zoom link or a standard `sip:`
address. MicroSIP is preferred because its command line supports both dialling
and `/hangupall`, allowing Stop/Home to end calls without screen automation. A
registered generic softphone remains a fallback. No credentials are stored in
source code.

TV content starts at the original YouTube watch/live URL in a dedicated
fullscreen browser profile. It deliberately avoids top-level `/embed/` URLs,
which YouTube rejects without a referring page as Error 153. Local video starts
fullscreen and always-on-top in VLC. Stop/Home closes only content launched by Dementia Computer Buttons;
VLC is left running when its checkbox is cleared.

A normal TV-button press opens the configured YouTube URL on release. Holding
the physical TV button for 1.5 seconds suppresses that TV action and opens the
configured Spotify URL instead.

The setup page selects Edge or Chrome for both YouTube and Spotify. It also
provides optional contact photos, configurable call names, speaker/headphone
audio profiles, and a display blackout schedule such as 22:00–07:00. Calls wake
the blacked-out display temporarily and take priority over managed media.

Optional call quiet hours can block both incoming and outgoing calls during a
daily local-time range, including ranges across midnight such as 22:00–07:00.
Incoming MicroSIP calls are declined without flashing the panel or showing the
call screen. Setting start and end to the same time blocks calls all day.
Stop/Home also dismisses the Windows Start or Search panel when it is open.

For SIP calls, install MicroSIP and select **Connect MicroSIP incoming calls**.
After restarting MicroSIP, its supported callbacks drive the large call screen
and panel lights. All six controllable lights flash while ringing; any physical
button except Stop answers, while Stop declines. During a call, Stop hangs up.
The call screen shows calling/ringing/connecting/connected state, connected-call
duration, the configured contact photo, and live microphone/speaker peak meters.
The meters use Windows endpoint levels only and never record audio.

Run all automated tests:

```powershell
dotnet test DementiaComputerButtons.sln
```

## Activity history

The diagnostics window has an **Open activity log folder** button. Each day it
creates a readable `activity-YYYY-MM-DD.txt` file under
`%LOCALAPPDATA%\DementiaComputerButtons\logs`. Entries use local date and time
and record physical button presses, their resolved actions, and state changes
such as YouTube playing, Spotify playing, video playing/finished, calls and Home.
The separate JSONL files in the same folder remain the detailed engineering log.

Run the console connection check:

```powershell
dotnet run --project tools/ControllerDiagnostic/ControllerDiagnostic.csproj
```

## Publish and install on another PC

Create the self-contained application and Windows Installer package:

```powershell
.\tools\Publish-DadConsole.ps1
.\tools\Build-Msi.ps1
msiexec.exe /i .\dist\DementiaComputerButtons-0.1.0-x64.msi
```

The MSI is self-contained and creates an all-users Start Menu shortcut. The
engineering console checks VLC, Zoom, MicroSIP, Spotify and Chrome and offers
trusted `winget` installation actions when needed. The CP210x driver normally
comes through Windows Update. Enable automatic launch with the **Start Dementia
Computer Buttons automatically after Windows sign-in** setting in the console.
See [build-and-install.md](docs/build-and-install.md) for clean-machine build,
installation, logging, firmware, upgrade and release instructions.

The application checks the public GitHub release channel at startup. A newer
version is offered only when its MSI has a matching SHA-256 release asset;
installation still uses the normal Windows Installer and UAC confirmation.

## Compile and upload firmware

```powershell
arduino-cli core install arduino:avr
arduino-cli lib install "Adafruit NeoPixel"
arduino-cli board list
arduino-cli compile --fqbn arduino:avr:uno firmware/keyestudio-max
arduino-cli upload --fqbn arduino:avr:uno --port COM3 firmware/keyestudio-max
```

Replace `COM3` with the port shown on the machine at upload time. The Windows
application does **not** save or assume that port: it enumerates every available
serial port and accepts only a successful versioned DCB handshake.

## Hardware diagnostics

Move A4 and A5 to right/ON for the matrix. Use D2 and D3 right/ON when testing
the onboard buttons, or left/OFF when the external volume buttons are connected.
Start the WPF application,
wait for “Connected and healthy”, then use individual output controls or **RUN
HARDWARE SELF TEST**. Visual and audible steps ask the operator for confirmation;
they are never marked passed merely because the serial command was acknowledged.

The LEFT button maps to volume down 5%; RIGHT maps to volume up 5%. Holding for
500 ms then ramps in 2% steps approximately every 180 ms until release. Their buzzer
earcons are deliberately distinct: descending for down, ascending for up, with a
three-note cue for a long press. All patterns are short and nonblocking.

Windows polls the real Core Audio master volume twice per second. Changes made
through physical buttons, media keys, or the Windows taskbar briefly display a
horizontal volume bar on the matrix before it returns to the connected icon.

The full panel connects eight active-low switches and six on/off LED outputs
directly to the Arduino headers; no extra control chips are required. The two
call buttons have no lights. Every bare LED needs its own series resistor. The
sixth light is ON for the headphone profile and OFF for the speaker profile.
Each profile selects both a Windows output device and microphone; the same
microphone may be chosen for both. Voicemeeter is not required. See
[hardware.md](docs/hardware.md) for the complete pin table before wiring.

## Current hardware state

USB driver, Uno bootloader upload, DCB handshake, heartbeat, controller identity,
matrix I²C acknowledgement and sensor telemetry have been verified on the
connected board. Output commands were acknowledged, and the external
eight-button/six-light panel was operator-confirmed. Some onboard RGB, buzzer
and controlled sensor stimulus checks remain outstanding. See
[testing.md](docs/testing.md) for the precise evidence.
