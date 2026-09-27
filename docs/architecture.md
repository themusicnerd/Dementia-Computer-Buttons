# Architecture

## Design rules

Windows is the authority for application state. The Arduino is a deterministic
input/output controller: it reports physical events and renders commanded state,
but it does not infer that a call, video or website succeeded. This distinction
is what lets a future illuminated button mean “connected to Contact 1” rather than
“the Contact 1 switch was pressed.”

Dependencies point inward toward small interfaces. WPF knows view models;
view models know service interfaces; serial, Windows audio and process-launch
details live behind those interfaces.

## Runtime flow

```text
Physical switch -> firmware debounce -> EVT BTN
    -> ArduinoService -> ButtonActionService -> SystemStateService / VolumeService
    -> real Windows state changes -> output commands -> LEDs / RGB / matrix / buzzer
```

`ArduinoService` enumerates current COM ports, opens one candidate at a time,
allows 1.7 seconds for the Uno reset, sends `HELLO DCB/1`, and accepts only a
`KS0501` reply. It then heartbeats once per second. An unplug, I/O exception or
heartbeat timeout closes that connection and returns to enumeration; the previous
working port is tried first if it still exists, but a changed COM number is fine.

The firmware stops considering Windows connected after five seconds without a
valid host message. It lights red, extinguishes green, pulses both RGB pixels
blue and displays an X while continuing to parse serial input. All timing uses
`millis()`; no animation or earcon blocks serial or button processing.

## Services

- `IArduinoService`: discovery, handshake, request correlation, events,
  heartbeat and reconnect.
- `IVolumeService`: Windows Core Audio master volume and mute.
- `IButtonActionService`: configurable physical-input-to-semantic-action route.
- `ISystemStateService`: home, media, outgoing call, `IncomingCall`, `InCall`,
  and error transitions.
- `IConfigurationService`: JSON loading, validation and safe defaults.
- `ILoggingService`: structured daily JSONL logs.
- `IAudioRoutingService`, `IVlcService`, `IBrowserService`, `ICallService` and
  `IIRService`: isolated integration seams. Missing integrations produce logged
  errors without crashing the controller.

`IAudioRoutingService` applies a complete speaker or headphone profile: default
render endpoint plus default capture endpoint. `ICallService` receives MicroSIP
events over a local named pipe from per-user callback scripts. Incoming calls
preempt managed browser/VLC media, open a large photo/name status screen, and
put panel lighting into a nonblocking flash state.
An optional local-time quiet-hours range is checked for both outgoing actions
and incoming callbacks. Blocked incoming calls are declined before call state
or panel-light state is published. Stop/Home uses `IWindowsShellService` to send
Escape only when a known Windows Start/Search host owns the foreground window.

`DisplayScheduleService` owns a full-screen black WPF window during configured
hours. The blackout window captures a keyboard key or mouse click as an explicit
temporary wake request. That wake is held until the period ends or Stop/Home
calls `IDisplayScheduleService.RearmBlackout`; saved schedule configuration is
never changed by either action. Its blackout-state event also drives
`PanelLightingService`: an active blackout selects the lowest non-zero firmware
PWM level, while wake, calls and the end of the scheduled period restore the
operator's configured brightness.

## Configuration and personal data

`appsettings.json` is human-readable and contains only non-personal defaults.
Future URLs, paths, call keys, brightness settings, external pin mappings and IR
commands belong there or in a separate user configuration, not in source code.
Invalid or missing settings produce warnings and safe defaults.

## Reliability notes

- Serial requests have IDs, two-second response timeouts and bounded line sizes.
- Firmware input uses a fixed 96-byte buffer; no dynamic `String` or JSON parser.
- Sensor telemetry is one sample per second by default and is not written to the
  structured log, preventing log floods.
- A mock Arduino service is available with `DCB_USE_MOCK=1`.
- Managed VLC/browser process ownership is kept separate from the state model.
- No network listener or unauthenticated remote API exists.
