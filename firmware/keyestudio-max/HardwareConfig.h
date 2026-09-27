#pragma once

#include <Arduino.h>

namespace Hardware {
constexpr uint8_t RedLedPin = 8;
constexpr uint8_t YellowLedPin = 10;
constexpr uint8_t GreenLedPin = 13;
constexpr uint8_t RgbPin = 4;
constexpr uint8_t RgbCount = 2;
constexpr uint8_t BuzzerPin = 9;
constexpr uint8_t AmbientLightPin = A6;
constexpr uint8_t MicrophonePin = A7;
constexpr uint8_t MatrixAddress = 0x70;
constexpr uint8_t PanelButtonCount = 8;
constexpr uint8_t PanelLedCount = 6;
constexpr uint8_t PanelButtonPins[PanelButtonCount] = {2, 3, 5, 6, 7, 11, 12, A0};
constexpr uint8_t PanelLedPins[PanelLedCount] = {A1, A2, A3, 8, 10, 13};

constexpr unsigned long SerialBaud = 115200;
constexpr unsigned long ControllerTimeoutMs = 5000;
constexpr unsigned long ButtonDebounceMs = 30;
constexpr unsigned long ButtonLongPressMs = 1500;
constexpr unsigned long ButtonRepeatDelayMs = 500;
constexpr unsigned long ButtonRepeatIntervalMs = 180;
constexpr unsigned long SensorSampleMs = 100;
constexpr unsigned long DefaultTelemetryMs = 1000;
}  // namespace Hardware
