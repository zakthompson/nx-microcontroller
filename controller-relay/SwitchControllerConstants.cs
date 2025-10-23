using System;

namespace SwitchController
{
    /// <summary>
    /// Constants for Switch Pro Controller emulation and serial communication
    /// </summary>
    public static class SwitchControllerConstants
    {
        // Timing and input settings
        public const int UPDATE_INTERVAL_MS = 1;        // 1ms = 1000Hz polling for minimum latency
        public const bool INVERT_Y_AXIS = true;         // Nintendo-style "up is negative"

        // Deadzone settings (matching Switch Joy-Con behavior)
        public const int RADIAL_DEADZONE = 4915;        // ~15% radial deadzone (Joy-Con default)
        public const int AXIAL_DEADZONE = 2000;         // ~6% axial deadzone (prevents cross-axis bleeding)

        public const byte TRIGGER_THRESHOLD = 30;       // Trigger threshold (0-255)

        // Pokkén 16-bit button bit masks (wire format)
        public const ushort BTN_Y = 1 << 0;
        public const ushort BTN_B = 1 << 1;
        public const ushort BTN_A = 1 << 2;
        public const ushort BTN_X = 1 << 3;
        public const ushort BTN_L = 1 << 4;
        public const ushort BTN_R = 1 << 5;
        public const ushort BTN_ZL = 1 << 6;
        public const ushort BTN_ZR = 1 << 7;
        public const ushort BTN_MINUS = 1 << 8;
        public const ushort BTN_PLUS = 1 << 9;
        public const ushort BTN_LCLICK = 1 << 10;
        public const ushort BTN_RCLICK = 1 << 11;
        public const ushort BTN_HOME = 1 << 12;
        public const ushort BTN_CAPTURE = 1 << 13;

        // Serial synchronization commands and responses
        public const byte CMD_SYNC_START = 0xFF;
        public const byte CMD_SYNC_1 = 0x33;
        public const byte CMD_SYNC_2 = 0xCC;
        public const byte RESP_SYNC_START = 0xFF;
        public const byte RESP_SYNC_1 = 0xCC;
        public const byte RESP_SYNC_OK = 0x33;
        public const byte RESP_UPDATE_ACK = 0x91;
        public const byte RESP_UPDATE_NACK = 0x92;
        public const byte RESP_USB_ACK = 0x90;

        // Axis mapping constants
        public const byte AXIS_CENTER = 0x80;
        public const byte AXIS_MIN = 0x00;
        public const byte AXIS_MAX = 0xFF;
        public const int AXIS_CENTER_TOLERANCE = 2;

        // HAT (D-Pad) direction values
        public const byte HAT_UP = 0x00;
        public const byte HAT_UP_RIGHT = 0x01;
        public const byte HAT_RIGHT = 0x02;
        public const byte HAT_DOWN_RIGHT = 0x03;
        public const byte HAT_DOWN = 0x04;
        public const byte HAT_DOWN_LEFT = 0x05;
        public const byte HAT_LEFT = 0x06;
        public const byte HAT_UP_LEFT = 0x07;
        public const byte HAT_NEUTRAL = 0x08;
    }
}
