using System;
using System.IO.Ports;
using System.Threading;
using static SwitchController.SwitchControllerConstants;

namespace SwitchController
{
    /// <summary>
    /// Manages serial communication with the Switch controller firmware
    /// </summary>
    public class SerialConnection : IDisposable
    {
        private SerialPort? _serial;

        public bool IsOpen => _serial?.IsOpen ?? false;

        /// <summary>
        /// Opens the serial port
        /// </summary>
        public bool Open(string portName)
        {
            try
            {
                _serial = new SerialPort(portName, 115200);
                _serial.Open();
                _serial.DtrEnable = false;
                _serial.RtsEnable = false;
                Console.WriteLine($"Opened {portName}");
                Console.WriteLine();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to open {portName}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Performs sync handshake with the firmware
        /// </summary>
        public bool PerformSyncHandshake()
        {
            bool WaitFor(byte expected, int timeoutMs = 1000)
            {
                if (_serial == null) return false;

                int start = Environment.TickCount;
                while (Environment.TickCount - start < timeoutMs)
                {
                    if (_serial.BytesToRead > 0)
                    {
                        int b = _serial.ReadByte();
                        if (b == expected) return true;
                    }
                    Thread.Sleep(2);
                }
                return false;
            }

            try
            {
                if (_serial == null) return false;

                _serial.DiscardInBuffer();

                _serial.Write(new byte[] { CMD_SYNC_START }, 0, 1);
                if (!WaitFor(RESP_SYNC_START)) return false;

                _serial.Write(new byte[] { CMD_SYNC_1 }, 0, 1);
                if (!WaitFor(RESP_SYNC_1)) return false;

                _serial.Write(new byte[] { CMD_SYNC_2 }, 0, 1);
                if (!WaitFor(RESP_SYNC_OK)) return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Sends a packet with CRC
        /// </summary>
        public void SendPacket(byte[] packet, byte crc)
        {
            if (_serial == null || !_serial.IsOpen)
                throw new InvalidOperationException("Serial port is not open");

            _serial.Write(packet, 0, packet.Length);
            _serial.Write(new byte[] { crc }, 0, 1);
        }

        /// <summary>
        /// Checks for ACK/NACK responses without blocking
        /// </summary>
        public void CheckResponses()
        {
            if (_serial == null || _serial.BytesToRead == 0)
                return;

            int b = _serial.ReadByte();
            if (b == RESP_UPDATE_NACK)
            {
                // Uncomment to debug CRC issues:
                // Console.WriteLine("\n⚠️  CRC NACK - frame dropped");
            }
        }

        /// <summary>
        /// Closes the serial port
        /// </summary>
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
    }
}
