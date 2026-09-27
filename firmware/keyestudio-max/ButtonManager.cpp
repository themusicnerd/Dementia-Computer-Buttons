#include "ButtonManager.h"

#include "HardwareConfig.h"

DebouncedButton::DebouncedButton(uint8_t pin, bool repeatEnabled)
    : pin_(pin), repeatEnabled_(repeatEnabled) {}

void DebouncedButton::begin() {
  pinMode(pin_, INPUT_PULLUP);
  rawPressed_ = digitalRead(pin_) == LOW;
  stablePressed_ = rawPressed_;
  rawChangedAt_ = millis();
  pressedAt_ = rawChangedAt_;
}

DebouncedButton::Event DebouncedButton::update(unsigned long now) {
  const bool pressed = digitalRead(pin_) == LOW;
  if (pressed != rawPressed_) {
    rawPressed_ = pressed;
    rawChangedAt_ = now;
  }

  if (rawPressed_ != stablePressed_ && now - rawChangedAt_ >= Hardware::ButtonDebounceMs) {
    stablePressed_ = rawPressed_;
    if (stablePressed_) {
      pressedAt_ = now;
      nextRepeatAt_ = now + Hardware::ButtonRepeatDelayMs;
      longReported_ = false;
      return Event::Down;
    }
    return Event::Up;
  }

  if (stablePressed_ && repeatEnabled_ && static_cast<long>(now - nextRepeatAt_) >= 0) {
    nextRepeatAt_ += Hardware::ButtonRepeatIntervalMs;
    return Event::Repeat;
  }

  if (stablePressed_ && !repeatEnabled_ && !longReported_ && now - pressedAt_ >= Hardware::ButtonLongPressMs) {
    longReported_ = true;
    return Event::LongPress;
  }

  return Event::None;
}
