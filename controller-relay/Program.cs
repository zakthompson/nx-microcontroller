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
        private static bool _running = true;
        private static bool _forceHomeOnce = false;

        // Button state tracking for edge detection
        private static bool _lastL3State = false;
        private static bool _lastAState = false;
        private static bool _lastXState = false;
        private static bool _lastYState = false;

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
            var config = Configuration.Load();

            // Initialize macro system
            string macroFilePath = Path.Combine(AppContext.BaseDirectory, "macro.json");
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
            _companionAppManager = new CompanionAppManager(config);
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
        /// Handles macro recording and playback controls via button combos
        /// </summary>
        private static void HandleMacroControls(bool controllerConnected, Gamepad gamepad, long currentTime)
        {
            if (_macroSystem == null)
                return;

            // Detect button states (only when controller connected)
            bool l3Pressed = controllerConnected && (gamepad.Buttons & GamepadButtonFlags.LeftThumb) != 0;
            bool aPressed = controllerConnected && (gamepad.Buttons & GamepadButtonFlags.A) != 0;
            bool xPressed = controllerConnected && (gamepad.Buttons & GamepadButtonFlags.X) != 0;
            bool yPressed = controllerConnected && (gamepad.Buttons & GamepadButtonFlags.Y) != 0;
            bool bPressed = controllerConnected && (gamepad.Buttons & GamepadButtonFlags.B) != 0;

            // Exit combo: L3 + B
            if (l3Pressed && bPressed)
            {
                Console.WriteLine("\n\nExit combo detected (L3 + B). Shutting down...");
                _running = false;
                return;
            }

            // Record toggle: L3 + A (edge-triggered)
            if (l3Pressed && aPressed && (!_lastL3State || !_lastAState))
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

            // Play once: L3 + X (edge-triggered)
            if (l3Pressed && xPressed && (!_lastL3State || !_lastXState))
            {
                if (!_macroSystem.IsRecording && _macroSystem.FrameCount > 0)
                {
                    _macroSystem.StartPlayback(currentTime, loop: false);
                }
            }

            // Loop toggle: L3 + Y (edge-triggered)
            if (l3Pressed && yPressed && (!_lastL3State || !_lastYState))
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
            _lastL3State = l3Pressed;
            _lastAState = aPressed;
            _lastXState = xPressed;
            _lastYState = yPressed;
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
