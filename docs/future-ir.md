# Future IR subsystem

No IR receiver or transmitter has been detected or assumed. The existing TX/RX
indicators belong to serial activity. Firmware exposes an inert `IrSubsystem`
seam and reports `ir=0`; Windows exposes `IIRService` but no fake learn/send UI.

## Likely additional hardware

- A 38 kHz demodulating IR receiver module compatible with 5 V logic, such as a
  TSOP38238-class device, with local supply decoupling.
- One or more suitable IR emitter LEDs.
- A transistor or logic-level MOSFET driver, base/gate resistor, LED
  current-limiting resistor and appropriate supply arrangement. Do not drive a
  high-current IR emitter directly from an ATmega328P pin.
- Wiring that keeps the receiver away from the emitter and noisy LED supplies.

Exact parts, currents and pins must be decided from the chosen modules' data
sheets. Available pins and timer use must be reviewed against the buzzer,
NeoPixels and future arcade panel before wiring.

## Planned software flow

1. Windows sends a versioned `IR LEARN <request-id>` command.
2. Firmware starts a bounded learn window using Arduino-IRremote.
3. The user points the original remote at the receiver.
4. Firmware returns decoded protocol/address/command when available, otherwise a
   bounded raw timing representation with carrier metadata.
5. Windows stores that payload under a friendly key such as `TV_POWER` or
   `TV_INPUT` in a backup-friendly configuration file.
6. `IR SEND <friendly-key>` resolves on Windows and sends the stored payload to
   firmware for transmission.

Windows remains the primary store; AVR EEPROM is not the command database.
Protocol messages will need chunking and checksums for raw captures because the
normal firmware line buffer is intentionally small. Learning and transmission
must time out cleanly, preserve heartbeat handling, and report unsupported
protocols without wedging the controller.
