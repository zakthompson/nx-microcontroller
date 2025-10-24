using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using SharpDX.XInput;
using static SwitchController.SwitchControllerConstants;

namespace SwitchController
{
    /// <summary>
    /// Main program for relaying XInput controller input to Nintendo Switch via serial
    /// </summary>
    class Program
    {
        private static SerialConnection? _serial;
        private static MacroSystem? _macroSystem;
        private static CompanionAppManager? _companionAppManager;
        private static Configuration? _config;
        private static bool _running = true;
        private static bool _forceHomeOnce = false;

        // Button state tracking for edge detection (using SwitchButton enum)
        private static readonly System.Collections.Generic.Dictionary<SwitchButton, bool> _lastButtonStates =
            new System.Collections.Generic.Dictionary<SwitchButton, bool>();

        static void Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("Usage: controller-relay.exe <COM_PORT>");
                Console.WriteLine();
                Console.WriteLine("Examples:");
                Console.WriteLine("  controller-relay.exe COM3");
                Console.WriteLine("  controller-relay.exe /dev/ttyUSB0");
                return;
            }

            // Load configuration
            _config = Configuration.Load();

            // Initialize macro system (using .macro text format by default)
            string macroFilePath = Path.Combine(AppContext.BaseDirectory, "macro.macro");
            _macroSystem = new MacroSystem(macroFilePath);
            _macroSystem.Load();

            // Open serial connection
            string portName = args[0];
            _serial = new SerialConnection();
            if (!_serial.Open(portName))
            {
                return;
            }

            // Setup cleanup handlers
            Console.CancelKeyPress += (s, e) => { _running = false; e.Cancel = true; };
            AppDomain.CurrentDomain.ProcessExit += (s, e) => Cleanup();

            // Start key watcher thread for 'H' key (HOME button)
            var keyThread = new Thread(KeyWatcherThread) { IsBackground = true };
            keyThread.Start();

            // Sync with firmware
            Console.WriteLine("Waiting for Arduino and syncing...");
            while (!_serial.PerformSyncHandshake())
            {
                Console.Write("\rArduino not responding, retrying...");
                Thread.Sleep(500);
            }
            Console.WriteLine("\nSynced successfully with Arduino.");

            // Wait for XInput controller
            var controller = new Controller(UserIndex.One);
            while (!controller.IsConnected)
            {
                Console.Write("\rWaiting for XInput controller...");
                Thread.Sleep(500);
                controller = new Controller(UserIndex.One);
            }
            Console.WriteLine("\nController connected.");

            // Launch companion app if configured
            _companionAppManager = new CompanionAppManager(_config);
            _companionAppManager.Launch();

            // Run main loop
            RunMainLoop(controller);

            // Cleanup
            Cleanup();
        }

        /// <summary>
        /// Main control loop - runs at fixed UPDATE_INTERVAL_MS cadence
        /// </summary>
        private static void RunMainLoop(Controller controller)
        {
            var sw = Stopwatch.StartNew();
            long nextTick = sw.ElapsedMilliseconds;

            while (_running)
            {
                long now = sw.ElapsedMilliseconds;
                if (now < nextTick)
                {
                    Thread.Sleep((int)Math.Max(0, nextTick - now));
                    continue;
                }
                nextTick += UPDATE_INTERVAL_MS;

                // Allow macro playback to continue even when controller disconnected
                bool macroActive = _macroSystem != null && (_macroSystem.IsPlaying || _macroSystem.IsLooping);
                if (!controller.IsConnected && !macroActive)
                {
                    Console.Write("\rController disconnected...");
                    Thread.Sleep(250);
                    continue;
                }

                // Get gamepad state (only if controller connected)
                Gamepad gamepad = default;
                bool controllerConnected = controller.IsConnected;

                if (controllerConnected)
                {
                    var state = controller.GetState();
                    gamepad = state.Gamepad;
                }

                // Handle button combos and macro control
                HandleMacroControls(controllerConnected, gamepad, sw.ElapsedMilliseconds);

                // Build packet from gamepad or macro
                byte[] packet = BuildCurrentPacket(gamepad, sw.ElapsedMilliseconds);

                // Record frame if recording
                if (_macroSystem?.IsRecording == true)
                {
                    _macroSystem.RecordFrame(sw.ElapsedMilliseconds, packet);
                }

                // Send packet over serial
                try
                {
                    byte crc = PacketBuilder.CalculateCrc8(packet, 0, packet.Length);
                    _serial?.SendPacket(packet, crc);
                    _serial?.CheckResponses();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\nSerial write error: {ex.Message}");
                }

                // Update console display periodically
                if (sw.ElapsedMilliseconds % 500 < UPDATE_INTERVAL_MS)
                {
                    UpdateConsoleDisplay(gamepad);
                }
            }
        }

        /// <summary>
        /// Checks if a specific Switch button is currently pressed
        /// </summary>
        private static bool IsButtonPressed(bool controllerConnected, Gamepad gamepad, SwitchButton button)
        {
            if (!controllerConnected || button == SwitchButton.None)
                return false;

            switch (button)
            {
                case SwitchButton.LS:
                    return (gamepad.Buttons & GamepadButtonFlags.LeftThumb) != 0;
                case SwitchButton.RS:
                    return (gamepad.Buttons & GamepadButtonFlags.RightThumb) != 0;
                case SwitchButton.Up:
                    return (gamepad.Buttons & GamepadButtonFlags.DPadUp) != 0;
                case SwitchButton.Down:
                    return (gamepad.Buttons & GamepadButtonFlags.DPadDown) != 0;
                case SwitchButton.Left:
                    return (gamepad.Buttons & GamepadButtonFlags.DPadLeft) != 0;
                case SwitchButton.Right:
                    return (gamepad.Buttons & GamepadButtonFlags.DPadRight) != 0;
                case SwitchButton.L:
                    return (gamepad.Buttons & GamepadButtonFlags.LeftShoulder) != 0;
                case SwitchButton.R:
                    return (gamepad.Buttons & GamepadButtonFlags.RightShoulder) != 0;
                case SwitchButton.ZL:
                    return gamepad.LeftTrigger > TRIGGER_THRESHOLD;
                case SwitchButton.ZR:
                    return gamepad.RightTrigger > TRIGGER_THRESHOLD;
                case SwitchButton.A:
                    // Switch A (right button) = Xbox B (right button)
                    return (gamepad.Buttons & GamepadButtonFlags.B) != 0;
                case SwitchButton.B:
                    // Switch B (bottom button) = Xbox A (bottom button)
                    return (gamepad.Buttons & GamepadButtonFlags.A) != 0;
                case SwitchButton.X:
                    // Switch X (top button) = Xbox Y (top button)
                    return (gamepad.Buttons & GamepadButtonFlags.Y) != 0;
                case SwitchButton.Y:
                    // Switch Y (left button) = Xbox X (left button)
                    return (gamepad.Buttons & GamepadButtonFlags.X) != 0;
                case SwitchButton.Plus:
                    return (gamepad.Buttons & GamepadButtonFlags.Start) != 0;
                case SwitchButton.Minus:
                    return (gamepad.Buttons & GamepadButtonFlags.Back) != 0;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Handles macro recording and playback controls via button combos
        /// </summary>
        private static void HandleMacroControls(bool controllerConnected, Gamepad gamepad, long currentTime)
        {
            if (_macroSystem == null || _config == null)
                return;

            // Check if hotkey enable button is pressed
            bool hotkeyEnablePressed = IsButtonPressed(controllerConnected, gamepad, _config.HotkeyEnable);

            // Check each configurable button
            bool quitPressed = IsButtonPressed(controllerConnected, gamepad, _config.HotkeyQuit);
            bool recordPressed = IsButtonPressed(controllerConnected, gamepad, _config.HotkeyMacroRecord);
            bool playOncePressed = IsButtonPressed(controllerConnected, gamepad, _config.HotkeyMacroPlayOnce);
            bool loopPressed = IsButtonPressed(controllerConnected, gamepad, _config.HotkeyMacroLoop);
            bool sendHomePressed = IsButtonPressed(controllerConnected, gamepad, _config.HotkeySendHome);

            // Exit combo: HotkeyEnable + HotkeyQuit
            if (hotkeyEnablePressed && quitPressed)
            {
                Console.WriteLine($"\n\nExit combo detected ({_config.HotkeyEnable} + {_config.HotkeyQuit}). Shutting down...");
                _running = false;
                return;
            }

            // Send HOME combo: HotkeyEnable + HotkeySendHome (state-based, like old exit)
            if (hotkeyEnablePressed && sendHomePressed)
            {
                _forceHomeOnce = true;
            }

            // Get last states for edge detection
            bool lastHotkeyEnable = _lastButtonStates.ContainsKey(_config.HotkeyEnable) && _lastButtonStates[_config.HotkeyEnable];
            bool lastRecord = _lastButtonStates.ContainsKey(_config.HotkeyMacroRecord) && _lastButtonStates[_config.HotkeyMacroRecord];
            bool lastPlayOnce = _lastButtonStates.ContainsKey(_config.HotkeyMacroPlayOnce) && _lastButtonStates[_config.HotkeyMacroPlayOnce];
            bool lastLoop = _lastButtonStates.ContainsKey(_config.HotkeyMacroLoop) && _lastButtonStates[_config.HotkeyMacroLoop];

            // Record toggle: HotkeyEnable + HotkeyMacroRecord (edge-triggered)
            if (hotkeyEnablePressed && recordPressed && (!lastHotkeyEnable || !lastRecord))
            {
                if (!_macroSystem.IsPlaying && !_macroSystem.IsLooping)
                {
                    if (!_macroSystem.IsRecording)
                    {
                        _macroSystem.StartRecording(currentTime);
                    }
                    else
                    {
                        _macroSystem.StopRecording();
                    }
                }
            }

            // Play once: HotkeyEnable + HotkeyMacroPlayOnce (edge-triggered)
            if (hotkeyEnablePressed && playOncePressed && (!lastHotkeyEnable || !lastPlayOnce))
            {
                if (!_macroSystem.IsRecording && _macroSystem.FrameCount > 0)
                {
                    _macroSystem.StartPlayback(currentTime, loop: false);
                }
            }

            // Loop toggle: HotkeyEnable + HotkeyMacroLoop (edge-triggered)
            if (hotkeyEnablePressed && loopPressed && (!lastHotkeyEnable || !lastLoop))
            {
                if (!_macroSystem.IsRecording && _macroSystem.FrameCount > 0)
                {
                    if (_macroSystem.IsLooping)
                    {
                        _macroSystem.StopPlayback();
                    }
                    else
                    {
                        _macroSystem.StartPlayback(currentTime, loop: true);
                    }
                }
            }

            // Update last button states for edge detection
            _lastButtonStates[_config.HotkeyEnable] = hotkeyEnablePressed;
            _lastButtonStates[_config.HotkeyQuit] = quitPressed;
            _lastButtonStates[_config.HotkeyMacroRecord] = recordPressed;
            _lastButtonStates[_config.HotkeyMacroPlayOnce] = playOncePressed;
            _lastButtonStates[_config.HotkeyMacroLoop] = loopPressed;
            _lastButtonStates[_config.HotkeySendHome] = sendHomePressed;
        }

        /// <summary>
        /// Builds the current packet from either macro playback or gamepad state
        /// </summary>
        private static byte[] BuildCurrentPacket(Gamepad gamepad, long currentTime)
        {
            if (_macroSystem != null && (_macroSystem.IsPlaying || _macroSystem.IsLooping))
            {
                byte[]? macroPacket = _macroSystem.GetCurrentPacket(currentTime);

                if (macroPacket == null)
                {
                    // Macro playback finished
                    if (_macroSystem.IsLooping)
                    {
                        // Restart loop
                        _macroSystem.RestartPlayback(currentTime);
                        macroPacket = _macroSystem.GetCurrentPacket(currentTime);
                    }
                    else
                    {
                        // Single playback finished
                        _macroSystem.StopPlayback();
                        return PacketBuilder.BuildPacket(gamepad, _forceHomeOnce);
                    }
                }

                if (macroPacket != null)
                {
                    _forceHomeOnce = false; // Clear HOME flag if set
                    return macroPacket;
                }
            }

            // Build packet from gamepad
            byte[] packet = PacketBuilder.BuildPacket(gamepad, _forceHomeOnce);
            _forceHomeOnce = false;
            return packet;
        }

        /// <summary>
        /// Updates the console display with current status
        /// </summary>
        private static void UpdateConsoleDisplay(Gamepad gamepad)
        {
            string status = "";

            if (_macroSystem != null)
            {
                if (_macroSystem.IsRecording)
                    status = $" [RECORDING: {_macroSystem.FrameCount} frames]";
                else if (_macroSystem.IsLooping)
                    status = $" [LOOPING: {_macroSystem.PlaybackIndex}/{_macroSystem.FrameCount}]";
                else if (_macroSystem.IsPlaying)
                    status = $" [PLAYING: {_macroSystem.PlaybackIndex}/{_macroSystem.FrameCount}]";
                else if (_macroSystem.FrameCount > 0)
                    status = $" [Macro ready: {_macroSystem.FrameCount} frames]";
            }

            Console.Write(
                $"\rBtn:{gamepad.Buttons,-18} LT:{gamepad.LeftTrigger,3} RT:{gamepad.RightTrigger,3} " +
                $"LX:{gamepad.LeftThumbX,6} LY:{gamepad.LeftThumbY,6} RX:{gamepad.RightThumbX,6} RY:{gamepad.RightThumbY,6}{status}  "
            );
        }

        /// <summary>
        /// Key watcher thread - allows pressing 'H' to send HOME button
        /// </summary>
        private static void KeyWatcherThread()
        {
            try
            {
                while (_running)
                {
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(true);
                        if (key.Key == ConsoleKey.H)
                        {
                            _forceHomeOnce = true;
                        }
                    }
                    Thread.Sleep(10);
                }
            }
            catch { /* Ignore errors in background thread */ }
        }

        /// <summary>
        /// Cleanup resources on exit
        /// </summary>
        private static void Cleanup()
        {
            _companionAppManager?.Dispose();
            _serial?.Dispose();
        }
    }
}
