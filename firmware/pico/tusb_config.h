#ifndef TUSB_CONFIG_H
#define TUSB_CONFIG_H

#ifdef __cplusplus
extern "C" {
#endif

//--------------------------------------------------------------------
// Common Configuration
//--------------------------------------------------------------------

// Enable device mode on RHPort 0
#define CFG_TUSB_RHPORT0_MODE  OPT_MODE_DEVICE

#ifndef CFG_TUSB_OS
#define CFG_TUSB_OS            OPT_OS_PICO
#endif

#ifndef CFG_TUSB_DEBUG
#define CFG_TUSB_DEBUG         0
#endif

#define CFG_TUSB_MEM_SECTION
#define CFG_TUSB_MEM_ALIGN     __attribute__((aligned(4)))

//--------------------------------------------------------------------
// Device Configuration
//--------------------------------------------------------------------

#define CFG_TUD_ENDPOINT0_SIZE    64

//------------- Class Enabled -------------//
#define CFG_TUD_HID               1
#define CFG_TUD_CDC               0
#define CFG_TUD_MSC               0
#define CFG_TUD_MIDI              0
#define CFG_TUD_VENDOR            0

//------------- HID Configuration -------------//
#define CFG_TUD_HID_EP_BUFSIZE    64

#ifdef __cplusplus
}
#endif

#endif // TUSB_CONFIG_H
