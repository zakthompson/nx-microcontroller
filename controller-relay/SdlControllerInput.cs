using System;
using static SDL2.SDL;
using static SwitchController.SwitchControllerConstants;

namespace SwitchController
{
    /// <summary>
    /// SDL2 implementation of controller input for generic controllers (DualShock, etc.)
    /// Note: Requires SDL2.dll to be present in the application directory or system path
    /// </summary>
    public class SdlControllerInput : IControllerInput
    {
        private IntPtr _controller;
        private bool _isConnected;

        // Cached axis values (SDL uses -32768 to 32767 range)
        private short _leftX, _leftY, _rightX, _rightY;
        private byte _leftTrigger, _rightTrigger;

        // Cached button states
        private ushort _buttons;

        public bool IsConnected => _isConnected;

        public SdlControllerInput()
        {
            try
            {
                // Initialize SDL2 game controller subsystem
                if (SDL_Init(SDL_INIT_GAMECONTROLLER) < 0)
                {
                    throw new InvalidOperationException($"SDL_Init failed: {SDL_GetError()}");
                }

                // Open the first available controller
                for (int i = 0; i < SDL_NumJoysticks(); i++)
                {
                    if (SDL_IsGameController(i) == SDL_bool.SDL_TRUE)
                    {
                        _controller = SDL_GameControllerOpen(i);
                        if (_controller != IntPtr.Zero)
                        {
                            _isConnected = true;
                            break;
                        }
                    }
                }
            }
            catch
            {
                _isConnected = false;
            }
        }

        public void Update()
        {
            if (_controller == IntPtr.Zero)
            {
                // Try to reconnect
                for (int i = 0; i < SDL_NumJoysticks(); i++)
                {
                    if (SDL_IsGameController(i) == SDL_bool.SDL_TRUE)
                    {
                        _controller = SDL_GameControllerOpen(i);
                        if (_controller != IntPtr.Zero)
                        {
                            _isConnected = true;
                            break;
                        }
                    }
                }

                if (_controller == IntPtr.Zero)
                {
                    _isConnected = false;
                    return;
                }
            }

            // Pump SDL events
            SDL_PumpEvents();

            // Read axes (negate Y to match XInput convention: positive = up)
            _leftX = SDL_GameControllerGetAxis(_controller, SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTX);
            _leftY = NegateAxis(SDL_GameControllerGetAxis(_controller, SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTY));
            _rightX = SDL_GameControllerGetAxis(_controller, SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_RIGHTX);
            _rightY = NegateAxis(SDL_GameControllerGetAxis(_controller, SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_RIGHTY));

            // Read triggers (SDL returns 0-32767, we normalize to 0-255)
            short leftTriggerRaw = SDL_GameControllerGetAxis(_controller, SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_TRIGGERLEFT);
            short rightTriggerRaw = SDL_GameControllerGetAxis(_controller, SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_TRIGGERRIGHT);
            _leftTrigger = (byte)(leftTriggerRaw / 128);
            _rightTrigger = (byte)(rightTriggerRaw / 128);

            // Build button mask
            _buttons = 0;

            // Face buttons (with Xbox→Nintendo remap like XInput)
            if (GetButton(SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_A))
                _buttons |= (ushort)BTN_B;  // Xbox A → Nintendo B
            if (GetButton(SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_B))
                _buttons |= (ushort)BTN_A;  // Xbox B → Nintendo A
            if (GetButton(SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_X))
                _buttons |= (ushort)BTN_Y;  // Xbox X → Nintendo Y
            if (GetButton(SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_Y))
                _buttons |= (ushort)BTN_X;  // Xbox Y → Nintendo X

            // Shoulders
            if (GetButton(SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_LEFTSHOULDER))
                _buttons |= (ushort)BTN_L;
            if (GetButton(SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_RIGHTSHOULDER))
                _buttons |= (ushort)BTN_R;

            // Triggers (as buttons)
            if (_leftTrigger > TRIGGER_THRESHOLD)
                _buttons |= (ushort)BTN_ZL;
            if (_rightTrigger > TRIGGER_THRESHOLD)
                _buttons |= (ushort)BTN_ZR;

            // Stick clicks
            if (GetButton(SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_LEFTSTICK))
                _buttons |= (ushort)BTN_LCLICK;
            if (GetButton(SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_RIGHTSTICK))
                _buttons |= (ushort)BTN_RCLICK;

            // System buttons
            if (GetButton(SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_START))
                _buttons |= (ushort)BTN_PLUS;
            if (GetButton(SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_BACK))
                _buttons |= (ushort)BTN_MINUS;
            if (GetButton(SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_GUIDE))
                _buttons |= (ushort)BTN_HOME;
        }

        public ControllerState GetCurrentState()
        {
            // Build axis values using shared PacketBuilder helpers
            var (lx, ly, rx, ry) = PacketBuilder.BuildAxisValues(_leftX, _leftY, _rightX, _rightY);

            // Build HAT value from D-pad buttons
            byte hat = BuildHatValue();

            return new ControllerState
            {
                Buttons0 = (byte)(_buttons & 0xFF),
                Buttons1 = (byte)((_buttons >> 8) & 0xFF),
                Dpad = hat,
                LX = lx,
                LY = ly,
                RX = rx,
                RY = ry
            };
        }

        public bool IsButtonPressed(SwitchButton button)
        {
            if (!IsConnected || button == SwitchButton.None)
                return false;

            switch (button)
            {
                case SwitchButton.A:
                    return GetButton(SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_B);
                case SwitchButton.B:
                    return GetButton(SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_A);
                case SwitchButton.X:
                    return GetButton(SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_Y);
                case SwitchButton.Y:
                    return GetButton(SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_X);
                case SwitchButton.L:
                    return GetButton(SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_LEFTSHOULDER);
                case SwitchButton.R:
                    return GetButton(SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_RIGHTSHOULDER);
                case SwitchButton.ZL:
                    return _leftTrigger > TRIGGER_THRESHOLD;
                case SwitchButton.ZR:
                    return _rightTrigger > TRIGGER_THRESHOLD;
                case SwitchButton.Plus:
                    return GetButton(SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_START);
                case SwitchButton.Minus:
                    return GetButton(SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_BACK);
                case SwitchButton.LS:
                    return GetButton(SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_LEFTSTICK);
                case SwitchButton.RS:
                    return GetButton(SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_RIGHTSTICK);
                case SwitchButton.Up:
                    return GetButton(SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_UP);
                case SwitchButton.Down:
                    return GetButton(SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_DOWN);
                case SwitchButton.Left:
                    return GetButton(SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_LEFT);
                case SwitchButton.Right:
                    return GetButton(SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_RIGHT);
                default:
                    return false;
            }
        }

        private static short NegateAxis(short value)
        {
            // Clamp -32768 to -32767 to avoid overflow
            return (short)(value == short.MinValue ? short.MaxValue : -value);
        }

        private bool GetButton(SDL_GameControllerButton button)
        {
            if (_controller == IntPtr.Zero)
                return false;

            return SDL_GameControllerGetButton(_controller, button) == 1;
        }

        private byte BuildHatValue()
        {
            bool up = GetButton(SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_UP);
            bool down = GetButton(SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_DOWN);
            bool left = GetButton(SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_LEFT);
            bool right = GetButton(SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_RIGHT);

            if (up && right) return HAT_UP_RIGHT;
            if (right && down) return HAT_DOWN_RIGHT;
            if (down && left) return HAT_DOWN_LEFT;
            if (left && up) return HAT_UP_LEFT;
            if (up) return HAT_UP;
            if (right) return HAT_RIGHT;
            if (down) return HAT_DOWN;
            if (left) return HAT_LEFT;
            return HAT_NEUTRAL;
        }

        public void Dispose()
        {
            if (_controller != IntPtr.Zero)
            {
                SDL_GameControllerClose(_controller);
                _controller = IntPtr.Zero;
            }

            SDL_Quit();
            _isConnected = false;
        }
    }
}
