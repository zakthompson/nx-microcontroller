using System;
using System.IO.Ports;
using System.Threading;
using static SwitchController.SwitchControllerConstants;

namespace SwitchController
{
    /// <summary>
    /// Transport for the native nx-microcontroller firmware protocol:
    /// 3-step sync handshake, 8-byte packets + CRC8
    /// </summary>
    public class NativeTransport : IControllerTransport
    {
        private SerialPort? _port;

        public bool Connect(SerialPort port)
        {
            _port = port;

            try
            {
                _port.DiscardInBuffer();

                _port.Write(new byte[] { CMD_SYNC_START }, 0, 1);
                if (!WaitFor(RESP_SYNC_START)) return false;

                _port.Write(new byte[] { CMD_SYNC_1 }, 0, 1);
                if (!WaitFor(RESP_SYNC_1)) return false;

                _port.Write(new byte[] { CMD_SYNC_2 }, 0, 1);
                if (!WaitFor(RESP_SYNC_OK)) return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        public void SendState(ControllerState state)
        {
            if (_port == null || !_port.IsOpen) return;

            byte[] packet = state.ToNativePacket();
            byte crc = PacketBuilder.CalculateCrc8(packet, 0, packet.Length);
            _port.Write(packet, 0, packet.Length);
            _port.Write(new byte[] { crc }, 0, 1);
        }

        public void Update()
        {
            if (_port == null || _port.BytesToRead == 0) return;

            // Drain responses (ACK/NACK)
            _port.ReadByte();
        }

        public void Dispose()
        {
            _port = null;
        }

        private bool WaitFor(byte expected, int timeoutMs = 1000)
        {
            if (_port == null) return false;

            int start = Environment.TickCount;
            while (Environment.TickCount - start < timeoutMs)
            {
                if (_port.BytesToRead > 0)
                {
                    int b = _port.ReadByte();
                    if (b == expected) return true;
                }
                Thread.Sleep(2);
            }
            return false;
        }
    }
}
