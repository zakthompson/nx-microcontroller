#ifndef SHARED_SWITCH_RESPONSES_H
#define SHARED_SWITCH_RESPONSES_H

#include "datatypes.h"
#include "emulated_spi.h"
#include <stdint.h>
#include <stdbool.h>
#include <string.h>

// Maximum size of a response buffer (matches USB endpoint size)
#define SWITCH_RESPONSE_MAX_SIZE 64

/*
 * Initialize the Switch response manager.
 * Call this once during firmware startup.
 */
void switch_responses_init(void);

/*
 * Process an OUT report received from the Switch.
 * This handles subcommands and prepares appropriate responses.
 *
 * Args:
 *   data: Pointer to the received OUT report data
 *   length: Length of the received data
 */
void switch_process_out_report(const uint8_t* data, uint8_t length);

/*
 * Check if a response is pending and retrieve it.
 * Call this before sending each IN report to the Switch.
 *
 * Args:
 *   buffer: Output buffer to fill with response data
 *   buffer_size: Size of the output buffer (should be at least SWITCH_RESPONSE_MAX_SIZE)
 *   current_report: Current controller state (used if no subcommand response is pending)
 *
 * Returns:
 *   Number of bytes written to buffer (0 if no response available)
 */
uint8_t switch_get_in_report(uint8_t* buffer, uint8_t buffer_size, const uint8_t* current_report);

#endif // SHARED_SWITCH_RESPONSES_H
