// Temporary first-flash sketch used to prove the Uno-compatible upload path
// and the DCB/1 serial handshake before the complete firmware is introduced.

#include <Arduino.h>
#include <string.h>

namespace {
constexpr unsigned long kBaudRate = 115200;
constexpr char kReadyMessage[] = "DCB/1 READY board=KS0501 fw=0.0.1-test";
constexpr size_t kLineCapacity = 64;

char lineBuffer[kLineCapacity];
size_t lineLength = 0;

void processLine() {
  lineBuffer[lineLength] = '\0';

  if (strcmp(lineBuffer, "HELLO DCB/1") == 0) {
    Serial.println(F("HELLO DCB/1 KS0501 0.0.1-test"));
  } else if (strncmp(lineBuffer, "PING ", 5) == 0) {
    Serial.print(F("PONG "));
    Serial.println(lineBuffer + 5);
  } else if (lineLength > 0) {
    Serial.println(F("ERR UNKNOWN_COMMAND"));
  }

  lineLength = 0;
}
}  // namespace

void setup() {
  Serial.begin(kBaudRate);
  Serial.println(kReadyMessage);
}

void loop() {
  while (Serial.available() > 0) {
    const char value = static_cast<char>(Serial.read());
    if (value == '\n') {
      processLine();
    } else if (value != '\r') {
      if (lineLength < kLineCapacity - 1) {
        lineBuffer[lineLength++] = value;
      } else {
        lineLength = 0;
        Serial.println(F("ERR LINE_TOO_LONG"));
      }
    }
  }
}
