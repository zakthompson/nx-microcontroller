/*
 * JoystickStandalone.c
 *
 * Standalone Nintendo Switch Pro Controller emulation firmware with embedded macro playback.
 * This firmware plays back pre-recorded macros stored in program memory (PROGMEM) without
 * requiring a PC, serial connection, or external controller.
 *
 * The macro is embedded at compile time via embedded_macro.h (generated from macro.json).
 * Build with: make standalone MACRO=path/to/macro.json [LOOP=1]
 */

#include "Joystick.h"
#include <util/delay.h>

// Include embedded macro if present
#ifdef EMBEDDED_MACRO_ENABLED
#include "embedded_macro.h"
#endif

// Macro playback state
#ifdef EMBEDDED_MACRO_ENABLED
static uint32_t macro_playback_index = 0;
static uint32_t macro_start_millis = 0;
static bool macro_started = false;

// Read a macro frame from PROGMEM to RAM
static inline void read_macro_frame(uint32_t index, EmbeddedMacroFrame_t* frame) {
    memcpy_P(frame, &embedded_macro_frames[index], sizeof(EmbeddedMacroFrame_t));
}

// Get the current macro packet based on elapsed time
static void get_macro_packet(uint32_t current_millis, uint8_t* report) {
    if (!macro_started) {
        macro_started = true;
        macro_start_millis = current_millis;
        macro_playback_index = 0;
    }

    uint32_t elapsed_ms = current_millis - macro_start_millis;

    // Read current frame from PROGMEM
    EmbeddedMacroFrame_t current_frame;
    read_macro_frame(macro_playback_index, &current_frame);

    // Check if we're beyond the last frame
    if (macro_playback_index >= EMBEDDED_MACRO_FRAME_COUNT - 1) {
        EmbeddedMacroFrame_t last_frame;
        read_macro_frame(EMBEDDED_MACRO_FRAME_COUNT - 1, &last_frame);

        if (elapsed_ms >= last_frame.timestamp_ms) {
#if EMBEDDED_MACRO_LOOP
            // Loop: restart from beginning
            macro_playback_index = 0;
            macro_start_millis = current_millis;
            elapsed_ms = 0;
            read_macro_frame(0, &current_frame);
#else
            // Play once: hold on last frame
            memcpy(report, last_frame.packet, 8);
            return;
#endif
        }
    }

    // Advance to next frame if we've passed its timestamp
    while (macro_playback_index < EMBEDDED_MACRO_FRAME_COUNT - 1) {
        EmbeddedMacroFrame_t next_frame;
        read_macro_frame(macro_playback_index + 1, &next_frame);

        if (elapsed_ms >= next_frame.timestamp_ms) {
            macro_playback_index++;
            read_macro_frame(macro_playback_index, &current_frame);
        } else {
            break;
        }
    }

    // Copy current frame to report
    memcpy(report, current_frame.packet, 8);
}

// Convert macro packet to firmware format
// This mirrors populate_report_from_serial from Joystick.c
static void populate_report_from_macro(uint8_t *in, uint8_t *report) {
    // Buttons - swap byte order from macro format [hi, lo] to firmware format [lo, hi]
    uint16_t buttons = ((uint16_t)in[0] << 8) | in[1];
    report[0] = buttons & 0xFF;          // lo byte
    report[1] = (buttons >> 8) & 0xFF;   // hi byte

    // Hat
    report[2] = (in[2] <= 8) ? in[2] : 8;

    // Axes - copy directly without smoothing
    // Note: Smoothing is intentionally disabled for timestamp-based playback
    // because it can cause overshooting by preventing quick stops
    report[3] = in[3];  // LX
    report[4] = in[4];  // LY
    report[5] = in[5];  // RX
    report[6] = in[6];  // RY
    report[7] = 0x00;   // Vendor-specific byte
}
#endif

// Neutral report (all buttons released, sticks centered)
static const uint8_t neutral_report[8] = {
    0x00, 0x00,  // Buttons: none pressed
    0x08,        // Hat: centered
    0x80, 0x80,  // Left stick: centered
    0x80, 0x80,  // Right stick: centered
    0x00         // Vendor-specific
};

// -----------------------------
// Hardware setup
// -----------------------------
void SetupHardware(void) {
    disable_watchdog();
    clock_prescale_set(clock_div_1);
    LEDs_Init();
    USB_Init();
}

// -----------------------------
// USB events
// -----------------------------
void EVENT_USB_Device_ConfigurationChanged(void) {
    bool success = true;
    success &= Endpoint_ConfigureEndpoint(JOYSTICK_IN_EPADDR, EP_TYPE_INTERRUPT, JOYSTICK_EPSIZE, 1);
    success &= Endpoint_ConfigureEndpoint(JOYSTICK_OUT_EPADDR, EP_TYPE_INTERRUPT, JOYSTICK_EPSIZE, 1);
    if (!success) {
        LEDs_SetAllLEDs(LEDS_NO_LEDS);
        for(;;);
    }
}

void EVENT_USB_Device_ControlRequest(void) {
    if (USB_ControlRequest.bRequest == HID_REQ_SetIdle &&
        (USB_ControlRequest.bmRequestType == (REQDIR_HOSTTODEVICE | REQTYPE_CLASS | REQREC_INTERFACE))) {
        Endpoint_ClearSETUP();
        Endpoint_ClearStatusStage();
    }
}

// -----------------------------
// Main IN/OUT handling loop
// -----------------------------
void HID_Task(void)
{
    if (USB_DeviceState != DEVICE_STATE_Configured)
        return;

    // === OUT endpoint (from Switch → device)
    Endpoint_SelectEndpoint(JOYSTICK_OUT_EPADDR);
    if (Endpoint_IsOUTReceived()) {
        while (Endpoint_IsReadWriteAllowed()) {
            uint8_t dummy __attribute__((unused)) = Endpoint_Read_8();
        }
        Endpoint_ClearOUT();
    }

    // === IN endpoint (from device → Switch)
    // IMPORTANT: Only send when ready - never block!
    Endpoint_SelectEndpoint(JOYSTICK_IN_EPADDR);
    if (Endpoint_IsINReady()) {
        uint8_t packet[8];
        uint8_t report[8];

        // Track elapsed time (USB endpoint polls at ~125Hz = every 8ms)
        static uint32_t millis = 0;
        static bool startup_delay_done = false;

        // Startup delay: wait ~2 seconds after USB config before starting macro
        // to give the Switch time to fully recognize the controller
        if (!startup_delay_done) {
            if (millis < 2000) {
                millis += 8;
                // Send neutral report during startup
                memcpy(report, neutral_report, 8);
                Endpoint_Write_Stream_LE(report, sizeof(report), NULL);
                Endpoint_ClearIN();
                return;
            }
            startup_delay_done = true;
            millis = 0;  // Reset millis for macro playback
        }

        millis += 8;

#ifdef EMBEDDED_MACRO_ENABLED
        // Get macro packet and convert to firmware format
        get_macro_packet(millis, packet);
        populate_report_from_macro(packet, report);

        // LED feedback
#if EMBEDDED_MACRO_LOOP
        LEDs_SetAllLEDs((millis / 500) % 2 ? LEDS_ALL_LEDS : LEDS_NO_LEDS);
#else
        LEDs_SetAllLEDs(LEDS_ALL_LEDS);
#endif
#else
        // No macro - send neutral
        memcpy(report, neutral_report, 8);
        LEDs_SetAllLEDs(LEDS_NO_LEDS);
#endif

        // Send 8-byte report directly (like original firmware)
        Endpoint_Write_Stream_LE(report, sizeof(report), NULL);
        Endpoint_ClearIN();
    }
}

// -----------------------------
// Entry point
// -----------------------------
int main(void) {
    SetupHardware();
    GlobalInterruptEnable();

#ifdef EMBEDDED_MACRO_ENABLED
    macro_started = false;
    macro_playback_index = 0;
#endif

    for (;;) {
        HID_Task();
        USB_USBTask();
    }
}
