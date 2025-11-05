# NX-Microcontroller

This project contains tools for using a microcontroller, such as an Arduino or Teensy, as a controller for a Nintendo Switch.

Currently, the project has two parts.

## Firmware

- Found in @firmware
- Written in C (with one python tool)
- Can build firmware either to accept UART input and relay as to a Switch console, OR firmware with an embedded macro that will run when the microcontroller is plugged into the Switch

## Controller Relay

- Found in @controller-relay
- Written in C#
- Listens to XInput controller events and relays them over a serial port to the microcontroller running the UART firmware
- Can optionally launch a companion app when it launches, and will close the app when it exits
- Can optionally send input to the companion app after it launches (such as clicking a fullscreen button in capture software)
- Can record macros using a button combination to start and stop recording, and then execute those macros either once or on a loop
