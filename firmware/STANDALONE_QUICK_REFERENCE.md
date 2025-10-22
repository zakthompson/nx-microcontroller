# Standalone Macro Firmware - Quick Reference

## Build Commands

```bash
# Play macro once
make standalone MACRO=path/to/macro.json

# Loop macro forever
make standalone MACRO=path/to/macro.json LOOP=1

# Flash to Arduino
make flash-standalone

# Clean build files
make clean
```

## Using the Build Script

```bash
# Play once
./build_standalone.sh ../controller-relay/macro.json

# Loop forever
./build_standalone.sh ../controller-relay/macro.json --loop

# Show help
./build_standalone.sh --help
```

## Manual Conversion

```bash
# Convert JSON to C header
python3 macro_to_c.py macro.json -o embedded_macro.h
python3 macro_to_c.py macro.json --loop -o embedded_macro.h

# Build with existing header
make TARGET=JoystickStandalone CC_FLAGS="-DUSE_LUFA_CONFIG_HEADER -IConfig/ -DEMBEDDED_MACRO_ENABLED=1"
```

## Flash Commands

```bash
# Using Makefile
make flash-standalone

# Manual DFU programmer
sudo dfu-programmer atmega16u2 erase
sudo dfu-programmer atmega16u2 flash JoystickStandalone.hex
sudo dfu-programmer atmega16u2 reset
```

## LED Indicators

| LED State | Meaning |
|-----------|---------|
| Solid ON | Playing macro once |
| Blinking (500ms) | Looping macro |
| OFF | No macro embedded or error |

## File Outputs

| File | Description |
|------|-------------|
| `embedded_macro.h` | Generated C header with macro data |
| `JoystickStandalone.hex` | Flashable firmware with embedded macro |
| `JoystickStandalone.elf` | Debug symbols (optional) |

## Macro Recording (controller-relay)

```bash
# Start controller-relay
./controller-relay COM3  # Windows
./controller-relay /dev/ttyUSB0  # Linux

# L3 + A: Start/Stop recording
# L3 + X: Play once
# L3 + Y: Loop/Stop loop
# L3 + B: Exit
```

Output: `macro.json` in the same directory

## Sharing Macros

To share with others:
1. Build: `make standalone MACRO=macro.json LOOP=1`
2. Share: `JoystickStandalone.hex`
3. They need: Arduino + dfu-programmer
4. They flash: `make flash-standalone` or manual DFU commands

## Memory Limits

- ATmega16u2: ~16KB flash, 512 bytes RAM
- Max frames: ~1000-2000 (12 bytes per frame)
- Long macros stored in PROGMEM (flash, not RAM)

## Reverting to Serial Mode

```bash
make clean
make
make flash
```

## Troubleshooting

| Problem | Solution |
|---------|----------|
| "MACRO parameter required" | Add `MACRO=path/to/file.json` |
| "File not found" | Check macro path, use absolute paths |
| "python3 not found" | Install Python 3 |
| Macro doesn't play | Verify JSON file, check LED behavior |
| Compilation error | Check macro size, may be too large |

## Quick Example

```bash
# Full workflow
cd controller-relay
./controller-relay COM3
# (record macro with L3+A)

cd ../firmware
make standalone MACRO=../controller-relay/macro.json LOOP=1
# (put Arduino in DFU mode)
make flash-standalone
# (plug into Switch)
```
