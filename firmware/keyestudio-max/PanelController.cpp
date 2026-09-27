#include "PanelController.h"

#include "HardwareConfig.h"

namespace {
constexpr unsigned long kSampleIntervalMs = 5;
constexpr unsigned long kPwmStepMicros = 500;
}

void PanelController::begin() {
  rawMask_ = stableMask_ = 0;
  for (uint8_t index = 0; index < Hardware::PanelButtonCount; ++index) {
    pinMode(Hardware::PanelButtonPins[index], INPUT_PULLUP);
    if (digitalRead(Hardware::PanelButtonPins[index]) == HIGH) rawMask_ |= static_cast<uint8_t>(1U << index);
  }
  stableMask_ = rawMask_;
  for (uint8_t index = 0; index < Hardware::PanelLedCount; ++index) {
    pinMode(Hardware::PanelLedPins[index], OUTPUT);
    digitalWrite(Hardware::PanelLedPins[index], LOW);
  }
}

void PanelController::update(unsigned long now) {
  const unsigned long nowMicros = micros();
  if (nowMicros - lastPwmAtMicros_ >= kPwmStepMicros) {
    lastPwmAtMicros_ = nowMicros;
    pwmPhase_ = static_cast<uint8_t>((pwmPhase_ + 1) & 0x0F);
    for (uint8_t index = 0; index < Hardware::PanelLedCount; ++index) {
      const bool enabled = ledOnMask_ & static_cast<uint8_t>(1U << index);
      digitalWrite(Hardware::PanelLedPins[index], enabled && pwmPhase_ < brightnessLevel_ ? HIGH : LOW);
    }
  }
  if (now - lastSampleAt_ < kSampleIntervalMs) return;
  lastSampleAt_ = now;
  uint8_t sample = 0;
  for (uint8_t index = 0; index < Hardware::PanelButtonCount; ++index) {
    if (digitalRead(Hardware::PanelButtonPins[index]) == HIGH) sample |= static_cast<uint8_t>(1U << index);
  }

  for (uint8_t index = 0; index < Hardware::PanelButtonCount; ++index) {
    const uint8_t bit = static_cast<uint8_t>(1U << index);
    const bool sampleHigh = sample & bit;
    const bool rawHigh = rawMask_ & bit;
    if (sampleHigh != rawHigh) {
      if (sampleHigh) rawMask_ |= bit; else rawMask_ &= static_cast<uint8_t>(~bit);
      rawChangedAt_[index] = now;
    }

    const bool stableHigh = stableMask_ & bit;
    const bool currentRawHigh = rawMask_ & bit;
    if (stableHigh != currentRawHigh && now - rawChangedAt_[index] >= Hardware::ButtonDebounceMs) {
      if (currentRawHigh) {
        stableMask_ |= bit;
        pendingUp_ |= bit;
      } else {
        stableMask_ &= static_cast<uint8_t>(~bit);
        pendingDown_ |= bit;
        pressedAt_[index] = now;
        longSentMask_ &= static_cast<uint8_t>(~bit);
        if (index < 2) nextRepeatAt_[index] = now + Hardware::ButtonRepeatDelayMs;
      }
    }

    if (index < 2 && !(stableMask_ & bit) && static_cast<long>(now - nextRepeatAt_[index]) >= 0) {
      pendingRepeat_ |= bit;
      nextRepeatAt_[index] += Hardware::ButtonRepeatIntervalMs;
    }
    if (index >= 2 && !(stableMask_ & bit) && !(longSentMask_ & bit) &&
        now - pressedAt_[index] >= Hardware::ButtonLongPressMs) {
      longSentMask_ |= bit;
      pendingLong_ |= bit;
    }
  }
}

bool PanelController::nextEvent(Event& event) {
  uint8_t* pending = pendingDown_ ? &pendingDown_ : (pendingUp_ ? &pendingUp_
      : (pendingLong_ ? &pendingLong_ : (pendingRepeat_ ? &pendingRepeat_ : nullptr)));
  if (!pending) return false;
  for (uint8_t index = 0; index < Hardware::PanelButtonCount; ++index) {
    const uint8_t bit = static_cast<uint8_t>(1U << index);
    if (!(*pending & bit)) continue;
    *pending &= static_cast<uint8_t>(~bit);
    event.index = index;
    event.action = pending == &pendingDown_ ? Action::Down : (pending == &pendingUp_ ? Action::Up
        : (pending == &pendingLong_ ? Action::Long : Action::Repeat));
    return true;
  }
  return false;
}

bool PanelController::setLed(uint8_t index, uint8_t value) {
  if (index >= Hardware::PanelLedCount) return false;
  const uint8_t bit = static_cast<uint8_t>(1U << index);
  if (value > 0) ledOnMask_ |= bit; else ledOnMask_ &= static_cast<uint8_t>(~bit);
  return true;
}

bool PanelController::setBrightness(uint8_t percent) {
  if (percent > 100) return false;
  brightnessLevel_ = percent == 0 ? 0 : static_cast<uint8_t>((percent * 16UL + 99UL) / 100UL);
  return true;
}

void PanelController::allLedsOff() {
  for (uint8_t index = 0; index < Hardware::PanelLedCount; ++index) setLed(index, 0);
}
