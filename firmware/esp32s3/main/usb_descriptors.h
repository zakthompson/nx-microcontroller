#ifndef USB_DESCRIPTORS_H
#define USB_DESCRIPTORS_H

#include "tusb.h"

extern tusb_desc_device_t const desc_device;
extern uint8_t const desc_hid_report[];
extern uint8_t const desc_configuration[];
extern char const* string_desc_arr[];

#define STRING_DESC_COUNT 3

#endif // USB_DESCRIPTORS_H
