using System;
using System.Collections.Generic;
using System.Globalization;

namespace SwitchController
{
    /// <summary>
    /// Defines the human-readable macro format and provides parsing/writing utilities
    /// </summary>
    public static class MacroFormat
    {
        // Cardinal tolerance for detecting stick positions when writing macros
        public const int CARDINAL_TOLERANCE = 5;

        // Recommended minimum duration (matches Switch USB polling rate)
        public const int RECOMMENDED_MIN_DURATION_MS = 8;

        /// <summary>
        /// Represents a parsed macro command with inputs and duration
        /// </summary>
        public class MacroCommand
        {
            public HashSet<string> Inputs { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public byte? LeftX { get; set; }
            public byte? LeftY { get; set; }
            public byte? RightX { get; set; }
            public byte? RightY { get; set; }
            public int DurationMs { get; set; }
        }

        /// <summary>
        /// Button name to bit mask mapping (case-insensitive)
        /// </summary>
        public static readonly Dictionary<string, ushort> ButtonMap = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
        {
            { "Y", SwitchControllerConstants.BTN_Y },
            { "B", SwitchControllerConstants.BTN_B },
            { "A", SwitchControllerConstants.BTN_A },
            { "X", SwitchControllerConstants.BTN_X },
            { "L", SwitchControllerConstants.BTN_L },
            { "R", SwitchControllerConstants.BTN_R },
            { "ZL", SwitchControllerConstants.BTN_ZL },
            { "ZR", SwitchControllerConstants.BTN_ZR },
            { "Minus", SwitchControllerConstants.BTN_MINUS },
            { "Plus", SwitchControllerConstants.BTN_PLUS },
            { "LClick", SwitchControllerConstants.BTN_LCLICK },
            { "RClick", SwitchControllerConstants.BTN_RCLICK },
            { "Home", SwitchControllerConstants.BTN_HOME },
            { "Capture", SwitchControllerConstants.BTN_CAPTURE }
        };

        /// <summary>
        /// D-Pad direction name to HAT value mapping
        /// </summary>
        public static readonly Dictionary<string, byte> DPadMap = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase)
        {
            { "Up", SwitchControllerConstants.HAT_UP },
            { "UpRight", SwitchControllerConstants.HAT_UP_RIGHT },
            { "Right", SwitchControllerConstants.HAT_RIGHT },
            { "DownRight", SwitchControllerConstants.HAT_DOWN_RIGHT },
            { "Down", SwitchControllerConstants.HAT_DOWN },
            { "DownLeft", SwitchControllerConstants.HAT_DOWN_LEFT },
            { "Left", SwitchControllerConstants.HAT_LEFT },
            { "UpLeft", SwitchControllerConstants.HAT_UP_LEFT }
        };

        /// <summary>
        /// Left stick cardinal direction to (X, Y) values
        /// </summary>
        public static readonly Dictionary<string, (byte X, byte Y)> LeftStickMap = new Dictionary<string, (byte, byte)>(StringComparer.OrdinalIgnoreCase)
        {
            { "LUp", (128, 255) },
            { "LDown", (128, 0) },
            { "LLeft", (0, 128) },
            { "LRight", (255, 128) },
            { "LUpLeft", (0, 255) },
            { "LUpRight", (255, 255) },
            { "LDownLeft", (0, 0) },
            { "LDownRight", (255, 0) }
        };

        /// <summary>
        /// Right stick cardinal direction to (X, Y) values
        /// </summary>
        public static readonly Dictionary<string, (byte X, byte Y)> RightStickMap = new Dictionary<string, (byte, byte)>(StringComparer.OrdinalIgnoreCase)
        {
            { "RUp", (128, 255) },
            { "RDown", (128, 0) },
            { "RLeft", (0, 128) },
            { "RRight", (255, 128) },
            { "RUpLeft", (0, 255) },
            { "RUpRight", (255, 255) },
            { "RDownLeft", (0, 0) },
            { "RDownRight", (255, 0) }
        };

        /// <summary>
        /// Neutral state keywords
        /// </summary>
        public static readonly HashSet<string> NeutralKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Wait", "Nothing", "Neutral"
        };

        /// <summary>
        /// Reverse button map for writing (bit mask to name)
        /// </summary>
        public static readonly Dictionary<ushort, string> ButtonNameMap = new Dictionary<ushort, string>
        {
            { SwitchControllerConstants.BTN_Y, "Y" },
            { SwitchControllerConstants.BTN_B, "B" },
            { SwitchControllerConstants.BTN_A, "A" },
            { SwitchControllerConstants.BTN_X, "X" },
            { SwitchControllerConstants.BTN_L, "L" },
            { SwitchControllerConstants.BTN_R, "R" },
            { SwitchControllerConstants.BTN_ZL, "ZL" },
            { SwitchControllerConstants.BTN_ZR, "ZR" },
            { SwitchControllerConstants.BTN_MINUS, "Minus" },
            { SwitchControllerConstants.BTN_PLUS, "Plus" },
            { SwitchControllerConstants.BTN_LCLICK, "LClick" },
            { SwitchControllerConstants.BTN_RCLICK, "RClick" },
            { SwitchControllerConstants.BTN_HOME, "Home" },
            { SwitchControllerConstants.BTN_CAPTURE, "Capture" }
        };

        /// <summary>
        /// Reverse D-Pad map for writing
        /// </summary>
        public static readonly Dictionary<byte, string> DPadNameMap = new Dictionary<byte, string>
        {
            { SwitchControllerConstants.HAT_UP, "Up" },
            { SwitchControllerConstants.HAT_UP_RIGHT, "UpRight" },
            { SwitchControllerConstants.HAT_RIGHT, "Right" },
            { SwitchControllerConstants.HAT_DOWN_RIGHT, "DownRight" },
            { SwitchControllerConstants.HAT_DOWN, "Down" },
            { SwitchControllerConstants.HAT_DOWN_LEFT, "DownLeft" },
            { SwitchControllerConstants.HAT_LEFT, "Left" },
            { SwitchControllerConstants.HAT_UP_LEFT, "UpLeft" }
        };

        /// <summary>
        /// Parses a single integer value, supporting decimal and hex (0x prefix)
        /// </summary>
        public static bool TryParseInt(string value, out int result)
        {
            value = value.Trim();
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return int.TryParse(value.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);
            }
            return int.TryParse(value, out result);
        }

        /// <summary>
        /// Checks if a value is close to a cardinal position (0, 128, or 255)
        /// </summary>
        public static bool IsCardinal(byte value, out byte cardinal)
        {
            if (Math.Abs(value - 0) <= CARDINAL_TOLERANCE)
            {
                cardinal = 0;
                return true;
            }
            if (Math.Abs(value - 128) <= CARDINAL_TOLERANCE)
            {
                cardinal = 128;
                return true;
            }
            if (Math.Abs(value - 255) <= CARDINAL_TOLERANCE)
            {
                cardinal = 255;
                return true;
            }
            cardinal = value;
            return false;
        }

        /// <summary>
        /// Attempts to convert stick values to a cardinal direction name
        /// </summary>
        public static bool TryGetStickCardinalName(byte x, byte y, bool isLeftStick, out string name)
        {
            // Check if both X and Y are cardinal values
            if (!IsCardinal(x, out byte cardinalX) || !IsCardinal(y, out byte cardinalY))
            {
                name = string.Empty;
                return false;
            }

            // Map to direction name
            var map = isLeftStick ? LeftStickMap : RightStickMap;
            foreach (var kvp in map)
            {
                if (kvp.Value.X == cardinalX && kvp.Value.Y == cardinalY)
                {
                    name = kvp.Key;
                    return true;
                }
            }

            name = string.Empty;
            return false;
        }

        /// <summary>
        /// Formats stick values for output - returns cardinal name if possible, otherwise L(x,y) format
        /// </summary>
        public static string FormatStickValue(byte x, byte y, bool isLeftStick)
        {
            // Skip if centered
            if (x == 128 && y == 128)
                return string.Empty;

            // Try to use cardinal name
            if (TryGetStickCardinalName(x, y, isLeftStick, out string cardinalName))
                return cardinalName;

            // Use explicit coordinate format
            string prefix = isLeftStick ? "L" : "R";
            return $"{prefix}({x},{y})";
        }

        /// <summary>
        /// Validates that a duration is reasonable and returns any warnings
        /// </summary>
        public static string? ValidateDuration(int durationMs)
        {
            if (durationMs < 0)
                return "Duration cannot be negative";

            if (durationMs == 0)
                return "Warning: 0ms duration may not register on the Switch";

            if (durationMs < RECOMMENDED_MIN_DURATION_MS && durationMs > 0)
                return $"Warning: Duration {durationMs}ms is below recommended minimum of {RECOMMENDED_MIN_DURATION_MS}ms";

            return null;
        }
    }
}
