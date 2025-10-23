using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace SwitchController
{
    /// <summary>
    /// Manages launching and closing companion applications and performing auto-click operations
    /// </summary>
    public class CompanionAppManager : IDisposable
    {
        private Process? _companionProcess;
        private int? _companionProcessId;
        private string? _companionProcessName;
        private readonly Configuration _config;

        public CompanionAppManager(Configuration config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>
        /// Launches the companion application if configured
        /// </summary>
        public void Launch()
        {
            if (string.IsNullOrEmpty(_config.CompanionAppPath))
                return;

            try
            {
                if (!File.Exists(_config.CompanionAppPath))
                {
                    Console.WriteLine($"⚠️  Companion app not found: {_config.CompanionAppPath}");
                    return;
                }

                Console.WriteLine($"Launching companion app: {_config.CompanionAppPath}");
                _companionProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = _config.CompanionAppPath,
                    UseShellExecute = false
                });

                if (_companionProcess != null)
                {
                    _companionProcessId = _companionProcess.Id;
                    _companionProcessName = _companionProcess.ProcessName;
                    Console.WriteLine($"✓ Companion app launched (PID: {_companionProcessId}, Name: {_companionProcessName})");

                    // Give the app time to initialize
                    Thread.Sleep(500);

                    // Perform auto-click if configured
                    if (_config.AutoClickX.HasValue && _config.AutoClickY.HasValue)
                    {
                        Console.WriteLine($"Waiting {_config.AutoClickDelay}ms before auto-click...");
                        Thread.Sleep(_config.AutoClickDelay);
                        PerformAutoClick(_config.AutoClickX.Value, _config.AutoClickY.Value);
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

        /// <summary>
        /// Performs an automatic mouse click at the specified coordinates
        /// </summary>
        private void PerformAutoClick(int x, int y)
        {
            try
            {
                int screenX = x;
                int screenY = y;

                if (_config.AutoClickRelative)
                {
                    Console.WriteLine($"Auto-clicking at window-relative position ({x}, {y})...");

                    IntPtr windowHandle = FindCompanionWindow();
                    if (windowHandle == IntPtr.Zero)
                    {
                        Console.WriteLine("⚠️  Could not find companion app window. Using absolute coordinates.");
                    }
                    else if (WindowsNative.GetWindowRect(windowHandle, out var windowRect))
                    {
                        screenX = windowRect.Left + x;
                        screenY = windowRect.Top + y;
                        Console.WriteLine($"  Window position: ({windowRect.Left}, {windowRect.Top})");
                        Console.WriteLine($"  Clicking at screen position: ({screenX}, {screenY})");

                        WindowsNative.SetForegroundWindow(windowHandle);
                        Thread.Sleep(100);
                    }
                    else
                    {
                        Console.WriteLine("⚠️  Could not get window rectangle. Using absolute coordinates.");
                    }
                }
                else
                {
                    Console.WriteLine($"Auto-clicking at absolute screen position ({x}, {y})...");
                }

                // Save and restore cursor position
                var currentPos = System.Windows.Forms.Cursor.Position;

                WindowsNative.SetCursorPos(screenX, screenY);
                Thread.Sleep(50);

                WindowsNative.mouse_event(WindowsNative.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                Thread.Sleep(50);
                WindowsNative.mouse_event(WindowsNative.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);

                Console.WriteLine("✓ Auto-click completed");

                Thread.Sleep(100);
                WindowsNative.SetCursorPos(currentPos.X, currentPos.Y);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Auto-click failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Finds the main window of the companion application
        /// </summary>
        private IntPtr FindCompanionWindow()
        {
            if (!_companionProcessId.HasValue)
                return IntPtr.Zero;

            try
            {
                // Collect child process IDs
                var childPids = new List<int>();
                var allProcesses = Process.GetProcesses();

                foreach (var proc in allProcesses)
                {
                    try
                    {
                        int parentPid = GetParentProcessId(proc.Id);
                        if (parentPid == _companionProcessId.Value)
                        {
                            childPids.Add(proc.Id);
                        }
                    }
                    catch { /* Process may have exited */ }
                    finally
                    {
                        proc.Dispose();
                    }
                }

                childPids.Add(_companionProcessId.Value);

                // Search for windows belonging to our process tree
                foreach (int pid in childPids)
                {
                    IntPtr windowHandle = FindWindowByProcessId((uint)pid);
                    if (windowHandle != IntPtr.Zero)
                    {
                        Console.WriteLine($"  Found window for PID {pid}");
                        return windowHandle;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error finding window: {ex.Message}");
            }

            return IntPtr.Zero;
        }

        /// <summary>
        /// Finds the main window handle for a given process ID
        /// </summary>
        private static IntPtr FindWindowByProcessId(uint processId)
        {
            Process? proc = null;
            try
            {
                proc = Process.GetProcessById((int)processId);
                return proc.MainWindowHandle;
            }
            catch
            {
                return IntPtr.Zero;
            }
            finally
            {
                proc?.Dispose();
            }
        }

        /// <summary>
        /// Gets the parent process ID for a given process
        /// </summary>
        private static int GetParentProcessId(int processId)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                var pbi = new WindowsNative.PROCESS_BASIC_INFORMATION();
                int status = WindowsNative.NtQueryInformationProcess(
                    process.Handle,
                    0, // ProcessBasicInformation
                    ref pbi,
                    System.Runtime.InteropServices.Marshal.SizeOf(pbi),
                    out _);

                if (status == 0)
                {
                    return pbi.InheritedFromUniqueProcessId.ToInt32();
                }
            }
            catch { /* Process may have exited or access denied */ }

            return -1;
        }

        /// <summary>
        /// Closes the companion application and all its child processes
        /// </summary>
        public void Close()
        {
            if (_companionProcess == null && _companionProcessId == null)
            {
                Console.WriteLine("No companion process to close");
                return;
            }

            try
            {
                if (_companionProcess != null)
                {
                    try
                    {
                        if (!_companionProcess.HasExited)
                        {
                            Console.WriteLine($"Closing main companion process (PID: {_companionProcess.Id})...");
                            _companionProcess.Kill(entireProcessTree: true);
                            _companionProcess.WaitForExit(1000);
                        }
                        else
                        {
                            Console.WriteLine($"Main companion process (PID: {_companionProcess.Id}) already exited");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Note: Main process already gone ({ex.Message})");
                    }
                }

                if (_companionProcessId.HasValue)
                {
                    Console.WriteLine($"Searching for child processes of PID {_companionProcessId}...");
                    KillProcessAndChildren(_companionProcessId.Value);
                }

                Console.WriteLine("✓ Companion app and children closed");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Error closing companion app: {ex.Message}");
            }
            finally
            {
                _companionProcess?.Dispose();
                _companionProcess = null;
                _companionProcessId = null;
                _companionProcessName = null;
            }
        }

        /// <summary>
        /// Recursively kills a process and all its children
        /// </summary>
        private static void KillProcessAndChildren(int pid)
        {
            try
            {
                var allProcesses = Process.GetProcesses();

                foreach (var proc in allProcesses)
                {
                    try
                    {
                        int parentPid = GetParentProcessId(proc.Id);
                        if (parentPid == pid)
                        {
                            Console.WriteLine($"  Found child process: {proc.Id} ({proc.ProcessName})");
                            KillProcessAndChildren(proc.Id);
                        }
                    }
                    catch { /* Process may have exited */ }
                    finally
                    {
                        proc.Dispose();
                    }
                }

                try
                {
                    var process = Process.GetProcessById(pid);
                    if (!process.HasExited)
                    {
                        Console.WriteLine($"  Killing process {pid} ({process.ProcessName})...");
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(1000);
                    }
                    process.Dispose();
                }
                catch (ArgumentException)
                {
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

        public void Dispose()
        {
            Close();
        }
    }
}
