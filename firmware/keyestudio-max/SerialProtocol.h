#pragma once

#include <Arduino.h>

class MatrixDisplay;
class OutputManager;
class PanelController;

class SerialProtocol {
 public:
  SerialProtocol(OutputManager& outputs, MatrixDisplay& matrix, PanelController& panel);
  void begin();
  void update(unsigned long now);
  bool connected() const { return connected_; }
  unsigned long telemetryInterval() const { return telemetryIntervalMs_; }
  void sendButton(const __FlashStringHelper* button, const __FlashStringHelper* action, unsigned long now);
  void sendTelemetry(uint16_t light, uint16_t microphone, unsigned long now);
  void sendPanelButton(uint8_t index, const __FlashStringHelper* action, unsigned long now);

 private:
  static constexpr uint8_t LineCapacity = 96;
  OutputManager& outputs_;
  MatrixDisplay& matrix_;
  PanelController& panel_;
  char line_[LineCapacity];
  uint8_t length_ = 0;
  bool discarding_ = false;
  bool connected_ = false;
  unsigned long lastHostMessageAt_ = 0;
  unsigned long telemetryIntervalMs_ = 1000;

  void processLine(unsigned long now);
  void processCommand(char* save, unsigned long now);
  void acknowledge(const char* id);
  void error(const char* id, const __FlashStringHelper* reason);
  static bool parseByte(const char* text, uint8_t& value);
  static bool parseUnsigned(const char* text, unsigned long maximum, unsigned long& value);
};
