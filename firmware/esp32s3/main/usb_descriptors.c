/*
 * USB Descriptors for Nintendo Switch Pro Controller (HORI Pokken Controller)
 * ESP32-S3 with esp_tinyusb component
 *
 * The esp_tinyusb component provides tud_descriptor_device_cb,
 * tud_descriptor_configuration_cb, and tud_descriptor_string_cb internally.
 * We pass our custom descriptors through tinyusb_config_t in main.c.
 * Only tud_hid_descriptor_report_cb must be provided by the application.
 */

#include "tusb.h"
#include "device/usbd.h"
#include "class/hid/hid_device.h"

#include "usb_descriptors.h"

//--------------------------------------------------------------------
// Device Descriptor
//--------------------------------------------------------------------

tusb_desc_device_t const desc_device = {
    .bLength            = sizeof(tusb_desc_device_t),
    .bDescriptorType    = TUSB_DESC_DEVICE,
    .bcdUSB             = 0x0200,
    .bDeviceClass       = 0x00,
    .bDeviceSubClass    = 0x00,
    .bDeviceProtocol    = 0x00,
    .bMaxPacketSize0    = CFG_TUD_ENDPOINT0_SIZE,

    .idVendor           = 0x0F0D,  // HORI
    .idProduct          = 0x0092,  // Pokken Controller
    .bcdDevice          = 0x0100,

    .iManufacturer      = 0x01,
    .iProduct           = 0x02,
    .iSerialNumber      = 0x00,

    .bNumConfigurations = 0x01
};

//--------------------------------------------------------------------
// HID Report Descriptor
//--------------------------------------------------------------------

uint8_t const desc_hid_report[] = {
    0x05, 0x01,        // Usage Page (Generic Desktop)
    0x09, 0x05,        // Usage (Joystick)
    0xA1, 0x01,        // Collection (Application)

    // Buttons (16 bits / 2 bytes)
    0x15, 0x00,        //   Logical Minimum (0)
    0x25, 0x01,        //   Logical Maximum (1)
    0x35, 0x00,        //   Physical Minimum (0)
    0x45, 0x01,        //   Physical Maximum (1)
    0x75, 0x01,        //   Report Size (1 bit)
    0x95, 0x10,        //   Report Count (16)
    0x05, 0x09,        //   Usage Page (Button)
    0x19, 0x01,        //   Usage Minimum (Button 1)
    0x29, 0x10,        //   Usage Maximum (Button 16)
    0x81, 0x02,        //   Input (Data,Var,Abs)

    // HAT Switch (4 bits)
    0x05, 0x01,        //   Usage Page (Generic Desktop)
    0x25, 0x07,        //   Logical Maximum (7)
    0x46, 0x3B, 0x01,  //   Physical Maximum (315)
    0x75, 0x04,        //   Report Size (4 bits)
    0x95, 0x01,        //   Report Count (1)
    0x65, 0x14,        //   Unit (Degrees)
    0x09, 0x39,        //   Usage (Hat switch)
    0x81, 0x42,        //   Input (Data,Var,Abs,Null)

    // Padding nibble (4 bits)
    0x65, 0x00,        //   Unit (None)
    0x95, 0x01,        //   Report Count (1)
    0x81, 0x01,        //   Input (Const,Array,Abs)

    // Analog Sticks (4 axes, 8 bits each = 4 bytes)
    0x26, 0xFF, 0x00,  //   Logical Maximum (255)
    0x46, 0xFF, 0x00,  //   Physical Maximum (255)
    0x09, 0x30,        //   Usage (X)
    0x09, 0x31,        //   Usage (Y)
    0x09, 0x32,        //   Usage (Z)
    0x09, 0x35,        //   Usage (Rz)
    0x75, 0x08,        //   Report Size (8 bits)
    0x95, 0x04,        //   Report Count (4)
    0x81, 0x02,        //   Input (Data,Var,Abs)

    // Vendor-specific byte (1 byte) - Required by Switch
    0x06, 0x00, 0xFF,  //   Usage Page (Vendor Defined 0xFF00)
    0x09, 0x20,        //   Usage (0x20)
    0x95, 0x01,        //   Report Count (1)
    0x81, 0x02,        //   Input (Data,Var,Abs)

    // Output Report (8 bytes) - For subcommands from Switch
    0x0A, 0x21, 0x26,  //   Usage (0x2621)
    0x95, 0x08,        //   Report Count (8)
    0x91, 0x02,        //   Output (Data,Var,Abs)

    0xC0              // End Collection
};

// Invoked when received GET HID REPORT DESCRIPTOR
uint8_t const* tud_hid_descriptor_report_cb(uint8_t instance) {
    (void) instance;
    return desc_hid_report;
}

//--------------------------------------------------------------------
// Configuration Descriptor
//--------------------------------------------------------------------

enum {
    ITF_NUM_HID = 0,
    ITF_NUM_TOTAL
};

#define CONFIG_TOTAL_LEN    (TUD_CONFIG_DESC_LEN + TUD_HID_INOUT_DESC_LEN)

#define EPNUM_HID_IN        0x81
#define EPNUM_HID_OUT       0x01

uint8_t const desc_configuration[] = {
    TUD_CONFIG_DESCRIPTOR(1, ITF_NUM_TOTAL, 0, CONFIG_TOTAL_LEN, 0x80, 500),
    TUD_HID_INOUT_DESCRIPTOR(ITF_NUM_HID, 0, HID_ITF_PROTOCOL_NONE, sizeof(desc_hid_report), EPNUM_HID_IN, EPNUM_HID_OUT, CFG_TUD_HID_EP_BUFSIZE, 8)
};

//--------------------------------------------------------------------
// String Descriptors
//--------------------------------------------------------------------

char const* string_desc_arr[] = {
    (const char[]) { 0x09, 0x04 }, // 0: Language (English US)
    "HORI CO.,LTD.",               // 1: Manufacturer
    "POKKEN CONTROLLER",           // 2: Product
};

// Verify STRING_DESC_COUNT matches the actual array size
_Static_assert(sizeof(string_desc_arr) / sizeof(string_desc_arr[0]) == STRING_DESC_COUNT,
               "STRING_DESC_COUNT must match string_desc_arr size");
