#include "MatrixDisplay.h"

#include <Wire.h>
#include <avr/pgmspace.h>

#include "HardwareConfig.h"

namespace {
// Compact 5x7 glyphs used by diagnostics and status. Each byte is one column.
struct Glyph { char value; uint8_t columns[5]; };
const Glyph kGlyphs[] PROGMEM = {
    {'0', {0x3E, 0x51, 0x49, 0x45, 0x3E}}, {'1', {0x00, 0x42, 0x7F, 0x40, 0x00}},
    {'2', {0x42, 0x61, 0x51, 0x49, 0x46}}, {'3', {0x21, 0x41, 0x45, 0x4B, 0x31}},
    {'4', {0x18, 0x14, 0x12, 0x7F, 0x10}}, {'5', {0x27, 0x45, 0x45, 0x45, 0x39}},
    {'6', {0x3C, 0x4A, 0x49, 0x49, 0x30}}, {'7', {0x01, 0x71, 0x09, 0x05, 0x03}},
    {'8', {0x36, 0x49, 0x49, 0x49, 0x36}}, {'9', {0x06, 0x49, 0x49, 0x29, 0x1E}},
    {'A', {0x7E, 0x11, 0x11, 0x11, 0x7E}}, {'B', {0x7F, 0x49, 0x49, 0x49, 0x36}},
    {'C', {0x3E, 0x41, 0x41, 0x41, 0x22}}, {'D', {0x7F, 0x41, 0x41, 0x22, 0x1C}},
    {'E', {0x7F, 0x49, 0x49, 0x49, 0x41}}, {'F', {0x7F, 0x09, 0x09, 0x09, 0x01}},
    {'H', {0x7F, 0x08, 0x08, 0x08, 0x7F}}, {'I', {0x00, 0x41, 0x7F, 0x41, 0x00}},
    {'L', {0x7F, 0x40, 0x40, 0x40, 0x40}}, {'M', {0x7F, 0x02, 0x0C, 0x02, 0x7F}},
    {'N', {0x7F, 0x04, 0x08, 0x10, 0x7F}}, {'O', {0x3E, 0x41, 0x41, 0x41, 0x3E}},
    {'P', {0x7F, 0x09, 0x09, 0x09, 0x06}}, {'R', {0x7F, 0x09, 0x19, 0x29, 0x46}},
    {'S', {0x46, 0x49, 0x49, 0x49, 0x31}}, {'T', {0x01, 0x01, 0x7F, 0x01, 0x01}},
    {'U', {0x3F, 0x40, 0x40, 0x40, 0x3F}}, {'V', {0x1F, 0x20, 0x40, 0x20, 0x1F}},
    {'W', {0x3F, 0x40, 0x38, 0x40, 0x3F}}, {'X', {0x63, 0x14, 0x08, 0x14, 0x63}},
};
constexpr uint8_t kGlyphCount = sizeof(kGlyphs) / sizeof(kGlyphs[0]);
}  // namespace

bool MatrixDisplay::begin() {
  Wire.begin();
  Wire.beginTransmission(Hardware::MatrixAddress);
  detected_ = Wire.endTransmission() == 0;
  if (!detected_) return false;

  Wire.beginTransmission(Hardware::MatrixAddress);
  Wire.write(0x21);  // oscillator on
  Wire.endTransmission();
  Wire.beginTransmission(Hardware::MatrixAddress);
  Wire.write(0x81);  // display on, blink off
  Wire.endTransmission();
  setBrightness(5);
  clear();
  return true;
}

void MatrixDisplay::setBrightness(uint8_t brightness) {
  if (!detected_) return;
  if (brightness > 15) brightness = 15;
  Wire.beginTransmission(Hardware::MatrixAddress);
  Wire.write(0xE0 | brightness);
  Wire.endTransmission();
}

void MatrixDisplay::drawPixel(uint8_t x, uint8_t y, bool on) {
  // The KS0501 is two 8x8 modules arranged horizontally: 16 columns x 8 rows.
  // HT16K33 RAM stores one 16-bit column mask for each of the eight rows.
  if (x > 15 || y > 7) return;
  const uint8_t row = y;
  const uint8_t bit = x;
  if (on) rows_[row] |= static_cast<uint16_t>(1U << bit);
  else rows_[row] &= static_cast<uint16_t>(~(1U << bit));
}

void MatrixDisplay::flush() {
  if (!detected_) return;
  Wire.beginTransmission(Hardware::MatrixAddress);
  Wire.write(0x00);
  for (uint8_t row = 0; row < 8; ++row) {
    Wire.write(rows_[row] & 0xFF);
    Wire.write(rows_[row] >> 8);
  }
  Wire.endTransmission();
}

void MatrixDisplay::clear() {
  temporaryUntil_ = 0;
  memset(rows_, 0, sizeof(rows_));
  flush();
}

void MatrixDisplay::testPattern() {
  temporaryUntil_ = 0;
  for (uint8_t y = 0; y < 8; ++y) {
    for (uint8_t x = 0; x < 16; ++x) drawPixel(x, y, ((x + y) & 1) == 0);
  }
  flush();
}

void MatrixDisplay::drawGlyph(char value, uint8_t xOffset) {
  if (value >= 'a' && value <= 'z') value -= 32;
  for (uint8_t index = 0; index < kGlyphCount; ++index) {
    if (static_cast<char>(pgm_read_byte(&kGlyphs[index].value)) != value) continue;
    for (uint8_t x = 0; x < 5; ++x) {
      const uint8_t column = pgm_read_byte(&kGlyphs[index].columns[x]);
      for (uint8_t y = 0; y < 7; ++y) drawPixel(x + xOffset, y, column & (1U << y));
    }
    return;
  }
}

void MatrixDisplay::showText(const char* text) {
  temporaryUntil_ = 0;
  memset(rows_, 0, sizeof(rows_));
  if (text && text[0] && text[1]) {
    drawGlyph(text[0], 1);
    drawGlyph(text[1], 9);
  } else if (text && text[0]) {
    drawGlyph(text[0], 5);
  }
  flush();
}

void MatrixDisplay::showIcon(const char* name) {
  temporaryUntil_ = 0;
  bool transient = false;
  memset(rows_, 0, sizeof(rows_));
  if (strcmp(name, "OK") == 0) {
    drawPixel(3, 4); drawPixel(4, 5); drawPixel(5, 6);
    for (uint8_t i = 0; i < 7; ++i) drawPixel(5 + i, 6 - i);
  } else if (strcmp(name, "X") == 0) {
    for (uint8_t i = 0; i < 8; ++i) { drawPixel(4 + i, i); drawPixel(11 - i, i); }
  } else {
    drawGlyph(name && name[0] ? name[0] : '?', 5);
    transient = true;
  }
  flush();
  if (transient) temporaryUntil_ = millis() + 1000;
}

void MatrixDisplay::showBar(uint8_t percent) {
  if (percent > 100) percent = 100;
  memset(rows_, 0, sizeof(rows_));
  const uint8_t width = static_cast<uint16_t>(percent) * 16 / 100;
  for (uint8_t x = 0; x < width; ++x) {
    for (uint8_t y = 1; y < 7; ++y) drawPixel(x, y);
  }
  flush();
  temporaryUntil_ = millis() + 1500;
}

void MatrixDisplay::update(unsigned long now, bool connected) {
  if (temporaryUntil_ != 0 && static_cast<long>(now - temporaryUntil_) >= 0) {
    temporaryUntil_ = 0;
    showIcon(connected ? "OK" : "X");
  }
}
