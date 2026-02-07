using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
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
        private readonly int? _ownerProcessId;
        private SerialConnection? _serial;
        private IControllerTransport? _transport;
        private MacroSystem? _macroSystem;
        private CompanionAppManager? _companionAppManager;
        private IControllerInput? _input;
        private FocusTracker? _focusTracker;
        private Thread? _relayThread;
        private bool _running;
        private bool _forceHomeOnce;
        private string? _lastStatus;

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
        /// <param name="ownerProcessId">
        /// Optional process ID of the owner window (GUI mode). Used for focus tracking.
        /// Pass null in headless mode to disable focus gating when no companion app.
        /// </param>
        public RelaySession(Configuration config, string portName, bool pairMode = false, int? ownerProcessId = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _portName = portName ?? throw new ArgumentNullException(nameof(portName));
            _pairMode = pairMode;
            _ownerProcessId = ownerProcessId;
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

            // Create controller input based on configured backend
            _input = _config.InputBackend.ToLowerInvariant() switch
            {
                "sdl2" or "sdl" => new SdlControllerInput(),
                _ => new XInputControllerInput()
            };

            LogStatus($"Using input backend: {_config.InputBackend}");

            // Wait for controller
            while (_running && !_input.IsConnected)
            {
                LogStatus("Waiting for controller...");
                Thread.Sleep(500);
                _input.Update();
            }

            if (!_running)
                return;

            LogStatus("Controller connected.");

            // Launch companion app if configured
            _companionAppManager = new CompanionAppManager(_config, LogStatus);
            _companionAppManager.Launch();

            // Set up focus tracking
            _focusTracker = new FocusTracker();
            if (_ownerProcessId.HasValue)
            {
                _focusTracker.AddProcessId(_ownerProcessId.Value);
            }
            if (_companionAppManager.ProcessId.HasValue)
            {
                _focusTracker.AddProcessId(_companionAppManager.ProcessId.Value);
            }

            // Run main loop
            RunMainLoop();
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

        private void RunMainLoop()
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

                // Update input state
                _input?.Update();

                // Update focus tracking
                _focusTracker?.Update(now);
                bool focused = _focusTracker?.IsFocused ?? true;

                // Allow macro playback to continue even when controller disconnected
                bool macroActive = _macroSystem != null && (_macroSystem.IsPlaying || _macroSystem.IsLooping);
                if (!(_input?.IsConnected ?? false) && !macroActive)
                {
                    LogStatus("Controller disconnected...");
                    Thread.Sleep(250);
                    continue;
                }

                // Handle button combos and macro control (only when focused)
                if (focused)
                {
                    HandleMacroControls(sw.ElapsedMilliseconds);
                }

                // Build controller state from input or macro
                ControllerState controllerState = BuildCurrentState(sw.ElapsedMilliseconds, focused);

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
                    UpdateStatusDisplay(focused);
                }
            }
        }

        private void HandleMacroControls(long currentTime)
        {
            if (_macroSystem == null || _input == null)
                return;

            // Check if hotkey enable button is pressed
            bool hotkeyEnablePressed = _input.IsButtonPressed(_config.HotkeyEnable);

            // Check each configurable button
            bool quitPressed = _input.IsButtonPressed(_config.HotkeyQuit);
            bool recordPressed = _input.IsButtonPressed(_config.HotkeyMacroRecord);
            bool playOncePressed = _input.IsButtonPressed(_config.HotkeyMacroPlayOnce);
            bool loopPressed = _input.IsButtonPressed(_config.HotkeyMacroLoop);
            bool sendHomePressed = _input.IsButtonPressed(_config.HotkeySendHome);

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

        private ControllerState BuildCurrentState(long currentTime, bool focused)
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
                        return BuildLiveState(focused);
                    }
                }

                if (macroPacket != null)
                {
                    _forceHomeOnce = false;
                    return ControllerState.FromNativePacket(macroPacket);
                }
            }

            return BuildLiveState(focused);
        }

        private ControllerState BuildLiveState(bool focused)
        {
            // When not focused, send idle input
            if (!focused)
            {
                return ControllerState.Idle;
            }

            if (_input == null)
            {
                return new ControllerState();
            }

            var state = _input.GetCurrentState();

            // Apply HOME button if requested by hotkey
            if (_forceHomeOnce)
            {
                state.Buttons1 |= (byte)(BTN_HOME >> 8);
                _forceHomeOnce = false;
            }

            return state;
        }

        private void UpdateStatusDisplay(bool focused)
        {
            string status = "Running";

            if (_macroSystem != null)
            {
                if (_macroSystem.IsRecording)
                    status = $"RECORDING: {_macroSystem.FrameCount} frames";
                else if (_macroSystem.IsLooping)
                    status = $"LOOPING: {_macroSystem.PlaybackIndex}/{_macroSystem.FrameCount}";
                else if (_macroSystem.IsPlaying)
                    status = $"PLAYING: {_macroSystem.PlaybackIndex}/{_macroSystem.FrameCount}";
                else if (_macroSystem.FrameCount > 0)
                    status = $"Macro ready: {_macroSystem.FrameCount} frames";
            }

            if (!focused)
            {
                status += " [PAUSED - unfocused]";
            }

            // Only log when status actually changes
            if (status != _lastStatus)
            {
                _lastStatus = status;
                LogStatus(status);
            }
        }

        private void Cleanup()
        {
            _input?.Dispose();
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
