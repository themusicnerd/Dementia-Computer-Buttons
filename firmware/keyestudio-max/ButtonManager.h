#pragma once

#include <Arduino.h>

class DebouncedButton {
 public:
  enum class Event : uint8_t { None, Down, Up, Repeat, LongPress };

  explicit DebouncedButton(uint8_t pin, bool repeatEnabled = false);
  void begin();
  Event update(unsigned long now);
  bool isPressed() const { return stablePressed_; }

 private:
  uint8_t pin_;
  bool repeatEnabled_;
  bool rawPressed_ = false;
  bool stablePressed_ = false;
  bool longReported_ = false;
  unsigned long rawChangedAt_ = 0;
  unsigned long pressedAt_ = 0;
  unsigned long nextRepeatAt_ = 0;
};
