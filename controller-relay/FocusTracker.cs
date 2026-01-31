using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SwitchController
{
    /// <summary>
    /// Tracks whether any of the registered process windows (or their descendants) have focus
    /// </summary>
    public class FocusTracker
    {
        private readonly HashSet<int> _trackedProcessIds = new HashSet<int>();
        private readonly HashSet<int> _knownDescendants = new HashSet<int>();
        private bool _isFocused = true;
        private long _lastCheckTime;
        private long _lastTreeScanTime;
        private const int CHECK_INTERVAL_MS = 100;
        private const int TREE_SCAN_INTERVAL_MS = 2000;

        /// <summary>
        /// Returns true if focus tracking is active (at least one process ID registered)
        /// </summary>
        public bool IsActive => _trackedProcessIds.Count > 0;

        /// <summary>
        /// Returns true if focus tracking is disabled or one of the tracked processes has focus
        /// </summary>
        public bool IsFocused => !IsActive || _isFocused;

        /// <summary>
        /// Registers a process ID to track for focus
        /// </summary>
        public void AddProcessId(int pid)
        {
            _trackedProcessIds.Add(pid);
        }

        /// <summary>
        /// Unregisters a process ID from focus tracking
        /// </summary>
        public void RemoveProcessId(int pid)
        {
            _trackedProcessIds.Remove(pid);
        }

        /// <summary>
        /// Updates the focus state. Should be called each tick.
        /// </summary>
        public void Update(long currentTimeMs)
        {
            if (!IsActive)
                return;

            if (currentTimeMs - _lastCheckTime < CHECK_INTERVAL_MS)
                return;

            _lastCheckTime = currentTimeMs;

            // Periodically rescan the process tree to discover new child processes
            if (currentTimeMs - _lastTreeScanTime >= TREE_SCAN_INTERVAL_MS)
            {
                _lastTreeScanTime = currentTimeMs;
                RefreshDescendants();
            }

            IntPtr hwnd = WindowsNative.GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                _isFocused = false;
                return;
            }

            WindowsNative.GetWindowThreadProcessId(hwnd, out uint foregroundPid);
            int pid = (int)foregroundPid;

            _isFocused = _trackedProcessIds.Contains(pid) || _knownDescendants.Contains(pid);
        }

        /// <summary>
        /// Scans all running processes to find descendants of tracked processes
        /// </summary>
        private void RefreshDescendants()
        {
            _knownDescendants.Clear();

            try
            {
                var allProcesses = Process.GetProcesses();
                // Build parent→children map
                var parentMap = new Dictionary<int, int>(); // child pid → parent pid

                foreach (var proc in allProcesses)
                {
                    try
                    {
                        int parentPid = GetParentProcessId(proc.Id);
                        if (parentPid > 0)
                        {
                            parentMap[proc.Id] = parentPid;
                        }
                    }
                    catch { }
                    finally
                    {
                        proc.Dispose();
                    }
                }

                // For each process, walk up its parent chain to see if it descends from a tracked PID
                foreach (var kvp in parentMap)
                {
                    if (_trackedProcessIds.Contains(kvp.Key))
                        continue;

                    int current = kvp.Key;
                    var visited = new HashSet<int>();
                    while (parentMap.TryGetValue(current, out int parent) && visited.Add(current))
                    {
                        if (_trackedProcessIds.Contains(parent))
                        {
                            _knownDescendants.Add(kvp.Key);
                            break;
                        }
                        current = parent;
                    }
                }
            }
            catch { }
        }

        private static int GetParentProcessId(int processId)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                var pbi = new WindowsNative.PROCESS_BASIC_INFORMATION();
                int status = WindowsNative.NtQueryInformationProcess(
                    process.Handle,
                    0,
                    ref pbi,
                    Marshal.SizeOf(pbi),
                    out _);

                if (status == 0)
                {
                    return pbi.InheritedFromUniqueProcessId.ToInt32();
                }
            }
            catch { }

            return -1;
        }
    }
}
