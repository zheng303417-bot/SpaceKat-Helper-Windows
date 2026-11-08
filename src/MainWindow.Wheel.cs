using System;
using System.Globalization;
using System.IO;
using System.Windows.Controls;

namespace SpaceKatMonitor
{
    public partial class MainWindow
    {
        private readonly MouseWheelModel _mouseWheel = new MouseWheelModel();
        private readonly DominantAxisFilter _mouseDominantAxisFilter = new DominantAxisFilter();
        private CheckBox _wheelEnabled, _wheelInvert, _wheelDominantAxis;
        private Slider _wheelUpGain, _wheelDownGain, _wheelUpSpeed, _wheelDownSpeed;
        private Slider _wheelUpDeadZone, _wheelDownDeadZone;
        private TextBlock _wheelUpGainText, _wheelDownGainText, _wheelUpSpeedText, _wheelDownSpeedText;
        private TextBlock _wheelUpDeadZoneText, _wheelDownDeadZoneText, _wheelStatusText, _wheelCenterText;
        private TextBlock _wheelDominantStatus;
        private bool _wheelDominantAxisEnabled = true;
        private bool _loadingWheelSettings;
        private string _wheelFeedback;

        private void InitializeMouseWheel()
        {
            _wheelEnabled = Find<CheckBox>("WheelEnabled");
            _wheelInvert = Find<CheckBox>("WheelInvert");
            _wheelDominantAxis = Find<CheckBox>("WheelDominantAxis");
            _wheelUpGain = Find<Slider>("WheelUpGain");
            _wheelDownGain = Find<Slider>("WheelDownGain");
            _wheelUpSpeed = Find<Slider>("WheelUpSpeed");
            _wheelDownSpeed = Find<Slider>("WheelDownSpeed");
            _wheelUpDeadZone = Find<Slider>("WheelUpDeadZone");
            _wheelDownDeadZone = Find<Slider>("WheelDownDeadZone");
            _wheelUpGainText = Find<TextBlock>("WheelUpGainText");
            _wheelDownGainText = Find<TextBlock>("WheelDownGainText");
            _wheelUpSpeedText = Find<TextBlock>("WheelUpSpeedText");
            _wheelDownSpeedText = Find<TextBlock>("WheelDownSpeedText");
            _wheelUpDeadZoneText = Find<TextBlock>("WheelUpDeadZoneText");
            _wheelDownDeadZoneText = Find<TextBlock>("WheelDownDeadZoneText");
            _wheelStatusText = Find<TextBlock>("WheelStatusText");
            _wheelCenterText = Find<TextBlock>("WheelCenterText");
            _wheelDominantStatus = Find<TextBlock>("WheelDominantStatus");
            _loadingWheelSettings = true;
            LoadWheelSettings();
            ApplyWheelSettings();
            _loadingWheelSettings = false;
            foreach (Slider slider in new[] { _wheelUpGain, _wheelDownGain, _wheelUpSpeed, _wheelDownSpeed,
                _wheelUpDeadZone, _wheelDownDeadZone })
                slider.ValueChanged += delegate { WheelSettingsChanged(); };
            _wheelEnabled.Click += delegate { WheelSettingsChanged(); };
            _wheelInvert.Click += delegate { WheelSettingsChanged(); };
            _wheelDominantAxis.Click += delegate { WheelSettingsChanged(); };
            Find<Button>("WheelCalibrateButton").Click += delegate
            {
                double age = _mouseClock.Elapsed.TotalSeconds - _lastAxisSeconds;
                if (!_axesKnown || _demo || _selected == null || age < 0 || age > .25)
                {
                    _wheelFeedback = "请松开操纵帽，收到新的真实六轴数据后再设置 Z 中心。";
                    RenderMouseWheel();
                    return;
                }
                _mouseWheel.Center = _axes[2];
                if (WheelDominantAxisActive) ResetMouseAxes();
                else _mouseWheel.Reset();
                _wheelFeedback = _mouseEnabled ? "Z 中心已设置，收到下一帧后继续。"
                    : "Z 中心已保存，开启鼠标控制后按新中心滚动。";
                SaveWheelSettings();
                RenderMouseWheel();
            };
            Find<Button>("WheelClearCenterButton").Click += delegate
            {
                _mouseWheel.Center = 0;
                if (WheelDominantAxisActive) ResetMouseAxes();
                else _mouseWheel.Reset();
                _wheelFeedback = _mouseEnabled ? "Z 中心已清除，收到下一帧后按新中心继续。"
                    : "Z 中心已清除，开启鼠标控制后生效。";
                SaveWheelSettings();
                RenderMouseWheel();
            };
            RenderMouseWheel();
        }

        private void ApplyWheelSettings()
        {
            _mouseWheel.Enabled = _wheelEnabled.IsChecked == true;
            _mouseWheel.Invert = _wheelInvert.IsChecked == true;
            _wheelDominantAxisEnabled = _wheelDominantAxis.IsChecked == true;
            _mouseWheel.UpGain = _wheelUpGain.Value;
            _mouseWheel.DownGain = _wheelDownGain.Value;
            _mouseWheel.UpSpeed = _wheelUpSpeed.Value;
            _mouseWheel.DownSpeed = _wheelDownSpeed.Value;
            _mouseWheel.UpDeadZone = _wheelUpDeadZone.Value;
            _mouseWheel.DownDeadZone = _wheelDownDeadZone.Value;
        }

        private void WheelSettingsChanged()
        {
            if (_loadingWheelSettings) return;
            bool filterWasActive = WheelDominantAxisActive;
            ApplyWheelSettings();
            if (filterWasActive || WheelDominantAxisActive) ResetMouseAxes();
            _wheelFeedback = null;
            SaveWheelSettings();
            RenderMouseWheel();
        }

        private bool WheelDominantAxisActive { get { return _wheelDominantAxisEnabled && _mouseWheel.Enabled; } }

        private void UpdateMouseAxes(short[] axes, double now)
        {
            short x = axes[0];
            short y = axes[1];
            short z = axes[2];
            if (WheelDominantAxisActive)
            {
                int movementMagnitude = Math.Max(Math.Abs((int)x), Math.Abs((int)y));
                double movementStrength = movementMagnitude <= _mouseMotion.DeadZone
                    ? 0 : Math.Min(1, movementMagnitude / 512.0);
                double centeredZ = (z - (double)_mouseWheel.Center) / 512;
                if (_mouseWheel.Invert) centeredZ = -centeredZ;
                double zStrength = Math.Min(1, Math.Abs(centeredZ));
                double zDeadZone = centeredZ < 0 ? _mouseWheel.DownDeadZone : _mouseWheel.UpDeadZone;
                if (zStrength <= zDeadZone) zStrength = 0;

                DominantAxisGroup group = _mouseDominantAxisFilter.Update(movementStrength, zStrength, now,
                    MouseWheelModel.SampleTimeoutSeconds);
                if (group != DominantAxisGroup.Movement) x = y = 0;
                if (group != DominantAxisGroup.Z) z = _mouseWheel.Center;
            }
            _mouseMotion.Update(x, y, now);
            _mouseWheel.Update(z, now);
        }

        private void ResetMouseAxes()
        {
            _mouseMotion.Reset();
            _mouseWheel.Reset();
            _mouseDominantAxisFilter.Reset();
        }

        private void RenderMouseWheel()
        {
            if (_wheelStatusText == null) return;
            _wheelUpGainText.Text = "力度灵敏度 " + _mouseWheel.UpGain.ToString("0.0", CultureInfo.InvariantCulture) + "×";
            _wheelDownGainText.Text = "力度灵敏度 " + _mouseWheel.DownGain.ToString("0.0", CultureInfo.InvariantCulture) + "×";
            _wheelUpSpeedText.Text = "上滚速度 " + _mouseWheel.UpSpeed.ToString("0", CultureInfo.InvariantCulture) + " 格/秒";
            _wheelDownSpeedText.Text = "下滚速度 " + _mouseWheel.DownSpeed.ToString("0", CultureInfo.InvariantCulture) + " 格/秒";
            _wheelUpDeadZoneText.Text = "回中死区 " + (_mouseWheel.UpDeadZone * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
            _wheelDownDeadZoneText.Text = "回中死区 " + (_mouseWheel.DownDeadZone * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
            _wheelCenterText.Text = "Z 中心：" + _mouseWheel.Center.ToString(CultureInfo.InvariantCulture);
            string direction = _mouseWheel.Invert ? "Z+ 下滚 · Z− 上滚" : "Z+ 上滚 · Z− 下滚";
            string state = _demo ? "演示模式 · 不发送滚轮输入"
                : !_mouseWheel.Enabled ? "Z 轴滚轮已关闭"
                : !_mouseEnabled ? "鼠标模拟未开启 · 不发送滚轮输入"
                : _mouseWheel.Input > 0 ? "上滚中" : _mouseWheel.Input < 0 ? "下滚中" : "等待 Z 偏移 · 回中停止滚动";
            _wheelStatusText.Text = state + " · " + direction +
                (String.IsNullOrEmpty(_wheelFeedback) ? "" : " · " + _wheelFeedback);
            _wheelStatusText.Foreground = !_demo && _mouseEnabled && _mouseWheel.Enabled ? Accent : Muted;
            _wheelDominantStatus.Text = !_wheelDominantAxisEnabled ? "主导轴过滤已关闭 · 光标和滚轮可同时操作"
                : !_mouseWheel.Enabled ? "启用 Z 滚轮后应用主导轴过滤"
                : !_mouseEnabled ? "主导轴过滤已开启 · 开启鼠标控制后生效"
                : _mouseDominantAxisFilter.Group == DominantAxisGroup.Movement ? "已锁定 X / Y 移动 · 松手回中后可滚动"
                : _mouseDominantAxisFilter.Group == DominantAxisGroup.Z ? "已锁定 Z 滚轮 · 松手回中后可移动"
                : "主导轴过滤已开启 · 等待移动或滚动";
        }

        private void LoadWheelSettings()
        {
            foreach (string candidate in SettingCandidates("wheel-settings.txt"))
            {
                MouseWheelSettings s;
                if (!MouseWheelSettings.TryParse(candidate, out s)) continue;
                _wheelEnabled.IsChecked = s.Enabled; _wheelInvert.IsChecked = s.Invert;
                _wheelDominantAxis.IsChecked = s.DominantAxisEnabled;
                _wheelUpGain.Value = s.UpGain; _wheelDownGain.Value = s.DownGain;
                _wheelUpSpeed.Value = s.UpSpeed; _wheelDownSpeed.Value = s.DownSpeed;
                _wheelUpDeadZone.Value = s.UpDeadZone; _wheelDownDeadZone.Value = s.DownDeadZone;
                _mouseWheel.Center = s.Center;
                return;
            }
        }

        private void SaveWheelSettings()
        {
            if (_loadingWheelSettings || _wheelEnabled == null) return;
            MouseWheelSettings s = new MouseWheelSettings {
                Enabled = _mouseWheel.Enabled, Invert = _mouseWheel.Invert,
                DominantAxisEnabled = _wheelDominantAxisEnabled,
                UpGain = _mouseWheel.UpGain, DownGain = _mouseWheel.DownGain,
                UpSpeed = _mouseWheel.UpSpeed, DownSpeed = _mouseWheel.DownSpeed,
                UpDeadZone = _mouseWheel.UpDeadZone, DownDeadZone = _mouseWheel.DownDeadZone, Center = _mouseWheel.Center
            };
            try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wheel-settings.txt"), s.Serialize()); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
