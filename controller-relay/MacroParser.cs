using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.IO;
using System.Linq;

namespace SwitchController
{
    /// <summary>
    /// Parses human-readable macro text format into MacroFrame list
    /// </summary>
    public static class MacroParser
    {
        private const int MaxIncludeDepth = 10;

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
            string baseDirectory = Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? Environment.CurrentDirectory;
            var includeChain = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.GetFullPath(filePath) };

            return ParseLines(lines, filePath, baseDirectory, includeChain, 0);
        }

        /// <summary>
        /// Parses macro text content
        /// </summary>
        /// <param name="content">Macro text content</param>
        /// <returns>List of macro frames</returns>
        public static List<MacroFrame> ParseContent(string content)
        {
            var lines = content.Split('\n');
            string baseDirectory = Environment.CurrentDirectory;
            var includeChain = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            return ParseLines(lines, "<content>", baseDirectory, includeChain, 0);
        }

        /// <summary>
        /// Parses an array of macro text lines with include support
        /// </summary>
        private static List<MacroFrame> ParseLines(string[] lines, string source, string baseDirectory, HashSet<string> includeChain, int depth)
        {
            // Check recursion depth
            if (depth > MaxIncludeDepth)
                throw new MacroIncludeDepthException($"Maximum include depth of {MaxIncludeDepth} exceeded", source);

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

                // Handle include directive
                if (line.StartsWith("@"))
                {
                    // macro includes may include optional loop count
                    string includeDirective = line.Substring(1).Trim();
                    string includePath;
                    int loopCount = 1;

                    if (includeDirective.Contains("*"))
                    {
                        string[] parts = Regex.Split(includeDirective, @",\s*\*");
                        includePath = parts[0].Trim();

                        if (parts.Length != 2)
                        {
                            throw new MacroParseException($"Invalid include directive format: {includeDirective}", source, lineNumber);
                        }

                        // Parse loop count to variable loopCount
                        if (!int.TryParse(parts[1].Trim(), out loopCount) || loopCount < 1)
                        {
                            throw new MacroParseException($"Invalid loop count in include directive: {parts[1]}", source, lineNumber);
                        }
                    }
                    else
                    {
                        includePath = includeDirective;
                    }


                    try
                    {
                        // Resolve path relative to base directory
                        string fullIncludePath = Path.IsPathRooted(includePath)
                            ? includePath
                            : Path.GetFullPath(Path.Combine(baseDirectory, includePath));

                        // Check for circular includes
                        if (includeChain.Contains(fullIncludePath))
                        {
                            string chain = string.Join(" -> ", includeChain) + " -> " + fullIncludePath;
                            throw new MacroCircularIncludeException($"Circular include detected: {chain}", source, lineNumber);
                        }

                        // Check if file exists
                        if (!File.Exists(fullIncludePath))
                            throw new MacroIncludeNotFoundException($"Include file not found: {fullIncludePath}", source, lineNumber, fullIncludePath);

                        // Read and parse included file
                        var includedLines = File.ReadAllLines(fullIncludePath);
                        string includeBaseDirectory = Path.GetDirectoryName(fullIncludePath) ?? baseDirectory;

                        // Create new include chain with this file added
                        var newIncludeChain = new HashSet<string>(includeChain, StringComparer.OrdinalIgnoreCase)
                        {
                            fullIncludePath
                        };

                        // Recursively parse included file
                        var includedFrames = ParseLines(includedLines, fullIncludePath, includeBaseDirectory, newIncludeChain, depth + 1);

                        // Extract commands from included frames and repeat them loopCount times
                        for (int loop = 0; loop < loopCount; loop++)
                        {
                            foreach (var frame in includedFrames)
                            {
                                // Skip the final neutral frame that ParseLines adds
                                if (frame == includedFrames[^1])
                                    continue;

                                // Calculate duration for this frame
                                int frameIndex = includedFrames.IndexOf(frame);
                                long duration = (frameIndex < includedFrames.Count - 1)
                                    ? includedFrames[frameIndex + 1].TimestampMs - frame.TimestampMs
                                    : 0;

                                if (duration > 0)
                                {
                                    var command = FrameToCommand(frame.Packet, (int)duration);
                                    commands.Add((command, lineNumber));  // Use include line number for error tracking
                                }
                            }
                        }
                        // Extract commands from included frames (we'll regenerate frames later with proper timing)
                        // For now, we need to extract the commands from the included frames
                        // This is a bit tricky - we'll convert frames back to commands
                        foreach (var frame in includedFrames)
                        {
                            // Skip the final neutral frame that ParseLines adds
                            if (frame == includedFrames[^1])
                                continue;

                            // Calculate duration for this frame
                            int frameIndex = includedFrames.IndexOf(frame);
                            long duration = (frameIndex < includedFrames.Count - 1)
                                ? includedFrames[frameIndex + 1].TimestampMs - frame.TimestampMs
                                : 0;

                            if (duration > 0)
                            {
                                var command = FrameToCommand(frame.Packet, (int)duration);
                                commands.Add((command, lineNumber));  // Use include line number for error tracking
                            }
                        }
                    }
                    catch (MacroParseException)
                    {
                        throw;  // Re-throw parse exceptions as-is
                    }
                    catch (Exception ex)
                    {
                        throw new MacroParseException($"Error processing include on line {lineNumber}: {ex.Message}", source, lineNumber);
                    }

                    continue;
                }

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

            // Parse duration (in frames)
            if (!MacroFormat.TryParseInt(durationPart, out int durationFrames))
                throw new FormatException($"Invalid duration: {durationPart}");

            if (durationFrames < 0)
                throw new FormatException("Duration cannot be negative");

            // Convert frames to milliseconds for internal timing
            int durationMs = durationFrames * SwitchControllerConstants.USB_FRAME_INTERVAL_MS;

            var command = new MacroFormat.MacroCommand { DurationMs = durationMs };

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
        /// Converts a frame's packet back to a MacroCommand (for include processing)
        /// </summary>
        private static MacroFormat.MacroCommand FrameToCommand(byte[] packet, int duration)
        {
            var command = new MacroFormat.MacroCommand { DurationMs = duration };

            // Extract buttons
            ushort buttons = (ushort)((packet[0] << 8) | packet[1]);
            foreach (var kvp in MacroFormat.ButtonMap)
            {
                if ((buttons & kvp.Value) != 0)
                    command.Inputs.Add(kvp.Key);
            }

            // Extract D-Pad
            byte hat = packet[2];
            foreach (var kvp in MacroFormat.DPadMap)
            {
                if (kvp.Value == hat)
                {
                    command.Inputs.Add(kvp.Key);
                    break;
                }
            }

            // Extract stick positions
            byte lx = packet[3];
            byte ly = packet[4];
            byte rx = packet[5];
            byte ry = packet[6];

            // Only set stick values if they're not centered
            if (lx != 128 || ly != 128)
            {
                command.LeftX = lx;
                command.LeftY = ly;
            }

            if (rx != 128 || ry != 128)
            {
                command.RightX = rx;
                command.RightY = ry;
            }

            return command;
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

    /// <summary>
    /// Exception thrown when an included macro file is not found
    /// </summary>
    public class MacroIncludeNotFoundException : MacroParseException
    {
        public string IncludePath { get; }

        public MacroIncludeNotFoundException(string message, string source, int lineNumber, string includePath)
            : base(message, source, lineNumber)
        {
            IncludePath = includePath;
        }
    }

    /// <summary>
    /// Exception thrown when a circular include is detected
    /// </summary>
    public class MacroCircularIncludeException : MacroParseException
    {
        public MacroCircularIncludeException(string message, string source, int lineNumber)
            : base(message, source, lineNumber)
        {
        }
    }

    /// <summary>
    /// Exception thrown when maximum include depth is exceeded
    /// </summary>
    public class MacroIncludeDepthException : MacroParseException
    {
        public MacroIncludeDepthException(string message, string source)
            : base(message, source, 0)
        {
        }
    }
}
