#pragma once

#include <Arduino.h>

class PanelController {
 public:
  enum class Action : uint8_t { None, Down, Up, Repeat, Long };
  struct Event { uint8_t index; Action action; };

  void begin();
  void update(unsigned long now);
  bool nextEvent(Event& event);
  bool setLed(uint8_t index, uint8_t value);
  bool setBrightness(uint8_t percent);
  bool setLongPressMilliseconds(unsigned long milliseconds);
  void allLedsOff();
  bool buttonsAvailable() const { return true; }
  bool ledsAvailable() const { return true; }

 private:
  uint8_t rawMask_ = 0xFF;
  uint8_t stableMask_ = 0xFF;
  uint8_t pendingDown_ = 0;
  uint8_t pendingUp_ = 0;
  uint8_t pendingRepeat_ = 0;
  uint8_t pendingLong_ = 0;
  uint8_t longSentMask_ = 0;
  uint8_t ledOnMask_ = 0;
  uint8_t brightnessLevel_ = 16;
  uint8_t pwmPhase_ = 0;
  unsigned long rawChangedAt_[8] = {};
  unsigned long nextRepeatAt_[2] = {};
  unsigned long pressedAt_[8] = {};
  unsigned long lastSampleAt_ = 0;
  unsigned long lastPwmAtMicros_ = 0;
  unsigned long longPressMilliseconds_ = 1500;
};
