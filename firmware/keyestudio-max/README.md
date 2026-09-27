# KS0501 firmware

Target: `arduino:avr:uno`, 115200 baud. Keep A4/A5 right/ON for the matrix. Use
D2/D3 right/ON for onboard-button testing and left/OFF for the external panel.

Build and upload from the repository root:

```powershell
arduino-cli compile --fqbn arduino:avr:uno firmware/keyestudio-max
arduino-cli upload --fqbn arduino:avr:uno --port COM3 firmware/keyestudio-max
```

`COM3` is an example only; use `arduino-cli board list` to locate the current
CP210x port. The Windows application discovers the controller by handshake.
