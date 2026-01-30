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

// Include platform-generated macro header first (defines MACRO_READ_FRAME)
#ifdef EMBEDDED_MACRO_ENABLED
#include "embedded_macro.h"
#else
// Define empty MACRO_READ_FRAME for when no macro is present
#define MACRO_READ_FRAME(index, dest) memcpy(dest, &embedded_macro_frames[index], sizeof(EmbeddedMacroFrame_t))
#endif

// Include shared code (requires MACRO_READ_FRAME to be defined)
#include "../shared/macro_player.h"
#include "../shared/switch_report.h"

// Hardware setup
void SetupHardware(void) {
    disable_watchdog();
    clock_prescale_set(clock_div_1);
    LEDs_Init();
    USB_Init();
}

// USB events
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

// Main IN/OUT handling loop
void HID_Task(void)
{
    if (USB_DeviceState != DEVICE_STATE_Configured)
        return;

    // OUT endpoint (from Switch → device)
    // Ignore OUT data for standalone firmware (no subcommand handling needed)
    Endpoint_SelectEndpoint(JOYSTICK_OUT_EPADDR);
    if (Endpoint_IsOUTReceived()) {
        while (Endpoint_IsReadWriteAllowed()) {
            uint8_t dummy __attribute__((unused)) = Endpoint_Read_8();
        }
        Endpoint_ClearOUT();
    }

    // IN endpoint (from device → Switch)
    Endpoint_SelectEndpoint(JOYSTICK_IN_EPADDR);
    if (Endpoint_IsINReady()) {
        uint8_t report[8];

        // Track elapsed time (USB endpoint polls at ~125Hz = every 8ms)
        static uint32_t millis = 0;
        millis += 8;

        // Get report from macro player
        bool playing = macro_player_get_report(millis, report);

#ifdef EMBEDDED_MACRO_ENABLED
        // LED feedback
#if EMBEDDED_MACRO_LOOP
        // Blink when looping
        if (playing) {
            LEDs_SetAllLEDs((millis / 500) % 2 ? LEDS_ALL_LEDS : LEDS_NO_LEDS);
        } else {
            LEDs_SetAllLEDs(LEDS_NO_LEDS);
        }
#else
        // Solid when playing
        LEDs_SetAllLEDs(playing ? LEDS_ALL_LEDS : LEDS_NO_LEDS);
#endif
#else
        // No macro - LED off
        LEDs_SetAllLEDs(LEDS_NO_LEDS);
#endif

        // Send 8-byte report
        Endpoint_Write_Stream_LE(report, sizeof(report), NULL);
        Endpoint_ClearIN();
    }
}

// Entry point
int main(void) {
    SetupHardware();
    GlobalInterruptEnable();

    macro_player_init();

    for (;;) {
        HID_Task();
        USB_USBTask();
    }
}
