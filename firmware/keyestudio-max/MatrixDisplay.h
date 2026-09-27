#pragma once

#include <Arduino.h>

class MatrixDisplay {
 public:
  bool begin();
  void clear();
  void testPattern();
  void showIcon(const char* name);
  void showText(const char* text);
  void showBar(uint8_t percent);
  void update(unsigned long now, bool connected);
  void setBrightness(uint8_t brightness);
  bool detected() const { return detected_; }

 private:
  uint16_t rows_[8] = {};
  bool detected_ = false;
  unsigned long temporaryUntil_ = 0;

  void drawPixel(uint8_t x, uint8_t y, bool on = true);
  void drawGlyph(char value, uint8_t xOffset);
  void flush();
};
