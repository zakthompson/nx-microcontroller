#ifndef DESCRIPTORS_H
#define DESCRIPTORS_H

#include <LUFA/Drivers/USB/USB.h>

/** Interface IDs */
enum InterfaceDescriptors_t {
    INTERFACE_ID_Joystick = 0, /**< Joystick interface descriptor ID */
};

/** Endpoint addresses */
#define JOYSTICK_IN_EPADDR  (ENDPOINT_DIR_IN  | 1)
#define JOYSTICK_OUT_EPADDR (ENDPOINT_DIR_OUT | 2)

/** Endpoint size in bytes */
#define JOYSTICK_EPSIZE 64

/** HID polling interval. Standalone macros advance one 8 ms frame per report, so they keep the
 *  125 Hz rate; the UART relay polls at 1 kHz so a report is never more than ~1 ms stale. */
#ifdef EMBEDDED_MACRO_ENABLED
#define HID_POLL_INTERVAL_MS 8
#else
#define HID_POLL_INTERVAL_MS 1
#endif

/** String descriptor IDs */
enum StringDescriptors_t {
    STRING_ID_Language = 0, /**< Supported Languages string ID (must be zero) */
    STRING_ID_Manufacturer, /**< Manufacturer string ID */
    STRING_ID_Product,      /**< Product string ID */
};

/** Configuration descriptor structure */
typedef struct {
    USB_Descriptor_Configuration_Header_t Config;
    USB_Descriptor_Interface_t            HID_Interface;
    USB_HID_Descriptor_HID_t              HID_JoystickHID;
    USB_Descriptor_Endpoint_t             HID_ReportINEndpoint;
    USB_Descriptor_Endpoint_t             HID_ReportOUTEndpoint;
} USB_Descriptor_Configuration_t;

uint16_t CALLBACK_USB_GetDescriptor(
    const uint16_t wValue,
    const uint16_t wIndex,
    const void** const DescriptorAddress
) ATTR_WARN_UNUSED_RESULT ATTR_NON_NULL_PTR_ARG(3);

#endif // DESCRIPTORS_H
