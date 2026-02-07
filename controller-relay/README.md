# Controller Relay

A Windows application that captures XInput controller input and relays it to a microcontroller over a serial connection, allowing you to control a Nintendo Switch using any XInput-compatible controller.

## Purpose

Controller Relay enables:
- **Remote gameplay**: Control your Switch from a PC using any XInput controller
- **Macro recording**: Record button sequences and analog stick movements
- **Macro playback**: Replay recorded macros once or loop them indefinitely
- **Companion app integration**: Automatically launch capture software or other applications
- **Standalone bot creation**: Export recorded macros to firmware for PC-free automation

## Features

- Real-time controller input relay via USB serial (1 Mbps baud rate)
- Macro recording with millisecond precision
- Hotkey system for in-game macro control
- Macro looping (continues even when controller disconnected)
- Auto-launch companion applications
- Auto-click support for companion app UI automation
- Outputs macros in human-readable `.macro` format

## Prerequisites

- **Windows**: .NET 9.0 or later
- **XInput controller**: Xbox controller or any XInput-compatible gamepad
- **Microcontroller**: Arduino Uno R3, Teensy 2.0++, or similar running relay firmware
- **USB-to-UART adapter**: CP210x, FTDI, or CH340-based adapter

## Building

### Using .NET CLI

```bash
cd controller-relay
dotnet build
dotnet run -- COM3  # Replace COM3 with your serial port
```

### Using Visual Studio

1. Open `controller-relay.csproj` in Visual Studio 2022 or later
2. Build the solution (Ctrl+Shift+B)
3. Run the project (F5)

### Publishing a Standalone Executable

```bash
cd controller-relay
dotnet publish -c Release -r win-x64 --self-contained
```

The executable will be in `bin/Release/net9.0-windows/win-x64/publish/`

## Usage

### Basic Usage

```bash
controller-relay.exe COM3
```

Replace `COM3` with the serial port your USB-to-UART adapter is connected to. On Windows, you can find the port in Device Manager under "Ports (COM & LPT)".

### With Configuration File

Create a file named `controller-relay.config` in the same directory as the executable. See the [Configuration](#configuration) section below.

## Default Hotkeys

All hotkeys require holding the **Left Stick Button (LS)** plus another button:

| Hotkey | Action |
|--------|--------|
| **LS + Plus** | Send HOME button to Switch |
| **LS + B** | Start/Stop macro recording |
| **LS + A** | Play recorded macro once |
| **LS + X** | Toggle macro looping |
| **LS + RS** | Quit controller relay and companion app |

**Note**: Button names use Nintendo Switch layout. Map these to your XInput controller accordingly (e.g., Plus = Start, LS = L3).

## Configuration

Create a file named `controller-relay.config` in the same directory as the executable. An example file is provided as `controller-relay.config.example`.

### Available Options

```ini
# Launch a companion application (e.g., capture software)
CompanionApp="C:\Program Files\OBS Studio\bin\64bit\obs64.exe"

# Auto-click coordinates after launching companion app
# Useful for clicking fullscreen buttons, etc.
AutoClickX=50
AutoClickY=20
AutoClickDelay=2000        # Milliseconds to wait before clicking
AutoClickRelative=true     # Coordinates relative to window (not screen)

# Firmware type: auto (default), native, or pabotbase
# - auto: tries native handshake first, then PABotBase
# - native: original UART relay firmware (3-step sync + CRC8)
# - pabotbase: PokemonAutomation PABotBase firmware (CRC32C + sequence numbers)
FirmwareType=auto

# Controller type to emulate when using PABotBase firmware (default: wireless-pro)
# Nintendo Switch 1:
#   wired, wired-pro, wired-left-joycon, wired-right-joycon,
#   wireless-pro, wireless-left-joycon, wireless-right-joycon
# Nintendo Switch 2:
#   ns2-wired, ns2-wired-pro, ns2-wired-left-joycon, ns2-wired-right-joycon,
#   ns2-wireless-pro, ns2-wireless-left-joycon, ns2-wireless-right-joycon
ControllerType=wireless-pro

# Customize hotkeys (button names: Y, B, A, X, L, R, ZL, ZR, LS, RS, Plus, Minus, Home, Capture)
HotkeyEnable=LS           # Enable button (must be held with others)
HotkeySendHome=Plus       # LS + Plus = Send HOME
HotkeyQuit=RS             # LS + RS = Quit
HotkeyMacroRecord=B       # LS + B = Record macro
HotkeyMacroPlayOnce=A     # LS + A = Play macro once
HotkeyMacroLoop=X         # LS + X = Loop macro
```

### Configuration Notes

- Lines starting with `#` are comments
- Paths should be quoted: `CompanionApp="C:\Path\To\App.exe"`
- Button names are case-insensitive
- `AutoClickRelative=true` makes coordinates relative to the companion app window (recommended)

## Macro Recording

### Recording a Macro

1. Start controller-relay
2. Press **LS + B** to start recording
3. Perform your controller inputs
4. Press **LS + B** again to stop recording
5. The macro is automatically saved as `macro.macro` in the application directory

### Playing Back a Macro

- **Play once**: Press **LS + A**
- **Loop forever**: Press **LS + X** (press again to stop)

### Macro Looping

When a macro is looping, it will continue even if you disconnect your controller! This is perfect for:
- Long farming sessions
- AFK grinding
- Overnight automation

Press **LS + X** again or restart the application to stop looping.

## Creating Standalone Bots

The recorded `.macro` file can be converted into firmware that runs without a PC. This allows you to:
- Share macros with friends (just send the `.hex` file)
- Run automation without keeping your computer on
- Create dedicated bot devices

### Steps to Create a Standalone Bot

1. **Record your macro** using controller-relay (creates `macro.macro`)
2. **Build standalone firmware** with the embedded macro:
   ```bash
   cd ../firmware
   make standalone MACRO=../controller-relay/macro.macro LOOP=1
   ```
3. **Flash the firmware** to your microcontroller:
   ```bash
   make flash-standalone
   ```
4. **Plug into Switch** - the macro runs automatically!

See the [firmware README](../firmware/README.md) for detailed instructions on building and flashing standalone firmware.

## Troubleshooting

### Controller Not Detected

- Ensure your controller is XInput-compatible (Xbox controllers work best)
- Try reconnecting the controller
- Check Windows' "Set up USB game controllers" to verify detection

### Serial Port Connection Failed

- Verify the COM port number in Device Manager
- Ensure no other application is using the serial port
- Try unplugging and reconnecting the USB-to-UART adapter
- On FTDI adapters, you may need to [reduce the latency timer](https://projectgus.com/2011/10/notes-on-ftdi-latency-with-arduino/)

### High Latency/Input Lag

- Use a high-quality USB-to-UART adapter (CP210x recommended)
- Reduce FTDI latency timer if using FTDI adapter
- Ensure capture card quality is good (if streaming)
- Check for USB bandwidth issues (try different USB ports)

### Macro Not Recording

- Ensure you pressed LS + B to start recording
- Check console output for error messages
- Verify write permissions in the application directory

## Technical Details

- **Baud Rate**: 1,000,000 bps (1 Mbps)
- **Packet Rate**: 125 Hz (one packet every 8ms, matching USB polling rate)
- **Packet Format**: 8-byte controller state packets
- **Macro Format**: Human-readable text format (`.macro` files) with include support and loop syntax
- **Macro Includes**: Support for `@filename.macro,*N` to include and repeat macros N times

## See Also

- [Firmware README](../firmware/README.md) - How to build and flash microcontroller firmware
- [Root README](../README.md) - Project overview and quick start
