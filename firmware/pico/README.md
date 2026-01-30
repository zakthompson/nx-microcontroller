# Raspberry Pi Pico Standalone Firmware

Standalone Nintendo Switch Pro Controller emulation firmware for Raspberry Pi Pico with embedded macro playback.

## Features

- Emulates HORI Pokken Controller (recognized by Nintendo Switch)
- Plays back pre-recorded macros without PC connection
- Optional looping mode
- LED feedback (blinks when looping, solid when playing)
- No external dependencies - runs entirely on the Pico

## Requirements

### Hardware
- Raspberry Pi Pico or Pico W (RP2040-based board)
- USB cable to connect to Nintendo Switch

### Software
- [Pico SDK](https://github.com/raspberrypi/pico-sdk)
- ARM cross-compiler (`arm-none-eabi-gcc`)
- CMake 3.13 or higher
- Python 3 (for macro conversion)

## Setup

1. Install Pico SDK:
```bash
git clone https://github.com/raspberrypi/pico-sdk.git
cd pico-sdk
git submodule update --init
export PICO_SDK_PATH=$(pwd)
```

2. Install toolchain (Ubuntu/Debian):
```bash
sudo apt install cmake gcc-arm-none-eabi libnewlib-arm-none-eabi build-essential
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
python3 ../macro_to_c.py path/to/macro.json --loop -o embedded_macro.h

# Build firmware
mkdir -p build && cd build
cmake .. -DCMAKE_BUILD_TYPE=Release
make -j$(nproc)
```

Output: `build/nx_standalone.uf2`

## Flashing

1. Hold the BOOTSEL button on your Pico while plugging it into your computer
2. The Pico will appear as a USB mass storage device
3. Drag `nx_standalone.uf2` onto the drive
4. The Pico will automatically reboot and start running your macro

## Usage

1. Flash the firmware to your Pico
2. Unplug the Pico from your computer
3. Plug the Pico into your Nintendo Switch
4. The macro will start playing after a brief initialization (~1 second)

## LED Indicators

- **Off**: Startup/initialization phase
- **Solid**: Macro is playing (non-loop mode)
- **Blinking**: Macro is looping

## Troubleshooting

### Build fails with "PICO_SDK_PATH not set"
```bash
export PICO_SDK_PATH=/path/to/pico-sdk
```

### Switch doesn't recognize the controller
- Ensure you're using the correct USB port on the Switch
- Try unplugging and replugging the Pico
- Verify the firmware flashed correctly (check for errors during flash)

### Macro doesn't play
- Ensure your macro file is valid JSON
- Check that EMBEDDED_MACRO_ENABLED is defined during build
- Verify the LED is blinking/solid (indicates macro is active)

## Technical Details

- **USB Stack**: TinyUSB (included with Pico SDK)
- **Poll Rate**: 125Hz (8ms per report, matching Switch USB poll rate)
- **Controller Type**: HORI Pokken Tournament Pro Pad (VID: 0x0F0D, PID: 0x0092)
- **Report Format**: 8-byte HID reports (buttons, hat, 4 axes, vendor byte)
- **Memory**: Macros stored in flash, accessed directly (no PROGMEM needed)

## Architecture

```
main.c                  Platform-specific (TinyUSB integration)
usb_descriptors.c       Platform-specific (TinyUSB descriptors)
tusb_config.h           Platform-specific (TinyUSB config)
CMakeLists.txt          Platform-specific (build config)
../shared/              Platform-independent (macro player, Switch protocol)
```

## License

This firmware is based on the LUFA library examples and follows the same MIT-style license.
