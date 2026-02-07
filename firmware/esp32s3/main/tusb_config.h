#ifndef TUSB_CONFIG_H
#define TUSB_CONFIG_H

#ifdef __cplusplus
extern "C" {
#endif

/*
 * TinyUSB configuration for ESP32-S3.
 * The esp_tinyusb component provides most settings via Kconfig.
 * These are guarded with #ifndef to avoid conflicts.
 */

#ifndef CFG_TUSB_RHPORT0_MODE
#define CFG_TUSB_RHPORT0_MODE  OPT_MODE_DEVICE
#endif

#ifndef CFG_TUD_ENDPOINT0_SIZE
#define CFG_TUD_ENDPOINT0_SIZE    64
#endif

#ifndef CFG_TUD_HID
#define CFG_TUD_HID               1
#endif

#ifndef CFG_TUD_CDC
#define CFG_TUD_CDC               0
#endif

#ifndef CFG_TUD_MSC
#define CFG_TUD_MSC               0
#endif

#ifndef CFG_TUD_MIDI
#define CFG_TUD_MIDI              0
#endif

#ifndef CFG_TUD_VENDOR
#define CFG_TUD_VENDOR            0
#endif

#ifndef CFG_TUD_HID_EP_BUFSIZE
#define CFG_TUD_HID_EP_BUFSIZE    64
#endif

#ifdef __cplusplus
}
#endif

#endif // TUSB_CONFIG_H
