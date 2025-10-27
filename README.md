[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/R5R61N70AU)

# NX Microcontroller

Use a microcontroller (Arduino Uno R3, Teensy 2.0++, etc.) as a Nintendo Switch controller. Stream your Switch gameplay remotely, record and playback macros, or build standalone automation bots.

https://github.com/user-attachments/assets/12d42620-9a44-4d1f-9b6e-9e876d126d2a

^ _Playing my **real Switch** remotely on a Retroid Pocket 5 via streaming._

## Overview

This project provides two complementary tools for controlling a Nintendo Switch using LUFA-compatible microcontrollers:

### 🎮 Controller Relay

A Windows application that captures XInput controller input and relays it to a microcontroller connected via USB serial. Perfect for:

- Remote gameplay streaming (combine with a capture card)
- Recording and replaying macros
- Creating automation sequences for grinding/farming

See [controller-relay/README.md](controller-relay/README.md) for details.

### 🔧 Firmware

Custom firmware that makes your microcontroller appear as a Pro Controller to the Switch. Build in two modes:

- **Relay mode**: Accept input from the Controller Relay software via UART
- **Standalone mode**: Embed macros directly into the firmware to run without a PC

See [firmware/README.md](firmware/README.md) for details.

## Prerequisites

### Required

- **LUFA-compatible microcontroller**: Arduino Uno R3, Teensy 2.0++, or similar
  - Must have USB capabilities and be compatible with the LUFA framework

### Optional (for Controller Relay)

- **USB-to-UART adapter**: CP210x, FTDI, or CH340-based adapter
  - Required to communicate with the microcontroller in relay mode
  - Note: FTDI adapters may need [latency timer adjustments](https://projectgus.com/2011/10/notes-on-ftdi-latency-with-arduino/)
- **HDMI capture card**: USB-based capture card for streaming gameplay
  - Quality significantly impacts streaming performance
  - Must be compatible with standard capture software

## Quick Start

### For Remote Play with Controller Relay

1. Flash relay firmware to your microcontroller (see [firmware/README.md](firmware/README.md))
2. Connect microcontroller to Switch via USB
3. Connect microcontroller to PC via USB-to-UART adapter
4. Build and run Controller Relay (see [controller-relay/README.md](controller-relay/README.md))
5. Connect your XInput controller to PC

### For Standalone Macro Bot

1. Record a macro using Controller Relay (or write it by hand)
2. Build standalone firmware with embedded macro (see [firmware/README.md](firmware/README.md))
3. Flash to microcontroller
4. Plug microcontroller into Switch - macro runs automatically!

## Acknowledgments

- **javmarina**, from which the relay firmware was adapted.
- **mzyy94** for his [work](https://mzyy94.com/blog/2020/03/20/nintendo-switch-pro-controller-usb-gadget/) on Pro Controller emulation using a Raspberry Pi.
- **wchill** for the [SwitchInputEmulator](https://github.com/wchill/SwitchInputEmulator) project. Javmarina's firmware was initially based on his work.
- **progmem** for the [Switch-Fightstick](https://github.com/progmem/Switch-Fightstick) repository, which itself is the base of **wchill** work and created the opportunity to control the Switch with a LUFA-compatible MCU.
- **abcminiuser**, who created the [LUFA](https://github.com/abcminiuser/lufa) library (Lightweight USB Framework for AVRs).
