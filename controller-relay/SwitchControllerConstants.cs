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
        public const int USB_FRAME_INTERVAL_MS = 8;     // 125Hz USB polling (must match firmware descriptor)
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

        // PA (PokemonAutomation) protocol constants
        public const byte PA_MSG_SEQNUM_RESET = 0x40;
        public const byte PA_MSG_CHANGE_CONTROLLER_MODE = 0x48;
        public const byte PA_MSG_WIRED_CONTROLLER_STATE = 0x90;
        public const byte PA_MSG_OEM_CONTROLLER_BUTTONS = 0xa0;
        public const byte PA_MSG_ACK_COMMAND = 0x10;
        public const byte PA_MSG_ACK_REQUEST = 0x11;
        public const byte PA_MSG_COMMAND_FINISHED = 0x4a;
        public const byte PA_MSG_COMMAND_DROPPED = 0x06;
        public const int PA_PROTOCOL_OVERHEAD = 6;         // ~len(1) + type(1) + CRC32(4)
        public const int PA_MAX_PACKET_SIZE = 64;
        public const int PA_QUEUE_SIZE = 4;
        public const int PA_RETRANSMIT_MS = 100;
        public const int PA_SEND_INTERVAL_WIRED_MS = 8;    // 125Hz for wired controllers
        public const int PA_SEND_INTERVAL_WIRELESS_MS = 15; // ~67Hz for wireless controllers
        public const ushort PA_COMMAND_DURATION_WIRED_MS = 24;   // 3x send interval — ensures next command is queued before current expires
        public const ushort PA_COMMAND_DURATION_WIRELESS_MS = 45; // 3x send interval

        // PA controller type IDs
        public const uint PA_CID_WIRED_CONTROLLER = 0x1000;
        public const uint PA_CID_WIRED_PRO_CONTROLLER = 0x1100;
        public const uint PA_CID_WIRELESS_PRO_CONTROLLER = 0x1180;

        // OEM button3 (right-side buttons)
        public const byte OEM_BTN3_Y = 1 << 0;
        public const byte OEM_BTN3_X = 1 << 1;
        public const byte OEM_BTN3_B = 1 << 2;
        public const byte OEM_BTN3_A = 1 << 3;
        public const byte OEM_BTN3_R = 1 << 6;
        public const byte OEM_BTN3_ZR = 1 << 7;

        // OEM button4 (center buttons)
        public const byte OEM_BTN4_MINUS = 1 << 0;
        public const byte OEM_BTN4_PLUS = 1 << 1;
        public const byte OEM_BTN4_RCLICK = 1 << 2;
        public const byte OEM_BTN4_LCLICK = 1 << 3;
        public const byte OEM_BTN4_HOME = 1 << 4;
        public const byte OEM_BTN4_CAPTURE = 1 << 5;

        // OEM button5 (left-side + D-pad)
        public const byte OEM_BTN5_DOWN = 1 << 0;
        public const byte OEM_BTN5_UP = 1 << 1;
        public const byte OEM_BTN5_RIGHT = 1 << 2;
        public const byte OEM_BTN5_LEFT = 1 << 3;
        public const byte OEM_BTN5_L = 1 << 6;
        public const byte OEM_BTN5_ZL = 1 << 7;
    }

    /// <summary>
    /// Switch controller button names for configuration
    /// </summary>
    public enum SwitchButton
    {
        None,
        LS,      // Left stick click
        RS,      // Right stick click
        Up,      // D-Pad Up
        Down,    // D-Pad Down
        Left,    // D-Pad Left
        Right,   // D-Pad Right
        L,       // Left shoulder
        R,       // Right shoulder
        ZL,      // Left trigger
        ZR,      // Right trigger
        A,       // A button
        B,       // B button
        X,       // X button
        Y,       // Y button
        Plus,    // Plus/Start button
        Minus    // Minus/Select button
    }
}
