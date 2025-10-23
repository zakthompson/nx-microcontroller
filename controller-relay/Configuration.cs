using System;
using System.IO;

namespace SwitchController
{
    /// <summary>
    /// Handles loading and storing configuration from controller-relay.config file
    /// </summary>
    public class Configuration
    {
        /// <summary>
        /// Path to companion application to launch alongside controller-relay
        /// </summary>
        public string? CompanionAppPath { get; set; }

        /// <summary>
        /// X coordinate for auto-click (window-relative or absolute depending on AutoClickRelative)
        /// </summary>
        public int? AutoClickX { get; set; }

        /// <summary>
        /// Y coordinate for auto-click (window-relative or absolute depending on AutoClickRelative)
        /// </summary>
        public int? AutoClickY { get; set; }

        /// <summary>
        /// Delay in milliseconds before performing auto-click after launching companion app
        /// </summary>
        public int AutoClickDelay { get; set; } = 2000;

        /// <summary>
        /// If true, auto-click coordinates are relative to window; if false, absolute screen coordinates
        /// </summary>
        public bool AutoClickRelative { get; set; } = true;

        /// <summary>
        /// Loads configuration from controller-relay.config file in the application directory
        /// </summary>
        /// <returns>Configuration object with loaded settings</returns>
        public static Configuration Load()
        {
            var config = new Configuration();

            try
            {
                string exeDir = AppContext.BaseDirectory;
                string configPath = Path.Combine(exeDir, "controller-relay.config");

                if (!File.Exists(configPath))
                {
                    Console.WriteLine($"No config file found at {configPath}");
                    Console.WriteLine("To launch a companion app, create controller-relay.config with:");
                    Console.WriteLine("  CompanionApp=\"C:\\Path\\To\\Your\\App.exe\"");
                    return config;
                }

                Console.WriteLine($"Loading config from: {configPath}");
                var lines = File.ReadAllLines(configPath);

                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                        continue;

                    if (TryParseConfigLine(trimmed, "CompanionApp=", out string? appPath))
                    {
                        config.CompanionAppPath = UnquoteValue(appPath);
                        Console.WriteLine($"  Companion app: {config.CompanionAppPath}");
                    }
                    else if (TryParseConfigLine(trimmed, "AutoClickX=", out string? xStr) && int.TryParse(xStr, out int x))
                    {
                        config.AutoClickX = x;
                        Console.WriteLine($"  Auto-click X: {x}");
                    }
                    else if (TryParseConfigLine(trimmed, "AutoClickY=", out string? yStr) && int.TryParse(yStr, out int y))
                    {
                        config.AutoClickY = y;
                        Console.WriteLine($"  Auto-click Y: {y}");
                    }
                    else if (TryParseConfigLine(trimmed, "AutoClickDelay=", out string? delayStr) && int.TryParse(delayStr, out int delay))
                    {
                        config.AutoClickDelay = delay;
                        Console.WriteLine($"  Auto-click delay: {delay}ms");
                    }
                    else if (TryParseConfigLine(trimmed, "AutoClickRelative=", out string? relStr) && relStr != null)
                    {
                        string val = relStr.ToLowerInvariant();
                        config.AutoClickRelative = val == "true" || val == "1" || val == "yes";
                        Console.WriteLine($"  Auto-click relative: {config.AutoClickRelative}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading config: {ex.Message}");
            }

            return config;
        }

        /// <summary>
        /// Attempts to parse a configuration line with the given key prefix
        /// </summary>
        private static bool TryParseConfigLine(string line, string key, out string? value)
        {
            if (line.StartsWith(key, StringComparison.OrdinalIgnoreCase))
            {
                value = line.Substring(key.Length).Trim();
                return true;
            }
            value = null;
            return false;
        }

        /// <summary>
        /// Removes surrounding quotes from a configuration value
        /// </summary>
        private static string UnquoteValue(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            if (value!.StartsWith("\"") && value.EndsWith("\"") && value.Length >= 2)
                return value.Substring(1, value.Length - 2);

            return value;
        }
    }
}
