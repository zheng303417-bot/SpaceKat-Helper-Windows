using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace SpaceKatMonitor
{
    public partial class MainWindow
    {
        private readonly RightStickModel _joystick = new RightStickModel();
        private readonly VirtualGamepadOutput _gamepadOutput = new VirtualGamepadOutput();
        private readonly GamepadButtonModel _stickButtons = new GamepadButtonModel();
        private readonly GamepadButtons[] _stickButtonActions =
            { GamepadButtons.A, GamepadButtons.B, GamepadButtons.X, GamepadButtons.Y };
        private readonly ComboBox[] _stickButtonCombos = new ComboBox[4];
        private readonly TextBlock[] _stickButtonLabels = new TextBlock[4];
        private TextBlock _stickButtonState;
        private Grid _joystickPage;
        private Button _joystickNav, _stickToggle;
        private TextBlock _stickStatus, _stickValues, _stickIntegers, _stickCameraText, _stickCenterText;
        private TextBlock _stickGainXText, _stickGainYText, _stickDeadZoneText, _stickGammaText;
        private ComboBox _stickSourceX, _stickSourceY, _stickOutputMode, _stickToggleKey;
        private CheckBox _stickInvertX, _stickInvertY;
        private Slider _stickGainX, _stickGainY, _stickDeadZone, _stickGamma;
        private CheckBox _zoomEnabled, _zoomInvert, _stickDominantAxis;
        private Slider _zoomInGain, _zoomOutGain, _zoomInSpeed, _zoomOutSpeed, _zoomInDeadZone, _zoomOutDeadZone;
        private TextBlock _zoomInGainText, _zoomOutGainText, _zoomInSpeedText, _zoomOutSpeedText;
        private TextBlock _zoomInDeadZoneText, _zoomOutDeadZoneText, _zoomLeftValue, _zoomRightValue, _zoomHint;
        private TextBlock _stickDominantStatus;
        private ProgressBar _zoomLeftMeter, _zoomRightMeter;
        private RightStickView _stickView;
        private FixedCameraView _stickCamera;
        private DispatcherTimer _stickTimer;
        private bool _loadingStickSettings, _stickHotkeyArmed;
        private uint _stickPreviousButtons;
        private double _lastStickToggle = Double.NegativeInfinity;
        private string _stickMessage = "已关闭 · 右摇杆回中；启用后持续偏转会持续转动视角";

        private void InitializeJoystick()
        {
            _joystickPage = Find<Grid>("JoystickPage");
            _joystickNav = Find<Button>("JoystickNavButton");
            _joystickNav.Click += delegate { ShowPage("joystick"); };
            _stickToggle = Find<Button>("StickToggleButton");
            _stickStatus = Find<TextBlock>("StickStatusText");
            _stickValues = Find<TextBlock>("StickValueText");
            _stickIntegers = Find<TextBlock>("StickIntegerText");
            _stickCameraText = Find<TextBlock>("StickCameraText");
            _stickCenterText = Find<TextBlock>("StickCenterText");
            _stickSourceX = Find<ComboBox>("StickSourceX");
            _stickSourceY = Find<ComboBox>("StickSourceY");
            _stickOutputMode = Find<ComboBox>("StickOutputMode");
            _stickToggleKey = Find<ComboBox>("StickToggleKey");
            _stickInvertX = Find<CheckBox>("StickInvertX");
            _stickInvertY = Find<CheckBox>("StickInvertY");
            _stickGainX = Find<Slider>("StickGainX");
            _stickGainY = Find<Slider>("StickGainY");
            _stickDeadZone = Find<Slider>("StickDeadZone");
            _stickGamma = Find<Slider>("StickGamma");
            _stickGainXText = Find<TextBlock>("StickGainXText");
            _stickGainYText = Find<TextBlock>("StickGainYText");
            _stickDeadZoneText = Find<TextBlock>("StickDeadZoneText");
            _stickGammaText = Find<TextBlock>("StickGammaText");
            _zoomEnabled = Find<CheckBox>("ZoomEnabled");
            _zoomInvert = Find<CheckBox>("ZoomInvert");
            _stickDominantAxis = Find<CheckBox>("StickDominantAxis");
            _stickDominantStatus = Find<TextBlock>("StickDominantStatus");
            _zoomInGain = Find<Slider>("ZoomInGain");
            _zoomOutGain = Find<Slider>("ZoomOutGain");
            _zoomInSpeed = Find<Slider>("ZoomInSpeed");
            _zoomOutSpeed = Find<Slider>("ZoomOutSpeed");
            _zoomInDeadZone = Find<Slider>("ZoomInDeadZone");
            _zoomOutDeadZone = Find<Slider>("ZoomOutDeadZone");
            _zoomInGainText = Find<TextBlock>("ZoomInGainText");
            _zoomOutGainText = Find<TextBlock>("ZoomOutGainText");
            _zoomInSpeedText = Find<TextBlock>("ZoomInSpeedText");
            _zoomOutSpeedText = Find<TextBlock>("ZoomOutSpeedText");
            _zoomInDeadZoneText = Find<TextBlock>("ZoomInDeadZoneText");
            _zoomOutDeadZoneText = Find<TextBlock>("ZoomOutDeadZoneText");
            _zoomLeftValue = Find<TextBlock>("ZoomLeftValue");
            _zoomRightValue = Find<TextBlock>("ZoomRightValue");
            _zoomHint = Find<TextBlock>("ZoomHintText");
            _zoomLeftMeter = Find<ProgressBar>("ZoomLeftMeter");
            _zoomRightMeter = Find<ProgressBar>("ZoomRightMeter");
            _stickView = new RightStickView();
            _stickCamera = new FixedCameraView();
            Find<Border>("RightStickHost").Child = _stickView;
            Find<Border>("StickCameraHost").Child = _stickCamera;
            _loadingStickSettings = true;
            foreach (string axis in new[] { "X", "Y", "Z", "Rx", "Ry", "Rz" })
            { _stickSourceX.Items.Add(axis); _stickSourceY.Items.Add(axis); }
            _stickSourceX.SelectedIndex = 5;
            _stickSourceY.SelectedIndex = 3;
            _stickOutputMode.Items.Add("本页视角预览");
            _stickOutputMode.Items.Add("虚拟 Xbox 手柄 + 本页预览");
            _stickOutputMode.SelectedIndex = 1;
            _stickToggleKey.Items.Add("不使用实体按键");
            for (int i = 1; i <= 4; i++) _stickToggleKey.Items.Add("按键 " + i + " 启停");
            _stickToggleKey.SelectedIndex = 0;
            InitializeStickButtonMappings();
            LoadJoystickSettings();
            ApplyJoystickSettings();
            ApplyZoomSettings();
            _loadingStickSettings = false;
            foreach (ComboBox combo in new[] { _stickSourceX, _stickSourceY, _stickOutputMode, _stickToggleKey })
                combo.SelectionChanged += delegate { JoystickSettingsChanged(); };
            foreach (Slider slider in new[] { _stickGainX, _stickGainY, _stickDeadZone, _stickGamma })
                slider.ValueChanged += delegate { JoystickSettingsChanged(); };
            _stickInvertX.Click += delegate { JoystickSettingsChanged(); };
            _stickInvertY.Click += delegate { JoystickSettingsChanged(); };
            foreach (Slider slider in new[] { _zoomInGain, _zoomOutGain, _zoomInSpeed, _zoomOutSpeed, _zoomInDeadZone, _zoomOutDeadZone })
                slider.ValueChanged += delegate { ZoomSettingsChanged(); };
            _zoomEnabled.Click += delegate { ZoomSettingsChanged(); };
            _zoomInvert.Click += delegate { ZoomSettingsChanged(); };
            _stickDominantAxis.Click += delegate { ZoomSettingsChanged(); };
            _stickToggle.Click += delegate
            {
                if (_joystick.Enabled) StopJoystick("已关闭 · 右摇杆已回中。"); else StartJoystick();
                Render(DateTime.UtcNow);
            };
            Find<Button>("StickResetViewButton").Click += delegate { _joystick.ResetView(); RenderJoystick(); };
            Find<Button>("StickCalibrateButton").Click += delegate
            {
                if (!_axesKnown || _demo || _selected == null || _mouseClock.Elapsed.TotalSeconds - _lastAxisSeconds > 0.15)
                { _stickMessage = "请先松开操纵帽，并等待设备发送新的六轴数据，再设置中心。"; RenderJoystick(); return; }
                StopJoystick("已校准中心，重新开启后生效。");
                _joystick.Calibrate(_axes);
                SaveJoystickSettings();
                RenderJoystick();
            };
            Find<Button>("StickClearCenterButton").Click += delegate
            {
                StopJoystick("已清除中心校准，重新开启后生效。");
                _joystick.ResetCalibration();
                SaveJoystickSettings();
                RenderJoystick();
            };
            _stickTimer = new DispatcherTimer(DispatcherPriority.Input);
            _stickTimer.Interval = TimeSpan.FromMilliseconds(10);
            _stickTimer.Tick += delegate { JoystickTick(); };
        }

        private void ApplyJoystickSettings()
        {
            _joystick.SourceX = _stickSourceX.SelectedIndex;
            _joystick.SourceY = _stickSourceY.SelectedIndex;
            _joystick.InvertX = _stickInvertX.IsChecked == true;
            _joystick.InvertY = _stickInvertY.IsChecked == true;
            _joystick.GainX = _stickGainX.Value;
            _joystick.GainY = _stickGainY.Value;
            _joystick.DeadZone = _stickDeadZone.Value;
            _joystick.Gamma = _stickGamma.Value;
        }

        private void JoystickSettingsChanged()
        {
            if (_loadingStickSettings) return;
            StopJoystick("设置已修改，右摇杆已回中；请重新开启。");
            ApplyJoystickSettings();
            _stickPreviousButtons = _buttonsKnown ? _buttons : 0;
            SaveJoystickSettings();
            Render(DateTime.UtcNow);
        }

        private void ApplyZoomSettings()
        {
            _joystick.ZoomEnabled = _zoomEnabled.IsChecked == true;
            _joystick.ZoomInvert = _zoomInvert.IsChecked == true;
            _joystick.ZoomInGain = _zoomInGain.Value;
            _joystick.ZoomOutGain = _zoomOutGain.Value;
            _joystick.ZoomInSpeed = _zoomInSpeed.Value / 100.0;
            _joystick.ZoomOutSpeed = _zoomOutSpeed.Value / 100.0;
            _joystick.ZoomInDeadZone = _zoomInDeadZone.Value;
            _joystick.ZoomOutDeadZone = _zoomOutDeadZone.Value;
            _joystick.DominantAxisEnabled = _stickDominantAxis.IsChecked == true;
        }

        private void ZoomSettingsChanged()
        {
            if (_loadingStickSettings) return;
            double now = _mouseClock.Elapsed.TotalSeconds;
            _joystick.Step(now);
            _stickButtons.Step(now);
            ApplyZoomSettings();
            if (_gamepadOutput.IsConnected)
            {
                string error;
                if (!_gamepadOutput.TryUpdate(_joystick.OutputX, _joystick.OutputY,
                    _joystick.LeftTrigger, _joystick.RightTrigger, _stickButtons.Output, out error))
                    StopJoystick("缩放设置已保存，虚拟手柄输出已停止：" + error);
            }
            SaveJoystickSettings();
            RenderJoystick();
        }

        private void StartJoystick()
        {
            if (_currentPage != "joystick" || _mouseOutput == null || (!_demo && (_selected == null || _input == null)))
            { _stickMessage = "请先连接六轴设备，或进入演示预览。"; return; }
            StopMouseControl("已切换为摇杆模拟，鼠标模拟已关闭。");
            CancelLearning(null);
            if (_gamepadOutput.HasPendingStop)
            {
                _gamepadOutput.Stop();
                if (_gamepadOutput.HasPendingStop)
                { _stickMessage = "请先停止上一次虚拟手柄输出：" + _gamepadOutput.LastError; return; }
            }
            try
            {
                _mouseOutput.Arm();
                _stickHotkeyArmed = _mouseOutput.StopHotkeyAvailable;
                if (!_stickHotkeyArmed)
                {
                    _stickMessage = !String.IsNullOrEmpty(_mouseOutput.LastReleaseError) ? _mouseOutput.LastReleaseError
                        : "Ctrl+Alt+F8 不可用，未开启摇杆模拟；请先关闭旧版或占用快捷键的程序。";
                    return;
                }
                if (!_demo && _stickOutputMode.SelectedIndex == 1)
                {
                    string error;
                    if (!_gamepadOutput.TryConnect(out error))
                    { StopJoystick("虚拟手柄未连接：" + error + " 可切换为本页视角预览。"); return; }
                }
                _joystick.Enable();
                _stickPreviousButtons = _buttonsKnown ? _buttons : 0;
                int[] bits = new int[_buttonCards.Length];
                for (int i = 0; i < bits.Length; i++) bits[i] = _buttonCards[i].LogicalBit;
                _stickButtons.Begin(bits, _stickButtonActions, StickToggleBit(), _stickPreviousButtons);
                _stickTimer.Start();
                _stickMessage = _demo ? "演示预览 · 不发送虚拟手柄输入" : "已开启 · 等待新的六轴数据，Ctrl+Alt+F8 停止";
                SaveJoystickSettings();
            }
            catch (Exception error) { StopJoystick("开启摇杆失败：" + error.Message); }
        }

        private void StopJoystick(string reason)
        {
            bool wasEnabled = _joystick.Enabled;
            _joystick.Disable();
            _stickButtons.Reset();
            if (_stickTimer != null) _stickTimer.Stop();
            _gamepadOutput.Stop();
            if (_stickHotkeyArmed && _mouseOutput != null) _mouseOutput.Disarm();
            _stickHotkeyArmed = false;
            _stickMessage = !String.IsNullOrEmpty(_gamepadOutput.LastError)
                ? "摇杆模拟已停止；" + _gamepadOutput.LastError : reason;
            if (wasEnabled) SaveJoystickSettings();
            RenderJoystick();
        }

        private void JoystickTick()
        {
            if (_closing || !_joystick.Enabled) return;
            try
            {
            if (_currentPage != "joystick" || (!_demo && _selected == null))
            { StopJoystick("设备或页面已切换，右摇杆已回中。"); return; }
            double now = _mouseClock.Elapsed.TotalSeconds;
            _joystick.Step(now);
            _stickButtons.Step(now);
            if (_gamepadOutput.IsConnected)
            {
                string error;
                if (!_gamepadOutput.TryUpdate(_joystick.OutputX, _joystick.OutputY,
                    _joystick.LeftTrigger, _joystick.RightTrigger, _stickButtons.Output, out error))
                { StopJoystick("虚拟手柄输出已停止：" + error); return; }
            }
            _stickMessage = !_joystick.IsFresh ? "已开启 · 暂无新数据，摇杆与扳机已归零；收到新帧自动继续"
                : _demo ? "演示预览 · 不发送虚拟手柄输入"
                : _gamepadOutput.IsConnected ? "虚拟 Xbox 手柄已连接 · 右摇杆、LT / RT 与映射按键输出中 · Ctrl+Alt+F8 停止"
                : "本页视角预览中 · 两轴转向，Z 轴缩放，松手停止";
            RenderJoystick();
            }
            catch (Exception error) { StopJoystick("摇杆输出异常，模拟已停止：" + error.Message); }
        }

        private void ProcessJoystickToggle(uint mask)
        {
            uint rising = mask & ~_stickPreviousButtons;
            _stickPreviousButtons = mask;
            int bit = StickToggleBit();
            if (_currentPage != "joystick" || bit < 0) return;
            double now = _mouseClock.Elapsed.TotalSeconds;
            if ((rising & (1u << bit)) == 0 || now - _lastStickToggle < 0.03) return;
            _lastStickToggle = now;
            if (_joystick.Enabled) StopJoystick("实体按键已关闭摇杆模拟，右摇杆已回中。"); else StartJoystick();
        }

        private int StickToggleBit()
        {
            int slot = _stickToggleKey == null ? -1 : _stickToggleKey.SelectedIndex - 1;
            return slot >= 0 && slot < _buttonCards.Length ? _buttonCards[slot].LogicalBit : -1;
        }

        private void ProcessJoystickButtons(uint mask)
        {
            if (!_joystick.Enabled) return;
            GamepadButtons previous = _stickButtons.Output;
            _stickButtons.Observe(mask, _mouseClock.Elapsed.TotalSeconds);
            // Deliver button edges even when two HID reports arrive between output ticks.
            if (_gamepadOutput.IsConnected && _stickButtons.Output != previous) JoystickTick();
        }

        private void InitializeStickButtonMappings()
        {
            const string fileName = "gamepad-button-actions.txt";
            _stickButtonState = Find<TextBlock>("StickButtonState");
            UniformGrid host = Find<UniformGrid>("StickButtonsHost");
            for (int i = 0; i < _stickButtonCombos.Length; i++)
            {
                int index = i;
                StackPanel card = new StackPanel { Margin = new Thickness(4, 0, 4, 0) };
                TextBlock label = new TextBlock { FontSize = 11, Foreground = Muted, Margin = new Thickness(0, 0, 0, 4) };
                ComboBox combo = new ComboBox { Height = 28, FontSize = 11 };
                foreach (GamepadButtons button in GamepadButtonModel.Options)
                    combo.Items.Add(button == GamepadButtons.None ? "无动作" : button.ToString());
                combo.SelectedIndex = Array.IndexOf(GamepadButtonModel.Options, _stickButtonActions[i]);
                combo.SelectionChanged += delegate
                {
                    if (_loadingStickSettings || combo.SelectedIndex < 0) return;
                    _stickButtonActions[index] = GamepadButtonModel.Options[combo.SelectedIndex];
                    StopJoystick("手柄按键映射已更新；请重新开启摇杆模拟。");
                    try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName),
                        String.Join(",", _stickButtonActions)); }
                    catch (IOException) { _stickMessage = "手柄按键映射已应用，保存失败。"; }
                    catch (UnauthorizedAccessException) { _stickMessage = "手柄按键映射已应用，保存失败。"; }
                    Render(DateTime.UtcNow);
                };
                _stickButtonCombos[i] = combo;
                _stickButtonLabels[i] = label;
                card.Children.Add(label);
                card.Children.Add(combo);
                host.Children.Add(card);
            }
            LoadStickButtonMappings();
        }

        private void LoadStickButtonMappings()
        {
            foreach (string candidate in SettingCandidates("gamepad-button-actions.txt"))
            {
                GamepadButtons[] actions;
                if (!GamepadButtonModel.TryParse(candidate, out actions)) continue;
                Array.Copy(actions, _stickButtonActions, actions.Length);
                for (int i = 0; i < _stickButtonCombos.Length; i++)
                    _stickButtonCombos[i].SelectedIndex = Array.IndexOf(GamepadButtonModel.Options, actions[i]);
                return;
            }
        }

        private void RenderJoystick()
        {
            if (_stickStatus == null || _stickView == null) return;
            _stickToggle.Content = _joystick.Enabled ? "停止摇杆模拟" : "开启摇杆模拟";
            _stickToggle.Background = _joystick.Enabled ? ErrorBrush : Accent;
            _stickToggle.IsEnabled = _demo || _selected != null;
            _stickStatus.Text = _demo && !_joystick.Enabled ? "演示模式 · 开启后仅预览视角与摇杆偏转" : _stickMessage;
            _stickStatus.Foreground = _joystick.Enabled ? Accent : Muted;
            int toggleBit = StickToggleBit();
            for (int i = 0; i < _stickButtonCombos.Length; i++)
            {
                bool reserved = toggleBit >= 0 && _buttonCards[i].LogicalBit == toggleBit;
                _stickButtonLabels[i].Text = "按键 " + (i + 1) + (reserved ? " · 启停专用" : " · 逻辑键 " + (_buttonCards[i].LogicalBit + 1));
                _stickButtonCombos[i].IsEnabled = !reserved;
            }
            _stickButtonState.Text = (_gamepadOutput.IsConnected ? "手柄输出 " : "按键预览 ") +
                (_stickButtons.Output == GamepadButtons.None ? "—" : _stickButtons.Output.ToString());
            _stickView.SetValue(_joystick.StickX, _joystick.StickY, _joystick.Enabled, _joystick.IsFresh);
            _stickCamera.SetView(_joystick.YawDegrees, _joystick.PitchDegrees, _joystick.HasData, _joystick.FieldOfViewDegrees);
            _stickValues.Text = "X " + _joystick.StickX.ToString("0.000", CultureInfo.InvariantCulture) + "   Y " + _joystick.StickY.ToString("0.000", CultureInfo.InvariantCulture);
            _stickIntegers.Text = "RX " + _joystick.OutputX + "   RY " + _joystick.OutputY;
            _stickCameraText.Text = "Yaw " + _joystick.YawDegrees.ToString("0.0", CultureInfo.InvariantCulture) + "° · Pitch " + _joystick.PitchDegrees.ToString("0.0", CultureInfo.InvariantCulture) + "° · FOV " + _joystick.FieldOfViewDegrees.ToString("0.0", CultureInfo.InvariantCulture) + "° · 位置 (0,0,0) · Roll 0°";
            _stickGainXText.Text = "X 增益 " + _joystick.GainX.ToString("0.0", CultureInfo.InvariantCulture);
            _stickGainYText.Text = "Y 增益 " + _joystick.GainY.ToString("0.0", CultureInfo.InvariantCulture);
            _stickDeadZoneText.Text = "径向死区 " + _joystick.DeadZone.ToString("0.00", CultureInfo.InvariantCulture);
            _stickGammaText.Text = "响应曲线 " + _joystick.Gamma.ToString("0.0", CultureInfo.InvariantCulture);
            short[] centers = _joystick.Centers;
            _stickCenterText.Text = "中心：转向 " + centers[_joystick.SourceX] + " / " + centers[_joystick.SourceY] + " · Z " + centers[2];
            _zoomInGainText.Text = "力度灵敏度 " + _joystick.ZoomInGain.ToString("0.0", CultureInfo.InvariantCulture) + "×";
            _zoomOutGainText.Text = "力度灵敏度 " + _joystick.ZoomOutGain.ToString("0.0", CultureInfo.InvariantCulture) + "×";
            _zoomInSpeedText.Text = "放大速度 " + (_joystick.ZoomInSpeed * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
            _zoomOutSpeedText.Text = "缩小速度 " + (_joystick.ZoomOutSpeed * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
            _zoomInDeadZoneText.Text = "回中死区 " + (_joystick.ZoomInDeadZone * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
            _zoomOutDeadZoneText.Text = "回中死区 " + (_joystick.ZoomOutDeadZone * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
            _zoomLeftValue.Text = _joystick.LeftTrigger + " / 255";
            _zoomRightValue.Text = _joystick.RightTrigger + " / 255";
            _zoomLeftMeter.Value = _joystick.LeftTrigger;
            _zoomRightMeter.Value = _joystick.RightTrigger;
            _zoomHint.Text = !_joystick.ZoomEnabled ? "Z 轴缩放已关闭，LT / RT 保持为 0；原有转向仍可使用。"
                : (_joystick.ZoomInvert ? "Z+ 缩小 · Z− 放大" : "Z+ 放大 · Z− 缩小")
                    + " · 松手停在当前倍率。两方向独立调节；外部软件需绑定 LT / RT 到缩放。"
                    + (_joystick.SourceX == 2 || _joystick.SourceY == 2 ? " 当前 Z 也被选为转向轴。" : "");
            if (!_joystick.DominantAxisEnabled)
                _stickDominantStatus.Text = "主导轴过滤已关闭，可同时转向与缩放。";
            else if (!_joystick.ZoomEnabled)
                _stickDominantStatus.Text = "启用 Z 轴缩放后，主导轴过滤生效。";
            else if (_joystick.DominantGroup == DominantAxisGroup.Movement)
                _stickDominantStatus.Text = "已锁定移动与转向，暂停 Z 轴缩放。";
            else if (_joystick.DominantGroup == DominantAxisGroup.Z)
                _stickDominantStatus.Text = "已锁定 Z 轴缩放，暂停转向。";
            else
                _stickDominantStatus.Text = "主导轴过滤已开启，每次手势只控制一组，回中后重新判断。";
        }

        private void LoadJoystickSettings()
        {
            foreach (string candidate in SettingCandidates("stick-settings.txt"))
            {
                JoystickSettings s;
                if (!JoystickSettings.TryParse(candidate, out s)) continue;
                _stickSourceX.SelectedIndex = s.SourceX; _stickSourceY.SelectedIndex = s.SourceY;
                _stickInvertX.IsChecked = s.InvertX; _stickInvertY.IsChecked = s.InvertY;
                _stickGainX.Value = s.GainX; _stickGainY.Value = s.GainY;
                _stickDeadZone.Value = s.DeadZone; _stickGamma.Value = s.Gamma;
                _stickOutputMode.SelectedIndex = s.OutputMode; _stickToggleKey.SelectedIndex = s.ToggleKey;
                _zoomEnabled.IsChecked = s.ZoomEnabled; _zoomInvert.IsChecked = s.ZoomInvert;
                _stickDominantAxis.IsChecked = s.DominantAxisEnabled;
                _zoomInGain.Value = s.ZoomInGain; _zoomOutGain.Value = s.ZoomOutGain;
                _zoomInSpeed.Value = s.ZoomInSpeed * 100; _zoomOutSpeed.Value = s.ZoomOutSpeed * 100;
                _zoomInDeadZone.Value = s.ZoomInDeadZone; _zoomOutDeadZone.Value = s.ZoomOutDeadZone;
                _joystick.SetCalibration(s.Centers);
                return;
            }
        }

        private void SaveJoystickSettings()
        {
            if (_loadingStickSettings || _stickOutputMode == null) return;
            JoystickSettings s = new JoystickSettings {
                SourceX = _joystick.SourceX, SourceY = _joystick.SourceY,
                InvertX = _joystick.InvertX, InvertY = _joystick.InvertY,
                GainX = _joystick.GainX, GainY = _joystick.GainY, DeadZone = _joystick.DeadZone, Gamma = _joystick.Gamma,
                OutputMode = _stickOutputMode.SelectedIndex, ToggleKey = _stickToggleKey.SelectedIndex, Centers = _joystick.Centers,
                ZoomEnabled = _joystick.ZoomEnabled, ZoomInvert = _joystick.ZoomInvert,
                DominantAxisEnabled = _joystick.DominantAxisEnabled,
                ZoomInGain = _joystick.ZoomInGain, ZoomOutGain = _joystick.ZoomOutGain,
                ZoomInSpeed = _joystick.ZoomInSpeed, ZoomOutSpeed = _joystick.ZoomOutSpeed,
                ZoomInDeadZone = _joystick.ZoomInDeadZone, ZoomOutDeadZone = _joystick.ZoomOutDeadZone
            };
            try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "stick-settings.txt"), s.Serialize()); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
