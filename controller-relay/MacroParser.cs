using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SwitchController
{
    /// <summary>
    /// Parses human-readable macro text format into MacroFrame list
    /// </summary>
    public static class MacroParser
    {
        /// <summary>
        /// Parses a macro file in text format
        /// </summary>
        /// <param name="filePath">Path to the .macro file</param>
        /// <returns>List of macro frames</returns>
        /// <exception cref="MacroParseException">Thrown when parsing fails</exception>
        public static List<MacroFrame> ParseFile(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Macro file not found: {filePath}");

            var lines = File.ReadAllLines(filePath);
            return ParseLines(lines, filePath);
        }

        /// <summary>
        /// Parses macro text content
        /// </summary>
        /// <param name="content">Macro text content</param>
        /// <returns>List of macro frames</returns>
        public static List<MacroFrame> ParseContent(string content)
        {
            var lines = content.Split('\n');
            return ParseLines(lines, "<content>");
        }

        /// <summary>
        /// Parses an array of macro text lines
        /// </summary>
        private static List<MacroFrame> ParseLines(string[] lines, string source)
        {
            var frames = new List<MacroFrame>();
            var commands = new List<(MacroFormat.MacroCommand Command, int LineNumber)>();

            // Parse all commands
            for (int i = 0; i < lines.Length; i++)
            {
                int lineNumber = i + 1;
                string line = lines[i].Trim();

                // Skip empty lines and comments
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#") || line.StartsWith("//"))
                    continue;

                try
                {
                    var command = ParseLine(line);
                    commands.Add((command, lineNumber));
                }
                catch (Exception ex)
                {
                    throw new MacroParseException($"Error on line {lineNumber}: {ex.Message}", source, lineNumber);
                }
            }

            if (commands.Count == 0)
                throw new MacroParseException("No valid commands found in macro file", source, 0);

            // Convert commands to frames with smart transitions
            long currentTimestamp = 0;
            MacroFormat.MacroCommand? previousCommand = null;

            foreach (var (command, lineNumber) in commands)
            {
                try
                {
                    // Build packet for this command
                    byte[] packet = BuildPacket(command, previousCommand);

                    // Add frame at current timestamp
                    frames.Add(new MacroFrame
                    {
                        TimestampMs = currentTimestamp,
                        Packet = packet
                    });

                    // Advance timestamp
                    currentTimestamp += command.DurationMs;
                    previousCommand = command;
                }
                catch (Exception ex)
                {
                    throw new MacroParseException($"Error building packet for line {lineNumber}: {ex.Message}", source, lineNumber);
                }
            }

            // Add final neutral frame to release all inputs
            frames.Add(new MacroFrame
            {
                TimestampMs = currentTimestamp,
                Packet = new byte[] { 0, 0, SwitchControllerConstants.HAT_NEUTRAL, 128, 128, 128, 128, 0 }
            });

            return frames;
        }

        /// <summary>
        /// Parses a single line into a MacroCommand
        /// </summary>
        private static MacroFormat.MacroCommand ParseLine(string line)
        {
            // Remove inline comments
            int commentIndex = line.IndexOf('#');
            if (commentIndex >= 0)
                line = line.Substring(0, commentIndex);
            commentIndex = line.IndexOf("//");
            if (commentIndex >= 0)
                line = line.Substring(0, commentIndex);

            line = line.Trim();

            // Split by comma: inputs,duration
            string[] parts = line.Split(',');
            if (parts.Length != 2)
                throw new FormatException("Line must be in format: inputs,duration");

            string inputsPart = parts[0].Trim();
            string durationPart = parts[1].Trim();

            // Parse duration
            if (!MacroFormat.TryParseInt(durationPart, out int duration))
                throw new FormatException($"Invalid duration: {durationPart}");

            if (duration < 0)
                throw new FormatException("Duration cannot be negative");

            var command = new MacroFormat.MacroCommand { DurationMs = duration };

            // Handle neutral state
            if (string.IsNullOrWhiteSpace(inputsPart) || MacroFormat.NeutralKeywords.Contains(inputsPart))
            {
                // Neutral command - no inputs
                return command;
            }

            // Parse inputs (separated by +, allowing spaces)
            string[] inputs = inputsPart.Split('+').Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();

            foreach (string input in inputs)
            {
                ParseInput(input, command);
            }

            return command;
        }

        /// <summary>
        /// Parses a single input token and updates the command
        /// </summary>
        private static void ParseInput(string input, MacroFormat.MacroCommand command)
        {
            input = input.Trim();

            // Check for complex analog format: L(x,y) or R(x,y)
            if (input.StartsWith("L(", StringComparison.OrdinalIgnoreCase) && input.EndsWith(")"))
            {
                ParseComplexAnalog(input.Substring(2, input.Length - 3), true, command);
                return;
            }
            if (input.StartsWith("R(", StringComparison.OrdinalIgnoreCase) && input.EndsWith(")"))
            {
                ParseComplexAnalog(input.Substring(2, input.Length - 3), false, command);
                return;
            }

            // Check for button
            if (MacroFormat.ButtonMap.ContainsKey(input))
            {
                command.Inputs.Add(input);
                return;
            }

            // Check for D-Pad
            if (MacroFormat.DPadMap.ContainsKey(input))
            {
                command.Inputs.Add(input);
                return;
            }

            // Check for left stick cardinal
            if (MacroFormat.LeftStickMap.TryGetValue(input, out var leftStickValues))
            {
                command.LeftX = leftStickValues.X;
                command.LeftY = leftStickValues.Y;
                return;
            }

            // Check for right stick cardinal
            if (MacroFormat.RightStickMap.TryGetValue(input, out var rightStickValues))
            {
                command.RightX = rightStickValues.X;
                command.RightY = rightStickValues.Y;
                return;
            }

            throw new FormatException($"Unknown input: {input}");
        }

        /// <summary>
        /// Parses complex analog format: "x,y"
        /// </summary>
        private static void ParseComplexAnalog(string coords, bool isLeftStick, MacroFormat.MacroCommand command)
        {
            string[] parts = coords.Split(',');
            if (parts.Length != 2)
                throw new FormatException($"Complex analog must be in format: L(x,y) or R(x,y)");

            if (!MacroFormat.TryParseInt(parts[0], out int x) || x < 0 || x > 255)
                throw new FormatException($"Invalid X coordinate: {parts[0]} (must be 0-255)");

            if (!MacroFormat.TryParseInt(parts[1], out int y) || y < 0 || y > 255)
                throw new FormatException($"Invalid Y coordinate: {parts[1]} (must be 0-255)");

            if (isLeftStick)
            {
                command.LeftX = (byte)x;
                command.LeftY = (byte)y;
            }
            else
            {
                command.RightX = (byte)x;
                command.RightY = (byte)y;
            }
        }

        /// <summary>
        /// Builds an 8-byte packet from a MacroCommand
        /// </summary>
        private static byte[] BuildPacket(MacroFormat.MacroCommand command, MacroFormat.MacroCommand? previousCommand)
        {
            // Start with neutral packet
            byte[] packet = new byte[] { 0, 0, SwitchControllerConstants.HAT_NEUTRAL, 128, 128, 128, 128, 0 };

            ushort buttons = 0;
            byte hat = SwitchControllerConstants.HAT_NEUTRAL;

            // Process inputs
            foreach (string input in command.Inputs)
            {
                // Check button
                if (MacroFormat.ButtonMap.TryGetValue(input, out ushort buttonMask))
                {
                    buttons |= buttonMask;
                    continue;
                }

                // Check D-Pad
                if (MacroFormat.DPadMap.TryGetValue(input, out byte hatValue))
                {
                    hat = hatValue;
                    continue;
                }
            }

            // Set stick values from command (default to center if not specified)
            // Each line specifies the complete state - no inheritance from previous commands
            byte lx = command.LeftX ?? 128;
            byte ly = command.LeftY ?? 128;
            byte rx = command.RightX ?? 128;
            byte ry = command.RightY ?? 128;

            // Build packet
            packet[0] = (byte)(buttons >> 8);    // buttons_hi
            packet[1] = (byte)(buttons & 0xFF);  // buttons_lo
            packet[2] = hat;                     // HAT
            packet[3] = lx;                      // LX
            packet[4] = ly;                      // LY
            packet[5] = rx;                      // RX
            packet[6] = ry;                      // RY
            packet[7] = 0;                       // vendor

            return packet;
        }
    }

    /// <summary>
    /// Exception thrown when macro parsing fails
    /// </summary>
    public class MacroParseException : Exception
    {
        public new string Source { get; }
        public int LineNumber { get; }

        public MacroParseException(string message, string source, int lineNumber)
            : base(message)
        {
            Source = source;
            LineNumber = lineNumber;
        }
    }
}
