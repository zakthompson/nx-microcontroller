using System;
using System.Drawing;
using System.IO.Ports;
using System.Linq;
using System.Windows.Forms;

namespace SwitchController
{
    /// <summary>
    /// Main GUI form for the controller relay
    /// </summary>
    public class MainForm : Form
    {
        private Configuration _config = null!;
        private RelaySession? _session;

        // Connection group controls
        private ComboBox _comPortCombo = null!;
        private ComboBox _firmwareCombo = null!;
        private ComboBox _controllerTypeCombo = null!;
        private ComboBox _inputBackendCombo = null!;
        private Button _refreshPortsButton = null!;

        // Companion app group controls
        private TextBox _companionPathTextBox = null!;
        private Button _browseButton = null!;
        private TextBox _clickXTextBox = null!;
        private TextBox _clickYTextBox = null!;
        private TextBox _clickDelayTextBox = null!;
        private CheckBox _relativeCoordinatesCheckBox = null!;

        // Hotkeys group controls
        private ComboBox _hotkeyEnableCombo = null!;
        private ComboBox _hotkeySendHomeCombo = null!;
        private ComboBox _hotkeyQuitCombo = null!;
        private ComboBox _hotkeyRecordCombo = null!;
        private ComboBox _hotkeyPlayCombo = null!;
        private ComboBox _hotkeyLoopCombo = null!;

        // Action controls
        private Button _connectButton = null!;
        private Button _pairButton = null!;

        // Status controls
        private TextBox _statusTextBox = null!;

        public MainForm()
        {
            InitializeComponent();
            _config = Configuration.Load();
            PopulateFormFromConfig();
            PopulateComPorts();
            UpdatePairButtonVisibility();
        }

        private void InitializeComponent()
        {
            Text = "Controller Relay";
            Size = new Size(600, 700);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            int y = 10;

            // Connection group
            var connectionGroup = CreateGroupBox("Connection", 10, y, 560, 110);
            Controls.Add(connectionGroup);

            var comPortLabel = new Label { Text = "COM Port:", Left = 10, Top = 20, Width = 80 };
            connectionGroup.Controls.Add(comPortLabel);

            _comPortCombo = new ComboBox
            {
                Left = 90,
                Top = 17,
                Width = 150,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            connectionGroup.Controls.Add(_comPortCombo);

            _refreshPortsButton = new Button
            {
                Text = "Refresh",
                Left = 250,
                Top = 16,
                Width = 80,
                Height = 25
            };
            _refreshPortsButton.Click += (s, e) => PopulateComPorts();
            connectionGroup.Controls.Add(_refreshPortsButton);

            var firmwareLabel = new Label { Text = "Firmware:", Left = 10, Top = 50, Width = 80 };
            connectionGroup.Controls.Add(firmwareLabel);

            _firmwareCombo = new ComboBox
            {
                Left = 90,
                Top = 47,
                Width = 120,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _firmwareCombo.Items.AddRange(new[] { "Auto", "Native", "PABotBase" });
            _firmwareCombo.SelectedIndex = 0;
            connectionGroup.Controls.Add(_firmwareCombo);

            var controllerLabel = new Label { Text = "Controller:", Left = 220, Top = 50, Width = 70 };
            connectionGroup.Controls.Add(controllerLabel);

            _controllerTypeCombo = new ComboBox
            {
                Left = 290,
                Top = 47,
                Width = 150,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _controllerTypeCombo.Items.AddRange(new[] { "Wireless Pro", "Wired Pro", "Wired" });
            _controllerTypeCombo.SelectedIndex = 0;
            _controllerTypeCombo.SelectedIndexChanged += (s, e) => UpdatePairButtonVisibility();
            connectionGroup.Controls.Add(_controllerTypeCombo);

            var inputBackendLabel = new Label { Text = "Input:", Left = 10, Top = 80, Width = 80 };
            connectionGroup.Controls.Add(inputBackendLabel);

            _inputBackendCombo = new ComboBox
            {
                Left = 90,
                Top = 77,
                Width = 150,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _inputBackendCombo.Items.AddRange(new[] { "XInput", "SDL2/Generic" });
            _inputBackendCombo.SelectedIndex = 0;
            connectionGroup.Controls.Add(_inputBackendCombo);

            y += 120;

            // Companion App group
            var companionGroup = CreateGroupBox("Companion App", 10, y, 560, 105);
            Controls.Add(companionGroup);

            var pathLabel = new Label { Text = "Path:", Left = 10, Top = 20, Width = 40 };
            companionGroup.Controls.Add(pathLabel);

            _companionPathTextBox = new TextBox
            {
                Left = 50,
                Top = 17,
                Width = 410
            };
            companionGroup.Controls.Add(_companionPathTextBox);

            _browseButton = new Button
            {
                Text = "Browse...",
                Left = 470,
                Top = 16,
                Width = 80,
                Height = 25
            };
            _browseButton.Click += BrowseButton_Click;
            companionGroup.Controls.Add(_browseButton);

            var clickXLabel = new Label { Text = "Click X:", Left = 10, Top = 50, Width = 50 };
            companionGroup.Controls.Add(clickXLabel);

            _clickXTextBox = new TextBox
            {
                Left = 60,
                Top = 47,
                Width = 60
            };
            companionGroup.Controls.Add(_clickXTextBox);

            var clickYLabel = new Label { Text = "Y:", Left = 130, Top = 50, Width = 20 };
            companionGroup.Controls.Add(clickYLabel);

            _clickYTextBox = new TextBox
            {
                Left = 150,
                Top = 47,
                Width = 60
            };
            companionGroup.Controls.Add(_clickYTextBox);

            var delayLabel = new Label { Text = "Delay:", Left = 220, Top = 50, Width = 40 };
            companionGroup.Controls.Add(delayLabel);

            _clickDelayTextBox = new TextBox
            {
                Left = 260,
                Top = 47,
                Width = 60
            };
            companionGroup.Controls.Add(_clickDelayTextBox);

            var msLabel = new Label { Text = "ms", Left = 325, Top = 50, Width = 25 };
            companionGroup.Controls.Add(msLabel);

            _relativeCoordinatesCheckBox = new CheckBox
            {
                Text = "Window-relative coordinates",
                Left = 10,
                Top = 75,
                Width = 200,
                Checked = true
            };
            companionGroup.Controls.Add(_relativeCoordinatesCheckBox);

            y += 110;

            // Hotkeys group
            var hotkeysGroup = CreateGroupBox("Hotkeys", 10, y, 560, 90);
            Controls.Add(hotkeysGroup);

            var enableLabel = new Label { Text = "Enable:", Left = 10, Top = 20, Width = 50 };
            hotkeysGroup.Controls.Add(enableLabel);

            _hotkeyEnableCombo = CreateButtonCombo(60, 17);
            hotkeysGroup.Controls.Add(_hotkeyEnableCombo);

            var sendHomeLabel = new Label { Text = "Send Home:", Left = 190, Top = 20, Width = 75 };
            hotkeysGroup.Controls.Add(sendHomeLabel);

            _hotkeySendHomeCombo = CreateButtonCombo(265, 17);
            hotkeysGroup.Controls.Add(_hotkeySendHomeCombo);

            var quitLabel = new Label { Text = "Quit:", Left = 10, Top = 50, Width = 50 };
            hotkeysGroup.Controls.Add(quitLabel);

            _hotkeyQuitCombo = CreateButtonCombo(60, 47);
            hotkeysGroup.Controls.Add(_hotkeyQuitCombo);

            var recordLabel = new Label { Text = "Record:", Left = 190, Top = 50, Width = 75 };
            hotkeysGroup.Controls.Add(recordLabel);

            _hotkeyRecordCombo = CreateButtonCombo(265, 47);
            hotkeysGroup.Controls.Add(_hotkeyRecordCombo);

            var playLabel = new Label { Text = "Play:", Left = 395, Top = 50, Width = 50 };
            hotkeysGroup.Controls.Add(playLabel);

            _hotkeyPlayCombo = CreateButtonCombo(445, 47);
            hotkeysGroup.Controls.Add(_hotkeyPlayCombo);

            var loopLabel = new Label { Text = "Loop:", Left = 395, Top = 20, Width = 50 };
            hotkeysGroup.Controls.Add(loopLabel);

            _hotkeyLoopCombo = CreateButtonCombo(445, 17);
            hotkeysGroup.Controls.Add(_hotkeyLoopCombo);

            y += 100;

            // Connect and Pair buttons
            _connectButton = new Button
            {
                Text = "Connect",
                Left = 200,
                Top = y,
                Width = 100,
                Height = 30
            };
            _connectButton.Click += ConnectButton_Click;
            Controls.Add(_connectButton);

            _pairButton = new Button
            {
                Text = "Pair",
                Left = 310,
                Top = y,
                Width = 100,
                Height = 30
            };
            _pairButton.Click += PairButton_Click;
            Controls.Add(_pairButton);

            y += 40;

            // Status group
            var statusGroup = CreateGroupBox("Status", 10, y, 560, 200);
            Controls.Add(statusGroup);

            _statusTextBox = new TextBox
            {
                Left = 10,
                Top = 20,
                Width = 535,
                Height = 170,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.Black,
                ForeColor = Color.LightGreen,
                Font = new Font("Consolas", 9)
            };
            statusGroup.Controls.Add(_statusTextBox);
        }

        private GroupBox CreateGroupBox(string title, int x, int y, int width, int height)
        {
            return new GroupBox
            {
                Text = title,
                Left = x,
                Top = y,
                Width = width,
                Height = height
            };
        }

        private ComboBox CreateButtonCombo(int x, int y)
        {
            var combo = new ComboBox
            {
                Left = x,
                Top = y,
                Width = 100,
                DropDownStyle = ComboBoxStyle.DropDownList
            };

            combo.Items.AddRange(new object[]
            {
                SwitchButton.None,
                SwitchButton.A,
                SwitchButton.B,
                SwitchButton.X,
                SwitchButton.Y,
                SwitchButton.L,
                SwitchButton.R,
                SwitchButton.ZL,
                SwitchButton.ZR,
                SwitchButton.Plus,
                SwitchButton.Minus,
                SwitchButton.LS,
                SwitchButton.RS,
                SwitchButton.Up,
                SwitchButton.Down,
                SwitchButton.Left,
                SwitchButton.Right
            });

            combo.SelectedIndex = 0;
            return combo;
        }

        private void PopulateComPorts()
        {
            string? currentSelection = _comPortCombo.SelectedItem as string;
            _comPortCombo.Items.Clear();

            var ports = SerialPort.GetPortNames().OrderBy(p => p).ToArray();
            if (ports.Length > 0)
            {
                _comPortCombo.Items.AddRange(ports);

                if (currentSelection != null && ports.Contains(currentSelection))
                {
                    _comPortCombo.SelectedItem = currentSelection;
                }
                else if (!string.IsNullOrEmpty(_config.ComPort) && ports.Contains(_config.ComPort))
                {
                    _comPortCombo.SelectedItem = _config.ComPort;
                }
                else
                {
                    _comPortCombo.SelectedIndex = 0;
                }
            }
            else
            {
                AppendStatus("No COM ports found");
            }
        }

        private void PopulateFormFromConfig()
        {
            // Firmware type
            switch (_config.FirmwareType.ToLowerInvariant())
            {
                case "native":
                    _firmwareCombo.SelectedIndex = 1;
                    break;
                case "pabotbase":
                    _firmwareCombo.SelectedIndex = 2;
                    break;
                default:
                    _firmwareCombo.SelectedIndex = 0;
                    break;
            }

            // Controller type
            switch (_config.ControllerType.ToLowerInvariant())
            {
                case "wired-pro":
                    _controllerTypeCombo.SelectedIndex = 1;
                    break;
                case "wired":
                    _controllerTypeCombo.SelectedIndex = 2;
                    break;
                default:
                    _controllerTypeCombo.SelectedIndex = 0;
                    break;
            }

            // Input backend
            switch (_config.InputBackend.ToLowerInvariant())
            {
                case "sdl2":
                case "sdl":
                    _inputBackendCombo.SelectedIndex = 1;
                    break;
                default:
                    _inputBackendCombo.SelectedIndex = 0;
                    break;
            }

            // Companion app
            _companionPathTextBox.Text = _config.CompanionAppPath ?? string.Empty;
            _clickXTextBox.Text = _config.AutoClickX?.ToString() ?? string.Empty;
            _clickYTextBox.Text = _config.AutoClickY?.ToString() ?? string.Empty;
            _clickDelayTextBox.Text = _config.AutoClickDelay.ToString();
            _relativeCoordinatesCheckBox.Checked = _config.AutoClickRelative;

            // Hotkeys
            _hotkeyEnableCombo.SelectedItem = _config.HotkeyEnable;
            _hotkeySendHomeCombo.SelectedItem = _config.HotkeySendHome;
            _hotkeyQuitCombo.SelectedItem = _config.HotkeyQuit;
            _hotkeyRecordCombo.SelectedItem = _config.HotkeyMacroRecord;
            _hotkeyPlayCombo.SelectedItem = _config.HotkeyMacroPlayOnce;
            _hotkeyLoopCombo.SelectedItem = _config.HotkeyMacroLoop;
        }

        private void BrowseButton_Click(object? sender, EventArgs e)
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Select Companion Application",
                Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                _companionPathTextBox.Text = dialog.FileName;
            }
        }

        private void ConnectButton_Click(object? sender, EventArgs e)
        {
            if (_session?.IsRunning == true)
            {
                StopSession();
            }
            else
            {
                StartSession(pairMode: false);
            }
        }

        private void PairButton_Click(object? sender, EventArgs e)
        {
            if (_session?.IsRunning == true)
            {
                StopSession();
            }
            else
            {
                StartSession(pairMode: true);
            }
        }

        private void UpdatePairButtonVisibility()
        {
            // Pair is only meaningful for wireless controller types (PABotBase firmware)
            bool isWireless = _controllerTypeCombo.SelectedIndex == 0; // "Wireless Pro"
            _pairButton.Visible = isWireless;
        }

        private void StartSession(bool pairMode)
        {
            // Validate inputs
            if (_comPortCombo.SelectedItem == null)
            {
                MessageBox.Show("Please select a COM port", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string portName = _comPortCombo.SelectedItem.ToString()!;

            // Build configuration from form
            _config.ComPort = portName;

            _config.FirmwareType = _firmwareCombo.SelectedIndex switch
            {
                1 => "native",
                2 => "pabotbase",
                _ => "auto"
            };

            _config.ControllerType = _controllerTypeCombo.SelectedIndex switch
            {
                1 => "wired-pro",
                2 => "wired",
                _ => "wireless-pro"
            };

            _config.InputBackend = _inputBackendCombo.SelectedIndex switch
            {
                1 => "sdl2",
                _ => "xinput"
            };

            _config.CompanionAppPath = string.IsNullOrWhiteSpace(_companionPathTextBox.Text) ? null : _companionPathTextBox.Text;

            if (int.TryParse(_clickXTextBox.Text, out int clickX))
                _config.AutoClickX = clickX;
            else
                _config.AutoClickX = null;

            if (int.TryParse(_clickYTextBox.Text, out int clickY))
                _config.AutoClickY = clickY;
            else
                _config.AutoClickY = null;

            if (int.TryParse(_clickDelayTextBox.Text, out int delay))
                _config.AutoClickDelay = delay;

            _config.AutoClickRelative = _relativeCoordinatesCheckBox.Checked;

            _config.HotkeyEnable = (SwitchButton)_hotkeyEnableCombo.SelectedItem!;
            _config.HotkeySendHome = (SwitchButton)_hotkeySendHomeCombo.SelectedItem!;
            _config.HotkeyQuit = (SwitchButton)_hotkeyQuitCombo.SelectedItem!;
            _config.HotkeyMacroRecord = (SwitchButton)_hotkeyRecordCombo.SelectedItem!;
            _config.HotkeyMacroPlayOnce = (SwitchButton)_hotkeyPlayCombo.SelectedItem!;
            _config.HotkeyMacroLoop = (SwitchButton)_hotkeyLoopCombo.SelectedItem!;

            // Save configuration
            try
            {
                _config.Save();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save configuration: {ex.Message}", "Save Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            // Clear status
            _statusTextBox.Clear();

            // Create and start session
            _session = new RelaySession(_config, portName, pairMode, Environment.ProcessId);
            _session.StatusUpdate += Session_StatusUpdate;
            _session.SessionStopped += Session_SessionStopped;
            _session.Start();

            // Update UI — show only the active button as "Stop"
            SetControlsEnabled(false);
            if (pairMode)
            {
                _connectButton.Visible = false;
                _pairButton.Text = "Stop";
            }
            else
            {
                _pairButton.Visible = false;
                _connectButton.Text = "Stop";
            }
        }

        private void StopSession()
        {
            _session?.Stop();
            _session = null;

            SetControlsEnabled(true);
            _connectButton.Visible = true;
            _connectButton.Text = "Connect";
            _pairButton.Text = "Pair";
            UpdatePairButtonVisibility();
        }

        private void SetControlsEnabled(bool enabled)
        {
            _comPortCombo.Enabled = enabled;
            _refreshPortsButton.Enabled = enabled;
            _firmwareCombo.Enabled = enabled;
            _controllerTypeCombo.Enabled = enabled;
            _inputBackendCombo.Enabled = enabled;
            if (enabled) UpdatePairButtonVisibility();
            _companionPathTextBox.Enabled = enabled;
            _browseButton.Enabled = enabled;
            _clickXTextBox.Enabled = enabled;
            _clickYTextBox.Enabled = enabled;
            _clickDelayTextBox.Enabled = enabled;
            _relativeCoordinatesCheckBox.Enabled = enabled;
            _hotkeyEnableCombo.Enabled = enabled;
            _hotkeySendHomeCombo.Enabled = enabled;
            _hotkeyQuitCombo.Enabled = enabled;
            _hotkeyRecordCombo.Enabled = enabled;
            _hotkeyPlayCombo.Enabled = enabled;
            _hotkeyLoopCombo.Enabled = enabled;
        }

        private void Session_StatusUpdate(string message)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => Session_StatusUpdate(message)));
                return;
            }

            AppendStatus(message);
        }

        private void Session_SessionStopped()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(Session_SessionStopped));
                return;
            }

            SetControlsEnabled(true);
            _connectButton.Visible = true;
            _connectButton.Text = "Connect";
            _pairButton.Text = "Pair";
            UpdatePairButtonVisibility();
            AppendStatus("Session stopped");
        }

        private void AppendStatus(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            _statusTextBox.AppendText($"[{timestamp}] {message}\r\n");
            _statusTextBox.SelectionStart = _statusTextBox.Text.Length;
            _statusTextBox.ScrollToCaret();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_session?.IsRunning == true)
            {
                _session.Stop();
            }

            base.OnFormClosing(e);
        }
    }
}
