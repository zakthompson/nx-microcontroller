using System;

namespace SwitchController
{
    /// <summary>
    /// Abstraction for controller input sources (XInput, SDL2, etc.)
    /// </summary>
    public interface IControllerInput : IDisposable
    {
        /// <summary>
        /// Returns true if a controller is currently connected
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Gets the current controller state
        /// </summary>
        ControllerState GetCurrentState();

        /// <summary>
        /// Checks if a specific button is currently pressed
        /// </summary>
        bool IsButtonPressed(SwitchButton button);

        /// <summary>
        /// Called each tick to poll/pump controller events
        /// </summary>
        void Update();
    }
}
