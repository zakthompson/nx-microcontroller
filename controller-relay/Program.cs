using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Text.Json;
using System.Threading;
using System.Linq;
using SharpDX.XInput;

namespace SwitchController
{
    class Program
    {
        // ====== Tunables ======
        const int  UPDATE_MS       = 1;     // 1ms = 1000Hz polling for minimum latency
        const bool INVERT_Y        = true;  // Nintendo-style "up is negative" to match firmware

        // Deadzone settings (matching Switch Joy-Con behavior)
        const int  RADIAL_DEADZONE = 4915;  // ~15% radial deadzone (Joy-Con default)
        const int  AXIAL_DEADZONE  = 2000;  // ~6% axial deadzone (prevents cross-axis bleeding)

        const byte TRIGGER_THRESH  = 30;    // 0..255

        // ====== Pokkén 16-bit button bit masks (wire format) ======
        const UInt16 BTN_Y       = 1 << 0;
        const UInt16 BTN_B       = 1 << 1;
        const UInt16 BTN_A       = 1 << 2;
        const UInt16 BTN_X       = 1 << 3;
        const UInt16 BTN_L       = 1 << 4;
        const UInt16 BTN_R       = 1 << 5;
        const UInt16 BTN_ZL      = 1 << 6;
        const UInt16 BTN_ZR      = 1 << 7;
        const UInt16 BTN_MINUS   = 1 << 8;
        const UInt16 BTN_PLUS    = 1 << 9;
        const UInt16 BTN_LCLICK  = 1 << 10;
        const UInt16 BTN_RCLICK  = 1 << 11;
        const UInt16 BTN_HOME    = 1 << 12;
        const UInt16 BTN_CAPTURE = 1 << 13;

        // ====== Sync constants ======
        const byte CMD_SYNC_START   = 0xFF;
        const byte CMD_SYNC_1       = 0x33;
        const byte CMD_SYNC_2       = 0xCC;
        const byte RESP_SYNC_START  = 0xFF;
        const byte RESP_SYNC_1      = 0xCC;
        const byte RESP_SYNC_OK     = 0x33;
        const byte RESP_UPDATE_ACK  = 0x91;
        const byte RESP_UPDATE_NACK = 0x92;
        const byte RESP_USB_ACK     = 0x90;

        static SerialPort? serial;
        static bool running = true;
        static bool forceHomeOnce = false;
        static Process? companionProcess = null;
        static string? companionAppPath = null;
        static int? companionProcessId = null;
        static string? companionProcessName = null;
        static int? autoClickX = null;
        static int? autoClickY = null;
        static int autoClickDelay = 2000; // Default 2 seconds
        static bool autoClickRelative = true; // Default to window-relative coordinates

        // ====== Macro recording/playback state ======
        static bool isRecording = false;
        static bool isPlayingMacro = false;
        static bool isLoopingMacro = false;
        static List<MacroFrame> recordedMacro = new List<MacroFrame>();
        static int macroPlaybackIndex = 0;
        static long macroPlaybackStartTime = 0;
        static long recordingStartTime = 0;
        static byte[] lastRecordedPacket = null;
        static bool lastL3State = false;
        static bool lastAState = false;
        static bool lastXState = false;
        static bool lastYState = false;
        static string macroFilePath = "";

        // ====== Macro data structure ======
        class MacroFrame
        {
            public long TimestampMs { get; set; }
            public byte[] Packet { get; set; } = new byte[8];
        }

        static void Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("Usage: SwitchController.exe <COM_PORT>");
                return;
            }

            // --- Load config file if it exists ---
            LoadConfig();

            // --- Set up macro file path and load existing macro if present ---
            string exeDir = AppContext.BaseDirectory;
            macroFilePath = Path.Combine(exeDir, "macro.json");
            LoadMacro();

            // --- Open serial ---
            string portName = args[0];
            serial = new SerialPort(portName, 115200);
            try
            {
                serial.Open();
                try { serial.DtrEnable = false; serial.RtsEnable = false; } catch { }
                Console.WriteLine($"Opened {portName}");
                Console.WriteLine();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to open {portName}: {ex.Message}");
                return;
            }

            Console.CancelKeyPress += (s, e) => { running = false; e.Cancel = true; };
            AppDomain.CurrentDomain.ProcessExit += (s, e) => { CloseCompanionApp(); CloseSerial(); };

            // Optional: press H in the console to send one HOME tap
            var keyThread = new Thread(KeyWatcher) { IsBackground = true };
            keyThread.Start();

            // --- Sync with UNO (firmware) ---
            Console.WriteLine("Waiting for UNO and syncing...");
            while (!DoSyncHandshake())
            {
                Console.Write("\rUNO not responding, retrying...");
                Thread.Sleep(500);
            }
            Console.WriteLine("\nSynced successfully with UNO.");

            // --- Wait for XInput controller ---
            Controller controller = new Controller(UserIndex.One);
            while (!controller.IsConnected)
            {
                Console.Write("\rWaiting for XInput controller...");
                Thread.Sleep(500);
                controller = new Controller(UserIndex.One);
            }
            Console.WriteLine("\nController connected.");

            // --- Launch companion app if configured ---
            LaunchCompanionApp();

            // --- Main loop at fixed cadence ---
            var sw = new Stopwatch();
            sw.Start();
            long nextTick = sw.ElapsedMilliseconds;

            while (running)
            {
                long now = sw.ElapsedMilliseconds;
                if (now < nextTick)
                {
                    Thread.Sleep((int)Math.Max(0, nextTick - now));
                    continue;
                }
                nextTick += UPDATE_MS;

                // Allow macro playback to continue even when controller is disconnected
                if (!controller.IsConnected && !isPlayingMacro && !isLoopingMacro)
                {
                    Console.Write("\rController disconnected...");
                    Thread.Sleep(250);
                    continue;
                }

                // Only read controller state if connected
                Gamepad gp = default(Gamepad);
                bool controllerConnected = controller.IsConnected;

                if (controllerConnected)
                {
                    var state = controller.GetState();
                    gp = state.Gamepad;
                }

                // --- Detect button states for edge detection (only when controller connected) ---
                bool l3Pressed = controllerConnected && (gp.Buttons & GamepadButtonFlags.LeftThumb) != 0;
                bool aPressed = controllerConnected && (gp.Buttons & GamepadButtonFlags.A) != 0;
                bool xPressed = controllerConnected && (gp.Buttons & GamepadButtonFlags.X) != 0;
                bool yPressed = controllerConnected && (gp.Buttons & GamepadButtonFlags.Y) != 0;
                bool bPressed = controllerConnected && (gp.Buttons & GamepadButtonFlags.B) != 0;

                // --- Check for exit combo: Left Stick Click + B ---
                if (l3Pressed && bPressed)
                {
                    Console.WriteLine("\n\nExit combo detected (L3 + B). Shutting down...");
                    running = false;
                    break;
                }

                // --- Check for macro button combos (edge-triggered) ---
                // L3 + A: Start/Stop recording
                if (l3Pressed && aPressed && (!lastL3State || !lastAState))
                {
                    if (!isPlayingMacro && !isLoopingMacro)
                    {
                        if (!isRecording)
                        {
                            StartRecording(sw.ElapsedMilliseconds);
                        }
                        else
                        {
                            StopRecording();
                        }
                    }
                }

                // L3 + X: Play macro once
                if (l3Pressed && xPressed && (!lastL3State || !lastXState))
                {
                    if (!isRecording && recordedMacro.Count > 0)
                    {
                        StartPlayback(sw.ElapsedMilliseconds, loop: false);
                    }
                }

                // L3 + Y: Loop macro
                if (l3Pressed && yPressed && (!lastL3State || !lastYState))
                {
                    if (!isRecording && recordedMacro.Count > 0)
                    {
                        if (isLoopingMacro)
                        {
                            StopPlayback();
                        }
                        else
                        {
                            StartPlayback(sw.ElapsedMilliseconds, loop: true);
                        }
                    }
                }

                // Update last button states
                lastL3State = l3Pressed;
                lastAState = aPressed;
                lastXState = xPressed;
                lastYState = yPressed;

                // --- Build packet from current gamepad state or macro playback ---
                byte[] packet;
                if (isPlayingMacro || isLoopingMacro)
                {
                    packet = GetMacroPacket(sw.ElapsedMilliseconds);
                    if (packet == null)
                    {
                        // Macro playback finished
                        if (isLoopingMacro)
                        {
                            // Restart the loop
                            macroPlaybackIndex = 0;
                            macroPlaybackStartTime = sw.ElapsedMilliseconds;
                            packet = recordedMacro[0].Packet;
                        }
                        else
                        {
                            // Single playback finished
                            StopPlayback();
                            packet = BuildPacket8BitPokken(gp);
                        }
                    }
                }
                else
                {
                    packet = BuildPacket8BitPokken(gp);
                }

                // --- Record frame if recording ---
                if (isRecording)
                {
                    RecordFrame(sw.ElapsedMilliseconds, packet);
                }
                byte crc = Crc8Ccitt(packet, 0, packet.Length);

                try
                {
                  // --- Send frame ---
                  serial.Write(packet, 0, packet.Length);
                  serial.Write(new byte[] { crc }, 0, 1);

                  // --- Non-blocking ACK check (no waiting) ---
                  // Drain any pending responses without blocking
                  if (serial.BytesToRead > 0)
                  {
                      int b = serial.ReadByte();
                      if (b == RESP_UPDATE_NACK)
                      {
                          // Uncomment to debug CRC issues:
                          // Console.WriteLine("\n⚠️  CRC NACK - frame dropped");
                      }
                      // ACK received but we don't wait for it - just continue
                  }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\nSerial write error: {ex.Message}");
                }

                // Update console less frequently to reduce latency
                if (sw.ElapsedMilliseconds % 500 < UPDATE_MS)
                {
                    string status = "";
                    if (isRecording)
                        status = $" [RECORDING: {recordedMacro.Count} frames]";
                    else if (isLoopingMacro)
                        status = $" [LOOPING: {macroPlaybackIndex}/{recordedMacro.Count}]";
                    else if (isPlayingMacro)
                        status = $" [PLAYING: {macroPlaybackIndex}/{recordedMacro.Count}]";
                    else if (recordedMacro.Count > 0)
                        status = $" [Macro ready: {recordedMacro.Count} frames]";

                    Console.Write(
                        $"\rBtn:{gp.Buttons,-18} LT:{gp.LeftTrigger,3} RT:{gp.RightTrigger,3} " +
                        $"LX:{gp.LeftThumbX,6} LY:{gp.LeftThumbY,6} RX:{gp.RightThumbX,6} RY:{gp.RightThumbY,6}{status}  "
                    );
                }
            }

            CloseCompanionApp();
            CloseSerial();
        }

        // Press H in console to send HOME once
        static void KeyWatcher()
        {
            try
            {
                while (running)
                {
                    if (Console.KeyAvailable)
                    {
                        var k = Console.ReadKey(true);
                        if (k.Key == ConsoleKey.H) forceHomeOnce = true;
                    }
                    Thread.Sleep(10);
                }
            }
            catch { }
        }

        // ====== Handshake ======
        static bool DoSyncHandshake()
        {
            bool WaitFor(byte expected, int timeoutMs = 1000)
            {
                if (serial == null) return false;

                int start = Environment.TickCount;
                while (Environment.TickCount - start < timeoutMs)
                {
                    if (serial.BytesToRead > 0)
                    {
                        int b = serial.ReadByte();
                        if (b == expected) return true;
                    }
                    Thread.Sleep(2);
                }
                return false;
            }

            try
            {
                if (serial == null) return false;

                serial.DiscardInBuffer();

                serial.Write(new byte[] { CMD_SYNC_START }, 0, 1);
                if (!WaitFor(RESP_SYNC_START)) return false;

                serial.Write(new byte[] { CMD_SYNC_1 }, 0, 1);
                if (!WaitFor(RESP_SYNC_1)) return false;

                serial.Write(new byte[] { CMD_SYNC_2 }, 0, 1);
                if (!WaitFor(RESP_SYNC_OK)) return false;

                return true;
            }
            catch { return false; }
        }

        // ====== Build 8-byte Pokkén report (buttons, hat, 4 axes, vendor byte) ======
        static byte[] BuildPacket8BitPokken(Gamepad gp)
        {
            UInt16 btn = 0;

            // ---- ABXY (Xbox -> Nintendo remap) ----
            // Xbox A (bottom)   -> Nintendo B (bottom)
            // Xbox B (right)    -> Nintendo A (right)
            // Xbox X (left)     -> Nintendo Y (left)
            // Xbox Y (top)      -> Nintendo X (top)
            if ((gp.Buttons & GamepadButtonFlags.A) != 0) btn |= BTN_B;
            if ((gp.Buttons & GamepadButtonFlags.B) != 0) btn |= BTN_A;
            if ((gp.Buttons & GamepadButtonFlags.X) != 0) btn |= BTN_Y;
            if ((gp.Buttons & GamepadButtonFlags.Y) != 0) btn |= BTN_X;

            // Shoulders / Clicks / System
            if ((gp.Buttons & GamepadButtonFlags.LeftShoulder)  != 0) btn |= BTN_L;
            if ((gp.Buttons & GamepadButtonFlags.RightShoulder) != 0) btn |= BTN_R;
            if ((gp.Buttons & GamepadButtonFlags.LeftThumb)     != 0) btn |= BTN_LCLICK;
            if ((gp.Buttons & GamepadButtonFlags.RightThumb)    != 0) btn |= BTN_RCLICK;
            if ((gp.Buttons & GamepadButtonFlags.Start)         != 0) btn |= BTN_PLUS;
            if ((gp.Buttons & GamepadButtonFlags.Back)          != 0) btn |= BTN_MINUS;

            // Triggers -> ZL / ZR
            if (gp.LeftTrigger  > TRIGGER_THRESH) btn |= BTN_ZL;
            if (gp.RightTrigger > TRIGGER_THRESH) btn |= BTN_ZR;

            // Select + Y -> HOME button
            bool selectPressed = (gp.Buttons & GamepadButtonFlags.Back) != 0;
            bool yPressed = (gp.Buttons & GamepadButtonFlags.Y) != 0;
            if (selectPressed && yPressed)
            {
                btn |= BTN_HOME;
                // Clear the MINUS and X buttons so they don't trigger on Switch
                unchecked
                {
                    btn &= (UInt16)(~BTN_MINUS);
                    btn &= (UInt16)(~BTN_X);
                }
            }

            // One-shot HOME via 'H' key
            if (forceHomeOnce) { btn |= BTN_HOME; forceHomeOnce = false; }

            // Hat (D-Pad)
            byte hat = ConvertDpadToHat(gp.Buttons);

            // Axes (do everything in int space; no short negation anywhere)
            int lx = gp.LeftThumbX;
            int ly = gp.LeftThumbY;
            int rx = gp.RightThumbX;
            int ry = gp.RightThumbY;

            // Apply combined radial + axial deadzone (like Joy-Cons)
            ApplyJoyConDeadzone(ref lx, ref ly);
            ApplyJoyConDeadzone(ref rx, ref ry);

            if (INVERT_Y)
            {
                ly = -ly;
                ry = -ry;
            }

            // Clamp, map to 0..255, snap near-center
            byte lx8 = MapAxisTo8BitInt(ClampAxis(lx));
            byte ly8 = MapAxisTo8BitInt(ClampAxis(ly));
            byte rx8 = MapAxisTo8BitInt(ClampAxis(rx));
            byte ry8 = MapAxisTo8BitInt(ClampAxis(ry));

            SnapToCenter(ref lx8);
            SnapToCenter(ref ly8);
            SnapToCenter(ref rx8);
            SnapToCenter(ref ry8);

            // Wire order: buttons_hi, buttons_lo, HAT, LX, LY, RX, RY, vendor
            return new byte[]
            {
                (byte)((btn >> 8) & 0xFF),
                (byte)(btn & 0xFF),
                hat,
                lx8, ly8, rx8, ry8,
                0x00
            };
        }

        // ====== Helpers ======

        /// <summary>
        /// Applies Joy-Con style deadzone: radial deadzone + axial deadzone to prevent cross-axis bleeding
        /// This matches Switch Joy-Con behavior where moving "up" stays purely vertical
        /// </summary>
        static void ApplyJoyConDeadzone(ref int x, ref int y)
        {
            // Step 1: Radial deadzone (like Joy-Con default ~15%)
            // Calculate magnitude using integer math to avoid floating point
            long magSquared = (long)x * x + (long)y * y;
            long radialThresholdSquared = (long)RADIAL_DEADZONE * RADIAL_DEADZONE;

            if (magSquared < radialThresholdSquared)
            {
                // Inside radial deadzone - zero out both axes
                x = 0;
                y = 0;
                return;
            }

            // Step 2: Axial deadzone (prevents cross-axis bleeding)
            // If moving primarily in one direction, zero out the minor axis
            int absX = Math.Abs(x);
            int absY = Math.Abs(y);

            // If one axis is dominant and the other is small, zero out the small one
            if (absX > absY * 3)  // Moving mostly horizontally (>3:1 ratio)
            {
                if (absY < AXIAL_DEADZONE)
                    y = 0;  // Zero out minor vertical movement
            }
            else if (absY > absX * 3)  // Moving mostly vertically (>3:1 ratio)
            {
                if (absX < AXIAL_DEADZONE)
                    x = 0;  // Zero out minor horizontal movement
            }
        }

        static int ClampAxis(int v)
        {
            if (v < -32767) v = -32767;   // avoid the -32768 edge case entirely
            if (v >  32767) v =  32767;
            return v;
        }

        static byte MapAxisTo8BitInt(int v)
        {
            int u = v + 32768;              // 0..65535
            if (u < 0) u = 0;
            if (u > 65535) u = 65535;
            return (byte)((u * 255L) / 65535L);
        }

        static void SnapToCenter(ref byte v)
        {
            const byte center = 0x80;
            const int tol = 2;
            if (Math.Abs(v - center) <= tol) v = center;
        }

        static byte ConvertDpadToHat(GamepadButtonFlags buttons)
        {
            bool up    = (buttons & GamepadButtonFlags.DPadUp)    != 0;
            bool down  = (buttons & GamepadButtonFlags.DPadDown)  != 0;
            bool left  = (buttons & GamepadButtonFlags.DPadLeft)  != 0;
            bool right = (buttons & GamepadButtonFlags.DPadRight) != 0;

            if (up && right)  return 0x01; // up-right
            if (right && down) return 0x03; // down-right
            if (down && left)  return 0x05; // down-left
            if (left && up)    return 0x07; // up-left
            if (up)            return 0x00; // up
            if (right)         return 0x02; // right
            if (down)          return 0x04; // down
            if (left)          return 0x06; // left
            return 0x08;                    // neutral
        }

        static byte Crc8Ccitt(byte[] data, int offset, int length)
        {
            byte crc = 0;
            for (int i = offset; i < offset + length; i++)
            {
                byte t = (byte)(crc ^ data[i]);
                for (int j = 0; j < 8; j++)
                    t = (byte)(((t & 0x80) != 0) ? ((t << 1) ^ 0x07) : (t << 1));
                crc = t;
            }
            return crc;
        }

        static void CloseSerial()
        {
            try { if (serial?.IsOpen == true) serial.Close(); }
            catch { }
        }

        // ====== Config file support ======
        static void LoadConfig()
        {
            try
            {
                // Use AppContext.BaseDirectory instead of Assembly.Location for single-file publish compatibility
                string exeDir = AppContext.BaseDirectory;
                string configPath = Path.Combine(exeDir, "controller-relay.config");

                if (File.Exists(configPath))
                {
                    Console.WriteLine($"Loading config from: {configPath}");
                    var lines = File.ReadAllLines(configPath);
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                            continue;

                        if (trimmed.StartsWith("CompanionApp=", StringComparison.OrdinalIgnoreCase))
                        {
                            companionAppPath = trimmed.Substring("CompanionApp=".Length).Trim();
                            if (!string.IsNullOrEmpty(companionAppPath) && companionAppPath.StartsWith("\"") && companionAppPath.EndsWith("\""))
                            {
                                companionAppPath = companionAppPath.Substring(1, companionAppPath.Length - 2);
                            }
                            Console.WriteLine($"  Companion app: {companionAppPath}");
                        }
                        else if (trimmed.StartsWith("AutoClickX=", StringComparison.OrdinalIgnoreCase))
                        {
                            if (int.TryParse(trimmed.Substring("AutoClickX=".Length).Trim(), out int x))
                            {
                                autoClickX = x;
                                Console.WriteLine($"  Auto-click X: {autoClickX}");
                            }
                        }
                        else if (trimmed.StartsWith("AutoClickY=", StringComparison.OrdinalIgnoreCase))
                        {
                            if (int.TryParse(trimmed.Substring("AutoClickY=".Length).Trim(), out int y))
                            {
                                autoClickY = y;
                                Console.WriteLine($"  Auto-click Y: {autoClickY}");
                            }
                        }
                        else if (trimmed.StartsWith("AutoClickDelay=", StringComparison.OrdinalIgnoreCase))
                        {
                            if (int.TryParse(trimmed.Substring("AutoClickDelay=".Length).Trim(), out int delay))
                            {
                                autoClickDelay = delay;
                                Console.WriteLine($"  Auto-click delay: {autoClickDelay}ms");
                            }
                        }
                        else if (trimmed.StartsWith("AutoClickRelative=", StringComparison.OrdinalIgnoreCase))
                        {
                            string val = trimmed.Substring("AutoClickRelative=".Length).Trim().ToLower();
                            autoClickRelative = (val == "true" || val == "1" || val == "yes");
                            Console.WriteLine($"  Auto-click relative: {autoClickRelative}");
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"No config file found at {configPath}");
                    Console.WriteLine("To launch a companion app, create controller-relay.config with:");
                    Console.WriteLine("  CompanionApp=\"C:\\Path\\To\\Your\\App.exe\"");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading config: {ex.Message}");
            }
        }

        static void LaunchCompanionApp()
        {
            if (string.IsNullOrEmpty(companionAppPath))
                return;

            try
            {
                if (!File.Exists(companionAppPath))
                {
                    Console.WriteLine($"⚠️  Companion app not found: {companionAppPath}");
                    return;
                }

                Console.WriteLine($"Launching companion app: {companionAppPath}");
                companionProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = companionAppPath,
                    UseShellExecute = false  // Changed to false so we can track the process properly
                });

                if (companionProcess != null)
                {
                    companionProcessId = companionProcess.Id;
                    companionProcessName = companionProcess.ProcessName;
                    Console.WriteLine($"✓ Companion app launched (PID: {companionProcessId}, Name: {companionProcessName})");

                    // Give it a moment to potentially spawn child processes and initialize
                    Thread.Sleep(500);

                    // Perform auto-click if configured
                    if (autoClickX.HasValue && autoClickY.HasValue)
                    {
                        Console.WriteLine($"Waiting {autoClickDelay}ms before auto-click...");
                        Thread.Sleep(autoClickDelay);
                        PerformAutoClick(autoClickX.Value, autoClickY.Value);
                    }
                }
                else
                {
                    Console.WriteLine("⚠️  Failed to get process handle for companion app");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Failed to launch companion app: {ex.Message}");
            }
        }

        static void PerformAutoClick(int x, int y)
        {
            try
            {
                int screenX = x;
                int screenY = y;

                if (autoClickRelative)
                {
                    Console.WriteLine($"Auto-clicking at window-relative position ({x}, {y})...");

                    // Find the companion app window
                    IntPtr windowHandle = FindCompanionWindow();

                    if (windowHandle == IntPtr.Zero)
                    {
                        Console.WriteLine("⚠️  Could not find companion app window. Using absolute coordinates.");
                    }
                    else
                    {
                        // Get window position
                        RECT windowRect;
                        if (GetWindowRect(windowHandle, out windowRect))
                        {
                            screenX = windowRect.Left + x;
                            screenY = windowRect.Top + y;
                            Console.WriteLine($"  Window position: ({windowRect.Left}, {windowRect.Top})");
                            Console.WriteLine($"  Clicking at screen position: ({screenX}, {screenY})");

                            // Bring window to foreground first
                            SetForegroundWindow(windowHandle);
                            Thread.Sleep(100);
                        }
                        else
                        {
                            Console.WriteLine("⚠️  Could not get window rectangle. Using absolute coordinates.");
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"Auto-clicking at absolute screen position ({x}, {y})...");
                }

                // Save current cursor position
                System.Drawing.Point currentPos = System.Windows.Forms.Cursor.Position;

                // Move cursor to target position
                SetCursorPos(screenX, screenY);
                Thread.Sleep(50); // Small delay to ensure cursor moved

                // Perform left click
                mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                Thread.Sleep(50);
                mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);

                Console.WriteLine("✓ Auto-click completed");

                // Optional: restore cursor position after a brief delay
                Thread.Sleep(100);
                SetCursorPos(currentPos.X, currentPos.Y);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Auto-click failed: {ex.Message}");
            }
        }

        static IntPtr FindCompanionWindow()
        {
            if (!companionProcessId.HasValue)
                return IntPtr.Zero;

            try
            {
                // Get all child processes (the actual app windows)
                var childPids = new List<int>();
                var allProcesses = Process.GetProcesses();

                foreach (var proc in allProcesses)
                {
                    try
                    {
                        int parentPid = GetParentProcessId(proc.Id);
                        if (parentPid == companionProcessId.Value)
                        {
                            childPids.Add(proc.Id);
                        }
                    }
                    catch { }
                    finally
                    {
                        proc.Dispose();
                    }
                }

                // Also include the main process
                childPids.Add(companionProcessId.Value);

                // Search through all windows to find one belonging to our process or its children
                IntPtr foundWindow = IntPtr.Zero;
                foreach (int pid in childPids)
                {
                    foundWindow = FindWindowByProcessId((uint)pid);
                    if (foundWindow != IntPtr.Zero)
                    {
                        Console.WriteLine($"  Found window for PID {pid}");
                        return foundWindow;
                    }
                }

                return IntPtr.Zero;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error finding window: {ex.Message}");
                return IntPtr.Zero;
            }
        }

        static IntPtr FindWindowByProcessId(uint processId)
        {
            IntPtr foundHandle = IntPtr.Zero;

            // Enumerate all top-level windows
            Process? proc = null;
            try
            {
                proc = Process.GetProcessById((int)processId);
                if (proc.MainWindowHandle != IntPtr.Zero)
                {
                    return proc.MainWindowHandle;
                }
            }
            catch { }
            finally
            {
                proc?.Dispose();
            }

            return IntPtr.Zero;
        }

        static void CloseCompanionApp()
        {
            if (companionProcess == null && companionProcessId == null)
            {
                Console.WriteLine("No companion process to close");
                return;
            }

            try
            {
                // First, try to close the original process if it's still alive
                if (companionProcess != null)
                {
                    try
                    {
                        if (!companionProcess.HasExited)
                        {
                            Console.WriteLine($"Closing main companion process (PID: {companionProcess.Id})...");
                            companionProcess.Kill(entireProcessTree: true);
                            companionProcess.WaitForExit(1000);
                        }
                        else
                        {
                            Console.WriteLine($"Main companion process (PID: {companionProcess.Id}) already exited");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Note: Main process already gone ({ex.Message})");
                    }
                }

                // Now find and kill all child processes spawned by the original process
                if (companionProcessId.HasValue && !string.IsNullOrEmpty(companionProcessName))
                {
                    Console.WriteLine($"Searching for child processes of PID {companionProcessId}...");
                    KillProcessAndChildren(companionProcessId.Value);
                }

                Console.WriteLine("✓ Companion app and children closed");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Error closing companion app: {ex.Message}");
            }
            finally
            {
                companionProcess?.Dispose();
                companionProcess = null;
                companionProcessId = null;
                companionProcessName = null;
            }
        }

        static void KillProcessAndChildren(int pid)
        {
            try
            {
                // Get all processes and find children by matching ParentProcessId
                var allProcesses = Process.GetProcesses();

                foreach (var proc in allProcesses)
                {
                    try
                    {
                        // Get parent process ID using native method
                        int parentPid = GetParentProcessId(proc.Id);

                        if (parentPid == pid)
                        {
                            Console.WriteLine($"  Found child process: {proc.Id} ({proc.ProcessName})");
                            // Recursively kill children of children
                            KillProcessAndChildren(proc.Id);
                        }
                    }
                    catch
                    {
                        // Process might have exited, continue
                    }
                    finally
                    {
                        proc.Dispose();
                    }
                }

                // Now kill the process itself
                try
                {
                    Process proc = Process.GetProcessById(pid);
                    if (!proc.HasExited)
                    {
                        Console.WriteLine($"  Killing process {pid} ({proc.ProcessName})...");
                        proc.Kill(entireProcessTree: true);
                        proc.WaitForExit(1000);
                    }
                    proc.Dispose();
                }
                catch (ArgumentException)
                {
                    // Process doesn't exist anymore, that's fine
                    Console.WriteLine($"  Process {pid} already exited");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Could not kill process {pid}: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error searching for child processes: {ex.Message}");
            }
        }

        // ====== P/Invoke for Windows APIs ======

        // Get parent process ID
        [System.Runtime.InteropServices.DllImport("ntdll.dll")]
        private static extern int NtQueryInformationProcess(
            IntPtr processHandle,
            int processInformationClass,
            ref PROCESS_BASIC_INFORMATION processInformation,
            int processInformationLength,
            out int returnLength);

        // Mouse click APIs
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, int dwExtraInfo);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct PROCESS_BASIC_INFORMATION
        {
            public IntPtr Reserved1;
            public IntPtr PebBaseAddress;
            public IntPtr Reserved2_0;
            public IntPtr Reserved2_1;
            public IntPtr UniqueProcessId;
            public IntPtr InheritedFromUniqueProcessId;
        }

        private static int GetParentProcessId(int processId)
        {
            try
            {
                using (var process = Process.GetProcessById(processId))
                {
                    PROCESS_BASIC_INFORMATION pbi = new PROCESS_BASIC_INFORMATION();
                    int returnLength;
                    int status = NtQueryInformationProcess(
                        process.Handle,
                        0, // ProcessBasicInformation
                        ref pbi,
                        System.Runtime.InteropServices.Marshal.SizeOf(pbi),
                        out returnLength);

                    if (status == 0)
                    {
                        return pbi.InheritedFromUniqueProcessId.ToInt32();
                    }
                }
            }
            catch
            {
                // Ignore errors
            }
            return -1;
        }

        // ====== Macro recording/playback functions ======

        static void StartRecording(long currentTime)
        {
            isRecording = true;
            recordedMacro.Clear();
            macroPlaybackIndex = 0;
            recordingStartTime = currentTime;
            lastRecordedPacket = null;
            Console.WriteLine("\n[MACRO] Recording started! Press L3 + A again to stop.");
        }

        static void StopRecording()
        {
            isRecording = false;
            lastRecordedPacket = null;
            Console.WriteLine($"\n[MACRO] Recording stopped. {recordedMacro.Count} unique frames captured.");
            SaveMacro();
        }

        static void RecordFrame(long currentTime, byte[] packet)
        {
            // Only record if packet has changed from last recorded packet
            if (lastRecordedPacket != null)
            {
                bool isDifferent = false;
                for (int i = 0; i < 8; i++)
                {
                    if (packet[i] != lastRecordedPacket[i])
                    {
                        isDifferent = true;
                        break;
                    }
                }

                if (!isDifferent)
                    return; // Skip identical packets
            }

            // Store frame with relative timestamp from recording start
            long relativeTime = currentTime - recordingStartTime;
            var frame = new MacroFrame
            {
                TimestampMs = relativeTime,
                Packet = new byte[8]
            };
            Array.Copy(packet, frame.Packet, 8);
            recordedMacro.Add(frame);

            // Update last recorded packet
            if (lastRecordedPacket == null)
                lastRecordedPacket = new byte[8];
            Array.Copy(packet, lastRecordedPacket, 8);
        }

        static void StartPlayback(long currentTime, bool loop)
        {
            isPlayingMacro = !loop;
            isLoopingMacro = loop;
            macroPlaybackIndex = 0;
            macroPlaybackStartTime = currentTime;

            string mode = loop ? "LOOPING" : "PLAYING ONCE";
            Console.WriteLine($"\n[MACRO] {mode} - {recordedMacro.Count} frames. " +
                            (loop ? "Press L3 + Y to stop." : ""));
        }

        static void StopPlayback()
        {
            isPlayingMacro = false;
            isLoopingMacro = false;
            macroPlaybackIndex = 0;
            Console.WriteLine("\n[MACRO] Playback stopped.");
        }

        static byte[] GetMacroPacket(long currentTime)
        {
            if (recordedMacro.Count == 0)
                return null;

            // Calculate elapsed time since playback started
            long elapsedMs = currentTime - macroPlaybackStartTime;

            // Find the appropriate frame based on elapsed time
            // Frames are stored with timestamps indicating when they should be applied

            // If we're beyond the last frame's timestamp, playback is done
            if (macroPlaybackIndex >= recordedMacro.Count - 1 &&
                elapsedMs >= recordedMacro[recordedMacro.Count - 1].TimestampMs)
            {
                return null; // Playback finished
            }

            // Find the current frame: the latest frame whose timestamp has been reached
            while (macroPlaybackIndex < recordedMacro.Count - 1 &&
                   elapsedMs >= recordedMacro[macroPlaybackIndex + 1].TimestampMs)
            {
                macroPlaybackIndex++;
            }

            // Return the current frame's packet
            return recordedMacro[macroPlaybackIndex].Packet;
        }

        static void SaveMacro()
        {
            try
            {
                if (recordedMacro.Count == 0)
                {
                    Console.WriteLine("[MACRO] No frames to save.");
                    return;
                }

                // Frames already have relative timestamps from recording
                var json = JsonSerializer.Serialize(recordedMacro, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(macroFilePath, json);

                long durationMs = recordedMacro[recordedMacro.Count - 1].TimestampMs;
                double durationSec = durationMs / 1000.0;
                Console.WriteLine($"[MACRO] Saved {recordedMacro.Count} frames to {Path.GetFileName(macroFilePath)}");
                Console.WriteLine($"[MACRO] Duration: {durationSec:F2}s");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MACRO] Error saving macro: {ex.Message}");
            }
        }

        static void LoadMacro()
        {
            try
            {
                if (!File.Exists(macroFilePath))
                {
                    Console.WriteLine("[MACRO] No saved macro found.");
                    return;
                }

                var json = File.ReadAllText(macroFilePath);
                var frames = JsonSerializer.Deserialize<List<MacroFrame>>(json);

                if (frames != null && frames.Count > 0)
                {
                    recordedMacro = frames;
                    long durationMs = recordedMacro[recordedMacro.Count - 1].TimestampMs;
                    double durationSec = durationMs / 1000.0;
                    Console.WriteLine($"[MACRO] Loaded {recordedMacro.Count} frames from {Path.GetFileName(macroFilePath)}");
                    Console.WriteLine($"[MACRO] Duration: {durationSec:F2}s");
                    Console.WriteLine("[MACRO] Press L3 + X to play once, L3 + Y to loop");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MACRO] Error loading macro: {ex.Message}");
            }
        }
    }
}
