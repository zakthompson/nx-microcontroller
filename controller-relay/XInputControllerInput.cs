using System;
using SharpDX.XInput;
using static SwitchController.SwitchControllerConstants;

namespace SwitchController
{
    /// <summary>
    /// XInput implementation of controller input
    /// </summary>
    public class XInputControllerInput : IControllerInput
    {
        private Controller? _controller;
        private Gamepad _gamepad;

        public bool IsConnected => _controller?.IsConnected ?? false;

        public XInputControllerInput()
        {
            _controller = new Controller(UserIndex.One);
        }

        public void Update()
        {
            if (_controller == null)
                return;

            if (!_controller.IsConnected)
            {
                // Try reconnecting
                _controller = new Controller(UserIndex.One);
                _gamepad = default;
                return;
            }

            var state = _controller.GetState();
            _gamepad = state.Gamepad;
        }

        public ControllerState GetCurrentState()
        {
            return PacketBuilder.BuildControllerState(_gamepad);
        }

        public bool IsButtonPressed(SwitchButton button)
        {
            if (!IsConnected || button == SwitchButton.None)
                return false;

            switch (button)
            {
                case SwitchButton.LS:
                    return (_gamepad.Buttons & GamepadButtonFlags.LeftThumb) != 0;
                case SwitchButton.RS:
                    return (_gamepad.Buttons & GamepadButtonFlags.RightThumb) != 0;
                case SwitchButton.Up:
                    return (_gamepad.Buttons & GamepadButtonFlags.DPadUp) != 0;
                case SwitchButton.Down:
                    return (_gamepad.Buttons & GamepadButtonFlags.DPadDown) != 0;
                case SwitchButton.Left:
                    return (_gamepad.Buttons & GamepadButtonFlags.DPadLeft) != 0;
                case SwitchButton.Right:
                    return (_gamepad.Buttons & GamepadButtonFlags.DPadRight) != 0;
                case SwitchButton.L:
                    return (_gamepad.Buttons & GamepadButtonFlags.LeftShoulder) != 0;
                case SwitchButton.R:
                    return (_gamepad.Buttons & GamepadButtonFlags.RightShoulder) != 0;
                case SwitchButton.ZL:
                    return _gamepad.LeftTrigger > TRIGGER_THRESHOLD;
                case SwitchButton.ZR:
                    return _gamepad.RightTrigger > TRIGGER_THRESHOLD;
                case SwitchButton.A:
                    return (_gamepad.Buttons & GamepadButtonFlags.B) != 0;
                case SwitchButton.B:
                    return (_gamepad.Buttons & GamepadButtonFlags.A) != 0;
                case SwitchButton.X:
                    return (_gamepad.Buttons & GamepadButtonFlags.Y) != 0;
                case SwitchButton.Y:
                    return (_gamepad.Buttons & GamepadButtonFlags.X) != 0;
                case SwitchButton.Plus:
                    return (_gamepad.Buttons & GamepadButtonFlags.Start) != 0;
                case SwitchButton.Minus:
                    return (_gamepad.Buttons & GamepadButtonFlags.Back) != 0;
                default:
                    return false;
            }
        }

        public void Dispose()
        {
            _controller = null;
        }
    }
}
