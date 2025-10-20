#include "Joystick.h"

typedef enum {
    SYNCED,
    SYNC_START,
    SYNC_1,
    OUT_OF_SYNC
} State_t;

// -----------------------------
// Private prototypes
// -----------------------------
static void populate_report_from_serial(Serial_Input_Packet_t *serialInputPacket, uint8_t *report);
static void send_neutral_report(void);
static void CALLBACK_beforeSend(void);
static void initialize_uart(void);

// -----------------------------
// Static globals
// -----------------------------
static Serial_Input_Packet_t serialInput;
static volatile bool has_new_serial = false;
static State_t state = OUT_OF_SYNC;
static uint16_t millis = 0;

// Neutral fallback report
static const uint8_t neutral_report[8] = {0x00, 0x00, 0x08, 0x80, 0x80, 0x80, 0x80, 0x00};

// -----------------------------
// UART ISR (sync + packet parsing)
// -----------------------------
ISR(USART1_RX_vect) {
    uint8_t b = UDR1;

    if (state == SYNCED) {
        if (serialInput.received_bytes < 8) {
            serialInput.input[serialInput.received_bytes++] = b;
            serialInput.crc8_ccitt = _crc8_ccitt_update(serialInput.crc8_ccitt, b);
        } else {
            // Expecting CRC
            if (serialInput.crc8_ccitt == b) {
                send_byte(RESP_UPDATE_ACK);
                has_new_serial = true;
            } else if (b == COMMAND_SYNC_START) {
                state = SYNC_START;
                send_byte(RESP_SYNC_START);
            } else {
                send_byte(RESP_UPDATE_NACK);
            }
            serialInput.received_bytes = 0;
            serialInput.crc8_ccitt = 0;
        }
    } else if (state == SYNC_START) {
        if (b == COMMAND_SYNC_1) {
            state = SYNC_1;
            send_byte(RESP_SYNC_1);
        } else {
            state = OUT_OF_SYNC;
        }
    } else if (state == SYNC_1) {
        if (b == COMMAND_SYNC_2) {
            state = SYNCED;
            send_byte(RESP_SYNC_OK);
        } else {
            state = OUT_OF_SYNC;
        }
    }

    if (state == OUT_OF_SYNC && b == COMMAND_SYNC_START) {
        state = SYNC_START;
        send_byte(RESP_SYNC_START);
    }
}

// Populate an 8-byte Pokkén input report (with smoothing)
// -----------------------------
// Layout we expect in serialInputPacket->input:
//   [0] buttons_hi   (bits 15..8)
//   [1] buttons_lo   (bits 7..0)
//   [2] hat          (0..7, 8 = neutral)
//   [3] LX           (0..255, center ~0x80)
//   [4] LY           (0..255, center ~0x80)
//   [5] RX           (0..255, center ~0x80)
//   [6] RY           (0..255, center ~0x80)
//   [7] (unused/vendor, ignored here)
// -----------------------------
static void populate_report_from_serial(Serial_Input_Packet_t *serialInputPacket, uint8_t *report)
{
    uint8_t *in = serialInputPacket->input;

    // Buttons (firmware expects hi first, lo second)
    uint16_t buttons = ((uint16_t)in[0] << 8) | in[1];
    report[0] = buttons & 0xFF;
    report[1] = (buttons >> 8) & 0xFF;

    // Hat (0–7 valid, 8 neutral)
    uint8_t hat = (in[2] <= 8) ? in[2] : 8;
    report[2] = hat;

    // Axes
    uint8_t lx = in[3];
    uint8_t ly = in[4];
    uint8_t rx = in[5];
    uint8_t ry = in[6];

    // --- Handle mis-scaled input (rare)
    if (lx <= 0x0F && ly <= 0x0F && rx <= 0x0F && ry <= 0x0F) {
        lx <<= 4; ly <<= 4; rx <<= 4; ry <<= 4;
    }

    // --- Light smoothing (simple average of current and previous)
    static uint8_t prev_lx = 0x80;
    static uint8_t prev_ly = 0x80;
    static uint8_t prev_rx = 0x80;
    static uint8_t prev_ry = 0x80;
    #define SMOOTH(new, prev) ((uint8_t)(((uint16_t)(prev) + (uint16_t)(new)) >> 1))

    lx = SMOOTH(lx, prev_lx);
    ly = SMOOTH(ly, prev_ly);
    rx = SMOOTH(rx, prev_rx);
    ry = SMOOTH(ry, prev_ry);

    prev_lx = lx;
    prev_ly = ly;
    prev_rx = rx;
    prev_ry = ry;

    // --- Write report
    report[3] = lx;
    report[4] = ly;
    report[5] = rx;
    report[6] = ry;
    report[7] = 0x00;
}

// -----------------------------
// Send neutral (idle) input
// -----------------------------
static void send_neutral_report(void) {
    Endpoint_SelectEndpoint(JOYSTICK_IN_EPADDR);
    if (!Endpoint_IsINReady()) return;
    Endpoint_Write_Stream_LE(neutral_report, sizeof(neutral_report), NULL);
    Endpoint_ClearIN();
}

// -----------------------------
// Hardware setup
// -----------------------------
void SetupHardware(void) {
    disable_watchdog();
    clock_prescale_set(clock_div_1);
    USART_Init();
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
        LEDs_SetAllLEDs(LEDMASK_NOT_SYNCED);
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
// Called before each IN report
// -----------------------------
static void CALLBACK_beforeSend(void)
{
    disable_rx_isr();

    if (state == SYNCED) {
        // Increment at each USB frame (approx 1 ms)
        millis++;

        if (millis >= 120) {
            // Haven't received a UART packet in ~120 ms
            LEDs_SetAllLEDs(LEDMASK_PAUSE_EMPTY_BUFFER);
        } else {
            LEDs_SetAllLEDs(LEDMASK_SYNCED);
        }
    } else {
        LEDs_SetAllLEDs(LEDMASK_NOT_SYNCED);
    }

    enable_rx_isr();
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
        while (Endpoint_IsReadWriteAllowed())
            Endpoint_Read_8();
        Endpoint_ClearOUT();
    }

    // === IN endpoint (from device → Switch)
    Endpoint_SelectEndpoint(JOYSTICK_IN_EPADDR);
    if (Endpoint_IsINReady()) {

        uint8_t report[8];
        static uint8_t last_report[8] = {0x00, 0x00, 0x08, 0x80, 0x80, 0x80, 0x80, 0x00};
        static uint32_t packet_counter = 0;
        static uint32_t dropped_counter = 0;

        disable_rx_isr();
        bool new_serial = has_new_serial;
        if (new_serial) {
            has_new_serial = false;
            packet_counter++;
            millis = 0;
        }
        Serial_Input_Packet_t local_copy;
        if (new_serial)
            memcpy(&local_copy, &serialInput, sizeof(local_copy));
        enable_rx_isr();

        if (new_serial) {
            populate_report_from_serial(&local_copy, report);
            memcpy(last_report, report, 8);  // Save for reuse
        } else {
            // Reuse last valid input instead of going neutral (reduces stutter)
            memcpy(report, last_report, 8);
            dropped_counter++;
        }

        Endpoint_Write_Stream_LE(report, sizeof(report), NULL);
        Endpoint_ClearIN();

        // --- Optional diagnostic (compile-time only)
        #ifdef DEBUG_UART_STATS
        if ((packet_counter % 1000) == 0) {
            printf("UART ok=%lu dropped=%lu\n",
                   (unsigned long)packet_counter,
                   (unsigned long)dropped_counter);
        }
        #endif
    }
}

// -----------------------------
// Entry point
// -----------------------------
int main(void) {
    memset(&serialInput, 0, sizeof(serialInput));
    SetupHardware();
    GlobalInterruptEnable();

    for (;;) {
        HID_Task();
        USB_USBTask();
    }
}
