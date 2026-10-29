using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Threading;
using static SwitchController.SwitchControllerConstants;

namespace SwitchController
{
    /// <summary>
    /// Transport for the native nx-microcontroller firmware protocol:
    /// 3-step sync handshake, 8-byte packets + CRC8.
    /// Sends only on change (plus a keepalive) so the line is idle when a change needs to go out.
    /// </summary>
    public class NativeTransport : IControllerTransport
    {
        private SerialPort? _port;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private ControllerState? _lastSentState;
        private long _lastSendTimeMs;
        private bool _resendRequested;

        public bool Connect(SerialPort port)
        {
            _port = port;

            try
            {
                _port.DiscardInBuffer();
                _lastSentState = null;

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

            long now = _clock.ElapsedMilliseconds;
            bool changed = !_lastSentState.HasValue || !_lastSentState.Value.Equals(state);
            if (!changed && !_resendRequested && now - _lastSendTimeMs < KEEPALIVE_INTERVAL_MS) return;

            // Packet and CRC in one write so they go out as a single USB transfer
            byte[] packet = state.ToNativePacket();
            byte[] frame = new byte[packet.Length + 1];
            packet.CopyTo(frame, 0);
            frame[^1] = PacketBuilder.CalculateCrc8(packet, 0, packet.Length);
            _port.Write(frame, 0, frame.Length);

            _lastSentState = state;
            _lastSendTimeMs = now;
            _resendRequested = false;
        }

        public void Update()
        {
            if (_port == null) return;

            // Drain all responses; a NACK means the firmware dropped a packet, so resend the state
            while (_port.BytesToRead > 0)
            {
                if (_port.ReadByte() == RESP_UPDATE_NACK)
                    _resendRequested = true;
            }
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
                while (_port.BytesToRead > 0)
                {
                    if (_port.ReadByte() == expected) return true;
                }
                Thread.Sleep(2);
            }
            return false;
        }
    }
}
