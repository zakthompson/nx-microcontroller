using System;
using SharpDX.XInput;
using static SwitchController.SwitchControllerConstants;

namespace SwitchController
{
    /// <summary>
    /// Builds Switch Pro Controller input packets from XInput gamepad state
    /// </summary>
    public static class PacketBuilder
    {
        /// <summary>
        /// Builds an 8-byte Pokkén report from gamepad state
        /// </summary>
        /// <param name="gamepad">XInput gamepad state</param>
        /// <param name="forceHome">If true, forces HOME button to be pressed</param>
        /// <returns>8-byte packet (buttons_hi, buttons_lo, HAT, LX, LY, RX, RY, vendor)</returns>
        public static byte[] BuildPacket(Gamepad gamepad, bool forceHome = false)
        {
            ushort buttons = BuildButtonMask(gamepad, forceHome);
            byte hat = BuildHatValue(gamepad.Buttons);

            // Build axis values with deadzone and inversion
            (byte lx, byte ly, byte rx, byte ry) = BuildAxisValues(gamepad);

            // Wire order: buttons_hi, buttons_lo, HAT, LX, LY, RX, RY, vendor
            return new byte[]
            {
                (byte)((buttons >> 8) & 0xFF),  // buttons high
                (byte)(buttons & 0xFF),         // buttons low
                hat,
                lx, ly, rx, ry,
                0x00                            // vendor byte
            };
        }

        /// <summary>
        /// Builds the 16-bit button mask from gamepad state
        /// </summary>
        private static ushort BuildButtonMask(Gamepad gamepad, bool forceHome)
        {
            ushort btn = 0;

            // ABXY mapping: Xbox -> Nintendo remap
            // Xbox A (bottom) -> Nintendo B (bottom)
            // Xbox B (right)  -> Nintendo A (right)
            // Xbox X (left)   -> Nintendo Y (left)
            // Xbox Y (top)    -> Nintendo X (top)
            if ((gamepad.Buttons & GamepadButtonFlags.A) != 0) btn |= BTN_B;
            if ((gamepad.Buttons & GamepadButtonFlags.B) != 0) btn |= BTN_A;
            if ((gamepad.Buttons & GamepadButtonFlags.X) != 0) btn |= BTN_Y;
            if ((gamepad.Buttons & GamepadButtonFlags.Y) != 0) btn |= BTN_X;

            // Shoulders and clicks
            if ((gamepad.Buttons & GamepadButtonFlags.LeftShoulder) != 0) btn |= BTN_L;
            if ((gamepad.Buttons & GamepadButtonFlags.RightShoulder) != 0) btn |= BTN_R;
            if ((gamepad.Buttons & GamepadButtonFlags.LeftThumb) != 0) btn |= BTN_LCLICK;
            if ((gamepad.Buttons & GamepadButtonFlags.RightThumb) != 0) btn |= BTN_RCLICK;

            // System buttons
            if ((gamepad.Buttons & GamepadButtonFlags.Start) != 0) btn |= BTN_PLUS;
            if ((gamepad.Buttons & GamepadButtonFlags.Back) != 0) btn |= BTN_MINUS;

            // Triggers -> ZL / ZR
            if (gamepad.LeftTrigger > TRIGGER_THRESHOLD) btn |= BTN_ZL;
            if (gamepad.RightTrigger > TRIGGER_THRESHOLD) btn |= BTN_ZR;

            // Special combo: Select + Y -> HOME button
            bool selectPressed = (gamepad.Buttons & GamepadButtonFlags.Back) != 0;
            bool yPressed = (gamepad.Buttons & GamepadButtonFlags.Y) != 0;
            if (selectPressed && yPressed)
            {
                btn |= BTN_HOME;
                // Clear MINUS and X to avoid triggering on Switch
                btn &= unchecked((ushort)(~BTN_MINUS));
                btn &= unchecked((ushort)(~BTN_X));
            }

            // Force HOME if requested (for 'H' key press)
            if (forceHome)
            {
                btn |= BTN_HOME;
            }

            return btn;
        }

        /// <summary>
        /// Builds the HAT (D-Pad) value from button flags
        /// </summary>
        private static byte BuildHatValue(GamepadButtonFlags buttons)
        {
            bool up = (buttons & GamepadButtonFlags.DPadUp) != 0;
            bool down = (buttons & GamepadButtonFlags.DPadDown) != 0;
            bool left = (buttons & GamepadButtonFlags.DPadLeft) != 0;
            bool right = (buttons & GamepadButtonFlags.DPadRight) != 0;

            if (up && right) return HAT_UP_RIGHT;
            if (right && down) return HAT_DOWN_RIGHT;
            if (down && left) return HAT_DOWN_LEFT;
            if (left && up) return HAT_UP_LEFT;
            if (up) return HAT_UP;
            if (right) return HAT_RIGHT;
            if (down) return HAT_DOWN;
            if (left) return HAT_LEFT;
            return HAT_NEUTRAL;
        }

        /// <summary>
        /// Builds axis values with deadzone processing and optional Y-inversion
        /// </summary>
        private static (byte lx, byte ly, byte rx, byte ry) BuildAxisValues(Gamepad gamepad)
        {
            int lx = gamepad.LeftThumbX;
            int ly = gamepad.LeftThumbY;
            int rx = gamepad.RightThumbX;
            int ry = gamepad.RightThumbY;

            // Apply Joy-Con style deadzones
            ApplyJoyConDeadzone(ref lx, ref ly);
            ApplyJoyConDeadzone(ref rx, ref ry);

            // Invert Y axes if configured (Nintendo style)
            if (INVERT_Y_AXIS)
            {
                ly = -ly;
                ry = -ry;
            }

            // Convert to 8-bit and snap to center
            byte lx8 = Map16BitTo8Bit(Clamp(lx));
            byte ly8 = Map16BitTo8Bit(Clamp(ly));
            byte rx8 = Map16BitTo8Bit(Clamp(rx));
            byte ry8 = Map16BitTo8Bit(Clamp(ry));

            SnapToCenter(ref lx8);
            SnapToCenter(ref ly8);
            SnapToCenter(ref rx8);
            SnapToCenter(ref ry8);

            return (lx8, ly8, rx8, ry8);
        }

        /// <summary>
        /// Applies Joy-Con style deadzone: radial deadzone + axial deadzone to prevent cross-axis bleeding
        /// </summary>
        private static void ApplyJoyConDeadzone(ref int x, ref int y)
        {
            // Radial deadzone (~15% like Joy-Con)
            long magSquared = (long)x * x + (long)y * y;
            long radialThresholdSquared = (long)RADIAL_DEADZONE * RADIAL_DEADZONE;

            if (magSquared < radialThresholdSquared)
            {
                x = 0;
                y = 0;
                return;
            }

            // Axial deadzone (prevents cross-axis bleeding)
            int absX = Math.Abs(x);
            int absY = Math.Abs(y);

            if (absX > absY * 3 && absY < AXIAL_DEADZONE)
            {
                y = 0;  // Moving horizontally, zero minor vertical
            }
            else if (absY > absX * 3 && absX < AXIAL_DEADZONE)
            {
                x = 0;  // Moving vertically, zero minor horizontal
            }
        }

        /// <summary>
        /// Clamps axis value to avoid -32768 edge case
        /// </summary>
        private static int Clamp(int value)
        {
            if (value < -32767) return -32767;
            if (value > 32767) return 32767;
            return value;
        }

        /// <summary>
        /// Maps 16-bit signed axis value to 8-bit unsigned (0-255)
        /// </summary>
        private static byte Map16BitTo8Bit(int value)
        {
            int unsigned = value + 32768;  // Convert to 0-65535
            if (unsigned < 0) unsigned = 0;
            if (unsigned > 65535) unsigned = 65535;
            return (byte)((unsigned * 255L) / 65535L);
        }

        /// <summary>
        /// Snaps axis value to center (0x80) if within tolerance
        /// </summary>
        private static void SnapToCenter(ref byte value)
        {
            if (Math.Abs(value - AXIS_CENTER) <= AXIS_CENTER_TOLERANCE)
            {
                value = AXIS_CENTER;
            }
        }

        /// <summary>
        /// Calculates CRC8-CCITT checksum for a packet
        /// </summary>
        public static byte CalculateCrc8(byte[] data, int offset, int length)
        {
            byte crc = 0;
            for (int i = offset; i < offset + length; i++)
            {
                byte t = (byte)(crc ^ data[i]);
                for (int j = 0; j < 8; j++)
                {
                    t = (byte)(((t & 0x80) != 0) ? ((t << 1) ^ 0x07) : (t << 1));
                }
                crc = t;
            }
            return crc;
        }
    }
}
