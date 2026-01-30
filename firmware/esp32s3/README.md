# ESP32-S3 Standalone Firmware

Standalone Nintendo Switch Pro Controller emulation firmware for ESP32-S3 with embedded macro playback.

## Features

- Emulates HORI Pokken Controller (recognized by Nintendo Switch)
- Plays back pre-recorded macros without PC connection
- Optional looping mode
- LED feedback (blinks when looping, solid when playing)
- No external dependencies - runs entirely on the ESP32-S3
- Uses TinyUSB via ESP-IDF

## Requirements

### Hardware
- ESP32-S3 DevKit (or any ESP32-S3 board with native USB)
- USB cable to connect to Nintendo Switch

**IMPORTANT**: Only ESP32-**S3** has native USB device support. Regular ESP32 and ESP32-S2 are NOT supported.

### Software
- [ESP-IDF](https://github.com/espressif/esp-idf) v5.0 or later
- Python 3.7+ (for ESP-IDF and macro conversion)
- Git

## Setup

1. Install ESP-IDF:
```bash
# Clone ESP-IDF
git clone --recursive https://github.com/espressif/esp-idf.git
cd esp-idf

# Install for ESP32-S3
./install.sh esp32s3

# Set up environment (run this in every new terminal)
. ./export.sh
export IDF_PATH=$(pwd)
```

2. Verify installation:
```bash
idf.py --version
```

## Building

### Quick Build

Use the build script with a macro file:

```bash
./build_standalone.sh path/to/macro.json          # Play once
./build_standalone.sh path/to/macro.json --loop   # Loop forever
```

### Manual Build

```bash
# Generate macro header
python3 ../macro_to_c.py path/to/macro.json --loop -o main/embedded_macro.h

# Set target
idf.py set-target esp32s3

# Build firmware
idf.py build
```

Output: `build/nx_standalone.bin` (app binary) and `build/nx_standalone_merged.bin` (merged with bootloader)

## Flashing

### Method 1: Using ESP-IDF (Recommended)

```bash
# Flash and open serial monitor
idf.py flash monitor

# Or just flash
idf.py flash
```

### Method 2: Using esptool directly

```bash
# Flash merged binary (includes bootloader and partition table)
esptool.py --chip esp32s3 --port /dev/ttyUSB0 write_flash 0x0 build/nx_standalone_merged.bin
```

### Method 3: Individual partitions

```bash
esptool.py --chip esp32s3 --port /dev/ttyUSB0 write_flash \
    0x0 build/bootloader/bootloader.bin \
    0x8000 build/partition_table/partition-table.bin \
    0x10000 build/nx_standalone.bin
```

## Usage

1. Flash the firmware to your ESP32-S3
2. Unplug from computer
3. Plug into Nintendo Switch
4. The macro will start playing after initialization (~1 second)

## LED Indicators

- **On (during boot)**: Initializing
- **Off**: Startup/initialization phase
- **Solid**: Macro is playing (non-loop mode)
- **Blinking**: Macro is looping

## Troubleshooting

### Build fails with "IDF_PATH not set"
```bash
export IDF_PATH=/path/to/esp-idf
source $IDF_PATH/export.sh
```

### "Target esp32s3 not found"
```bash
cd $IDF_PATH
./install.sh esp32s3
```

### Switch doesn't recognize the controller
- Ensure you're using the **USB port**, not the UART port
- ESP32-S3-DevKitC-1: Use the USB port labeled "USB" (not "UART")
- Try a different USB cable
- Check that the firmware flashed successfully

### Macro doesn't play
- Check serial output with `idf.py monitor`
- Verify EMBEDDED_MACRO_ENABLED is defined
- Ensure your macro JSON is valid

### "Component esp_tinyusb not found"
The component will be downloaded automatically by ESP-IDF component manager when you build. Ensure you have internet connection during the first build.

## Technical Details

- **USB Stack**: TinyUSB (via ESP-IDF)
- **RTOS**: FreeRTOS (built into ESP-IDF)
- **Poll Rate**: 125Hz (8ms per report, matching Switch USB poll rate)
- **Controller Type**: HORI Pokken Tournament Pro Pad (VID: 0x0F0D, PID: 0x0092)
- **Report Format**: 8-byte HID reports (buttons, hat, 4 axes, vendor byte)
- **Memory**: Macros stored in flash, accessed directly (no PROGMEM needed)
- **GPIO**: LED on GPIO 48 (standard for ESP32-S3-DevKitC-1)

## Architecture

```
main/main.c             Platform-specific (ESP-IDF + TinyUSB integration)
main/usb_descriptors.c  Platform-specific (TinyUSB descriptors)
main/tusb_config.h      Platform-specific (TinyUSB config)
main/CMakeLists.txt     Component-level build config
CMakeLists.txt          Project-level build config
sdkconfig.defaults      ESP-IDF default configuration
../shared/              Platform-independent (macro player, Switch protocol)
```

## Pin Configuration

ESP32-S3-DevKitC-1:
- **USB D+/D-**: Built-in (GPIO 19/20, handled automatically)
- **LED**: GPIO 48 (onboard RGB LED, using red channel)

For custom boards, modify `LED_GPIO` in `main/main.c`.

## Debugging

Enable debug logging by modifying `sdkconfig.defaults`:

```
CONFIG_LOG_DEFAULT_LEVEL_INFO=y
CONFIG_TINYUSB_DEBUG_LEVEL=2
```

Then rebuild and monitor:
```bash
idf.py build flash monitor
```

## License

This firmware uses ESP-IDF and TinyUSB, both of which are Apache 2.0 licensed.
