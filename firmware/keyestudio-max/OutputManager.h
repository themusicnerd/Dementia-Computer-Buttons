#pragma once

#include <Adafruit_NeoPixel.h>
#include <Arduino.h>

class MatrixDisplay;

class OutputManager {
 public:
  enum class FeedbackSound : uint8_t { None, VolumeDown, VolumeUp, Confirm, Diagnostic };

  explicit OutputManager(MatrixDisplay& matrix);
  void begin();
  void update(unsigned long now);
  void setConnected(bool connected);
  void noteActivity();
  bool setLed(const char* name, bool on);
  bool setRgb(uint8_t index, uint8_t red, uint8_t green, uint8_t blue);
  bool buzz(uint16_t frequency, uint16_t durationMs);
  void playFeedback(FeedbackSound sound);

 private:
  MatrixDisplay& matrix_;
  Adafruit_NeoPixel pixels_;
  // Start opposite to the real boot state so begin()->setConnected(false)
  // always applies every disconnected indicator on first power-up.
  bool connected_ = true;
  unsigned long lastPulseAt_ = 0;
  uint8_t pulseValue_ = 5;
  int8_t pulseDirection_ = 5;
  FeedbackSound feedback_ = FeedbackSound::None;
  uint8_t feedbackStep_ = 0;
  unsigned long feedbackNextAt_ = 0;

  void startFeedbackStep(unsigned long now);
};
