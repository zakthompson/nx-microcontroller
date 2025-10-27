# Firmware

Custom firmware that makes your microcontroller appear as a Nintendo Switch Pro Controller. Can be built in two modes: as a relay for PC-based control, or as a standalone bot with embedded macros.

## Purpose

This firmware enables two distinct use cases:

### 1. Relay Mode (SPI Communication)

The microcontroller acts as a bridge between your PC and the Nintendo Switch:

- Receives controller input from the Controller Relay application via UART
- Presents itself as a Pro Controller to the Switch
- Forwards commands in real-time
- Perfect for remote gameplay and live macro playback

### 2. Standalone Mode (Embedded Macros)

The microcontroller runs pre-programmed macros without a PC:

- Macro is compiled directly into the firmware
- Runs automatically when plugged into the Switch
- No PC or controller needed after flashing
- Perfect for automation, grinding, and sharing bots with friends

## Prerequisites

### Software

- **avr-gcc**: AVR toolchain for compiling C code

  - Linux: `sudo apt-get install gcc-avr avr-libc`
  - macOS: `brew install avr-gcc`
  - Windows: [Install WinAVR](https://sourceforge.net/projects/winavr/) or use WSL

- **dfu-programmer**: For flashing firmware to the microcontroller

  - Linux: `sudo apt-get install dfu-programmer`
  - macOS: `brew install dfu-programmer`
  - Windows: [Download from SourceForge](https://sourceforge.net/projects/dfu-programmer/)
  - Windows Alternative: [Atmel Flip](https://www.microchip.com/en-us/development-tool/flip)

- **Python 3**: Required for standalone macro builds (macro conversion)

  - Linux/macOS: Usually pre-installed, or use package manager
  - Windows: [Download from python.org](https://www.python.org/downloads/)

- **LUFA Library**: Included as a git submodule

  ```bash
  git submodule update --init --recursive
  ```

### Hardware

- **LUFA-compatible microcontroller**:
  - Arduino Uno R3 (ATmega16U2)
  - Teensy 2.0++ (AT90USB1286)
  - Other AVR microcontrollers with USB support

## Building Firmware

### Relay Firmware (Default)

Build firmware that accepts input from Controller Relay:

```bash
cd firmware
make
```

This produces `Joystick.hex` - the relay firmware.

### Standalone Firmware (With Embedded Macro)

Build firmware with a macro embedded that runs automatically:

```bash
cd firmware
make standalone MACRO=path/to/macro.macro
```

**With looping:**

```bash
make standalone MACRO=path/to/macro.macro LOOP=1
```

This produces `JoystickStandalone.hex` - standalone firmware that runs without a PC.

**Example:**

```bash
# Using a macro recorded by controller-relay
make standalone MACRO=../controller-relay/macro.macro LOOP=1

# Using a custom macro
make standalone MACRO=my_custom_macro.macro
```

## Flashing Firmware

### Entering DFU Mode

Before flashing, put your microcontroller into DFU (Device Firmware Update) mode:

**Arduino Uno R3:**

1. Short the two pins closest to the USB port (RESET and GND on the ATmega16U2)
2. Verify DFU mode: `lsusb` should show "Atmel Corp. ATmega16U2"

**Teensy 2.0++:**

1. Press the reset button on the board

### Flashing Firmware

```bash
make flash
```

Or manually (often required due to timing issues):

```bash
sudo dfu-programmer atmega16u2 erase
sudo dfu-programmer atmega16u2 flash Joystick.hex # Or JoystickStandalone.hex
sudo dfu-programmer atmega16u2 reset
```

**Note:** Replace `atmega16u2` with your MCU if different (e.g., `at90usb1286` for Teensy 2.0++). Update the `MCU` variable in the `Makefile` to match your hardware.

Alternatively, you can use the GUI in Atmel Flip on Windows.

## Macro Format (.macro files)

Macros use a human-readable text format with the following syntax:

### Basic Syntax

Each line represents a controller state and duration:

```
inputs,duration_in_frames
```

- **inputs**: Button presses, D-pad directions, and analog stick positions (separated by `+`)
- **duration_in_frames**: How long to hold this state (in USB frames, 8ms each)

### Examples

```
# Press A button for 10 frames (80ms)
A,10

# Press A+B together for 5 frames (40ms)
A+B,5

# Wait (neutral state) for 20 frames (160ms)
Wait,20

# Hold nothing for 100 frames
,100
```

### Buttons

Available buttons: `Y`, `B`, `A`, `X`, `L`, `R`, `ZL`, `ZR`, `Plus`, `Minus`, `Home`, `Capture`, `LClick`, `RClick`

```
A,10            # A button for 80ms
A+B,5           # A and B together for 40ms
L+R+A,20        # L, R, and A for 160ms
Home,5          # HOME button for 40ms
```

### D-Pad (HAT Switch)

Directions: `Up`, `Down`, `Left`, `Right`, `UpLeft`, `UpRight`, `DownLeft`, `DownRight`

```
Up,10           # D-pad Up for 80ms
Down+A,5        # D-pad Down and A button for 40ms
UpRight,15      # D-pad diagonal up-right for 120ms
```

**Note:** Only one D-pad direction at a time (diagonal directions are single inputs).

### Analog Sticks

#### Cardinal Directions

Predefined directions for convenience:

**Left Stick:**

- `LUp`, `LDown`, `LLeft`, `LRight`
- `LUpLeft`, `LUpRight`, `LDownLeft`, `LDownRight`

**Right Stick:**

- `RUp`, `RDown`, `RLeft`, `RRight`
- `RUpLeft`, `RUpRight`, `RDownLeft`, `RDownRight`

```
LUp,10          # Left stick up for 80ms
RRight,20       # Right stick right for 160ms
LLeft+A,15      # Left stick left + A button for 120ms
```

#### Precise Coordinates

For precise analog input, use `L(x,y)` or `R(x,y)`:

```
L(128,255),10   # Left stick: x=128 (center), y=255 (full up)
R(255,128),20   # Right stick: x=255 (full right), y=128 (center)
L(0,0),5        # Left stick: bottom-left corner
```

- **Range**: 0-255 for both X and Y axes
- **Center**: 128 for both axes
- **Format**: Decimal or hex (e.g., `L(0x80,0xFF)`)

### Comments

Lines starting with `#` or `//` are ignored:

```
# This is a comment
// This is also a comment
A,10  # Inline comments work too
```

### Include Directives

Include other macro files using the `@` prefix:

```
# my_macro.macro
@basic_movement.macro   # Include another macro
A,10
@jump_sequence.macro    # Include multiple times
Wait,20
```

**Include Rules:**

- Paths are relative to the current macro file
- Absolute paths are supported
- Circular includes are detected and prevented
- Maximum include depth: 10 levels

**Example:**

```
# main.macro
@macros/startup.macro
@macros/farming_loop.macro
@macros/cleanup.macro
```

### Complete Example

```
# Example: Simple A-button mashing macro

# Press A for 10 frames (80ms)
A,10

# Wait for 10 frames (80ms)
Wait,10

# Press A for 10 frames (80ms)
A,10

# Wait for 10 frames (80ms)
Wait,10

# Press A for 10 frames (80ms)
A,10

# Final wait
Wait,20
```

### Include Example

```
# farming_bot.macro - Main farming sequence

# Start by going to the farming spot
@navigation/go_to_spot.macro

# Repeat the farming action 10 times
@actions/collect_item.macro
@actions/collect_item.macro
@actions/collect_item.macro
@actions/collect_item.macro
@actions/collect_item.macro
@actions/collect_item.macro
@actions/collect_item.macro
@actions/collect_item.macro
@actions/collect_item.macro
@actions/collect_item.macro

# Return to base
@navigation/return_to_base.macro
```

## Configuration

### Microcontroller Selection

Edit `Makefile` to match your hardware:

```makefile
# For Arduino Uno R3
MCU = atmega16u2
BOARD = UNO

# For Teensy 2.0++
MCU = at90usb1286
BOARD = TEENSY2PP
```

### USB Polling Rate

The firmware uses **125 Hz (8ms per frame)** polling rate to match standard USB HID devices. This is defined in the USB descriptor and must match the macro format's frame duration.

## Troubleshooting

### Build Errors

**"avr-gcc: command not found"**

- Install avr-gcc toolchain (see [Prerequisites](#prerequisites))

**"LUFA library not found"**

- Initialize git submodules: `git submodule update --init --recursive`

**"macro_to_c.py: command not found"**

- Install Python 3 (see [Prerequisites](#prerequisites))
- Ensure Python 3 is in your PATH

### Flashing Errors

**"Could not find USB device"**

- Ensure microcontroller is in DFU mode
- Check `lsusb` (Linux) or Device Manager (Windows) for DFU device
- Try different USB ports

**"Permission denied"**

- Use `sudo` on Linux/macOS: `sudo make flash`
- On Windows, run as Administrator

**"Verification failed"**

- Erase before flashing: `sudo dfu-programmer <mcu> erase`
- Try flashing again

### Runtime Issues

**Switch doesn't recognize controller**

- Verify firmware flashed successfully
- Check that correct MCU is selected in Makefile
- Try unplugging and reconnecting

**Macro doesn't run**

- Verify `EMBEDDED_MACRO_ENABLED` is defined during build
- Check that macro file has valid syntax
- Review `embedded_macro.h` generated by `macro_to_c.py`

**Macro timing is off**

- Ensure USB frame interval in firmware matches macro conversion (8ms)
- Check that macro uses frame-based timing (not milliseconds)

## Technical Details

- **USB Polling Rate**: 125 Hz (8ms interval)
- **Packet Format**: 8-byte HID input reports
- **PROGMEM Usage**: Embedded macros stored in flash memory to save RAM
- **Timing**: Hardware timer-based for precise frame timing
- **Protocol**: Nintendo Switch Pro Controller USB HID protocol

## Cleaning Build Files

```bash
# Remove all build artifacts
make clean

# Remove relay firmware only
rm Joystick.hex Joystick.elf

# Remove standalone firmware
rm JoystickStandalone.hex JoystickStandalone.elf embedded_macro.h
```

## See Also

- [Controller Relay README](../controller-relay/README.md) - How to record macros and use relay mode
- [Root README](../README.md) - Project overview and quick start
- [LUFA Documentation](http://www.fourwalledcubicle.com/LUFA.php) - USB framework documentation
