#include "OutputManager.h"

#include <string.h>

#include "HardwareConfig.h"
#include "MatrixDisplay.h"

namespace {
struct ToneStep {
  uint16_t frequency;
  uint8_t durationMs;
  uint8_t gapMs;
};

// Brief earcons: descending means less, ascending means more. The long-press
// diagnostic cue is intentionally unmistakable but still under 120 ms total.
const ToneStep kVolumeDown[] = {{1047, 22, 10}, {784, 28, 0}};
const ToneStep kVolumeUp[] = {{784, 22, 10}, {1047, 28, 0}};
const ToneStep kConfirm[] = {{1568, 25, 0}};
const ToneStep kDiagnostic[] = {{1175, 18, 8}, {1568, 18, 8}, {2093, 24, 0}};
}

OutputManager::OutputManager(MatrixDisplay& matrix)
    : matrix_(matrix), pixels_(Hardware::RgbCount, Hardware::RgbPin, NEO_GRB + NEO_KHZ800) {}

void OutputManager::begin() {
  pinMode(Hardware::BuzzerPin, OUTPUT);
  pixels_.begin();
  pixels_.setBrightness(64);
  pixels_.clear();
  pixels_.show();
  setConnected(false);
}

void OutputManager::setConnected(bool connected) {
  if (connected_ == connected) return;
  connected_ = connected;
  if (connected) {
    for (uint8_t i = 0; i < Hardware::RgbCount; ++i) pixels_.setPixelColor(i, pixels_.Color(0, 180, 0));
    pixels_.show();
    matrix_.showIcon("OK");
  } else {
    matrix_.showIcon("X");
  }
}

void OutputManager::noteActivity() {}

void OutputManager::update(unsigned long now) {
  if (feedback_ != FeedbackSound::None && static_cast<long>(now - feedbackNextAt_) >= 0) {
    startFeedbackStep(now);
  }
  if (!connected_ && now - lastPulseAt_ >= 50) {
    lastPulseAt_ = now;
    int next = static_cast<int>(pulseValue_) + pulseDirection_;
    if (next >= 80 || next <= 5) { pulseDirection_ = -pulseDirection_; next += pulseDirection_; }
    pulseValue_ = static_cast<uint8_t>(next);
    for (uint8_t i = 0; i < Hardware::RgbCount; ++i) pixels_.setPixelColor(i, pixels_.Color(0, 0, pulseValue_));
    pixels_.show();
  }
}

bool OutputManager::setLed(const char* name, bool on) {
  (void)name;
  (void)on;
  return false;
}

bool OutputManager::setRgb(uint8_t index, uint8_t red, uint8_t green, uint8_t blue) {
  if (index >= Hardware::RgbCount || !connected_) return false;
  pixels_.setPixelColor(index, pixels_.Color(red, green, blue));
  pixels_.show();
  return true;
}

bool OutputManager::buzz(uint16_t frequency, uint16_t durationMs) {
  if (frequency < 100 || frequency > 5000 || durationMs == 0 || durationMs > 250) return false;
  feedback_ = FeedbackSound::None;
  tone(Hardware::BuzzerPin, frequency, durationMs);
  return true;
}

void OutputManager::playFeedback(FeedbackSound sound) {
  feedback_ = sound;
  feedbackStep_ = 0;
  noTone(Hardware::BuzzerPin);
  startFeedbackStep(millis());
}

void OutputManager::startFeedbackStep(unsigned long now) {
  const ToneStep* steps = nullptr;
  uint8_t count = 0;
  if (feedback_ == FeedbackSound::VolumeDown) {
    steps = kVolumeDown;
    count = sizeof(kVolumeDown) / sizeof(kVolumeDown[0]);
  } else if (feedback_ == FeedbackSound::VolumeUp) {
    steps = kVolumeUp;
    count = sizeof(kVolumeUp) / sizeof(kVolumeUp[0]);
  } else if (feedback_ == FeedbackSound::Confirm) {
    steps = kConfirm;
    count = sizeof(kConfirm) / sizeof(kConfirm[0]);
  } else if (feedback_ == FeedbackSound::Diagnostic) {
    steps = kDiagnostic;
    count = sizeof(kDiagnostic) / sizeof(kDiagnostic[0]);
  }

  if (!steps || feedbackStep_ >= count) {
    feedback_ = FeedbackSound::None;
    return;
  }

  const ToneStep& step = steps[feedbackStep_++];
  tone(Hardware::BuzzerPin, step.frequency, step.durationMs);
  feedbackNextAt_ = now + step.durationMs + step.gapMs;
}
