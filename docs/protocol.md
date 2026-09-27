# DCB serial protocol version 1

Transport is 115200 baud, 8 data bits, no parity, one stop bit. Messages are
printable ASCII terminated by LF; CR before LF is tolerated. Firmware accepts at
most 95 characters before LF. Keywords and symbolic values are uppercase.

## Discovery and identity

On boot the controller emits:

```text
DCB/1 READY board=KS0501 fw=0.2.2
```

Windows must not identify the controller from a COM number. It sends:

```text
HELLO DCB/1
```

The controller replies:

```text
HELLO DCB/1 KS0501 0.2.2 caps=RGB,MATRIX,BUZZ,BTN,PANEL,SENSORS ir=0 matrix=1 pb=1 pl=1
```

`ir=0` truthfully means no IR subsystem is installed. `matrix=0` means the
HT16K33 did not acknowledge at `0x70`, commonly because of its DIP switch.

## Heartbeat and watchdog

```text
PING <nonce>
PONG <nonce> uptime=<milliseconds>
```

Windows sends a ping every second and treats four seconds without a pong as a
lost controller. Firmware treats five seconds without a valid host message as a
lost Windows controller. A firmware `READY` observed during an established link
means the microcontroller restarted; Windows closes and re-handshakes.

## Commands and responses

Every command has a short request ID:

```text
CMD <id> <command...>
ACK <id>
ERR <id> <REASON>
```

`GET` commands return `DATA <id> ...` instead of an additional ACK. IDs are
opaque tokens echoed by firmware.

Supported commands:

```text
CMD 4 RGB 0 255 0 0
CMD 5 RGB 1 0 0 255
CMD 6 MATRIX CLEAR
CMD 7 MATRIX PATTERN
CMD 8 MATRIX ICON OK
CMD 9 MATRIX ICON X
CMD 10 MATRIX TEXT OK
CMD 11 MATRIX BAR 65
CMD 12 BUZZ 1000 50
CMD 13 TELEMETRY 1000
CMD 14 TELEMETRY OFF
CMD 15 GET STATUS
CMD 16 PANEL LED 0 ON
CMD 17 PANEL LED 5 OFF
CMD 18 PANEL ALL OFF
CMD 19 PANEL BRIGHTNESS 50
CMD 20 PANEL LONGPRESS 2500
```

RGB indices are zero-based (`0` and `1`); channels are 0–255. Matrix text is
currently the first two alphanumeric characters, shown left-to-right on the
physical 16-column × 8-row display.
Panel LED indices are zero-based and range from 0–5. Values are strictly `ON`
or `OFF`; every bare LED still requires a physical series resistor. Invalid
indices or states return `PANEL_LED_UNAVAILABLE`.
`PANEL BRIGHTNESS` sets one shared 0–100% brightness for all six outputs using
nonblocking 16-step software PWM. It does not change which state-controlled
lights are logically on.
Matrix bars accept 0–100. Buzzer frequency is 100–5000 Hz and duration 1–250 ms,
which prevents continuous host-commanded noise. Telemetry intervals are either
`OFF` or 250–10000 ms.

`PANEL LONGPRESS` sets the external panel's non-repeating-button hold threshold
in milliseconds. Valid values are 500 through 10000. It takes effect immediately
and remains active until reset; Windows reapplies the saved setting after every
handshake.

## Events and telemetry

```text
EVT BTN PANEL_1 DOWN uptime=4600
EVT BTN PANEL_1 REPEAT uptime=4780
EVT BTN PANEL_5 LONG uptime=4800
EVT BTN PANEL_8 UP uptime=4900
EVT LINK TIMEOUT
EVT HW MATRIX_UNAVAILABLE
TEL SENSORS light=321 mic=18 uptime=5000
DATA 15 STATUS uptime=5100 matrix=1
```

The volume buttons emit `REPEAT` after a 500 ms hold and then approximately every
180 ms until `UP`. Their normal step is 5%; repeat steps default to 2%.
Non-repeating buttons emit `LONG` after the configured threshold, which defaults
to 1500 ms. The default Windows mapping defers TV until release and maps a TV
long press to Spotify. The microphone
field is a smoothed analogue sound-level value only; no audio is recorded.

Handshake/status fields `pb=1` and `pl=1` mean the direct-pin button and LED
subsystems are configured. They do not prove that external switches or LEDs are
physically connected; the hardware self-test provides that confirmation.

## Error handling

Known reasons include `LINE_TOO_LONG`, `PROTOCOL_MISMATCH`, `NOT_CONNECTED`,
`MALFORMED`, `BAD_LED`, `BAD_RGB`, `MATRIX_UNAVAILABLE`, `BAD_MATRIX`,
`BAD_BUZZ`, `BAD_GET`, `BAD_TELEMETRY`, `UNKNOWN_COMMAND`, and
`UNKNOWN_MESSAGE`. Malformed input never blocks the firmware; parsing resumes at
the next LF.
