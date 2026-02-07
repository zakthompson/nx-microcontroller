/*
 * Nintendo Switch Pro Controller Emulation for ESP32-S3
 * Standalone macro playback firmware using TinyUSB
 */

#include <stdlib.h>
#include <stdio.h>
#include <string.h>

#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "esp_log.h"
#include "esp_timer.h"
#include "driver/gpio.h"
#include "tinyusb.h"
#include "tusb.h"
#include "class/hid/hid_device.h"
#include "usb_descriptors.h"

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

static const char *TAG = "NX_CONTROLLER";

// LED GPIO (onboard LED on ESP32-S3-DevKitC-1)
#define LED_GPIO GPIO_NUM_48

// Timing
static uint32_t current_millis = 0;
static int64_t last_report_time_us = 0;

// Initialize GPIO for LED
static void init_led(void) {
    gpio_config_t io_conf = {
        .pin_bit_mask = (1ULL << LED_GPIO),
        .mode = GPIO_MODE_OUTPUT,
        .pull_up_en = GPIO_PULLUP_DISABLE,
        .pull_down_en = GPIO_PULLDOWN_DISABLE,
        .intr_type = GPIO_INTR_DISABLE,
    };
    gpio_config(&io_conf);
    gpio_set_level(LED_GPIO, 0);
}

// LED control
static void set_led(bool on) {
    gpio_set_level(LED_GPIO, on ? 1 : 0);
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
static void hid_task(void *pvParameters) {
    (void) pvParameters;

    ESP_LOGI(TAG, "HID task started");

    last_report_time_us = esp_timer_get_time();

    while (true) {
        // Wait for USB to be mounted
        if (!tud_mounted()) {
            vTaskDelay(pdMS_TO_TICKS(100));
            continue;
        }

        // Remote wakeup
        if (tud_suspended()) {
            tud_remote_wakeup();
        }

        // Skip if not ready
        if (!tud_hid_ready()) {
            vTaskDelay(pdMS_TO_TICKS(1));
            continue;
        }

        // Send report every 8ms (125Hz, matching Switch USB poll rate)
        int64_t now_us = esp_timer_get_time();
        int64_t elapsed_us = now_us - last_report_time_us;

        if (elapsed_us >= 8000) {  // 8ms = 8000us
            last_report_time_us = now_us;
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

        // Yield to other tasks
        vTaskDelay(pdMS_TO_TICKS(1));
    }
}

// Main application
void app_main(void) {
    ESP_LOGI(TAG, "Nintendo Switch Controller Emulator (ESP32-S3)");
    ESP_LOGI(TAG, "Firmware: Standalone Macro Playback");

    // Initialize LED
    init_led();
    set_led(true);  // LED on during initialization

    // Initialize TinyUSB with our HORI Pokken Controller descriptors
    ESP_LOGI(TAG, "Initializing USB...");
    const tinyusb_config_t tusb_cfg = {
        .device_descriptor = &desc_device,
        .string_descriptor = string_desc_arr,
        .string_descriptor_count = STRING_DESC_COUNT,
        .external_phy = false,
        .configuration_descriptor = desc_configuration,
    };
    ESP_ERROR_CHECK(tinyusb_driver_install(&tusb_cfg));
    ESP_LOGI(TAG, "USB initialized");

    // Initialize macro player
    macro_player_init();
    ESP_LOGI(TAG, "Macro player initialized");

    // Initialization complete
    set_led(false);

    // Create HID task
    xTaskCreate(hid_task, "hid_task", 4096, NULL, 5, NULL);

    ESP_LOGI(TAG, "Setup complete, starting macro playback");
}
