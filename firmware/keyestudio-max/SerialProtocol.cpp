#include "SerialProtocol.h"

#include <stdlib.h>
#include <string.h>

#include "HardwareConfig.h"
#include "MatrixDisplay.h"
#include "OutputManager.h"
#include "PanelController.h"

namespace {
constexpr char kFirmwareVersion[] = "0.2.2";
}

SerialProtocol::SerialProtocol(OutputManager& outputs, MatrixDisplay& matrix, PanelController& panel)
    : outputs_(outputs), matrix_(matrix), panel_(panel) {}

void SerialProtocol::begin() {
  Serial.begin(Hardware::SerialBaud);
  Serial.print(F("DCB/1 READY board=KS0501 fw="));
  Serial.println(kFirmwareVersion);
}

void SerialProtocol::update(unsigned long now) {
  while (Serial.available() > 0) {
    const char value = static_cast<char>(Serial.read());
    if (value == '\n') {
      if (discarding_) {
        Serial.println(F("ERR - LINE_TOO_LONG"));
        discarding_ = false;
      } else if (length_ > 0) {
        processLine(now);
      }
      length_ = 0;
    } else if (value != '\r') {
      if (length_ < LineCapacity - 1 && !discarding_) line_[length_++] = value;
      else discarding_ = true;
    }
  }

  if (connected_ && now - lastHostMessageAt_ > Hardware::ControllerTimeoutMs) {
    connected_ = false;
    outputs_.setConnected(false);
    Serial.println(F("EVT LINK TIMEOUT"));
  }
}

void SerialProtocol::processLine(unsigned long now) {
  line_[length_] = '\0';
  outputs_.noteActivity();
  char* save = nullptr;
  char* first = strtok_r(line_, " ", &save);
  if (!first) return;

  if (strcmp(first, "HELLO") == 0) {
    const char* version = strtok_r(nullptr, " ", &save);
    if (!version || strcmp(version, "DCB/1") != 0) {
      Serial.println(F("ERR - PROTOCOL_MISMATCH"));
      return;
    }
    connected_ = true;
    lastHostMessageAt_ = now;
    outputs_.setConnected(true);
    Serial.print(F("HELLO DCB/1 KS0501 "));
    Serial.print(kFirmwareVersion);
    Serial.print(F(" caps=RGB,MATRIX,BUZZ,BTN,PANEL,SENSORS ir=0 matrix="));
    Serial.print(matrix_.detected() ? F("1") : F("0"));
    Serial.print(F(" pb=")); Serial.print(panel_.buttonsAvailable() ? F("1") : F("0"));
    Serial.print(F(" pl=")); Serial.println(panel_.ledsAvailable() ? F("1") : F("0"));
    return;
  }

  if (strcmp(first, "PING") == 0) {
    const char* nonce = strtok_r(nullptr, " ", &save);
    if (!nonce) nonce = "-";
    lastHostMessageAt_ = now;
    Serial.print(F("PONG "));
    Serial.print(nonce);
    Serial.print(F(" uptime="));
    Serial.println(now);
    return;
  }

  if (strcmp(first, "CMD") == 0) {
    if (!connected_) { Serial.println(F("ERR - NOT_CONNECTED")); return; }
    lastHostMessageAt_ = now;
    processCommand(save, now);
    return;
  }

  Serial.println(F("ERR - UNKNOWN_MESSAGE"));
}

void SerialProtocol::processCommand(char* save, unsigned long now) {
  const char* id = strtok_r(nullptr, " ", &save);
  const char* target = strtok_r(nullptr, " ", &save);
  if (!id || !target) { error(id ? id : "-", F("MALFORMED")); return; }

  if (strcmp(target, "LED") == 0) {
    const char* name = strtok_r(nullptr, " ", &save);
    const char* state = strtok_r(nullptr, " ", &save);
    if (!name || !state) { error(id, F("BAD_LED")); return; }
    if ((strcmp(state, "ON") != 0 && strcmp(state, "OFF") != 0) ||
               !outputs_.setLed(name, strcmp(state, "ON") == 0)) {
      error(id, F("BAD_LED")); return;
    }
  } else if (strcmp(target, "RGB") == 0) {
    uint8_t index, red, green, blue;
    const char* i = strtok_r(nullptr, " ", &save); const char* r = strtok_r(nullptr, " ", &save);
    const char* g = strtok_r(nullptr, " ", &save); const char* b = strtok_r(nullptr, " ", &save);
    if (!parseByte(i, index) || !parseByte(r, red) || !parseByte(g, green) || !parseByte(b, blue) ||
        !outputs_.setRgb(index, red, green, blue)) { error(id, F("BAD_RGB")); return; }
  } else if (strcmp(target, "MATRIX") == 0) {
    const char* operation = strtok_r(nullptr, " ", &save);
    if (!operation || !matrix_.detected()) { error(id, F("MATRIX_UNAVAILABLE")); return; }
    if (strcmp(operation, "CLEAR") == 0) matrix_.clear();
    else if (strcmp(operation, "PATTERN") == 0) matrix_.testPattern();
    else if (strcmp(operation, "ICON") == 0) { const char* icon = strtok_r(nullptr, " ", &save); if (!icon) { error(id, F("BAD_MATRIX")); return; } matrix_.showIcon(icon); }
    else if (strcmp(operation, "TEXT") == 0) { const char* text = strtok_r(nullptr, " ", &save); if (!text) { error(id, F("BAD_MATRIX")); return; } matrix_.showText(text); }
    else if (strcmp(operation, "BAR") == 0) { uint8_t value; if (!parseByte(strtok_r(nullptr, " ", &save), value) || value > 100) { error(id, F("BAD_MATRIX")); return; } matrix_.showBar(value); }
    else { error(id, F("BAD_MATRIX")); return; }
  } else if (strcmp(target, "BUZZ") == 0) {
    unsigned long frequency, duration;
    if (!parseUnsigned(strtok_r(nullptr, " ", &save), 5000, frequency) ||
        !parseUnsigned(strtok_r(nullptr, " ", &save), 250, duration) ||
        !outputs_.buzz(frequency, duration)) { error(id, F("BAD_BUZZ")); return; }
  } else if (strcmp(target, "PANEL") == 0) {
    const char* item = strtok_r(nullptr, " ", &save);
    if (!item) { error(id, F("BAD_PANEL")); return; }
    if (strcmp(item, "LED") == 0) {
      uint8_t index;
      const char* indexText = strtok_r(nullptr, " ", &save);
      const char* operation = strtok_r(nullptr, " ", &save);
      if (!parseByte(indexText, index) || !operation ||
          (strcmp(operation, "ON") != 0 && strcmp(operation, "OFF") != 0) ||
          !panel_.setLed(index, strcmp(operation, "ON") == 0 ? 1 : 0)) {
        error(id, F("PANEL_LED_UNAVAILABLE")); return;
      }
    } else if (strcmp(item, "ALL") == 0) {
      const char* operation = strtok_r(nullptr, " ", &save);
      if (!operation || strcmp(operation, "OFF") != 0 || !panel_.ledsAvailable()) {
        error(id, F("PANEL_LED_UNAVAILABLE")); return;
      }
      panel_.allLedsOff();
    } else if (strcmp(item, "BRIGHTNESS") == 0) {
      uint8_t percent;
      if (!parseByte(strtok_r(nullptr, " ", &save), percent) || !panel_.setBrightness(percent)) {
        error(id, F("BAD_PANEL")); return;
      }
    } else if (strcmp(item, "LONGPRESS") == 0) {
      unsigned long milliseconds;
      if (!parseUnsigned(strtok_r(nullptr, " ", &save), 10000, milliseconds) ||
          !panel_.setLongPressMilliseconds(milliseconds)) {
        error(id, F("BAD_PANEL")); return;
      }
    } else { error(id, F("BAD_PANEL")); return; }
  } else if (strcmp(target, "GET") == 0) {
    const char* item = strtok_r(nullptr, " ", &save);
    if (!item || strcmp(item, "STATUS") != 0) { error(id, F("BAD_GET")); return; }
    Serial.print(F("DATA ")); Serial.print(id); Serial.print(F(" STATUS uptime=")); Serial.print(now);
    Serial.print(F(" matrix=")); Serial.print(matrix_.detected() ? F("1") : F("0"));
    Serial.print(F(" pb=")); Serial.print(panel_.buttonsAvailable() ? F("1") : F("0"));
    Serial.print(F(" pl=")); Serial.println(panel_.ledsAvailable() ? F("1") : F("0"));
    return;
  } else if (strcmp(target, "TELEMETRY") == 0) {
    const char* value = strtok_r(nullptr, " ", &save);
    if (value && strcmp(value, "OFF") == 0) telemetryIntervalMs_ = 0;
    else { unsigned long interval; if (!parseUnsigned(value, 10000, interval) || interval < 250) { error(id, F("BAD_TELEMETRY")); return; } telemetryIntervalMs_ = interval; }
  } else {
    error(id, F("UNKNOWN_COMMAND"));
    return;
  }
  acknowledge(id);
}

void SerialProtocol::acknowledge(const char* id) { Serial.print(F("ACK ")); Serial.println(id); }
void SerialProtocol::error(const char* id, const __FlashStringHelper* reason) {
  Serial.print(F("ERR ")); Serial.print(id); Serial.print(' '); Serial.println(reason);
}

bool SerialProtocol::parseUnsigned(const char* text, unsigned long maximum, unsigned long& value) {
  if (!text || !*text) return false;
  char* end = nullptr;
  const unsigned long parsed = strtoul(text, &end, 10);
  if (*end != '\0' || parsed > maximum) return false;
  value = parsed;
  return true;
}

bool SerialProtocol::parseByte(const char* text, uint8_t& value) {
  unsigned long parsed;
  if (!parseUnsigned(text, 255, parsed)) return false;
  value = static_cast<uint8_t>(parsed);
  return true;
}

void SerialProtocol::sendButton(const __FlashStringHelper* button, const __FlashStringHelper* action, unsigned long now) {
  Serial.print(F("EVT BTN ")); Serial.print(button); Serial.print(' '); Serial.print(action);
  Serial.print(F(" uptime=")); Serial.println(now);
}

void SerialProtocol::sendTelemetry(uint16_t light, uint16_t microphone, unsigned long now) {
  if (!connected_ || telemetryIntervalMs_ == 0) return;
  Serial.print(F("TEL SENSORS light=")); Serial.print(light); Serial.print(F(" mic=")); Serial.print(microphone);
  Serial.print(F(" uptime=")); Serial.println(now);
}

void SerialProtocol::sendPanelButton(uint8_t index, const __FlashStringHelper* action, unsigned long now) {
  Serial.print(F("EVT BTN PANEL_")); Serial.print(index + 1); Serial.print(' '); Serial.print(action);
  Serial.print(F(" uptime=")); Serial.println(now);
}
