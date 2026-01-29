using System;
using System.IO.Ports;

namespace SwitchController
{
    /// <summary>
    /// Thin wrapper around SerialPort for opening/closing the connection.
    /// Protocol logic lives in IControllerTransport implementations.
    /// </summary>
    public class SerialConnection : IDisposable
    {
        private SerialPort? _serial;
        private readonly Action<string>? _statusCallback;

        public SerialConnection(Action<string>? statusCallback = null)
        {
            _statusCallback = statusCallback;
        }

        public bool IsOpen => _serial?.IsOpen ?? false;

        /// <summary>
        /// The underlying serial port, for use by transports
        /// </summary>
        public SerialPort? Port => _serial;

        public bool Open(string portName)
        {
            try
            {
                _serial = new SerialPort(portName, 115200);
                _serial.Open();
                _serial.DtrEnable = false;
                _serial.RtsEnable = false;
                LogStatus($"Opened {portName}");
                return true;
            }
            catch (Exception ex)
            {
                LogStatus($"Failed to open {portName}: {ex.Message}");
                return false;
            }
        }

        public void Close()
        {
            try
            {
                if (_serial?.IsOpen == true)
                {
                    _serial.Close();
                }
            }
            catch { /* Ignore errors on close */ }
        }

        public void Dispose()
        {
            Close();
            _serial?.Dispose();
        }

        private void LogStatus(string message)
        {
            if (_statusCallback != null)
            {
                _statusCallback(message);
            }
            else
            {
                Console.WriteLine(message);
            }
        }
    }
}
