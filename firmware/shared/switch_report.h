#ifndef SHARED_SWITCH_REPORT_H
#define SHARED_SWITCH_REPORT_H

#include <stdint.h>
#include <string.h>

// Neutral report (all buttons released, sticks centered)
static const uint8_t neutral_report[8] = {
    0x00, 0x00,  // Buttons: none pressed
    0x08,        // Hat: centered
    0x80, 0x80,  // Left stick: centered
    0x80, 0x80,  // Right stick: centered
    0x00         // Vendor-specific
};

// B button press report (to activate controller on Switch)
static const uint8_t b_button_report[8] = {
    0x02, 0x00,  // Buttons: B pressed (bit 1 in low byte)
    0x08,        // Hat: centered
    0x80, 0x80,  // Left stick: centered
    0x80, 0x80,  // Right stick: centered
    0x00         // Vendor-specific
};

/*
 * Convert macro packet format to firmware report format.
 * Input: 8-byte macro packet [buttons_hi, buttons_lo, hat, LX, LY, RX, RY, vendor]
 * Output: 8-byte firmware report with bytes swapped and hat validated
 */
static inline void populate_report_from_macro(const uint8_t *in, uint8_t *report) {
    // Buttons - swap byte order from macro format [hi, lo] to firmware format [lo, hi]
    uint16_t buttons = ((uint16_t)in[0] << 8) | in[1];
    report[0] = buttons & 0xFF;          // lo byte
    report[1] = (buttons >> 8) & 0xFF;   // hi byte

    // Hat (validate: must be 0-8)
    report[2] = (in[2] <= 8) ? in[2] : 8;

    // Axes - copy directly
    report[3] = in[3];  // LX
    report[4] = in[4];  // LY
    report[5] = in[5];  // RX
    report[6] = in[6];  // RY
    report[7] = 0x00;   // Vendor-specific byte
}

#endif // SHARED_SWITCH_REPORT_H
