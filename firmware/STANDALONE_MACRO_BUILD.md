# Building Standalone Macro Firmware

This guide explains how to build firmware with embedded macros that will play automatically when the Arduino is connected to a Switch, without requiring a PC, controller-relay, or USB serial connection.

## Overview

The standalone macro firmware allows you to:
- Record a macro using controller-relay on your PC
- Build custom firmware with that macro embedded
- Flash the firmware to an Arduino
- Plug the Arduino directly into a Nintendo Switch to execute the macro

This is perfect for sharing macros with others who don't have the controller-relay setup!

## Prerequisites

1. **AVR toolchain** (`avr-gcc`, `avr-libc`)
2. **dfu-programmer** (for flashing to Arduino)
3. **Python 3** (for macro conversion)
4. A recorded macro file (`macro.json`)

## Quick Start

### 1. Record a Macro

First, use controller-relay to record your macro:

```bash
# Run controller-relay with your XInput controller
./controller-relay COM3

# Press L3 + A to start recording
# Perform your inputs
# Press L3 + A to stop recording
# The macro is saved to macro.json
```

### 2. Build Standalone Firmware

Navigate to the firmware directory and build with your macro:

```bash
cd firmware

# Build firmware that plays macro once
make standalone MACRO=../controller-relay/macro.json

# OR build firmware that loops the macro forever
make standalone MACRO=../controller-relay/macro.json LOOP=1
```

This will:
- Convert your `macro.json` to a C header file (`embedded_macro.h`)
- Compile `JoystickStandalone.c` with the embedded macro
- Generate `JoystickStandalone.hex` firmware file

### 3. Flash to Arduino

Put your Arduino into DFU mode (short the RESET and GND pins), then:

```bash
make flash-standalone
```

Or manually:
```bash
sudo dfu-programmer atmega16u2 erase
sudo dfu-programmer atmega16u2 flash JoystickStandalone.hex
sudo dfu-programmer atmega16u2 reset
```

### 4. Use It!

Simply plug the Arduino into your Nintendo Switch. The macro will start playing automatically!

**LED Behavior:**
- **Play-once mode**: Solid LED while playing
- **Loop mode**: Blinking LED (on/off every 500ms)

## Build Options

### Play Once vs Loop

```bash
# Play macro once, then hold last frame
make standalone MACRO=path/to/macro.json

# Loop macro continuously
make standalone MACRO=path/to/macro.json LOOP=1
```

### Custom Macro Path

You can specify any macro file path:

```bash
make standalone MACRO=/path/to/my_custom_macro.json
make standalone MACRO=~/Desktop/farming_macro.json LOOP=1
```

## Advanced Usage

### Manual Conversion

If you want to inspect or modify the generated header:

```bash
# Convert macro to C header
python3 macro_to_c.py macro.json --loop -o embedded_macro.h

# Manually build
make TARGET=JoystickStandalone CC_FLAGS="-DUSE_LUFA_CONFIG_HEADER -IConfig/ -DEMBEDDED_MACRO_ENABLED=1"
```

### Inspecting the Macro Header

After running `make standalone`, examine `embedded_macro.h` to see the compiled macro data:

```bash
cat embedded_macro.h
```

## How It Works

1. **Macro Recording**: The controller-relay records timestamped input frames to `macro.json`

2. **Conversion**: The Python script `macro_to_c.py` reads the JSON and generates a C header with:
   - Frame count and duration
   - Array of frames in PROGMEM (program memory)
   - Loop configuration

3. **Compilation**: The `JoystickStandalone.c` firmware:
   - Reads frames from PROGMEM at the appropriate timestamps
   - Sends them to the Switch as HID reports
   - Loops or stops based on configuration

4. **Execution**: When connected to a Switch, the firmware replays the exact sequence of inputs

## Sharing Macros

To share a macro with someone:

1. Build the standalone firmware with your macro
2. Send them the `JoystickStandalone.hex` file
3. They only need:
   - An Arduino with ATmega16u2 (like Arduino Uno)
   - `dfu-programmer` to flash it
   - Instructions to put Arduino in DFU mode and run flash command

They don't need the controller-relay software, USB serial adapter, or XInput controller!

## Cleaning Up

```bash
# Remove built files including embedded_macro.h
make clean
```

## Troubleshooting

### "Error: MACRO parameter required"
You forgot to specify the MACRO parameter. Use:
```bash
make standalone MACRO=path/to/macro.json
```

### "Error: File 'macro.json' not found"
Check the path to your macro file. Use absolute paths if needed:
```bash
make standalone MACRO=/absolute/path/to/macro.json
```

### "Python3 not found"
Install Python 3:
- Ubuntu/Debian: `sudo apt install python3`
- macOS: `brew install python3` or use built-in Python 3
- Windows: Download from python.org

### Firmware doesn't play macro
1. Verify the macro was recorded successfully (check file size > 0)
2. Check LED behavior when plugged into Switch
3. Rebuild with verbose output: `make standalone MACRO=... V=1`

### Macro plays too fast/slow
The timing should match your recording. If it doesn't:
1. Verify the Switch is in the correct game/menu state
2. Check that frame timestamps in `embedded_macro.h` look reasonable

## Memory Limitations

The ATmega16u2 has limited memory:
- **Flash (program memory)**: 16KB
- **RAM**: 512 bytes

Long macros are stored in flash (PROGMEM), so RAM isn't an issue. However:
- **Maximum macro length**: ~1000-2000 frames depending on other code
- **Each frame**: 12 bytes (4-byte timestamp + 8-byte packet)

If you get compilation errors about memory, try:
1. Recording a shorter macro
2. Removing duplicate frames (the Python script doesn't deduplicate)

## Example Workflow

```bash
# 1. Record a 30-second farming macro
cd controller-relay
./controller-relay COM3
# (record macro with L3+A)

# 2. Build looping firmware
cd ../firmware
make standalone MACRO=../controller-relay/macro.json LOOP=1

# 3. Flash to Arduino
# (put Arduino in DFU mode)
make flash-standalone

# 4. Test on Switch
# (plug Arduino into Switch)

# 5. Share the hex file
cp JoystickStandalone.hex ~/farming_macro_v1.hex
```

## Reverting to Serial Mode

To go back to using controller-relay with serial communication:

```bash
# Build normal firmware
make clean
make

# Flash normal firmware
make flash
```
