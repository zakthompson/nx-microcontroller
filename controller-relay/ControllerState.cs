using static SwitchController.SwitchControllerConstants;

namespace SwitchController
{
    /// <summary>
    /// Protocol-neutral 7-byte controller state shared by all transports
    /// </summary>
    public struct ControllerState
    {
        public byte Buttons0;   // bits 0-7:  Y, B, A, X, L, R, ZL, ZR
        public byte Buttons1;   // bits 8-15: Minus, Plus, LClick, RClick, Home, Capture
        public byte Dpad;       // HAT value 0-8
        public byte LX, LY, RX, RY;  // stick axes (0x00-0xFF, 0x80 = center)

        /// <summary>
        /// Returns a neutral idle state (no buttons pressed, sticks centered)
        /// </summary>
        public static ControllerState Idle => new ControllerState
        {
            Buttons0 = 0,
            Buttons1 = 0,
            Dpad = HAT_NEUTRAL,
            LX = AXIS_CENTER,
            LY = AXIS_CENTER,
            RX = AXIS_CENTER,
            RY = AXIS_CENTER
        };

        /// <summary>
        /// Converts to the 8-byte native wire packet [buttons1, buttons0, dpad, LX, LY, RX, RY, 0x00]
        /// </summary>
        public byte[] ToNativePacket()
        {
            return new byte[] { Buttons1, Buttons0, Dpad, LX, LY, RX, RY, 0x00 };
        }

        /// <summary>
        /// Parses a ControllerState from the 8-byte native wire packet
        /// </summary>
        public static ControllerState FromNativePacket(byte[] packet)
        {
            return new ControllerState
            {
                Buttons1 = packet[0],
                Buttons0 = packet[1],
                Dpad = packet[2],
                LX = packet[3],
                LY = packet[4],
                RX = packet[5],
                RY = packet[6]
            };
        }

        /// <summary>
        /// Converts to the 10-byte OEM controller state used by PA's 0xa0 command.
        /// Format: button3, button4, button5, left_joystick[3], right_joystick[3], vibrator
        /// </summary>
        public byte[] ToOemState()
        {
            byte button3 = 0; // Right-side buttons
            byte button4 = 0; // Center buttons
            byte button5 = 0; // Left-side + D-pad buttons

            // Buttons0 → button3/button5
            if ((Buttons0 & (byte)BTN_Y) != 0)  button3 |= OEM_BTN3_Y;
            if ((Buttons0 & (byte)BTN_B) != 0)  button3 |= OEM_BTN3_B;
            if ((Buttons0 & (byte)BTN_A) != 0)  button3 |= OEM_BTN3_A;
            if ((Buttons0 & (byte)BTN_X) != 0)  button3 |= OEM_BTN3_X;
            if ((Buttons0 & (byte)BTN_R) != 0)  button3 |= OEM_BTN3_R;
            if ((Buttons0 & (byte)BTN_ZR) != 0) button3 |= OEM_BTN3_ZR;
            if ((Buttons0 & (byte)BTN_L) != 0)  button5 |= OEM_BTN5_L;
            if ((Buttons0 & (byte)BTN_ZL) != 0) button5 |= OEM_BTN5_ZL;

            // Buttons1 → button4
            if ((Buttons1 & (byte)(BTN_MINUS >> 8)) != 0)  button4 |= OEM_BTN4_MINUS;
            if ((Buttons1 & (byte)(BTN_PLUS >> 8)) != 0)   button4 |= OEM_BTN4_PLUS;
            if ((Buttons1 & (byte)(BTN_LCLICK >> 8)) != 0) button4 |= OEM_BTN4_LCLICK;
            if ((Buttons1 & (byte)(BTN_RCLICK >> 8)) != 0) button4 |= OEM_BTN4_RCLICK;
            if ((Buttons1 & (byte)(BTN_HOME >> 8)) != 0)   button4 |= OEM_BTN4_HOME;
            if ((Buttons1 & (byte)(BTN_CAPTURE >> 8)) != 0) button4 |= OEM_BTN4_CAPTURE;

            // HAT → button5 D-pad bits
            button5 |= HatToOemDpad(Dpad);

            // Convert 8-bit axes to 12-bit, pack into 3 bytes each.
            // OEM Y-axis is opposite to our internal format: 0xFFF=up, 0x000=down.
            // Our internal format (after INVERT_Y_AXIS): 0x00=up, 0xFF=down.
            byte[] lStick = PackJoystick12Bit(LX, (byte)(0xFF - LY));
            byte[] rStick = PackJoystick12Bit(RX, (byte)(0xFF - RY));

            return new byte[]
            {
                button3, button4, button5,
                lStick[0], lStick[1], lStick[2],
                rStick[0], rStick[1], rStick[2],
                0x00 // vibrator
            };
        }

        private static byte HatToOemDpad(byte hat)
        {
            switch (hat)
            {
                case HAT_UP:         return OEM_BTN5_UP;
                case HAT_UP_RIGHT:   return (byte)(OEM_BTN5_UP | OEM_BTN5_RIGHT);
                case HAT_RIGHT:      return OEM_BTN5_RIGHT;
                case HAT_DOWN_RIGHT: return (byte)(OEM_BTN5_DOWN | OEM_BTN5_RIGHT);
                case HAT_DOWN:       return OEM_BTN5_DOWN;
                case HAT_DOWN_LEFT:  return (byte)(OEM_BTN5_DOWN | OEM_BTN5_LEFT);
                case HAT_LEFT:       return OEM_BTN5_LEFT;
                case HAT_UP_LEFT:    return (byte)(OEM_BTN5_UP | OEM_BTN5_LEFT);
                default:             return 0;
            }
        }

        /// <summary>
        /// Packs two 8-bit axis values into 3 bytes of 12-bit encoding.
        /// Format: [X_lo8] [X_hi4 | Y_lo4] [Y_hi8]
        /// </summary>
        private static byte[] PackJoystick12Bit(byte x8, byte y8)
        {
            // 8-bit to 12-bit: replicate upper nibble into lower for full range
            ushort x12 = (ushort)((x8 << 4) | (x8 >> 4));
            ushort y12 = (ushort)((y8 << 4) | (y8 >> 4));

            return new byte[]
            {
                (byte)(x12 & 0xFF),
                (byte)((x12 >> 8) | ((y12 & 0x0F) << 4)),
                (byte)(y12 >> 4)
            };
        }
    }
}
