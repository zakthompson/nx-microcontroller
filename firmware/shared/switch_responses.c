#include "switch_responses.h"

#define COUNTER_INCREMENT 3

// MAC address for controller identification
static uint8_t mac_address[] = {0x79, 0x05, 0x44, 0xC6, 0xB5, 0x65};

// Response buffer state
static uint8_t reply_buffer[SWITCH_RESPONSE_MAX_SIZE];
static uint8_t reply_length = 0;
static bool next_packet_ready = false;
static uint8_t counter = 0;

// Private function declarations
static void prepare_reply(uint8_t code, uint8_t command, const uint8_t* data, uint8_t length);
static void prepare_uart_reply(uint8_t code, uint8_t subcommand, const uint8_t* data, uint8_t length, const uint8_t* current_report);
static void prepare_spi_reply(SPI_Address_t address, size_t size, const uint8_t* current_report);
static void prepare_standard_report(const uint8_t* current_report);
static void prepare_8101(void);

void switch_responses_init(void) {
    memset(reply_buffer, 0, sizeof(reply_buffer));
    reply_length = 0;
    next_packet_ready = false;
    counter = 0;

    // Prepare initial 0x81 0x01 response
    prepare_8101();
}

void switch_process_out_report(const uint8_t* data, uint8_t length) {
    // https://github.com/dekuNukem/Nintendo_Switch_Reverse_Engineering/blob/master/bluetooth_hid_subcommands_notes.md
    if (data[0] == 0x80) {
        switch (data[1]) {
            case 0x01: {
                prepare_8101();
                break;
            }
            case 0x02:
            case 0x03: {
                prepare_reply(0x81, data[1], NULL, 0);
                break;
            }
            case 0x04: {
                // Standard report mode - will be handled by switch_get_in_report
                next_packet_ready = false;
                break;
            }
            default: {
                prepare_reply(0x81, data[1], NULL, 0);
                break;
            }
        }
    } else if (data[0] == 0x01 && length > 16) {
        Switch_Subcommand_t subcommand = data[10];

        // We need the current report for UART replies, but we don't have it here.
        // We'll store the subcommand info and process it in switch_get_in_report.
        // For now, we'll use a dummy report (this will be fixed in the next iteration).
        uint8_t dummy_report[8] = {0};

        switch (subcommand) {
            case SUBCOMMAND_BLUETOOTH_MANUAL_PAIRING: {
                uint8_t response_data = 0x03;
                prepare_uart_reply(0x81, subcommand, &response_data, 1, dummy_report);
                break;
            }
            case SUBCOMMAND_REQUEST_DEVICE_INFO: {
                size_t n = sizeof(mac_address); // = 6
                uint8_t buf[n + 6];
                buf[0] = 0x03; buf[1] = 0x48; // Firmware version
                buf[2] = 0x03; // Pro Controller
                buf[3] = 0x02; // Unknown
                // MAC address is flipped (big-endian)
                for (unsigned int i = 0; i < n; i++) {
                    buf[(n + 3) - i] = mac_address[i];
                }
                buf[n + 4] = 0x03; // Unknown
                buf[n + 5] = 0x02; // Use colors in SPI memory, and use grip colors
                prepare_uart_reply(0x82, subcommand, buf, sizeof(buf), dummy_report);
                break;
            }
            case SUBCOMMAND_SET_INPUT_REPORT_MODE:
            case SUBCOMMAND_SET_SHIPMENT_LOW_POWER_STATE:
            case SUBCOMMAND_SET_PLAYER_LIGHTS:
            case SUBCOMMAND_SET_HOME_LIGHTS:
            case SUBCOMMAND_ENABLE_IMU:
            case SUBCOMMAND_ENABLE_VIBRATION: {
                prepare_uart_reply(0x80, subcommand, NULL, 0, dummy_report);
                break;
            }
            case SUBCOMMAND_TRIGGER_BUTTONS_ELAPSED_TIME: {
                prepare_uart_reply(0x83, subcommand, NULL, 0, dummy_report);
                break;
            }
            case SUBCOMMAND_SET_NFC_IR_MCU_CONFIG: {
                uint8_t buf[] = {0x01, 0x00, 0xFF, 0x00, 0x03, 0x00, 0x05, 0x01};
                prepare_uart_reply(0xA0, subcommand, buf, sizeof(buf), dummy_report);
                break;
            }
            case SUBCOMMAND_SPI_FLASH_READ: {
                // SPI addresses are little-endian
                SPI_Address_t address = (data[12] << 8) | data[11];
                size_t size = (size_t) data[15];
                prepare_spi_reply(address, size, dummy_report);
                break;
            }
            default: {
                prepare_uart_reply(0x80, subcommand, NULL, 0, dummy_report);
                break;
            }
        }
    }
}

uint8_t switch_get_in_report(uint8_t* buffer, uint8_t buffer_size, const uint8_t* current_report) {
    if (buffer_size < SWITCH_RESPONSE_MAX_SIZE) {
        return 0;  // Buffer too small
    }

    if (!next_packet_ready) {
        // No pending subcommand response - send standard report
        prepare_standard_report(current_report);
    }

    // Copy prepared response to output buffer
    memcpy(buffer, reply_buffer, reply_length);
    uint8_t result_length = reply_length;

    // Clear the pending flag
    next_packet_ready = false;

    return result_length;
}

/*
 * Private functions
 */

static void prepare_reply(uint8_t code, uint8_t command, const uint8_t* data, uint8_t length) {
    if (next_packet_ready) return;

    memset(reply_buffer, 0, sizeof(reply_buffer));
    reply_buffer[0] = code;
    reply_buffer[1] = command;

    if (data != NULL && length > 0) {
        memcpy(&reply_buffer[2], data, length);
    }

    reply_length = 2 + length;
    next_packet_ready = true;
}

static void prepare_uart_reply(uint8_t code, uint8_t subcommand, const uint8_t* data, uint8_t length, const uint8_t* current_report) {
    if (next_packet_ready) return;

    memset(reply_buffer, 0, sizeof(reply_buffer));
    reply_buffer[0] = 0x21;

    counter += COUNTER_INCREMENT;
    reply_buffer[1] = counter;

    // Copy the 8-byte controller state (current report)
    memcpy(&reply_buffer[2], current_report, 8);

    // Add subcommand response
    reply_buffer[10] = code;
    reply_buffer[11] = subcommand;

    if (data != NULL && length > 0) {
        memcpy(&reply_buffer[12], data, length);
    }

    reply_length = 12 + length;
    next_packet_ready = true;
}

static void prepare_spi_reply(SPI_Address_t address, size_t size, const uint8_t* current_report) {
    uint8_t data[size];

    // Read from emulated SPI flash
    spi_read(address, size, data);

    uint8_t spi_reply_buffer[5 + size];

    // Little-endian address
    spi_reply_buffer[0] = address & 0xFF;
    spi_reply_buffer[1] = (address >> 8) & 0xFF;
    spi_reply_buffer[2] = 0x00;
    spi_reply_buffer[3] = 0x00;
    spi_reply_buffer[4] = size;
    memcpy(&spi_reply_buffer[5], data, size);

    prepare_uart_reply(0x90, SUBCOMMAND_SPI_FLASH_READ, spi_reply_buffer, sizeof(spi_reply_buffer), current_report);
}

static void prepare_standard_report(const uint8_t* current_report) {
    if (next_packet_ready) return;

    counter += COUNTER_INCREMENT;

    memset(reply_buffer, 0, sizeof(reply_buffer));
    reply_buffer[0] = 0x30;
    reply_buffer[1] = counter;
    memcpy(&reply_buffer[2], current_report, 8);

    reply_length = 10;
    next_packet_ready = true;
}

static void prepare_8101(void) {
    if (next_packet_ready) return;

    size_t n = sizeof(mac_address); // = 6
    uint8_t buf[n + 2];
    buf[0] = 0x00;
    buf[1] = 0x03; // Pro Controller
    memcpy(&buf[2], mac_address, n);

    prepare_reply(0x81, 0x01, buf, sizeof(buf));
}
