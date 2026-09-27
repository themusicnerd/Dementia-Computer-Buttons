# Test record

Test computer date: 2026-09-26 (Australia/Sydney). Board under test: connected
Jaycar XC4417 / Keyestudio KS0501.

## Environment discovery

| Check | Result | Evidence |
|---|---|---|
| Git | PASS | 2.48.1; repository initialized on `main` |
| .NET 8 SDK | PASS | 8.0.425 installed from Microsoft winget package |
| Arduino CLI | PASS | 1.5.1 installed from `ArduinoSA.CLI` winget package |
| AVR core | PASS | `arduino:avr` 1.8.8 |
| CP2102 detection | PASS after repair | `VID_10C4&PID_EA60`, serial 0001 |
| CP210x driver | PASS | Silicon Labs 11.6.0.420; catalog signature valid from Microsoft Windows Hardware Compatibility Publisher |
| Serial port | PASS | COM3 during this session; number is not assumed by software |

Initially the CP2102 had Device Manager problem code 28 and no COM port. The
official signed Silicon Labs universal VCP package was installed, after which it
enumerated normally as `Silicon Labs CP210x USB to UART Bridge (COM3)`.

## Firmware and transport

| Check | Result | Evidence |
|---|---|---|
| Minimal Uno compile | PASS | 1,950 bytes flash, 310 bytes SRAM |
| Uno upload / bootloader | PASS | avrdude programmer `arduino`, 115200, signature `1E 95 0F` |
| Minimal boot identity | PASS | `DCB/1 READY board=KS0501 fw=0.0.1-test` |
| Minimal handshake | PASS | `HELLO DCB/1 KS0501 0.0.1-test` |
| Minimal heartbeat | PASS | `PONG 42` |
| Full firmware compile | PASS | 14,790 bytes (45%) flash; 880 bytes (42%) SRAM |
| Full firmware upload | PASS | uploaded to COM3 as Uno |
| Full identity | PASS | board KS0501, firmware 0.2.1, protocol DCB/1; direct panel flags `pb=1 pl=1` parsed |
| Windows auto-discovery | PASS | console utility enumerated and selected COM3 by handshake |
| Heartbeat | PASS | repeated PONG processing remained healthy |
| Command correlation | PASS | ACK and DATA requests completed |
| Panel command path | PASS | `PANEL ALL OFF`, LED 3 ON/OFF, and brightness 25%/100% all ACKed on the real controller |
| Telemetry | PASS | repeated A6/A7 fields received |
| WPF diagnostics startup | PASS | responsive main window remained running and logged real-board handshake |

Observed sensor samples included light 0–11 and smoothed microphone 0–9. This
confirms the analogue acquisition and serial telemetry path, but response to a
known light/sound stimulus has not yet been operator-verified.

## Peripheral status

| Peripheral | Current status | What is actually known |
|---|---|---|
| HT16K33 matrix bus | PASS | address `0x70` acknowledged; firmware reported `matrix=1` |
| Matrix pixels/orientation | FIXED, RETEST NEEDED | operator found the original 8×16 logical mapping split icons across side-by-side modules; firmware now uses the physical 16×8 arrangement |
| Red/yellow/green LEDs | PASS | operator confirmed all panel lights behave as expected |
| RGB LED 1 and 2 | UNCONFIRMED | independent colour commands ACKed; visual result not observed |
| Buzzer | UNCONFIRMED | 50 ms command ACKed and earcon firmware uploaded; audible result not yet confirmed |
| Left/right buttons | PASS | operator confirmed all eight panel buttons behave as expected |
| Ambient light sensor | PARTIAL PASS | raw values received; controlled stimulus pending |
| Microphone sensor | PARTIAL PASS | smoothed values received; controlled sound stimulus pending |
| IR | NOT INSTALLED | correctly advertised as `ir=0` |
| 8-button / 6-light panel | PASS | operator confirmed all eight buttons and all six lights behave as expected |

For the external panel set D2 and D3 DIP switches left/OFF. For onboard-button
testing set them right/ON. If the matrix reports unavailable, set A4 and A5
right/ON. Use the WPF **RUN
HARDWARE SELF TEST** workflow to obtain and record operator-confirmed results.

## Automated tests

`dotnet test DementiaComputerButtons.sln --configuration Debug` passed 30/30.
Coverage includes handshake/event/telemetry parsing, malformed and oversized
messages, state transitions, configurable button routing, configuration fallback
and validation, COM-number changes, reconnect ordering, heartbeat timeout and
mock-controller development.

On 2026-09-27 the live WPF build started responsively, enumerated Windows render
and capture endpoints without error, and re-established the KS0501 handshake on
COM3 with firmware 0.2.1. A synthetic local named-pipe call event produced
`Home -> IncomingCall -> Home` and exercised the call-screen/flashing-light path.
MicroSIP 3.22.16 was installed successfully through the console's winget action.
A real SIP account and real inbound/outbound call remain to be verified.

Subsequent live testing verified a real incoming SIP event and ringing callback.
An outgoing Adrian call progressed to connected state, remained connected for
approximately 11 seconds, and was ended successfully by the physical Stop button.
The call window's microphone and speaker endpoint meters returned live normalized
peak values. The incoming call was intentionally rejected during overlapping
operator/development button testing, so a clean answer-and-conversation test is
still required.

A later non-call synthetic sequence verified the final UI without button input:
the screen displayed `CONNECTED: PRESS STOP TO END`, an advancing `00:00:04`
duration, two distinct microphone/speaker progress meters, and then returned to
Home. The application remained responsive and logged zero errors during this
sequence. A duplicate Core Audio COM declaration discovered during testing was
replaced with CLSID activation and the volume poller then started without errors.

The September 27 follow-up makes Stop idempotent after a completed call and
observes the managed VLC process exiting naturally. VLC completion now returns
the central state to Home, which switches off the Video light. Stop during an
active SIP call also requests MicroSIP to minimize after hanging up. A daily
plain-text activity log records local timestamps, button presses, resolved
actions and media state. The automated-test total below supersedes the earlier
30/30 result after this revision is built.

The revised solution built with zero warnings and zero errors and passed 31/31
automated tests. This includes the regression that a second Stop press cannot
end an already-ended call or republish stale call state.
Direct verification against the installed MicroSIP process confirmed that the
Windows minimize operation leaves it responsive while its main window is
minimized; the ineffective running-instance `/minimized` approach was not used.

The call quiet-hours and Windows-shell revision built with zero warnings and
zero errors and passed 40/40 automated tests. Coverage includes disabled,
daytime, overnight, boundary and all-day quiet ranges; blocked outgoing calls;
quiet-hours configuration persistence; and Stop requesting shell dismissal.
A live Windows 11 test opened Start/Search (`SearchHost`), invoked Stop, and
verified that the panel closed while Dementia Computer Buttons remained
responsive. No real call was placed or rejected for this test.

The release-readiness revision also passed 47/47 tests in Release configuration
with zero build warnings or errors. The Start with Windows setting was enabled
through the real WPF console, verified against the per-user Windows Run value
with the exact published executable path, then disabled and verified removed.
The final WiX 5.0.2 MSI built with zero warnings or errors. Its Windows Installer
database reports product `Dementia Computer Buttons`, version `0.1.0`, four file
rows, one shortcut row, and five component rows. The final 57,200,640-byte MSI
has SHA-256
`B1E7351C3372A90D0F9CDB827A71E96AAC59153884A5D879E78E742D3D68871C`.
GitHub release tag parsing, update configuration, and checksum-gated update
selection are covered by the additional automated tests.

The 0.1.1 revision embeds the button-grid artwork as the executable and WPF
window icon and explicitly uses the executable icon for the notification area.
Scheduled blackout state now also selects the lowest non-zero panel-light PWM
level and restores configured brightness on keyboard/mouse wake. A live
all-day-schedule test against the connected controller logged 1% when blackout
became active, then 36% immediately after a mouse click woke the screen. The
user configuration was restored byte-for-byte after the test. The Release build
passed 48/48 automated tests. The final 57,499,648-byte 0.1.1 MSI has SHA-256
`FCDBC27D40CF89DB130D9BC60790490765104AFFEC10100B329BF17EA7D514D3`.
