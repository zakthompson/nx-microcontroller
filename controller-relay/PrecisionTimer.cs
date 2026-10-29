using System;
using System.Threading;

namespace SwitchController
{
    /// <summary>
    /// Sleeps with sub-millisecond precision. Thread.Sleep rounds up to the 15.6 ms system tick,
    /// which would cap controller sampling at ~64 Hz.
    /// </summary>
    public sealed class PrecisionTimer : IDisposable
    {
        private readonly IntPtr _handle;
        private readonly bool _usingFallback;

        public PrecisionTimer()
        {
            _handle = WindowsNative.CreateWaitableTimerExW(
                IntPtr.Zero,
                null,
                WindowsNative.CREATE_WAITABLE_TIMER_HIGH_RESOLUTION,
                WindowsNative.TIMER_ALL_ACCESS);

            // High-resolution timers require Windows 10 1803+; older systems get a 1 ms system tick instead
            if (_handle == IntPtr.Zero)
            {
                WindowsNative.timeBeginPeriod(1);
                _usingFallback = true;
            }
        }

        /// <summary>
        /// Blocks the calling thread for the given number of milliseconds
        /// </summary>
        public void Sleep(int milliseconds)
        {
            // Negative due time means relative, in 100 ns units
            long dueTime = -milliseconds * 10_000L;

            if (_usingFallback ||
                !WindowsNative.SetWaitableTimer(_handle, ref dueTime, 0, IntPtr.Zero, IntPtr.Zero, false))
            {
                Thread.Sleep(milliseconds);
                return;
            }

            WindowsNative.WaitForSingleObject(_handle, WindowsNative.INFINITE);
        }

        public void Dispose()
        {
            if (_usingFallback)
            {
                WindowsNative.timeEndPeriod(1);
            }
            else
            {
                WindowsNative.CloseHandle(_handle);
            }
        }
    }
}
