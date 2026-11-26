using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace SpaceKatMonitor
{
    public partial class MainWindow
    {
        private Grid _navigationPage;
        private Button _navigationNav, _navigationToggle, _navigationExport;
        private ComboBox _navigationTarget;
        private TextBlock _navigationStatus, _navigationInstructions, _navigationValues;
        private Slider _navigationLinear, _navigationAngular, _navigationDeadZone;
        private CheckBox[] _navigationInvert;
        private readonly ComboBox[] _navigationButtonPickers = new ComboBox[4];
        private readonly Button[] _navigationLearnButtons = new Button[4];
        private readonly ComboBox[] _fusionButtonPickers = new ComboBox[4];
        private readonly Border[] _navigationButtonPanels = new Border[4];
        private readonly System.Collections.Generic.List<Button> _navigationTargetTiles = new System.Collections.Generic.List<Button>();
        private readonly System.Collections.Generic.List<Border> _navigationTargetBadges = new System.Collections.Generic.List<Border>();
        private readonly System.Collections.Generic.List<TextBlock> _navigationTargetMarks = new System.Collections.Generic.List<TextBlock>();
        private static readonly Brush BadgeIdle = ColorBrush("#E8E4DD");
        private static readonly Brush Graphite = ColorBrush("#2B2A27");
        private readonly TextBlock[] _navigationButtonLabels = new TextBlock[4];
        private TextBlock _navigationButtonHelp;
        private readonly int[] _fusionLogicalBits = new int[4];
        private static readonly FusionViewCommand[] FusionButtonOptions =
            { FusionViewCommand.None, FusionViewCommand.Fit, FusionViewCommand.Top, FusionViewCommand.Right, FusionViewCommand.Front };
        private static readonly string[] FusionButtonLabels = { "无动作", "适合视图", "顶视图", "右视图", "前视图" };
        private readonly FusionViewCommand[] _fusionButtonCommands =
            { FusionViewCommand.Fit, FusionViewCommand.Top, FusionViewCommand.Right, FusionViewCommand.Front };
        private DispatcherTimer _navigationTimer;
        private NavigationBridge _navigationBridge;
        private readonly GlobalNavigationOutput _globalNavigation = new GlobalNavigationOutput();
        private readonly SolidWorksNavigationOutput _solidWorksNavigation = new SolidWorksNavigationOutput();
        private readonly FusionNativeNavigation _fusionNativeNavigation = new FusionNativeNavigation();
        private NavigationTarget _activeNavigation;
        private bool _navigationStartupPending;
        private string _navigationMessage;
        private long _navigationSample;
        private long _nextNavigationRelease;
        private readonly float[] _navigationAxes = new float[6];

        private bool NavigationEnabled { get { return _activeNavigation != NavigationTarget.None; } }
        private bool NavigationReleasePending { get { return _globalNavigation.HasPendingRelease; } }

        private void InitializeNavigation()
        {
            _navigationPage = Find<Grid>("NavigationPage");
            _navigationNav = Find<Button>("NavigationNavButton");
            _navigationNav.Click += delegate { ShowPage("navigation"); };
            _navigationToggle = Find<Button>("NavigationToggleButton");
            _navigationExport = Find<Button>("NavigationExportButton");
            _navigationTarget = Find<ComboBox>("NavigationTargetPicker");
            _navigationStatus = Find<TextBlock>("NavigationStatusText");
            _navigationInstructions = Find<TextBlock>("NavigationInstructionsText");
            _navigationValues = Find<TextBlock>("NavigationValuesText");
            _navigationLinear = Find<Slider>("NavigationLinear");
            _navigationAngular = Find<Slider>("NavigationAngular");
            _navigationDeadZone = Find<Slider>("NavigationDeadZone");
            _navigationInvert = new[] { Find<CheckBox>("NavInvertX"), Find<CheckBox>("NavInvertY"), Find<CheckBox>("NavInvertZ"),
                Find<CheckBox>("NavInvertRx"), Find<CheckBox>("NavInvertRy"), Find<CheckBox>("NavInvertRz") };
            InitializeNavigationButtons();
            _navigationTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
            _navigationTimer.Tick += delegate { PublishNavigation(); };
            _navigationToggle.Click += delegate
            {
                if (NavigationEnabled || _navigationStartupPending) StopNavigation("已关闭 3D 导航。"); else StartNavigation();
                Render(DateTime.UtcNow);
            };
            _navigationTarget.SelectionChanged += delegate { UpdateNavigationInstructions(); };
            InitializeNavigationTargetTiles();
            _navigationTarget.SelectedValue = NavigationTarget.Global.ToString();
            LoadNavigationSettings();
            LoadFusionButtonSettings();
            _navigationExport.Click += delegate { ExportNavigationAddIn(); };
            _navigationLinear.ValueChanged += delegate { UpdateNavigationValues(); };
            _navigationAngular.ValueChanged += delegate { UpdateNavigationValues(); };
            _navigationDeadZone.ValueChanged += delegate { UpdateNavigationValues(); };
            UpdateNavigationValues();
        }

        // The target software is the first decision on this page, so it is laid
        // out as a set of large tiles. The hidden picker stays the single source
        // of truth for saving, enabling and the existing navigation logic.
        private void InitializeNavigationTargetTiles()
        {
            UniformGrid host = Find<UniformGrid>("NavigationTargetTiles");
            Style tileStyle = (Style)_root.Resources["TargetTile"];
            FontFamily display = (FontFamily)_root.Resources["DisplayFont"];
            foreach (object entry in _navigationTarget.Items)
            {
                ComboBoxItem item = (ComboBoxItem)entry;
                string name, detail, mark;
                DescribeNavigationTarget(item.Tag as string, out name, out detail, out mark);
                Grid content = new Grid();
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                content.ColumnDefinitions.Add(new ColumnDefinition());
                TextBlock markText = new TextBlock { Text = mark, FontFamily = display, FontSize = 14, FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                Border badge = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(12), Child = markText };
                content.Children.Add(badge);
                StackPanel text = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                text.Children.Add(new TextBlock { Text = name, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = Ink,
                    TextTrimming = TextTrimming.CharacterEllipsis });
                text.Children.Add(new TextBlock { Text = detail, FontSize = 11.5, Foreground = Muted, Margin = new Thickness(0, 2, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis });
                Grid.SetColumn(text, 1);
                content.Children.Add(text);
                Button tile = new Button { Style = tileStyle, Content = content, ToolTip = item.Content };
                System.Windows.Automation.AutomationProperties.SetName(tile, item.Content as string);
                ComboBoxItem target = item;
                tile.Click += delegate { _navigationTarget.SelectedItem = target; };
                _navigationTargetTiles.Add(tile);
                _navigationTargetBadges.Add(badge);
                _navigationTargetMarks.Add(markText);
                host.Children.Add(tile);
            }
            _navigationTarget.SelectionChanged += delegate { RefreshNavigationTargetTiles(); };
            _navigationTarget.IsEnabledChanged += delegate { RefreshNavigationTargetTiles(); };
            RefreshNavigationTargetTiles();
        }

        private static void DescribeNavigationTarget(string tag, out string name, out string detail, out string mark)
        {
            switch (tag)
            {
                case "Blender": name = "Blender"; detail = "专用适配器"; mark = "Bl"; break;
                case "FusionNative": name = "Fusion 360"; detail = "相机直连"; mark = "Fu"; break;
                case "FreeCAD": name = "FreeCAD"; detail = "试用适配"; mark = "Fc"; break;
                case "Rhino": name = "Rhino 8"; detail = "试用适配"; mark = "Rh"; break;
                case "SolidWorks": name = "SOLIDWORKS"; detail = "六轴直连"; mark = "Sw"; break;
                default: name = "全局适配"; detail = "自动识别软件"; mark = "全"; break;
            }
        }

        private void RefreshNavigationTargetTiles()
        {
            for (int i = 0; i < _navigationTargetTiles.Count; i++)
            {
                bool selected = _navigationTarget.SelectedIndex == i;
                _navigationTargetTiles[i].Tag = selected ? "on" : null;
                _navigationTargetTiles[i].IsEnabled = _navigationTarget.IsEnabled;
                if (selected) _navigationTargetBadges[i].SetResourceReference(Border.BackgroundProperty, "AccentFillBrush");
                else _navigationTargetBadges[i].Background = BadgeIdle;
                if (selected) _navigationTargetMarks[i].SetResourceReference(TextBlock.ForegroundProperty, "OnAccentBrush");
                else _navigationTargetMarks[i].Foreground = Graphite;
            }
        }

        private void InitializeNavigationButtons()
        {
            UniformGrid host = Find<UniformGrid>("NavigationButtonsHost");
            _navigationButtonHelp = Find<TextBlock>("NavigationButtonHelp");
            Style mini = (Style)_root.Resources["MiniButton"];
            for (int slot = 0; slot < _buttonCards.Length; slot++)
            {
                int button = slot;
                ButtonCard card = _buttonCards[slot];
                var column = new StackPanel();
                var label = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Ink, Margin = new Thickness(0, 0, 0, 10),
                    TextTrimming = TextTrimming.CharacterEllipsis };
                column.Children.Add(label);
                _navigationButtonLabels[slot] = label;
                _navigationButtonPanels[slot] = new Border { Background = TileFill, CornerRadius = new CornerRadius(16),
                    Padding = new Thickness(14, 12, 14, 14), Margin = new Thickness(6, 0, 6, 0), Child = column };

                var binding = new Grid();
                binding.ColumnDefinitions.Add(new ColumnDefinition());
                binding.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var logical = new ComboBox { Height = 34, MinWidth = 70, Margin = new Thickness(0, 0, 6, 0), Background = FieldOnTile };
                for (int number = 1; number <= 32; number++) logical.Items.Add("逻辑键 " + number);
                logical.SelectedIndex = card.LogicalBit;
                logical.SelectionChanged += delegate
                {
                    if (logical.SelectedIndex >= 0 && card.Mapping.SelectedIndex != logical.SelectedIndex)
                    {
                        if (_learningCard != null) CancelLearning(null);
                        card.Mapping.SelectedIndex = logical.SelectedIndex;
                    }
                };
                _navigationButtonPickers[slot] = logical;
                binding.Children.Add(logical);

                var learn = new Button { Content = "学习此键", Style = mini, Height = 34, Background = FieldOnTile, Tag = card };
                learn.Click += BeginLearning;
                Grid.SetColumn(learn, 1);
                binding.Children.Add(learn);
                _navigationLearnButtons[slot] = learn;
                column.Children.Add(binding);

                var action = new ComboBox { Height = 34, Margin = new Thickness(0, 8, 0, 0), Background = FieldOnTile,
                    Visibility = Visibility.Collapsed, ItemsSource = FusionButtonLabels,
                    SelectedIndex = Array.IndexOf(FusionButtonOptions, _fusionButtonCommands[slot]) };
                action.SelectionChanged += delegate
                {
                    if (action.SelectedIndex >= 0) _fusionButtonCommands[button] = FusionButtonOptions[action.SelectedIndex];
                };
                _fusionButtonPickers[slot] = action;
                column.Children.Add(action);
                host.Children.Add(_navigationButtonPanels[slot]);
            }
        }

        private void SyncNavigationButtonMapping(ButtonCard card)
        {
            int slot = Array.IndexOf(_buttonCards, card);
            if (slot >= 0 && _navigationButtonPickers[slot] != null)
                _navigationButtonPickers[slot].SelectedIndex = card.LogicalBit;
        }

        private void RenderNavigationButtonMapping()
        {
            for (int slot = 0; slot < _navigationLearnButtons.Length; slot++)
            {
                bool learning = _learningCard == _buttonCards[slot];
                _navigationLearnButtons[slot].Content = learning ? "取消学习" : "学习此键";
                _navigationLearnButtons[slot].IsEnabled = !_demo && _selected != null;
            }
            string feedback = _learningCard != null ? "松开所有键，再短按并松开「" +
                ButtonName(_learningCard) + "」。" : _learningFeedback;
            _navigationButtonHelp.Text = feedback ?? (SelectedNavigationTarget == NavigationTarget.FusionNative ?
                "动作在 Fusion 相机直连模式生效。逻辑键与鼠标映射页共用。" :
                "逻辑键与鼠标映射页共用。连接设备后可按下物理键学习。");
            _navigationButtonHelp.Foreground = feedback == null ? Muted : Warn;
        }

        private NavigationTarget SelectedNavigationTarget
        {
            get
            {
                NavigationTarget target;
                ComboBoxItem item = _navigationTarget.SelectedItem as ComboBoxItem;
                return item != null && Enum.TryParse(item.Tag as string, out target) ? target : NavigationTarget.None;
            }
        }

        private static bool UsesNavigationBridge(NavigationTarget target)
        {
            return target == NavigationTarget.Blender || target == NavigationTarget.FreeCAD || target == NavigationTarget.Rhino;
        }

        private static bool CanExportNavigationAddIn(NavigationTarget target)
        {
            return target == NavigationTarget.FusionNative || UsesNavigationBridge(target);
        }

        private void StartNavigation()
        {
            if (_demo || _selected == null || _input == null || _mouseOutput == null || _currentPage != "navigation") return;
            _navigationStartupPending = false;
            StopMouseControl("已切换为 3D 导航。");
            StopJoystick("已切换为 3D 导航。");
            if (_gamepadOutput.HasPendingStop || _mouseOutput.HasPendingRelease || !TryReleaseGlobalNavigation())
            {
                _navigationMessage = "上一项输出尚未释放。请点左侧“重试停止输出”。";
                return;
            }
            try
            {
                _mouseOutput.Arm();
                if (!_mouseOutput.StopHotkeyAvailable)
                {
                    _navigationMessage = "无法注册 Ctrl+Alt+F8。请关闭占用快捷键的程序后重试。";
                    return;
                }
                NavigationTarget target = SelectedNavigationTarget;
                if (UsesNavigationBridge(target) && _navigationBridge == null)
                    _navigationBridge = new NavigationBridge();
                _navigationSample = 0;
                _activeNavigation = target;
                if (target == NavigationTarget.FusionNative) _fusionNativeNavigation.EnsureStarted();
                PublishNavigation();
                if (NavigationEnabled) _navigationTimer.Start();
            }
            catch (Exception error)
            {
                _mouseOutput.Disarm();
                StopNavigation("无法开启 3D 导航。" + " " + error.Message);
            }
        }

        private void PublishNavigation()
        {
            if (!NavigationEnabled) return;
            if (_demo || _selected == null || _currentPage != "navigation")
            {
                StopNavigation("设备或页面已切换，3D 导航已停止。");
                return;
            }
            try
            {
                double linear = _navigationLinear.Value, angular = _navigationAngular.Value;
                NavigationBridge.Shape(_axes, (int)_navigationDeadZone.Value, NavigationInvertMask(),
                    linear, angular, _navigationAxes);
                if (_activeNavigation == NavigationTarget.Global)
                    _globalNavigation.Update(_navigationAxes, HasFreshNavigationInput(), _navigationLinear.Value);
                else if (_activeNavigation == NavigationTarget.SolidWorks)
                {
                    SolidWorksNavigationOutput.ApplyPrecisionCurve(_navigationAxes, linear, angular);
                    _solidWorksNavigation.Publish(_navigationSample, _navigationAxes);
                }
                else if (_activeNavigation == NavigationTarget.FusionNative)
                    _fusionNativeNavigation.Publish(_navigationSample, _navigationAxes);
                else
                    _navigationBridge.Publish(_activeNavigation, _navigationSample, _navigationAxes);
            }
            catch (Exception error) { StopNavigation("3D 导航输出已停止：" + error.Message); }
        }

        private void StopNavigation(string reason, bool keepStartupRequest = false)
        {
            if (!keepStartupRequest) _navigationStartupPending = false;
            NavigationTarget previous = _activeNavigation;
            _activeNavigation = NavigationTarget.None;
            _navigationSample = 0;
            if (_navigationTimer != null) _navigationTimer.Stop();
            if (previous == NavigationTarget.Global || NavigationReleasePending)
                _globalNavigation.Stop();
            if (previous == NavigationTarget.SolidWorks)
                _solidWorksNavigation.Stop();
            if (previous == NavigationTarget.FusionNative)
                _fusionNativeNavigation.Stop();
            if (previous != NavigationTarget.None)
            {
                if (UsesNavigationBridge(previous))
                {
                    try { _navigationBridge.Publish(NavigationTarget.None, 0, null); }
                    catch (Exception) { } // Reader's short deadline also expires if the mapping is lost.
                }
                if (_mouseOutput != null) _mouseOutput.Disarm();
            }
            if (previous != NavigationTarget.None || _currentPage == "navigation") _navigationMessage = reason;
        }

        private void PublishFusionButtonPresses(uint pressed)
        {
            if (_activeNavigation != NavigationTarget.FusionNative || _learningCard != null || pressed == 0) return;
            try
            {
                for (int slot = 0; slot < _fusionLogicalBits.Length; slot++)
                    _fusionLogicalBits[slot] = _buttonCards[slot].LogicalBit;
                _fusionNativeNavigation.PublishButtons(pressed, _fusionLogicalBits, _fusionButtonCommands);
            }
            catch (Exception error) { StopNavigation("3D 导航输出已停止：" + error.Message); }
        }

        private bool HasFreshNavigationInput()
        {
            long age = NavigationBridge.GetTickCount64() - _navigationSample;
            return _navigationSample > 0 && age >= 0 && age < NavigationBridge.SampleTimeoutMs;
        }

        private bool TryReleaseGlobalNavigation()
        {
            if (NavigationReleasePending) _globalNavigation.Stop();
            return !NavigationReleasePending;
        }

        private void RetryNavigationRelease()
        {
            if (!NavigationReleasePending || NavigationEnabled) return;
            long now = NavigationBridge.GetTickCount64();
            if (now < _nextNavigationRelease) return;
            _nextNavigationRelease = now + 250;
            TryReleaseGlobalNavigation();
        }

        private void RenderAppNavigation()
        {
            NavigationTarget target = SelectedNavigationTarget;
            bool global = target == NavigationTarget.Global;
            bool solidWorks = target == NavigationTarget.SolidWorks;
            bool fusionNative = target == NavigationTarget.FusionNative;
            _navigationToggle.IsEnabled = !_demo && (_navigationStartupPending ||
                (_selected != null && (NavigationEnabled || !NavigationReleasePending)));
            _navigationToggle.Content = NavigationEnabled || _navigationStartupPending ? "停止导航" : "开启导航";
            SetRunButton(_navigationToggle, NavigationEnabled || _navigationStartupPending);
            _navigationTarget.IsEnabled = !NavigationEnabled;
            _navigationExport.Visibility = CanExportNavigationAddIn(target) ? Visibility.Visible : Visibility.Collapsed;
            _navigationInvert[5].IsEnabled = !global;
            string status;
            if (NavigationReleasePending) status = "还有导航按键待释放。可点左侧“重试停止输出”。";
            else if (_demo) status = "演示模式 · 不控制外部软件";
            else if (_navigationStartupPending) status = "等待设备连接 · 连接后自动开启导航";
            else if (NavigationEnabled && global)
            {
                string profile = _globalNavigation.CurrentProfile;
                status = profile == null ? "已开启 · 请切到支持的建模软件，把鼠标放进 3D 视图"
                    : "已识别 " + profile + (HasFreshNavigationInput() ? " · 导航就绪" : " · 等待六轴数据");
            }
            else if (NavigationEnabled && solidWorks) status = _solidWorksNavigation.Status;
            else if (NavigationEnabled && fusionNative) status = _fusionNativeNavigation.Status(HasFreshNavigationInput());
            else if (NavigationEnabled)
                status = HasFreshNavigationInput() ? "已开启 · 请切到所选软件的 3D 视图" : "已开启 · 等待六轴数据，当前无输出";
            else if (_selected == null)
                status = CanExportNavigationAddIn(target) ? "未连接设备 · 可以先准备适配器" : "未连接设备";
            else status = _navigationMessage ?? (CanExportNavigationAddIn(target) ?
                "尚未开启。请先在目标软件里启用适配器。" : "尚未开启。无需安装插件。");
            _navigationStatus.Text = status;
            _navigationStatus.Foreground = NavigationReleasePending ? Warn : NavigationEnabled ? Live : Muted;
            RenderNavigationButtonMapping();
        }

        private void UpdateNavigationInstructions()
        {
            _navigationMessage = null;
            foreach (ComboBox settings in _fusionButtonPickers)
                settings.Visibility = SelectedNavigationTarget == NavigationTarget.FusionNative ?
                    Visibility.Visible : Visibility.Collapsed;
            switch (SelectedNavigationTarget)
            {
                case NavigationTarget.Blender:
                    _navigationInstructions.Text = "Blender：在偏好设置 → 插件 → 从磁盘安装，选择导出的 ZIP 并启用。然后在要控制的 3D 视图按 N，打开 SpaceKat 面板，点“启动导航”。切换工作区后可在新视图重新启动。";
                    break;
                case NavigationTarget.FusionNative:
                    _navigationInstructions.Text = "首次在 Fusion 的「实用程序 → 脚本和加载项」中添加并运行 SpaceKatFusion。连接摇杆后，在此开启导航，再切回 Fusion 模型。";
                    break;
                case NavigationTarget.SolidWorks:
                    _navigationInstructions.Text = "打开 SOLIDWORKS 的零件或装配体，开启导航后切到模型视图。支持平移、连续缩放、旋转和滚转。工程图、草图编辑和命令运行期间暂停。";
                    break;
                case NavigationTarget.FreeCAD:
                    _navigationInstructions.Text = "FreeCAD：解压 ZIP，打开并执行 SpaceKatNavigation.FCMacro。请保持桥接文件与宏在同一文件夹。再次运行宏可停止。";
                    break;
                case NavigationTarget.Rhino:
                    _navigationInstructions.Text = "Rhino 8：解压 ZIP，用 ScriptEditor 打开并运行 SpaceKatNavigation.py。请保持桥接文件与脚本在同一文件夹。再次运行脚本可停止。";
                    break;
                case NavigationTarget.Global:
                    _navigationInstructions.Text = "开启后切到 Blender、SOLIDWORKS、AutoCAD、SketchUp 等软件，把鼠标放进 3D 视图。全局适配用默认鼠标手势平移、缩放或旋转，每次执行一种动作。Rz 滚转需用专用适配器。";
                    break;
            }
        }

        private void ExportNavigationAddIn()
        {
            NavigationTarget target = SelectedNavigationTarget;
            if (!CanExportNavigationAddIn(target)) return;
            var dialog = new SaveFileDialog { Title = "导出 3D 导航适配器", Filter = "ZIP 压缩包|*.zip",
                FileName = "SpaceKat-" + target + "-Navigation.zip", AddExtension = true, OverwritePrompt = true };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                using (var content = new MemoryStream())
                {
                    NavigationBridge.ExportAddIn(content, target);
                    File.WriteAllBytes(dialog.FileName, content.ToArray());
                }
                MessageBox.Show(this, "已导出。请按本页说明在目标软件中启用适配器。", "3D 导航", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception error)
            {
                MessageBox.Show(this, "无法导出适配器：" + error.Message, "3D 导航", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private int NavigationInvertMask()
        {
            int mask = 0;
            for (int i = 0; i < 6; i++) if (_navigationInvert[i].IsChecked == true) mask |= 1 << i;
            return mask;
        }

        private void UpdateNavigationValues()
        {
            _navigationValues.Text = String.Format(CultureInfo.InvariantCulture,
                "平移与缩放 {0:0.00}×   ·   旋转 {1:0.00}×   ·   死区 {2:0}/512",
                _navigationLinear.Value, _navigationAngular.Value, _navigationDeadZone.Value);
        }

        private string NavigationSettingsPath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "navigation-settings.txt"); } }
        private string FusionButtonSettingsPath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fusion-button-settings.txt"); } }

        private void LoadNavigationSettings()
        {
            try
            {
                string[] fields = File.ReadAllText(NavigationSettingsPath).Trim().Split(',');
                int target, deadZone, invert;
                double linear, angular;
                if (fields.Length != 5 || !Int32.TryParse(fields[0], out target) || target < 0 || target >= (int)NavigationTarget.FusionNative ||
                    !Double.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out linear) || !(linear >= .01 && linear <= 3) ||
                    !Double.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out angular) || !(angular >= .01 && angular <= 3) ||
                    !Int32.TryParse(fields[3], out deadZone) || deadZone < 0 || deadZone > 100 ||
                    !Int32.TryParse(fields[4], out invert) || invert < 0 || invert > 63) return;
                // The settings store the original zero-based IDs, including retired Fusion profiles.
                NavigationTarget savedTarget = target == 1 ? NavigationTarget.FusionNative : (NavigationTarget)(target + 1);
                _navigationTarget.SelectedValue = savedTarget.ToString();
                _navigationLinear.Value = linear;
                _navigationAngular.Value = angular;
                _navigationDeadZone.Value = deadZone;
                for (int i = 0; i < 6; i++) _navigationInvert[i].IsChecked = (invert & (1 << i)) != 0;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private void LoadFusionButtonSettings()
        {
            try
            {
                string[] fields = File.ReadAllText(FusionButtonSettingsPath).Trim().Split(',');
                if (fields.Length != _fusionButtonPickers.Length) return;
                for (int slot = 0; slot < fields.Length; slot++)
                {
                    int command;
                    if (!Int32.TryParse(fields[slot], out command)) continue;
                    int index = Array.IndexOf(FusionButtonOptions, (FusionViewCommand)command);
                    if (index >= 0) _fusionButtonPickers[slot].SelectedIndex = index;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private void CloseNavigation()
        {
            StopNavigation("窗口已关闭，3D 导航已停止。");
            _globalNavigation.Dispose();
            _solidWorksNavigation.Dispose();
            _fusionNativeNavigation.Dispose();
            if (_navigationBridge != null) _navigationBridge.Dispose();
            try
            {
                File.WriteAllText(NavigationSettingsPath, String.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3},{4}",
                    (int)SelectedNavigationTarget - 1, _navigationLinear.Value, _navigationAngular.Value,
                    (int)_navigationDeadZone.Value, NavigationInvertMask()));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            try
            {
                string[] commands = Array.ConvertAll(_fusionButtonCommands, command => ((int)command).ToString(CultureInfo.InvariantCulture));
                File.WriteAllText(FusionButtonSettingsPath, String.Join(",", commands));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
