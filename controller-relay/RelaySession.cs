using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using SharpDX.XInput;
using static SwitchController.SwitchControllerConstants;

namespace SwitchController
{
    /// <summary>
    /// Manages a controller relay session on a background thread
    /// </summary>
    public class RelaySession
    {
        private readonly Configuration _config;
        private readonly string _portName;
        private readonly bool _pairMode;
        private SerialConnection? _serial;
        private IControllerTransport? _transport;
        private MacroSystem? _macroSystem;
        private CompanionAppManager? _companionAppManager;
        private Thread? _relayThread;
        private bool _running;
        private bool _forceHomeOnce;

        // Button state tracking for edge detection
        private readonly System.Collections.Generic.Dictionary<SwitchButton, bool> _lastButtonStates =
            new System.Collections.Generic.Dictionary<SwitchButton, bool>();

        /// <summary>
        /// Fired when a status message should be displayed
        /// </summary>
        public event Action<string>? StatusUpdate;

        /// <summary>
        /// Fired when the session has stopped
        /// </summary>
        public event Action? SessionStopped;

        /// <summary>
        /// Returns true if the relay session is currently running
        /// </summary>
        public bool IsRunning => _running;

        /// <param name="pairMode">
        /// When true, sends RESET_TO_CONTROLLER during handshake to wipe pairing
        /// state and put the virtual controller into discoverable mode.
        /// </param>
        public RelaySession(Configuration config, string portName, bool pairMode = false)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _portName = portName ?? throw new ArgumentNullException(nameof(portName));
            _pairMode = pairMode;
        }

        /// <summary>
        /// Starts the relay session on a background thread
        /// </summary>
        public void Start()
        {
            if (_running)
            {
                LogStatus("Session is already running");
                return;
            }

            _running = true;
            _relayThread = new Thread(RelayThreadMain)
            {
                IsBackground = true,
                Name = "RelaySession"
            };
            _relayThread.Start();
        }

        /// <summary>
        /// Stops the relay session and waits for the background thread to complete
        /// </summary>
        public void Stop()
        {
            if (!_running)
                return;

            LogStatus("Stopping relay session...");
            _running = false;

            _relayThread?.Join(5000);
            Cleanup();
        }

        private void RelayThreadMain()
        {
            try
            {
                RunSession();
            }
            catch (Exception ex)
            {
                LogStatus($"Session error: {ex.Message}");
            }
            finally
            {
                _running = false;
                Cleanup();
                SessionStopped?.Invoke();
            }
        }

        private void RunSession()
        {
            // Initialize macro system
            string macroFilePath = Path.Combine(AppContext.BaseDirectory, "macro.macro");
            _macroSystem = new MacroSystem(macroFilePath);
            _macroSystem.Load();

            // Open serial connection
            _serial = new SerialConnection(LogStatus);
            if (!_serial.Open(_portName))
            {
                LogStatus("Failed to open serial port");
                return;
            }

            // Connect transport
            _transport = ConnectTransport(_serial, _config.FirmwareType);
            if (_transport == null)
            {
                LogStatus("Failed to connect to firmware. Exiting.");
                return;
            }

            // Wait for XInput controller
            var controller = new Controller(UserIndex.One);
            while (_running && !controller.IsConnected)
            {
                LogStatus("Waiting for XInput controller...");
                Thread.Sleep(500);
                controller = new Controller(UserIndex.One);
            }

            if (!_running)
                return;

            LogStatus("Controller connected.");

            // Launch companion app if configured
            _companionAppManager = new CompanionAppManager(_config, LogStatus);
            _companionAppManager.Launch();

            // Run main loop
            RunMainLoop(controller);
        }

        private IControllerTransport? ConnectTransport(SerialConnection serial, string firmwareType)
        {
            if (serial.Port == null)
                return null;

            uint controllerId = ResolveControllerId(_config);

            if (firmwareType == "native")
            {
                LogStatus("Connecting with native firmware...");
                return TryConnect(new NativeTransport(), serial, "native");
            }

            if (firmwareType == "pabotbase")
            {
                string mode = _pairMode ? " (pair mode)" : "";
                LogStatus($"Connecting with PABotBase firmware ({SwitchControllerConstants.ControllerTypeName(controllerId)}{mode})...");
                return TryConnect(new PABotBaseTransport(controllerId, _pairMode), serial, "PABotBase");
            }

            // Auto-detect
            LogStatus("Auto-detecting firmware...");
            while (_running)
            {
                LogStatus("Trying native handshake...");
                var native = new NativeTransport();
                if (native.Connect(serial.Port))
                {
                    LogStatus("Connected with native firmware.");
                    return native;
                }
                native.Dispose();

                LogStatus($"Trying PABotBase handshake ({SwitchControllerConstants.ControllerTypeName(controllerId)})...");
                var pa = new PABotBaseTransport(controllerId, _pairMode);
                if (pa.Connect(serial.Port))
                {
                    LogStatus("Connected with PABotBase firmware.");
                    return pa;
                }
                pa.Dispose();

                LogStatus("Firmware not responding, retrying...");
                Thread.Sleep(500);
            }

            return null;
        }

        private IControllerTransport? TryConnect(IControllerTransport transport, SerialConnection serial, string name)
        {
            while (_running)
            {
                if (serial.Port != null && transport.Connect(serial.Port))
                {
                    LogStatus($"Connected with {name} firmware.");
                    return transport;
                }
                LogStatus($"{name} firmware not responding, retrying...");
                Thread.Sleep(500);
            }
            transport.Dispose();
            return null;
        }

        private uint ResolveControllerId(Configuration config)
        {
            uint? id = SwitchControllerConstants.ParseControllerType(config.ControllerType);
            if (id == null)
            {
                LogStatus($"Warning: Unknown ControllerType \"{config.ControllerType}\", using wireless-pro");
                return PA_CID_NS1_WIRELESS_PRO_CONTROLLER;
            }
            return id.Value;
        }

        private void RunMainLoop(Controller controller)
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
                    LogStatus("Controller disconnected...");
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

                // Build controller state from gamepad or macro
                ControllerState controllerState = BuildCurrentState(gamepad, sw.ElapsedMilliseconds);

                // Record frame if recording
                if (_macroSystem?.IsRecording == true)
                {
                    _macroSystem.RecordFrame(sw.ElapsedMilliseconds, controllerState.ToNativePacket());
                }

                // Send state via transport
                try
                {
                    _transport?.SendState(controllerState);
                    _transport?.Update();
                }
                catch (Exception ex)
                {
                    LogStatus($"Serial write error: {ex.Message}");
                }

                // Update status display periodically
                if (sw.ElapsedMilliseconds % 500 < UPDATE_INTERVAL_MS)
                {
                    UpdateStatusDisplay(gamepad);
                }
            }
        }

        private bool IsButtonPressed(bool controllerConnected, Gamepad gamepad, SwitchButton button)
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
                    return (gamepad.Buttons & GamepadButtonFlags.B) != 0;
                case SwitchButton.B:
                    return (gamepad.Buttons & GamepadButtonFlags.A) != 0;
                case SwitchButton.X:
                    return (gamepad.Buttons & GamepadButtonFlags.Y) != 0;
                case SwitchButton.Y:
                    return (gamepad.Buttons & GamepadButtonFlags.X) != 0;
                case SwitchButton.Plus:
                    return (gamepad.Buttons & GamepadButtonFlags.Start) != 0;
                case SwitchButton.Minus:
                    return (gamepad.Buttons & GamepadButtonFlags.Back) != 0;
                default:
                    return false;
            }
        }

        private void HandleMacroControls(bool controllerConnected, Gamepad gamepad, long currentTime)
        {
            if (_macroSystem == null)
                return;

            // Check if hotkey enable button is pressed
            bool hotkeyEnablePressed = IsButtonPressed(controllerConnected, gamepad, _config.HotkeyEnable);

            // Check each configurable button
            bool quitPressed = IsButtonPressed(controllerConnected, gamepad, _config.HotkeyQuit);
            bool recordPressed = IsButtonPressed(controllerConnected, gamepad, _config.HotkeyMacroRecord);
            bool playOncePressed = IsButtonPressed(controllerConnected, gamepad, _config.HotkeyMacroPlayOnce);
            bool loopPressed = IsButtonPressed(controllerConnected, gamepad, _config.HotkeyMacroLoop);
            bool sendHomePressed = IsButtonPressed(controllerConnected, gamepad, _config.HotkeySendHome);

            // Exit combo
            if (hotkeyEnablePressed && quitPressed)
            {
                LogStatus($"Exit combo detected ({_config.HotkeyEnable} + {_config.HotkeyQuit}). Shutting down...");
                _running = false;
                return;
            }

            // Send HOME combo
            if (hotkeyEnablePressed && sendHomePressed)
            {
                _forceHomeOnce = true;
            }

            // Get last states for edge detection
            bool lastHotkeyEnable = _lastButtonStates.ContainsKey(_config.HotkeyEnable) && _lastButtonStates[_config.HotkeyEnable];
            bool lastRecord = _lastButtonStates.ContainsKey(_config.HotkeyMacroRecord) && _lastButtonStates[_config.HotkeyMacroRecord];
            bool lastPlayOnce = _lastButtonStates.ContainsKey(_config.HotkeyMacroPlayOnce) && _lastButtonStates[_config.HotkeyMacroPlayOnce];
            bool lastLoop = _lastButtonStates.ContainsKey(_config.HotkeyMacroLoop) && _lastButtonStates[_config.HotkeyMacroLoop];

            // Record toggle (edge-triggered)
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

            // Play once (edge-triggered)
            if (hotkeyEnablePressed && playOncePressed && (!lastHotkeyEnable || !lastPlayOnce))
            {
                if (!_macroSystem.IsRecording && _macroSystem.FrameCount > 0)
                {
                    _macroSystem.StartPlayback(currentTime, loop: false);
                }
            }

            // Loop toggle (edge-triggered)
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

        private ControllerState BuildCurrentState(Gamepad gamepad, long currentTime)
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
                        return BuildGamepadState(gamepad);
                    }
                }

                if (macroPacket != null)
                {
                    _forceHomeOnce = false;
                    return ControllerState.FromNativePacket(macroPacket);
                }
            }

            return BuildGamepadState(gamepad);
        }

        private ControllerState BuildGamepadState(Gamepad gamepad)
        {
            var state = PacketBuilder.BuildControllerState(gamepad, _forceHomeOnce);
            _forceHomeOnce = false;
            return state;
        }

        private void UpdateStatusDisplay(Gamepad gamepad)
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

            LogStatus(
                $"Btn:{gamepad.Buttons,-18} LT:{gamepad.LeftTrigger,3} RT:{gamepad.RightTrigger,3} " +
                $"LX:{gamepad.LeftThumbX,6} LY:{gamepad.LeftThumbY,6} RX:{gamepad.RightThumbX,6} RY:{gamepad.RightThumbY,6}{status}"
            );
        }

        private void Cleanup()
        {
            _companionAppManager?.Dispose();
            _transport?.Dispose();
            _serial?.Dispose();
        }

        private void LogStatus(string message)
        {
            StatusUpdate?.Invoke(message);
        }
    }
}
