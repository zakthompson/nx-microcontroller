/*
 * Nintendo Switch Pro Controller Emulation for Raspberry Pi Pico
 * Standalone macro playback firmware using TinyUSB
 */

#include <stdlib.h>
#include <stdio.h>
#include <string.h>

#include "pico/stdlib.h"
#include "pico/time.h"
#include "tusb.h"
#include "class/hid/hid_device.h"

// Include platform-generated macro header first (defines MACRO_READ_FRAME)
#ifdef EMBEDDED_MACRO_ENABLED
#include "embedded_macro.h"
#else
// Define empty MACRO_READ_FRAME for when no macro is present
#define MACRO_READ_FRAME(index, dest) memcpy(dest, &embedded_macro_frames[index], sizeof(EmbeddedMacroFrame_t))
#endif

// Include shared code (requires MACRO_READ_FRAME to be defined)
#include "macro_player.h"
#include "switch_report.h"

// LED pin for Pico (onboard LED)
#define PICO_DEFAULT_LED_PIN 25

// Timing
static uint32_t current_millis = 0;
static absolute_time_t last_report_time;

// Initialize hardware
static void init_hardware(void) {
    // Initialize stdlib (clocks, etc.)
    stdio_init_all();

    // Initialize onboard LED
    gpio_init(PICO_DEFAULT_LED_PIN);
    gpio_set_dir(PICO_DEFAULT_LED_PIN, GPIO_OUT);

    // Initialize TinyUSB
    tusb_init();

    // Initialize macro player
    macro_player_init();

    // Initialize timing
    last_report_time = get_absolute_time();
}

// LED control
static void set_led(bool on) {
    gpio_put(PICO_DEFAULT_LED_PIN, on ? 1 : 0);
}

// TinyUSB HID callbacks

// Invoked when received GET_REPORT control request
// Application must fill buffer with report's content and return its length
// Return zero will cause the stack to STALL request
uint16_t tud_hid_get_report_cb(uint8_t instance, uint8_t report_id,
                                hid_report_type_t report_type,
                                uint8_t* buffer, uint16_t reqlen) {
    // Not used for this application
    (void) instance;
    (void) report_id;
    (void) report_type;
    (void) buffer;
    (void) reqlen;

    return 0;
}

// Invoked when received SET_REPORT control request or
// received data on OUT endpoint (Report ID = 0, Type = 0)
void tud_hid_set_report_cb(uint8_t instance, uint8_t report_id,
                            hid_report_type_t report_type,
                            uint8_t const* buffer, uint16_t bufsize) {
    (void) instance;
    (void) report_id;
    (void) report_type;

    // Switch sends OUT reports here (subcommands)
    // For standalone firmware, we ignore these (no Switch protocol handling needed)
    (void) buffer;
    (void) bufsize;
}

// Invoked when sent REPORT successfully to host
// Application can use this to send the next report
// Note: For composite reports, this is only called for the first report
void tud_hid_report_complete_cb(uint8_t instance, uint8_t const* report,
                                 uint16_t len) {
    (void) instance;
    (void) report;
    (void) len;

    // Could trigger next report here if needed
}

// HID task - called periodically to send reports
static void hid_task(void) {
    // Remote wakeup
    if (tud_suspended()) {
        tud_remote_wakeup();
    }

    // Skip if not ready
    if (!tud_hid_ready())
        return;

    // Send report every 8ms (125Hz, matching Switch USB poll rate)
    absolute_time_t now = get_absolute_time();
    int64_t elapsed_us = absolute_time_diff_us(last_report_time, now);

    if (elapsed_us >= 8000) {  // 8ms = 8000us
        last_report_time = now;
        current_millis += 8;

        uint8_t report[8];

        // Get report from macro player
        bool playing = macro_player_get_report(current_millis, report);

#ifdef EMBEDDED_MACRO_ENABLED
        // LED feedback
#if EMBEDDED_MACRO_LOOP
        // Blink when looping
        if (playing) {
            set_led((current_millis / 500) % 2);
        } else {
            set_led(false);
        }
#else
        // Solid when playing
        set_led(playing);
#endif
#else
        // No macro - LED off
        set_led(false);
#endif

        // Send report to Switch
        tud_hid_report(0, report, sizeof(report));
    }
}

// Main entry point
int main(void) {
    init_hardware();

    while (true) {
        // TinyUSB device task
        tud_task();

        // HID report task
        hid_task();
    }

    return 0;
}
