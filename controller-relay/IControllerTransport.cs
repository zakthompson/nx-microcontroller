using System;
using System.IO.Ports;

namespace SwitchController
{
    public interface IControllerTransport : IDisposable
    {
        /// <summary>
        /// Performs the transport-specific handshake on an already-opened serial port.
        /// Returns true if the handshake succeeds.
        /// </summary>
        bool Connect(SerialPort port);

        /// <summary>
        /// Sends the current controller state to the device.
        /// The transport handles its own wire format and timing.
        /// </summary>
        void SendState(ControllerState state);

        /// <summary>
        /// Processes incoming serial data (non-blocking).
        /// Call this each iteration of the main loop.
        /// </summary>
        void Update();
    }
}
