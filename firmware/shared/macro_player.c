#include "switch_report.h"
#include "macro_player.h"

// Include the platform-generated macro header if present
// This header defines EMBEDDED_MACRO_ENABLED, EMBEDDED_MACRO_LOOP, EMBEDDED_MACRO_FRAME_COUNT,
// MACRO_READ_FRAME, and the embedded_macro_frames array
#ifdef EMBEDDED_MACRO_ENABLED
#include "embedded_macro.h"
#endif

// Macro playback state
#ifdef EMBEDDED_MACRO_ENABLED
static uint32_t macro_playback_index = 0;
static uint32_t macro_start_millis = 0;
static bool macro_started = false;
#endif

// Startup state tracking
static uint32_t elapsed_millis = 0;
static bool startup_delay_done = false;
static bool input_priming_done = false;

void macro_player_init(void) {
#ifdef EMBEDDED_MACRO_ENABLED
    macro_started = false;
    macro_playback_index = 0;
#endif
    elapsed_millis = 0;
    startup_delay_done = false;
    input_priming_done = false;
}

bool macro_player_get_report(uint32_t current_millis, uint8_t* report) {
    elapsed_millis = current_millis;

    // Phase 1: Startup delay (~1 second)
    // Give the Switch time to fully recognize the controller
    if (!startup_delay_done) {
        if (elapsed_millis < 1000) {
            memcpy(report, neutral_report, 8);
            return false;
        }
        startup_delay_done = true;
        elapsed_millis = 0;  // Reset for next phase
        return false;
    }

    // Phase 2: Input priming (B button press and release)
    // The Switch needs an actual button press before it properly processes inputs
    if (!input_priming_done) {
        if (elapsed_millis < 80) {  // Press B for ~10 frames (80ms)
            memcpy(report, b_button_report, 8);
            return false;
        } else if (elapsed_millis < 160) {  // Release B for ~10 frames (80ms)
            memcpy(report, neutral_report, 8);
            return false;
        }
        input_priming_done = true;
        elapsed_millis = 0;  // Reset for macro playback
        return false;
    }

#ifdef EMBEDDED_MACRO_ENABLED
    // Phase 3: Macro playback
    if (!macro_started) {
        macro_started = true;
        macro_start_millis = elapsed_millis;
        macro_playback_index = 0;
    }

    uint32_t playback_time = elapsed_millis - macro_start_millis;

    // Read current frame
    EmbeddedMacroFrame_t current_frame;
    MACRO_READ_FRAME(macro_playback_index, &current_frame);

    // Check if we're beyond the last frame
    if (macro_playback_index >= EMBEDDED_MACRO_FRAME_COUNT - 1) {
        EmbeddedMacroFrame_t last_frame;
        MACRO_READ_FRAME(EMBEDDED_MACRO_FRAME_COUNT - 1, &last_frame);

        if (playback_time >= last_frame.timestamp_ms) {
#if EMBEDDED_MACRO_LOOP
            // Loop: restart from beginning
            macro_playback_index = 0;
            macro_start_millis = elapsed_millis;
            playback_time = 0;
            MACRO_READ_FRAME(0, &current_frame);
#else
            // Play once: hold on last frame
            populate_report_from_macro(last_frame.packet, report);
            return true;
#endif
        }
    }

    // Advance to next frame if we've passed its timestamp
    while (macro_playback_index < EMBEDDED_MACRO_FRAME_COUNT - 1) {
        EmbeddedMacroFrame_t next_frame;
        MACRO_READ_FRAME(macro_playback_index + 1, &next_frame);

        if (playback_time >= next_frame.timestamp_ms) {
            macro_playback_index++;
            MACRO_READ_FRAME(macro_playback_index, &current_frame);
        } else {
            break;
        }
    }

    // Convert macro packet to report format
    populate_report_from_macro(current_frame.packet, report);
    return true;
#else
    // No macro defined - send neutral report
    memcpy(report, neutral_report, 8);
    return false;
#endif
}
