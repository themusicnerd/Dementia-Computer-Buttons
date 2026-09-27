# KS0501 / XC4417 hardware

The connected board is a Jaycar XC4417 / Keyestudio MAX board, manufacturer
model KS0501, using an ATmega328P and CP2102 USB-to-UART bridge. It compiles and
uploads as `arduino:avr:uno`.

## Onboard mapping

| Device | ATmega pin | Firmware role |
|---|---:|---|
| Red LED | D8 | mirrors direct panel Video light |
| Yellow LED | D10 | mirrors direct panel Stop/Home light |
| Green LED | D13 | mirrors direct panel Speakers light |
| Two 6812/WS2812-style RGB LEDs | D4 | independently addressable state pixels |
| Left button | D3 | panel button 2, `VOLUME_UP` |
| Right button | D2 | panel button 1, `VOLUME_DOWN` |
| Buzzer through onboard driver | D9 | bounded feedback earcons |
| Ambient light sensor | A6 | raw 0–1023 telemetry |
| Microphone/sound-level sensor | A7 | smoothed 0–1023 telemetry; no recording |
| HT16K33 16×8 matrix SDA | A4 | I²C data; two 8×8 modules side-by-side |
| HT16K33 16×8 matrix SCL | A5 | I²C clock |
| HT16K33 address | `0x70` | probed during firmware startup |

The four-way DIP switch gates D2, D3, A4 and A5. Keyestudio documents the
board's **right/ON** position as connected to the onboard devices and the left
position as disconnected. For the final panel, set D2 and D3 to **left/OFF** so
the external volume switches own those pins. Keep A4 and A5 right/ON for the
matrix.

Pin mappings are centralized in `firmware/keyestudio-max/HardwareConfig.h`.
Future external arcade input pins and lamp outputs must be added to configuration
rather than repeated throughout firmware. Final lamp wiring, current-limiting
resistors and any transistor drivers will be specified after the chosen buttons
and LED current are known.

The lights marked TX/RX are treated as USB serial activity indicators, not IR
hardware. No IR capability is advertised by current firmware.

## Eight-button / six-light panel

The panel uses the remaining Arduino pins directly:

| Button | Function | Input pin | Light output |
|---:|---|---:|---:|
| 1 | Volume Down | D2 | A1 |
| 2 | Volume Up | D3 | A2 |
| 3 | Call Contact 1 | D5 | none |
| 4 | Call Contact 2 | D6 | none |
| 5 | TV | D7 | A3 |
| 6 | Video | D11 | D8 |
| 7 | Stop/Home | D12 | D10 |
| 8 | Speaker/headphone audio profile | A0 | D13 (ON = headphones) |

Physical front-panel arrangement (the protocol/input numbers remain those in
the table above):

| | | |
|---|---|---|
| Volume Down (D2) | Mute Speakers (A0) | Volume Up (D3) |
| Video (D11) | gap | TV (D7) |
| Contact 2 (D6) | Stop/Home (D12) | Contact 1 (D5) |

The physical Video and TV buttons are blue. The two contact buttons are yellow.
This colour order is cosmetic only and does not change their protocol mappings.

Disconnect USB power while wiring. Connect one contact of each momentary switch
to its input pin and the other contact to GND. Firmware uses `INPUT_PULLUP`, so
no external pull-up is needed and a press reads LOW.

For each ordinary low-current LED, connect its output pin through its own 470 Ω
series resistor to the LED anode; connect the cathode to GND. The lights are
state-controlled ON or OFF, with one shared software-PWM brightness setting.
Do not connect 12 V lamps, LED strips, or high-current arcade lamps directly;
those still require a transistor/MOSFET and suitable supply.

D0/D1 remain reserved for USB serial, D4 for the two RGB pixels, D9 for the
buzzer, A4/A5 for the matrix, and A6/A7 for sensors. This consumes the remaining
general-purpose pins, so later IR hardware will require deliberate pin
reallocation or an added expander.
