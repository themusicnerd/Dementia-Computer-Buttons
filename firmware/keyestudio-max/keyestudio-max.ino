#include "HardwareConfig.h"
#include "IrSubsystem.h"
#include "MatrixDisplay.h"
#include "OutputManager.h"
#include "PanelController.h"
#include "SerialProtocol.h"

MatrixDisplay matrix;
OutputManager outputs(matrix);
PanelController panel;
SerialProtocol protocol(outputs, matrix, panel);
IrSubsystem ir;

unsigned long lastSensorSampleAt = 0;
unsigned long lastTelemetryAt = 0;
uint16_t ambientLight = 0;
uint16_t microphoneSmoothed = 0;

void setup() {
  const bool matrixAvailable = matrix.begin();
  outputs.begin();
  ir.begin();
  panel.begin();
  protocol.begin();
  if (!matrixAvailable) Serial.println(F("EVT HW MATRIX_UNAVAILABLE"));
  if (!panel.buttonsAvailable()) Serial.println(F("EVT HW PANEL_BUTTONS_UNAVAILABLE"));
  if (!panel.ledsAvailable()) Serial.println(F("EVT HW PANEL_LEDS_UNAVAILABLE"));
}

void loop() {
  const unsigned long now = millis();
  protocol.update(now);
  outputs.update(now);
  matrix.update(now, protocol.connected());
  ir.update();
  panel.update(now);

  PanelController::Event panelEvent;
  while (panel.nextEvent(panelEvent)) {
    const __FlashStringHelper* action = panelEvent.action == PanelController::Action::Down ? F("DOWN")
        : (panelEvent.action == PanelController::Action::Up ? F("UP")
        : (panelEvent.action == PanelController::Action::Long ? F("LONG") : F("REPEAT")));
    protocol.sendPanelButton(panelEvent.index, action, now);
    if (panelEvent.action == PanelController::Action::Down) {
      outputs.playFeedback(panelEvent.index == 0 ? OutputManager::FeedbackSound::VolumeDown
          : (panelEvent.index == 1 ? OutputManager::FeedbackSound::VolumeUp
                                   : OutputManager::FeedbackSound::Confirm));
    } else if (panelEvent.action == PanelController::Action::Long) {
      outputs.playFeedback(OutputManager::FeedbackSound::Diagnostic);
    }
  }

  if (now - lastSensorSampleAt >= Hardware::SensorSampleMs) {
    lastSensorSampleAt = now;
    ambientLight = analogRead(Hardware::AmbientLightPin);
    const uint16_t rawMic = analogRead(Hardware::MicrophonePin);
    microphoneSmoothed = (microphoneSmoothed * 7U + rawMic) / 8U;
  }

  const unsigned long interval = protocol.telemetryInterval();
  if (interval > 0 && now - lastTelemetryAt >= interval) {
    lastTelemetryAt = now;
    protocol.sendTelemetry(ambientLight, microphoneSmoothed, now);
  }
}
