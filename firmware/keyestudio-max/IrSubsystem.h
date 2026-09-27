#pragma once

// Architectural seam for a future external IR receiver/transmitter. No IR
// behavior is exposed until hardware is fitted and Arduino-IRremote is added.
class IrSubsystem {
 public:
  void begin() {}
  void update() {}
  bool available() const { return false; }
};
