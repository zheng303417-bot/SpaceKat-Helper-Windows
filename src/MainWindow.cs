using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Path = System.IO.Path;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace SpaceKatMonitor
{
    public partial class MainWindow : Window
    {
        private sealed class AxisCard
        {
            public Border Panel;
            public TextBlock Value;
            public Grid Track;
            public Border Negative;
            public Border Positive;
        }

        private sealed class ButtonCard
        {
            public Border Panel;
            public Ellipse Dot;
            public TextBlock State;
            public TextBlock Count;
            public ComboBox Mapping;
            public ComboBox MouseAction;
            public Button Learn;
            public Border TestPanel;
            public Ellipse TestDot;
            public TextBlock TestState;
            public TextBlock TestCount;
            public TextBlock TestLogical;
            public int LogicalBit;
            public long PressCount;
            public DateTime FlashUntil;
        }

        private static readonly Brush Ink = ColorBrush("#193138");
        private static readonly Brush Muted = ColorBrush("#71868C");
        private static readonly Brush Line = ColorBrush("#DFE8EA");
        private static readonly Brush Orange = ColorBrush("#B5751E");
        private static readonly Brush QuietDot = ColorBrush("#A9B8BD");
        private static readonly Brush ErrorBrush = ColorBrush("#B64D47");
        private readonly UserControl _root;
        private readonly ComboBox _deviceSelector;
        private readonly TextBlock _noDeviceText;
        private readonly TextBlock _statusText;
        private readonly Ellipse _statusDot;
        private readonly TextBlock _rateText;
        private readonly TextBlock _batteryText;
        private readonly Rectangle _batteryFill;
        private readonly BatteryMonitor _battery = new BatteryMonitor(
            path => Task.Run(() => BluetoothBattery.ReadPercent(path)));
        private readonly TextBlock _rawReportText;
        private readonly TextBlock _buttonMaskText;
        private readonly TextBlock _buttonHelpText;
        private readonly TextBlock _mappingHelpText;
        private readonly TextBlock _footerText;
        private readonly Button _demoButton;
        private readonly Button _copyButton;
        private readonly Grid _testPage;
        private readonly Grid _mappingPage;
        private readonly Button _testNav;
        private readonly Button _mappingNav;
        private readonly TextBlock _pageTitle;
        private readonly TextBlock _pageDescription;
        private readonly TextBlock _globalMouseStatus;
        private readonly Button _globalStop;
        private readonly SixAxisCubeView _cubeView;
        private string _currentPage = "test";
        private readonly Button _mouseToggle;
        private readonly TextBlock _mouseStatus;
        private readonly TextBlock _mouseButtonStatus;
        private readonly Button _releaseLeftButton;
        private readonly TextBlock _mouseSpeedText;
        private readonly TextBlock _mouseDeadZoneText;
        private readonly Slider _mouseSpeed;
        private readonly Slider _mouseDeadZone;
        private readonly CheckBox _mouseInvertX;
        private readonly CheckBox _mouseInvertY;
        private readonly MouseMotionModel _mouseMotion = new MouseMotionModel();
        private readonly MouseControlSession _mouseSession = new MouseControlSession();
        private readonly MouseButtonModel _mouseButtons = new MouseButtonModel();
        private readonly Stopwatch _mouseClock = Stopwatch.StartNew();
        private readonly DispatcherTimer _mouseTimer;
        private MouseOutput _mouseOutput;
        private bool _mouseEnabled { get { return _mouseSession.Enabled; } }
        private double _lastAxisSeconds = Double.NegativeInfinity;
        private string _mouseMessage = "已关闭 · 开启后 X、Y 控制光标，四键执行下方映射";
        private bool _loadingMouseSettings;
        private bool _loadingMouseActions;
        private readonly CheckBox _rawButtonMode;
        private readonly TextBlock _lastButtonReportText;
        private readonly TextBox _buttonHistoryText;
        private readonly ButtonState _buttonState = new ButtonState();
        private readonly Queue<string> _buttonHistory = new Queue<string>();
        private ButtonCard _learningCard;
        private uint _learnCandidate;
        private bool _learnReady;
        private DateTime _learnStarted;
        private bool _loadingMappings;
        private string _learningFeedback;
        private string _lastButtonRaw;
        private long _buttonPacketCount;
        private readonly AxisCard[] _axisCards = new AxisCard[6];
        private readonly ButtonCard[] _buttonCards = new ButtonCard[4];
        private readonly DispatcherTimer _timer;
        private RawInputService _input;
        private HwndSource _source;
        private HidDevice _selected;
        private string _preferredPath;
        private bool _refreshingDevices;
        private bool _closing;
        private bool _demo;
        private bool _axesKnown;
        private bool _buttonsKnown;
        private short[] _axes = new short[6];
        private uint _buttons;
        private long _packetCount;
        private long _recognizedCount;
        private long _rateStartCount;
        private double _packetRate;
        private string _lastRaw;
        private string _lastReportKind;
        private string _error;
        private DateTime _lastReceived;
        private DateTime _rateStart;
        private DateTime _demoStart;
        private DateTime _nextRefresh;
        private DateTime _copyFeedbackUntil;

        public MainWindow(bool demo)
        {
            Title = Program.ProductName + " 1.16";
            using (Stream icon = typeof(MainWindow).Assembly.GetManifestResourceStream("SpaceKat.Joystick.ico"))
                Icon = BitmapFrame.Create(icon, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            Width = 1180;
            Height = Math.Min(960, Math.Max(650, SystemParameters.WorkArea.Height - 50));
            MinWidth = 980;
            MinHeight = 650;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = ColorBrush("#F4F7F8");
            FontFamily = new FontFamily("Microsoft YaHei UI");
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MainWindow.xaml"))
            {
                if (stream == null)
                    throw new InvalidOperationException("缺少界面资源 MainWindow.xaml。");
                _root = (UserControl)XamlReader.Load(stream);
            }
            Content = _root;
            _deviceSelector = Find<ComboBox>("DeviceSelector");
            _noDeviceText = Find<TextBlock>("NoDeviceText");
            _statusText = Find<TextBlock>("StatusText");
            _statusDot = Find<Ellipse>("StatusDot");
            _rateText = Find<TextBlock>("RateText");
            _batteryText = Find<TextBlock>("BatteryText");
            _batteryFill = Find<Rectangle>("BatteryFill");
            _rawReportText = Find<TextBlock>("RawReportText");
            _buttonMaskText = Find<TextBlock>("ButtonMaskText");
            _buttonHelpText = Find<TextBlock>("ButtonHelpText");
            _mappingHelpText = Find<TextBlock>("MappingHelpText");
            _footerText = Find<TextBlock>("FooterText");
            _demoButton = Find<Button>("DemoButton");
            _copyButton = Find<Button>("CopyButton");
            _testPage = Find<Grid>("TestPage");
            _mappingPage = Find<Grid>("MappingPage");
            _testNav = Find<Button>("TestNavButton");
            _mappingNav = Find<Button>("MappingNavButton");
            _pageTitle = Find<TextBlock>("PageTitleText");
            _pageDescription = Find<TextBlock>("PageDescriptionText");
            _globalMouseStatus = Find<TextBlock>("GlobalMouseStatusText");
            _globalStop = Find<Button>("GlobalStopButton");
            _testNav.Click += delegate { ShowPage("test"); };
            _mappingNav.Click += delegate { ShowPage("mapping"); };
            _globalStop.Click += delegate
            {
                StopMouseControl("已停止鼠标模拟并尝试释放按键。");
                StopJoystick("已停止摇杆模拟，右摇杆已回中。");
                if (_mouseOutput == null || !_mouseOutput.HasPendingRelease)
                    _mouseMessage = "鼠标模拟已关闭 · 程序按住的鼠标键已释放";
                Render(DateTime.UtcNow);
            };
            _cubeView = new SixAxisCubeView();
            Find<Border>("CubeHost").Child = _cubeView;
            Find<Button>("ResetCubeButton").Click += delegate { _cubeView.ResetView(); };
            _mouseToggle = Find<Button>("MouseToggleButton");
            _mouseStatus = Find<TextBlock>("MouseStatusText");
            _mouseButtonStatus = Find<TextBlock>("MouseButtonStatusText");
            _releaseLeftButton = Find<Button>("ReleaseLeftButton");
            _releaseLeftButton.Click += delegate
            {
                if (_mouseEnabled) SendMouseButtons(_mouseButtons.ReleaseLeftHold());
                else if (_mouseOutput != null)
                {
                    _mouseOutput.Disarm();
                    if (!String.IsNullOrEmpty(_mouseOutput.LastReleaseError))
                        _mouseMessage = _mouseOutput.LastReleaseError;
                    else _mouseMessage = "鼠标模拟已关闭 · 程序按住的鼠标键已释放";
                }
                Render(DateTime.UtcNow);
            };
            _mouseSpeedText = Find<TextBlock>("MouseSpeedText");
            _mouseDeadZoneText = Find<TextBlock>("MouseDeadZoneText");
            _mouseSpeed = Find<Slider>("MouseSpeedSlider");
            _mouseDeadZone = Find<Slider>("MouseDeadZoneSlider");
            _mouseInvertX = Find<CheckBox>("MouseInvertX");
            _mouseInvertY = Find<CheckBox>("MouseInvertY");
            _mouseTimer = new DispatcherTimer(DispatcherPriority.Input);
            _mouseTimer.Interval = TimeSpan.FromMilliseconds(16);
            _mouseTimer.Tick += MouseTick;
            _mouseToggle.Click += delegate { if (_mouseEnabled) StopMouseControl("鼠标模拟已关闭。"); else StartMouseControl(); };
            _mouseSpeed.ValueChanged += delegate { UpdateMouseSettings(); };
            _mouseDeadZone.ValueChanged += delegate { UpdateMouseSettings(); };
            _mouseInvertX.Click += delegate { UpdateMouseSettings(); };
            _mouseInvertY.Click += delegate { UpdateMouseSettings(); };
            LoadMouseSettings();
            UpdateMouseSettings();
            InitializeMouseWheel();
            PreviewKeyDown += delegate(object sender, KeyEventArgs args)
            {
                if ((_mouseEnabled || _joystick.Enabled) && args.Key == Key.Escape)
                {
                    StopMouseControl("已按 Esc 停止鼠标模拟。");
                    StopJoystick("已按 Esc 停止摇杆模拟。");
                    args.Handled = true;
                }
            };
            _rawButtonMode = Find<CheckBox>("RawButtonMode");
            _lastButtonReportText = Find<TextBlock>("LastButtonReportText");
            _buttonHistoryText = Find<TextBox>("ButtonHistoryText");

            BuildAxisCards(Find<UniformGrid>("AxesHost"));
            BuildButtonCards(Find<UniformGrid>("ButtonsHost"));
            BuildTestButtonCards(Find<UniformGrid>("TestButtonsHost"));
            LoadMappings();
            LoadMouseActions();
            _rawButtonMode.Click += delegate
            {
                StopMouseControl("按键读取方式已修改，鼠标模拟已关闭。");
                _buttons = ButtonState.DisplayMask(_buttonState.RawMask, DeclaredButtonMask(), RawButtonMode());
                Render(DateTime.UtcNow);
            };
            _deviceSelector.SelectionChanged += DeviceSelectionChanged;
            Find<Button>("RefreshButton").Click += delegate { RefreshDevices(); _battery.RefreshSoon(); };
            _demoButton.Click += delegate { SetDemo(!_demo); };
            Find<Button>("ResetCountsButton").Click += delegate
            {
                foreach (ButtonCard card in _buttonCards)
                    card.PressCount = 0;
                Render(DateTime.UtcNow);
            };
            _copyButton.Click += CopySnapshot;
            InitializeJoystick();
            InitializeSettings();
            SourceInitialized += InitializeInput;
            Closed += OnClosed;

            _timer = new DispatcherTimer(DispatcherPriority.Background);
            _timer.Interval = TimeSpan.FromMilliseconds(33);
            _timer.Tick += Tick;
            _rateStart = DateTime.UtcNow;
            _nextRefresh = DateTime.UtcNow;
            SetDemo(demo);
            Render(DateTime.UtcNow);
            _timer.Start();
            ShowPage("test");
        }

        public void ShowPage(string page)
        {
            bool joystick = String.Equals(page, "joystick", StringComparison.OrdinalIgnoreCase);
            bool mapping = String.Equals(page, "mapping", StringComparison.OrdinalIgnoreCase);
            bool settings = String.Equals(page, "settings", StringComparison.OrdinalIgnoreCase);
            if (!mapping && _currentPage == "mapping")
            {
                StopMouseControl("已切换页面，鼠标模拟已停止。");
                CancelLearning(null);
            }
            if (!joystick && _currentPage == "joystick") StopJoystick("已切换页面，右摇杆已回中。");
            if ((mapping || joystick || settings) && _currentPage == "test")
                _cubeView.SetInput(null, false, _mouseClock.Elapsed.TotalSeconds);
            _currentPage = joystick ? "joystick" : mapping ? "mapping" : settings ? "settings" : "test";
            _testPage.Visibility = _currentPage == "test" ? Visibility.Visible : Visibility.Collapsed;
            _mappingPage.Visibility = mapping ? Visibility.Visible : Visibility.Collapsed;
            _joystickPage.Visibility = joystick ? Visibility.Visible : Visibility.Collapsed;
            _settingsPage.Visibility = settings ? Visibility.Visible : Visibility.Collapsed;
            RenderNavigation();
            _pageTitle.Text = joystick ? "摇杆模拟" : mapping ? "鼠标映射" : settings ? "设置" : "设备测试";
            _pageDescription.Text = settings ? "管理自启动、主题色和配置文件"
                : joystick ? "两轴转向，Z 轴缩放；放大与缩小独立调节"
                : mapping ? "X / Y 控制光标，Z 控制滚轮，四键执行鼠标动作"
                : "用立方体观察六轴操控，实时检查原始数值与四个按键";
            if (settings) RefreshStartupSetting();
            Render(DateTime.UtcNow);
        }

        private void RenderNavigation()
        {
            SetNavStyle(_testNav, _currentPage == "test");
            SetNavStyle(_mappingNav, _currentPage == "mapping");
            SetNavStyle(_joystickNav, _currentPage == "joystick");
            SetNavStyle(_settingsNav, _currentPage == "settings");
        }

        private void SetNavStyle(Button button, bool active)
        {
            button.Background = active ? Accent : Brushes.Transparent;
            button.Foreground = active ? Brushes.White : ColorBrush("#A8C1C7");
        }

        private T Find<T>(string name) where T : FrameworkElement
        {
            T control = _root.FindName(name) as T;
            if (control == null)
                throw new InvalidOperationException("缺少界面控件：" + name);
            return control;
        }

        private static Brush ColorBrush(string color)
        {
            SolidColorBrush brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze();
            return brush;
        }

        private void BuildAxisCards(UniformGrid host)
        {
            string[] names = { "X", "Y", "Z", "Rx", "Ry", "Rz" };
            string[] captions = { "平移 / X", "平移 / Y", "平移 / Z", "旋转 / X", "旋转 / Y", "旋转 / Z" };
            for (int i = 0; i < _axisCards.Length; i++)
            {
                AxisCard card = new AxisCard();
                card.Panel = new Border
                {
                    Background = Brushes.White, BorderBrush = Line, BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10, 14, 9), Margin = new Thickness(5, 0, 5, 10)
                };
                Grid content = new Grid();
                content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid heading = new Grid();
                TextBlock axisName = new TextBlock { Text = names[i], FontSize = 13, FontWeight = FontWeights.SemiBold };
                axisName.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
                heading.Children.Add(axisName);
                heading.Children.Add(new TextBlock { Text = captions[i], FontSize = 10, Foreground = Muted, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center });
                content.Children.Add(heading);
                card.Value = new TextBlock { Text = "—", FontFamily = new FontFamily("Consolas"), FontSize = 30, Foreground = Ink, Margin = new Thickness(0, 5, 0, 5) };
                Grid.SetRow(card.Value, 1);
                content.Children.Add(card.Value);
                card.Track = new Grid { Height = 7, Background = ColorBrush("#EDF3F4"), ClipToBounds = true };
                card.Track.ColumnDefinitions.Add(new ColumnDefinition());
                card.Track.ColumnDefinitions.Add(new ColumnDefinition());
                card.Negative = new Border { Background = ColorBrush("#58A7B8"), Width = 0, HorizontalAlignment = HorizontalAlignment.Right };
                card.Positive = new Border { Width = 0, HorizontalAlignment = HorizontalAlignment.Left };
                card.Positive.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                Grid.SetColumn(card.Positive, 1);
                card.Track.Children.Add(card.Negative);
                card.Track.Children.Add(card.Positive);
                Border centerLine = new Border { Background = ColorBrush("#9DB3BA"), Width = 1, HorizontalAlignment = HorizontalAlignment.Center };
                Grid.SetColumnSpan(centerLine, 2);
                card.Track.Children.Add(centerLine);
                Grid.SetRow(card.Track, 2);
                content.Children.Add(card.Track);
                Grid labels = new Grid { Margin = new Thickness(0, 5, 0, 0) };
                labels.Children.Add(new TextBlock { Text = "−512", FontSize = 9, Foreground = QuietDot, FontFamily = new FontFamily("Consolas") });
                labels.Children.Add(new TextBlock { Text = "0", FontSize = 9, Foreground = QuietDot, HorizontalAlignment = HorizontalAlignment.Center, FontFamily = new FontFamily("Consolas") });
                labels.Children.Add(new TextBlock { Text = "+511", FontSize = 9, Foreground = QuietDot, HorizontalAlignment = HorizontalAlignment.Right, FontFamily = new FontFamily("Consolas") });
                Grid.SetRow(labels, 3);
                content.Children.Add(labels);
                card.Panel.Child = content;
                host.Children.Add(card.Panel);
                _axisCards[i] = card;
            }
        }

        private void BuildButtonCards(UniformGrid host)
        {
            for (int i = 0; i < _buttonCards.Length; i++)
            {
                ButtonCard card = new ButtonCard();
                card.LogicalBit = i;
                card.Panel = new Border
                {
                    Background = Brushes.White, BorderBrush = Line, BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(5, 0, 5, 0)
                };
                StackPanel content = new StackPanel();
                Grid heading = new Grid();
                heading.Children.Add(new TextBlock { Text = "按键 " + (i + 1).ToString(CultureInfo.InvariantCulture), FontSize = 12, FontWeight = FontWeights.SemiBold });
                card.Dot = new Ellipse { Width = 8, Height = 8, Fill = QuietDot, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                heading.Children.Add(card.Dot);
                content.Children.Add(heading);
                card.State = new TextBlock { Text = "等待数据", FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Muted, Margin = new Thickness(0, 8, 0, 4) };
                content.Children.Add(card.State);
                card.Count = new TextBlock { Text = "触发 0 次", FontSize = 11, Foreground = Muted, Margin = new Thickness(0, 0, 0, 9) };
                content.Children.Add(card.Count);
                Grid mappingRow = new Grid();
                mappingRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                mappingRow.ColumnDefinitions.Add(new ColumnDefinition());
                mappingRow.Children.Add(new TextBlock { Text = "逻辑键", Foreground = Muted, FontSize = 11, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
                card.Mapping = new ComboBox { Height = 28, FontSize = 11, MinWidth = 52, ToolTip = "选择此物理按键对应的固件逻辑按键编号（1–32）" };
                for (int logical = 1; logical <= 32; logical++)
                    card.Mapping.Items.Add(logical);
                card.Mapping.SelectedIndex = i;
                card.Mapping.Tag = card;
                card.Mapping.SelectionChanged += delegate(object sender, SelectionChangedEventArgs args)
                {
                    ComboBox mapping = (ComboBox)sender;
                    ButtonCard mappedCard = (ButtonCard)mapping.Tag;
                    if (!_loadingMappings) StopMouseControl("按键映射已修改，鼠标模拟已关闭。");
                    mappedCard.LogicalBit = mapping.SelectedIndex;
                    mappedCard.PressCount = 0;
                    mappedCard.FlashUntil = DateTime.MinValue;
                    SaveMappings();
                    Render(DateTime.UtcNow);
                };
                Grid.SetColumn(card.Mapping, 1);
                mappingRow.Children.Add(card.Mapping);
                content.Children.Add(mappingRow);
                card.Learn = new Button { Content = "学习此键", Height = 27, FontSize = 10,
                    Margin = new Thickness(0, 7, 0, 0), Padding = new Thickness(8, 0, 8, 0), Tag = card };
                card.Learn.Click += BeginLearning;
                content.Children.Add(card.Learn);
                content.Children.Add(new TextBlock { Text = "鼠标功能", Foreground = Muted, FontSize = 11, Margin = new Thickness(0, 10, 0, 4) });
                card.MouseAction = new ComboBox { Height = 30, FontSize = 11, Tag = card,
                    ToolTip = "开启鼠标控制后生效；每次短按触发一次。修改映射会停止模拟并释放左键。" };
                // Keep the order aligned with MouseButtonAction for stored mappings.
                foreach (string label in new[] { "无动作", "左键单击", "右键单击", "左键锁定 / 释放",
                    "左键双击", "中键单击", "后退（侧键）", "前进（侧键）", "滚轮上滚一格", "滚轮下滚一格" })
                    card.MouseAction.Items.Add(label);
                card.MouseAction.SelectedIndex = i == 0 ? 1 : i == 1 ? 2 : i == 2 ? 3 : 0;
                card.MouseAction.SelectionChanged += delegate
                {
                    if (_loadingMouseActions) return;
                    StopMouseControl("鼠标按键功能已修改，鼠标模拟已关闭。");
                    SaveMouseActions();
                    Render(DateTime.UtcNow);
                };
                content.Children.Add(card.MouseAction);
                card.Panel.Child = content;
                host.Children.Add(card.Panel);
                _buttonCards[i] = card;
            }
        }

        private void BuildTestButtonCards(UniformGrid host)
        {
            for (int i = 0; i < _buttonCards.Length; i++)
            {
                ButtonCard card = _buttonCards[i];
                card.TestPanel = new Border { Background = Brushes.White, BorderBrush = Line, BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(5, 0, 5, 0) };
                StackPanel content = new StackPanel();
                Grid heading = new Grid();
                heading.Children.Add(new TextBlock { Text = "按键 " + (i + 1), FontSize = 12, FontWeight = FontWeights.SemiBold });
                card.TestDot = new Ellipse { Width = 8, Height = 8, Fill = QuietDot, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                heading.Children.Add(card.TestDot);
                content.Children.Add(heading);
                card.TestState = new TextBlock { Text = "等待数据", FontSize = 19, FontWeight = FontWeights.SemiBold, Foreground = Muted, Margin = new Thickness(0, 7, 0, 4) };
                card.TestCount = new TextBlock { Text = "触发 0 次", FontSize = 11, Foreground = Muted };
                card.TestLogical = new TextBlock { FontSize = 10, Foreground = Muted, Margin = new Thickness(0, 6, 0, 0) };
                content.Children.Add(card.TestState);
                content.Children.Add(card.TestCount);
                content.Children.Add(card.TestLogical);
                card.TestPanel.Child = content;
                host.Children.Add(card.TestPanel);
            }
        }

        private void InitializeInput(object sender, EventArgs args)
        {
            try
            {
                _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
                _source.AddHook(WindowMessage);
                _mouseOutput = new MouseOutput(new WindowInteropHelper(this).Handle);
                _input = new RawInputService(new WindowInteropHelper(this).Handle);
                _input.ReportReceived += OnReportReceived;
                _input.DevicesChanged += OnDevicesChanged;
                RefreshDevices();
            }
            catch (Exception ex)
            {
                StopMouseControl("监听异常，鼠标模拟已停止。");
                StopJoystick("监听异常，摇杆模拟已停止。");
                _error = "无法开始监听：" + ex.Message;
            }
        }

        private IntPtr WindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x00FE && wParam.ToInt64() == 2 && _selected != null && _selected.Handle == lParam)
            {
                _battery.Reset(null, _demo);
                StopMouseControl("设备已断开，鼠标模拟已停止。");
                StopJoystick("设备已断开，右摇杆已回中。");
            }
            if (_mouseOutput != null && _mouseOutput.IsStopMessage(msg, wParam))
            {
                StopMouseControl("已按 Ctrl+Alt+F8 停止鼠标模拟。");
                StopJoystick("已按 Ctrl+Alt+F8 停止摇杆模拟。");
                handled = true;
                return IntPtr.Zero;
            }
            if (_input != null)
            {
                try { _input.ProcessMessage(msg, wParam, lParam); }
                catch (Exception ex)
                {
                    StopMouseControl("读取设备异常，鼠标模拟已停止。");
                    StopJoystick("读取设备异常，摇杆模拟已停止。");
                    _error = "读取设备失败：" + ex.Message;
                }
            }
            // WM_INPUT must continue to the default window procedure for system cleanup.
            return IntPtr.Zero;
        }

        private void OnDevicesChanged()
        {
            if (_closing) return;
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(OnDevicesChanged));
                return;
            }
            _nextRefresh = DateTime.UtcNow.AddMilliseconds(100);
        }

        private void RefreshDevices()
        {
            if (_closing) return;
            try
            {
                List<HidDevice> devices = RawInputService.EnumerateDevices();
                HidDevice target = null;
                string desiredPath = _selected != null ? _selected.Path : _preferredPath;
                if (!String.IsNullOrEmpty(desiredPath))
                {
                    foreach (HidDevice device in devices)
                    {
                        if (String.Equals(device.Path, desiredPath, StringComparison.OrdinalIgnoreCase))
                        {
                            target = device;
                            break;
                        }
                    }
                }
                else if (devices.Count > 0)
                {
                    foreach (HidDevice device in devices)
                    {
                        if (device.VendorId == 0x256F || device.VendorId == 0x046D ||
                            (!String.IsNullOrEmpty(device.Name) && device.Name.IndexOf("SpaceKat", StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            target = device;
                            break;
                        }
                    }
                    if (target == null) target = devices[0];
                }

                bool changed = (_selected == null) != (target == null) ||
                    (_selected != null && target != null && (_selected.Handle != target.Handle ||
                    !String.Equals(_selected.Path, target.Path, StringComparison.OrdinalIgnoreCase)));
                _refreshingDevices = true;
                try
                {
                    _deviceSelector.ItemsSource = devices;
                    _deviceSelector.SelectedItem = target;
                }
                finally { _refreshingDevices = false; }
                _selected = target;
                if (_selected != null)
                {
                    _preferredPath = _selected.Path;
                    _deviceSelector.ToolTip = _selected.Path;
                }
                _noDeviceText.Visibility = target == null ? Visibility.Visible : Visibility.Collapsed;
                _noDeviceText.Text = devices.Count == 0 ? "未找到 3D 鼠标" : "选择要监视的 3D 鼠标";
                if (changed && !_demo)
                    ClearReadings();
                if (_input != null)
                    _error = null;
            }
            catch (Exception ex)
            {
                StopMouseControl("读取设备列表异常，鼠标模拟已停止。");
                StopJoystick("读取设备列表异常，摇杆模拟已停止。");
                _error = "查找设备失败：" + ex.Message;
            }
            _nextRefresh = DateTime.UtcNow.AddSeconds(2);
        }

        private void DeviceSelectionChanged(object sender, SelectionChangedEventArgs args)
        {
            if (_refreshingDevices) return;
            _selected = _deviceSelector.SelectedItem as HidDevice;
            if (_selected != null)
            {
                _preferredPath = _selected.Path;
                _deviceSelector.ToolTip = _selected.Path;
            }
            _noDeviceText.Visibility = _selected == null ? Visibility.Visible : Visibility.Collapsed;
            if (!_demo)
                ClearReadings();
        }

        private void OnReportReceived(HidDevice device, byte[] report)
        {
            if (_closing || _demo || _selected == null || device.Handle != _selected.Handle) return;
            if (!Dispatcher.CheckAccess())
            {
                byte[] copy = (byte[])report.Clone();
                Dispatcher.BeginInvoke(new Action(delegate { OnReportReceived(device, copy); }));
                return;
            }
            AcceptReport(report, DateTime.UtcNow);
        }

        private void AcceptReport(byte[] report, DateTime now)
        {
            if (report == null || report.Length == 0) return;
            _packetCount++;
            _lastReceived = now;
            _lastRaw = BitConverter.ToString(report, 0, Math.Min(report.Length, 64)).Replace('-', ' ');
            if (report.Length > 64) _lastRaw += " …";
            DecodedReport decoded;
            if (!ReportDecoder.TryDecode(report, out decoded))
            {
                _lastReportKind = "未知报告";
                return;
            }
            _recognizedCount++;
            if (decoded.Axes != null && decoded.Axes.Length >= 6)
            {
                Array.Copy(decoded.Axes, _axes, 6);
                _axesKnown = true;
                _lastReportKind = "六轴输入";
                if (_currentPage == "joystick" && _joystick.Enabled)
                    _joystick.Update(decoded.Axes, _mouseClock.Elapsed.TotalSeconds);
                if (_currentPage == "test")
                    _cubeView.SetInput(decoded.Axes, true, _mouseClock.Elapsed.TotalSeconds);
                if (!_demo)
                {
                    _lastAxisSeconds = _mouseClock.Elapsed.TotalSeconds;
                    if (_mouseEnabled)
                    {
                        _mouseSession.ObserveXY(_lastAxisSeconds);
                        UpdateMouseAxes(decoded.Axes, _lastAxisSeconds);
                    }
                }
            }
            if (decoded.Buttons.HasValue)
            {
                _buttonPacketCount++;
                bool changed = !_buttonState.Known || _buttonState.RawMask != decoded.Buttons.Value;
                _buttonState.Apply(decoded.Buttons.Value);
                _lastButtonRaw = _lastRaw;
                if (changed)
                    RecordButtonEvent(now, _lastRaw, decoded.Buttons.Value);
                uint nextButtons = ButtonState.DisplayMask(_buttonState.RawMask, DeclaredButtonMask(), RawButtonMode());
                uint previousButtons = _buttonsKnown ? _buttons : 0;
                uint rising = nextButtons & ~previousButtons;
                foreach (ButtonCard card in _buttonCards)
                {
                    if (card.LogicalBit >= 0 && (rising & (1u << card.LogicalBit)) != 0)
                    {
                        card.PressCount++;
                        card.FlashUntil = now.AddMilliseconds(220);
                    }
                }
                _buttons = nextButtons;
                _buttonsKnown = true;
                _lastReportKind = "按键输入";
                UpdateLearning(now);
                ProcessJoystickToggle(nextButtons);
                ProcessJoystickButtons(nextButtons);
                if (!_demo && _mouseEnabled && _learningCard == null)
                {
                    try { SendMouseButtons(_mouseButtons.Process(nextButtons)); }
                    catch (Exception error) { StopMouseControl("鼠标按键模拟已停止：" + error.Message); }
                }
            }
        }

        private void ClearReadings()
        {
            _battery.Reset(_selected == null ? null : _selected.Path, _demo);
            StopMouseControl("设备或模式已切换，鼠标模拟已关闭。");
            StopJoystick("设备或模式已切换，右摇杆已回中。");
            _lastAxisSeconds = Double.NegativeInfinity;
            _cubeView.ResetView();
            _cubeView.SetInput(null, false, _mouseClock.Elapsed.TotalSeconds);
            _axesKnown = false;
            _buttonsKnown = false;
            _axes = new short[6];
            _buttons = 0;
            _buttonState.Reset();
            _buttonHistory.Clear();
            _lastButtonRaw = null;
            _buttonPacketCount = 0;
            CancelLearning(null);
            _packetCount = 0;
            _recognizedCount = 0;
            _rateStartCount = 0;
            _packetRate = 0;
            _lastRaw = null;
            _lastReportKind = null;
            _lastReceived = DateTime.MinValue;
            _rateStart = DateTime.UtcNow;
            foreach (ButtonCard card in _buttonCards)
            {
                card.PressCount = 0;
                card.FlashUntil = DateTime.MinValue;
            }
        }

        private void SetDemo(bool enabled)
        {
            _demo = enabled;
            ClearReadings();
            _demoStart = DateTime.UtcNow;
            _demoButton.Content = enabled ? "退出演示" : "演示模式";
            _deviceSelector.IsEnabled = !enabled;
            _footerText.Text = enabled
                ? "演示数据：轴值与按键均由程序模拟，不代表设备读数。"
                : "显示设备实际发送的六轴数值；按键状态取决于固件上报内容。";
            _footerText.Foreground = enabled ? Orange : ColorBrush("#82969C");
        }

        private void UpdateDemo(DateTime now)
        {
            double t = (now - _demoStart).TotalSeconds;
            byte[] axisReport = new byte[13];
            axisReport[0] = 1;
            for (int i = 0; i < 6; i++)
            {
                short value = (short)Math.Round(Math.Sin(t * (0.65 + i * 0.13) + i * 0.68) * (430 - i * 25));
                axisReport[1 + i * 2] = (byte)(value & 255);
                axisReport[2 + i * 2] = (byte)((value >> 8) & 255);
            }
            AcceptReport(axisReport, now);
            byte[] buttonReport = new byte[5];
            buttonReport[0] = 3;
            int slot = ((int)(t / 1.2)) % 4;
            if (t % 1.2 < 0.3)
            {
                int logical = _buttonCards[slot].LogicalBit;
                uint mask = logical >= 0 ? 1u << logical : 0;
                for (int i = 0; i < 4; i++)
                    buttonReport[1 + i] = (byte)(mask >> (8 * i));
            }
            AcceptReport(buttonReport, now);
        }

        private void Tick(object sender, EventArgs args)
        {
            if (_closing) return;
            DateTime now = DateTime.UtcNow;
            if (now >= _nextRefresh)
                RefreshDevices();
            _battery.Step(_mouseClock.Elapsed.TotalSeconds);
            if (_demo)
                UpdateDemo(now);
            if (_currentPage == "test")
                _cubeView.Advance(_mouseClock.Elapsed.TotalSeconds);
            if (_learningCard != null && (now - _learnStarted).TotalSeconds > 40)
                CancelLearning("学习超时：未收到独立的按下和释放，请检查下面的按键事件。");
            double elapsed = (now - _rateStart).TotalSeconds;
            if (elapsed >= 1)
            {
                _packetRate = (_packetCount - _rateStartCount) / elapsed;
                _rateStartCount = _packetCount;
                _rateStart = now;
            }
            Render(now);
        }

        private void Render(DateTime now)
        {
            _batteryText.Text = _battery.Text;
            int? batteryPercent = _battery.Percent;
            Brush batteryBrush = !batteryPercent.HasValue ? Muted : batteryPercent.Value <= 20 ? Orange : Accent;
            _batteryText.Foreground = batteryBrush;
            _batteryFill.Fill = batteryBrush;
            _batteryFill.Width = 13 * batteryPercent.GetValueOrDefault() / 100.0;
            _mouseToggle.Content = _mouseEnabled ? "停止鼠标控制" : "开启鼠标控制";
            _mouseToggle.Background = _mouseEnabled ? ErrorBrush : Accent;
            _mouseToggle.IsEnabled = !_demo && _selected != null;
            _mouseStatus.Text = _demo ? "演示模式仅展示数据，不发送鼠标动作。" : _mouseMessage;
            _mouseStatus.Foreground = _mouseEnabled ? Accent : Muted;
            bool pendingRelease = _mouseOutput != null && _mouseOutput.HasPendingRelease;
            _globalStop.IsEnabled = _mouseEnabled || pendingRelease || _joystick.Enabled || _gamepadOutput.HasPendingStop;
            _globalStop.Content = _gamepadOutput.HasPendingStop ? "重试停止输出" : !_mouseEnabled && pendingRelease ? "重试释放按键" : "停止并释放";
            _globalMouseStatus.Text = _gamepadOutput.HasPendingStop ? "虚拟手柄待停止" : _joystick.Enabled ? (_gamepadOutput.IsConnected ? "虚拟手柄已开启" : "摇杆预览已开启") : pendingRelease && !_mouseEnabled ? "鼠标按键待释放"
                : _mouseButtons.IsLeftHeld ? "左键已锁定"
                : _mouseEnabled ? "鼠标控制已开启" : "所有模拟已关闭";
            _globalMouseStatus.Foreground = _gamepadOutput.HasPendingStop || _mouseButtons.IsLeftHeld || (pendingRelease && !_mouseEnabled)
                ? ColorBrush("#FFC882") : _mouseEnabled || _joystick.Enabled ? ColorBrush("#71DBCB") : ColorBrush("#A8C1C7");
            RenderJoystick();
            RenderMouseWheel();
            _releaseLeftButton.IsEnabled = _mouseButtons.IsLeftHeld || pendingRelease;
            _releaseLeftButton.Content = !_mouseEnabled && pendingRelease ? "重试释放" : "释放左键";
            _mouseButtonStatus.Text = !_mouseEnabled && pendingRelease
                ? "仍有鼠标按键待释放 · 请点“重试释放”，或按下并松开实际鼠标按键"
                : _mouseButtons.IsLeftHeld ? "左键已锁定 · 再按同一锁定键松开，也可点“释放左键”"
                : _mouseEnabled ? "左键未锁定 · 四个按键按下方“鼠标功能”设置执行"
                : "左键未锁定 · 开启鼠标控制后，四键映射生效";
            _mouseButtonStatus.Foreground = _mouseButtons.IsLeftHeld || pendingRelease ? Orange : Muted;
            for (int i = 0; i < _axisCards.Length; i++)
            {
                AxisCard card = _axisCards[i];
                if (card == null) continue;
                card.Value.Text = _axesKnown ? _axes[i].ToString(CultureInfo.InvariantCulture) : "—";
                card.Value.Foreground = _axesKnown ? Ink : QuietDot;
                double half = Math.Max(0, card.Track.ActualWidth / 2);
                card.Negative.Width = _axesKnown && _axes[i] < 0 ? Math.Min(1, -_axes[i] / 512.0) * half : 0;
                card.Positive.Width = _axesKnown && _axes[i] > 0 ? Math.Min(1, _axes[i] / 511.0) * half : 0;
            }
            foreach (ButtonCard card in _buttonCards)
            {
                if (card == null) continue;
                uint bit = card.LogicalBit >= 0 ? 1u << card.LogicalBit : 0;
                bool declared = (DeclaredButtonMask() & bit) != 0;
                bool observed = (_buttonState.ObservedMask & bit) != 0;
                bool supported = declared || (RawButtonMode() && observed);
                bool pressed = supported && _buttonsKnown && card.LogicalBit >= 0 && (_buttons & (1u << card.LogicalBit)) != 0;
                bool highlighted = pressed || now < card.FlashUntil;
                card.Panel.Background = highlighted ? HighlightBackground : Brushes.White;
                card.Panel.BorderBrush = highlighted ? HighlightBorder : Line;
                card.Dot.Fill = highlighted ? Accent : QuietDot;
                card.State.Text = !supported ? (RawButtonMode() ? "未观察到" : "未声明") : !_buttonsKnown ? "等待数据" : pressed ? "按下" : highlighted ? "刚触发" : "释放";
                card.State.Foreground = highlighted ? Accent : _buttonsKnown && supported ? Ink : Muted;
                card.Count.Text = "触发 " + card.PressCount.ToString(CultureInfo.InvariantCulture) + " 次";
                card.State.ToolTip = declared ? "固件报告描述符声明的逻辑按键" : observed
                    ? "描述符未声明此位；当前依据原始按键报文观察。可通过学习核对物理按键。"
                    : "尚未观察到此原始位。点击学习此键，再短按并释放对应物理键。";
                if (card.TestPanel != null)
                {
                    card.TestPanel.Background = card.Panel.Background;
                    card.TestPanel.BorderBrush = card.Panel.BorderBrush;
                    card.TestDot.Fill = card.Dot.Fill;
                    card.TestState.Text = card.State.Text;
                    card.TestState.Foreground = card.State.Foreground;
                    card.TestState.ToolTip = card.State.ToolTip;
                    card.TestCount.Text = card.Count.Text;
                    card.TestLogical.Text = "逻辑键 " + (card.LogicalBit + 1).ToString(CultureInfo.InvariantCulture);
                }
            }

            if (_demo)
                SetStatus("演示中 · 模拟信号", Orange);
            else if (!String.IsNullOrEmpty(_error))
                SetStatus(_error, ErrorBrush);
            else if (_selected == null)
                SetStatus(String.IsNullOrEmpty(_preferredPath) ? "等待连接 · 请插入 USB 或配对蓝牙" : "设备已断开 · 等待重新连接", Muted);
            else if (_packetCount == 0)
                SetStatus("已连接" + ConnectionLabel() + " · 等待设备数据，请移动操纵帽或按键", Accent);
            else if (_recognizedCount == 0)
                SetStatus("已连接 · 收到数据，尚未识别六轴或按键报告", Orange);
            else if ((now - _lastReceived).TotalSeconds >= 2)
                SetStatus("已连接 · 暂无新数据，显示最近读数", Muted);
            else
                SetStatus("实时接收" + ConnectionLabel() + " · " + (_lastReportKind ?? "设备数据"), Accent);
            _rateText.Text = _packetRate.ToString("0", CultureInfo.InvariantCulture) + " Hz  ·  " + _packetCount.ToString("N0", CultureInfo.InvariantCulture) + " 帧";
            _rawReportText.Text = _lastRaw ?? "等待设备发送数据…";
            _buttonMaskText.Text = _buttonsKnown
                ? "原始位图：0x" + _buttonState.RawMask.ToString("X8", CultureInfo.InvariantCulture) + "    当前显示：" + PressedButtonList(_buttons)
                : "按键位图：—    已按下：尚未收到按键报告";
            _lastButtonReportText.Text = "最后按键帧：" + (_lastButtonRaw ?? "尚未收到") +
                "    已观察到：" + PressedButtonList(_buttonState.ObservedMask);
            _buttonHistoryText.Text = _buttonHistory.Count > 0 ? String.Join(Environment.NewLine, _buttonHistory.ToArray()) : "等待按键事件；此处独立保存，不被六轴刷新覆盖。";
            if (!_demo)
            {
                int supportedCount = _selected != null && _selected.HasButtonCaps ? CountBits(_selected.ValidButtonsMask) : 32;
                _buttonHelpText.Text = _learningCard != null ? "正在学习按键 " + (Array.IndexOf(_buttonCards, _learningCard) + 1) + "：先松开所有键，再短按并释放对应物理键。"
                    : !String.IsNullOrEmpty(_learningFeedback) ? _learningFeedback
                    : supportedCount < 4 ? "描述符声明 " + supportedCount + " 个逻辑键；兼容读取保留原始位，请逐键学习实际对应关系。"
                    : "逐个点击“学习此键”，再短按并释放对应物理键；短脉冲会高亮并计数。";
                _buttonHelpText.Foreground = _learningCard != null || supportedCount < 4 ? Orange : Muted;
            }
            else
            {
                _buttonHelpText.Text = "演示按键：依次模拟四键触发；短脉冲也会高亮并计数。";
                _buttonHelpText.Foreground = Orange;
            }
            _mappingHelpText.Text = _learningCard != null || !String.IsNullOrEmpty(_learningFeedback)
                ? _buttonHelpText.Text : _demo ? "演示仅展示四键信号；退出演示后可开启鼠标控制。"
                : "先选择四键的鼠标功能，再开启鼠标控制。需要核对物理位置时，点击“学习此键”并短按对应按键。";
            _mappingHelpText.Foreground = _learningCard != null || !String.IsNullOrEmpty(_learningFeedback) || _demo ? Orange : Muted;
            if (now >= _copyFeedbackUntil)
                _copyButton.Content = "复制当前值";
        }

        private void SetStatus(string text, Brush brush)
        {
            _statusText.Text = text;
            _statusText.Foreground = brush;
            _statusDot.Fill = brush;
        }

        private string ConnectionLabel()
        {
            if (_selected == null || String.IsNullOrEmpty(_selected.Path)) return "";
            string path = _selected.Path;
            if (BluetoothBattery.IsBluetoothHidPath(path))
                return "（蓝牙）";
            if (path.IndexOf("VID_", StringComparison.OrdinalIgnoreCase) >= 0)
                return "（USB / HID）";
            return "";
        }

        private static int CountBits(uint mask)
        {
            int count = 0;
            while (mask != 0)
            {
                mask &= mask - 1;
                count++;
            }
            return count;
        }

        private static string PressedButtonList(uint mask)
        {
            List<string> numbers = new List<string>();
            for (int i = 0; i < 32; i++)
                if ((mask & (1u << i)) != 0)
                    numbers.Add((i + 1).ToString(CultureInfo.InvariantCulture));
            return numbers.Count == 0 ? "无" : String.Join(", ", numbers.ToArray());
        }

        private void CopySnapshot(object sender, RoutedEventArgs args)
        {
            try
            {
                Clipboard.SetText(GetSnapshot());
                _copyButton.Content = "已复制";
                _copyFeedbackUntil = DateTime.UtcNow.AddSeconds(1.5);
            }
            catch (Exception)
            {
                _copyButton.Content = "复制失败，请重试";
                _copyFeedbackUntil = DateTime.UtcNow.AddSeconds(2);
            }
        }

        private void StartMouseControl()
        {
            if (_gamepadOutput.HasPendingStop)
            {
                StopJoystick("已请求停止虚拟手柄输出。");
                if (_gamepadOutput.HasPendingStop)
                {
                    _mouseMessage = "上一虚拟手柄输出尚未停止：" + _gamepadOutput.LastError;
                    Render(DateTime.UtcNow);
                    return;
                }
            }
            if (_currentPage != "mapping" || _demo || _selected == null || _mouseOutput == null || _input == null)
            {
                _mouseMessage = "请先连接设备并等待监听就绪。";
                Render(DateTime.UtcNow);
                return;
            }
            int[] bits = new int[4];
            MouseButtonAction[] actions = new MouseButtonAction[4];
            for (int i = 0; i < 4; i++)
            {
                bits[i] = _buttonCards[i].LogicalBit;
                actions[i] = (MouseButtonAction)_buttonCards[i].MouseAction.SelectedIndex;
            }
            CancelLearning(null);
            try
            {
                _mouseButtons.Configure(bits, actions);
                _mouseOutput.Arm();
            }
            catch (Exception error)
            {
                StopMouseControl("无法开启鼠标模拟：" + error.Message);
                _mouseMessage = "无法开启鼠标模拟：" + error.Message +
                    (String.IsNullOrEmpty(_mouseOutput.LastReleaseError) ? "" : " " + _mouseOutput.LastReleaseError);
                Render(DateTime.UtcNow);
                return;
            }
            if (!_mouseOutput.StopHotkeyAvailable)
            {
                _mouseOutput.Disarm();
                _mouseMessage = !String.IsNullOrEmpty(_mouseOutput.LastReleaseError) ? _mouseOutput.LastReleaseError
                    : "Ctrl+Alt+F8 已被占用，未开启鼠标模拟；关闭占用此快捷键的程序后重试。";
                Render(DateTime.UtcNow);
                return;
            }
            ResetMouseAxes();
            _mouseButtons.Enable(_buttonsKnown ? _buttons : 0);
            _mouseSession.Enable();
            _mouseMessage = "已开启 · 等待新六轴数据，X / Y 控制光标，Z 控制滚轮";
            SaveMouseSettings();
            SaveMouseActions();
            _mouseTimer.Start();
            Render(DateTime.UtcNow);
        }

        private void StopMouseControl(string reason)
        {
            bool wasEnabled = _mouseEnabled;
            _mouseSession.Disable();
            _mouseButtons.Disable();
            _mouseTimer.Stop();
            ResetMouseAxes();
            if (_mouseOutput != null) _mouseOutput.Disarm();
            if (wasEnabled)
            {
                _mouseMessage = reason;
                SaveMouseSettings();
            }
            if (_mouseOutput != null && !String.IsNullOrEmpty(_mouseOutput.LastReleaseError))
                _mouseMessage = reason + " " + _mouseOutput.LastReleaseError;
        }

        private void SendMouseButtons(MouseButtonSignal[] signals)
        {
            if (!_mouseEnabled || signals == null || signals.Length == 0 || _mouseOutput == null) return;
            string error;
            if (!_mouseOutput.TryButtons(signals, out error))
                StopMouseControl("鼠标按键模拟已停止：" + error);
        }

        private void MouseTick(object sender, EventArgs args)
        {
            if (_closing || !_mouseEnabled) return;
            double now = _mouseClock.Elapsed.TotalSeconds;
            if (_demo || _selected == null)
            {
                StopMouseControl("设备或模式已切换，鼠标模拟已停止。");
                return;
            }
            if (_mouseSession.GetPhase(now) != MouseControlPhase.Active)
            {
                // A quiet controller or a brief Bluetooth gap is not a user
                // request to turn the feature off. Never replay stale motion;
                // retain the enabled state and wait for a fresh real XY frame.
                ResetMouseAxes();
                _mouseMessage = "已开启 · 等待新的轴数据，光标与滚轮暂停，按键仍生效 · Ctrl+Alt+F8 停止";
                return;
            }
            _mouseMessage = "控制中 · X / Y 平移、Z 滚轮与四键映射 · Ctrl+Alt+F8 停止";
            try
            {
                MouseDelta delta = _mouseMotion.Step(now);
                int wheelDelta = _mouseWheel.Step(now);
                string error;
                if (!_mouseOutput.TryMove(delta.X, delta.Y, out error))
                { StopMouseControl("鼠标模拟已停止：" + error); return; }
                if (!_mouseOutput.TryScroll(wheelDelta, out error))
                    StopMouseControl("滚轮模拟已停止：" + error);
            }
            catch (Exception error)
            {
                StopMouseControl("鼠标模拟已停止：" + error.Message);
            }
        }

        private void UpdateMouseSettings()
        {
            _mouseMotion.Speed = _mouseSpeed.Value;
            _mouseMotion.DeadZone = (int)_mouseDeadZone.Value;
            _mouseMotion.InvertX = _mouseInvertX.IsChecked == true;
            _mouseMotion.InvertY = _mouseInvertY.IsChecked == true;
            _mouseSpeedText.Text = "速度 " + _mouseMotion.Speed.ToString("0", CultureInfo.InvariantCulture);
            _mouseDeadZoneText.Text = "死区 " + _mouseMotion.DeadZone;
            if (WheelDominantAxisActive) ResetMouseAxes();
            else _mouseMotion.Reset();
        }

        private static IEnumerable<string> SettingCandidates(string fileName)
        {
            string saved = null;
            try { saved = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName)); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            if (saved != null) yield return saved;

            using (Stream stream = typeof(MainWindow).Assembly.GetManifestResourceStream("SpaceKatMonitor.Defaults." + fileName))
            {
                if (stream == null) throw new InvalidOperationException("缺少内置设置 " + fileName);
                using (StreamReader reader = new StreamReader(stream)) yield return reader.ReadToEnd();
            }
        }

        private void LoadMouseSettings()
        {
            _loadingMouseSettings = true;
            try
            {
                foreach (string candidate in SettingCandidates("mouse-settings.txt"))
                {
                    double speed;
                    int deadZone;
                    bool invertX, invertY;
                    if (!ConfigurationProfiles.TryParseMouseMotion(candidate, out speed, out deadZone, out invertX, out invertY)) continue;
                    _mouseSpeed.Value = speed;
                    _mouseDeadZone.Value = deadZone;
                    _mouseInvertX.IsChecked = invertX;
                    _mouseInvertY.IsChecked = invertY;
                    return;
                }
            }
            finally { _loadingMouseSettings = false; }
        }

        private void SaveMouseSettings()
        {
            if (_loadingMouseSettings) return;
            string[] parts = { _mouseMotion.Speed.ToString(CultureInfo.InvariantCulture), _mouseMotion.DeadZone.ToString(CultureInfo.InvariantCulture),
                _mouseMotion.InvertX.ToString(), _mouseMotion.InvertY.ToString() };
            try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mouse-settings.txt"), String.Join(",", parts)); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private void LoadMouseActions()
        {
            _loadingMouseActions = true;
            try
            {
                foreach (string candidate in SettingCandidates("button-actions.txt"))
                {
                    MouseButtonAction[] actions;
                    if (!ConfigurationProfiles.TryParseMouseActions(candidate, out actions)) continue;
                    for (int i = 0; i < actions.Length; i++) _buttonCards[i].MouseAction.SelectedIndex = (int)actions[i];
                    return;
                }
            }
            finally { _loadingMouseActions = false; }
        }

        private void SaveMouseActions()
        {
            if (_loadingMouseActions) return;
            string[] actions = new string[4];
            for (int i = 0; i < 4; i++)
            {
                if (_buttonCards[i] == null || _buttonCards[i].MouseAction.SelectedIndex < 0) return;
                actions[i] = ((MouseButtonAction)_buttonCards[i].MouseAction.SelectedIndex).ToString();
            }
            try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "button-actions.txt"), String.Join(",", actions)); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private bool RawButtonMode()
        {
            return _demo || _rawButtonMode.IsChecked == true;
        }

        private uint DeclaredButtonMask()
        {
            return _demo || _selected == null || !_selected.HasButtonCaps ? UInt32.MaxValue : _selected.ValidButtonsMask;
        }

        private void RecordButtonEvent(DateTime now, string raw, uint mask)
        {
            string entry = now.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) +
                "  0x" + mask.ToString("X8", CultureInfo.InvariantCulture) + "  位 " + PressedButtonList(mask) + "  |  " + raw;
            _buttonHistory.Enqueue(entry);
            while (_buttonHistory.Count > 10) _buttonHistory.Dequeue();
            if (!_demo)
            {
                try
                {
                    string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "button-events.log");
                    File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd ", CultureInfo.InvariantCulture) + entry + Environment.NewLine,
                        new UTF8Encoding(true));
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private void BeginLearning(object sender, RoutedEventArgs args)
        {
            StopMouseControl("学习按键时已停止鼠标模拟。");
            ButtonCard card = (ButtonCard)((Button)sender).Tag;
            if (_learningCard == card) { CancelLearning("已取消学习。"); return; }
            CancelLearning(null);
            _learningCard = card;
            _learnCandidate = 0;
            _learnReady = _buttonState.Known && _buttonState.RawMask == 0;
            _learnStarted = DateTime.UtcNow;
            _learningFeedback = null;
            card.Learn.Content = "取消学习";
            Render(DateTime.UtcNow);
        }

        private void CancelLearning(string feedback)
        {
            if (_learningCard != null) _learningCard.Learn.Content = "学习此键";
            _learningCard = null;
            _learnCandidate = 0;
            _learnReady = false;
            _learningFeedback = feedback;
        }

        private void UpdateLearning(DateTime now)
        {
            if (_learningCard == null) return;
            if (_learnCandidate == 0)
            {
                if (_buttonState.RawMask == 0) { _learnReady = true; return; }
                uint rising = _buttonState.RisingMask;
                if (_learnReady && rising != 0 && (rising & (rising - 1)) == 0 && _buttonState.RawMask == rising)
                    _learnCandidate = rising;
                return;
            }
            if (_buttonState.RawMask != 0) return;
            ButtonCard learned = _learningCard;
            int logical = 0;
            while ((1u << logical) != _learnCandidate && logical < 31) logical++;
            int physical = Array.IndexOf(_buttonCards, learned) + 1;
            bool duplicate = false;
            foreach (ButtonCard other in _buttonCards)
                if (other != learned && other.LogicalBit == logical) duplicate = true;
            // Learning observes raw frames even in strict display mode. Enable
            // compatibility if the confirmed bit is absent from the descriptor.
            if ((DeclaredButtonMask() & _learnCandidate) == 0) _rawButtonMode.IsChecked = true;
            learned.Mapping.SelectedIndex = logical;
            learned.PressCount = 1;
            learned.FlashUntil = now.AddMilliseconds(350);
            SaveMappings();
            CancelLearning("按键 " + physical + " 已学习为逻辑键 " + (logical + 1) +
                (duplicate ? "；此编号也被其他卡片使用，请继续逐键学习。" : "。"));
        }

        private void LoadMappings()
        {
            _loadingMappings = true;
            try
            {
                foreach (string candidate in SettingCandidates("button-mapping.txt"))
                {
                    int[] parsed;
                    if (!ConfigurationProfiles.TryParseLogicalKeys(candidate, out parsed)) continue;
                    for (int i = 0; i < parsed.Length; i++) _buttonCards[i].Mapping.SelectedIndex = parsed[i] - 1;
                    return;
                }
            }
            finally { _loadingMappings = false; }
        }

        private void SaveMappings()
        {
            if (_loadingMappings) return;
            foreach (ButtonCard card in _buttonCards) if (card == null) return;
            string[] numbers = new string[4];
            for (int i = 0; i < 4; i++) numbers[i] = (_buttonCards[i].LogicalBit + 1).ToString(CultureInfo.InvariantCulture);
            try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "button-mapping.txt"), String.Join(",", numbers)); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public string GetSnapshot()
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine(Program.ProductName + (_demo ? " [演示数据]" : " [设备数据]"));
            text.AppendLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            text.AppendLine("设备：" + (_demo ? "模拟设备" : _selected != null ? _selected.ToString() : "未连接"));
            text.AppendLine(_battery.Text);
            text.AppendLine("连接：" + (_demo ? "演示" : ConnectionLabel()));
            text.AppendLine("设备路径：" + (_selected != null && !_demo ? _selected.Path : "—"));
            text.AppendLine("PacketCount=" + _packetCount.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("RecognizedCount=" + _recognizedCount.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("ReportRate=" + _packetRate.ToString("0.0", CultureInfo.InvariantCulture));
            text.AppendLine("AxesKnown=" + _axesKnown.ToString());
            text.AppendLine("ButtonsKnown=" + _buttonsKnown.ToString());
            text.AppendLine("MouseEnabled=" + _mouseEnabled);
            text.AppendLine("CurrentPage=" + _currentPage);
            text.AppendLine("Theme=" + _theme.Id);
            text.AppendLine("RightStickEnabled=" + _joystick.Enabled);
            text.AppendLine("StickDominantAxis=" + _joystick.DominantAxisEnabled + " (" + _joystick.DominantGroup + ")");
            text.AppendLine("MouseDominantAxis=" + _wheelDominantAxisEnabled + " (" + _mouseDominantAxisFilter.Group + ")");
            text.AppendLine("RightStickX=" + _joystick.StickX.ToString("0.000", CultureInfo.InvariantCulture));
            text.AppendLine("RightStickY=" + _joystick.StickY.ToString("0.000", CultureInfo.InvariantCulture));
            text.AppendLine("ZZoom=" + _joystick.ZoomEnabled + "; Invert=" + _joystick.ZoomInvert
                + "; InGain=" + _joystick.ZoomInGain.ToString("0.0", CultureInfo.InvariantCulture)
                + "; InDeadZone=" + _joystick.ZoomInDeadZone.ToString("0.00", CultureInfo.InvariantCulture)
                + "; InSpeed=" + _joystick.ZoomInSpeed.ToString("0.00", CultureInfo.InvariantCulture)
                + "; OutGain=" + _joystick.ZoomOutGain.ToString("0.0", CultureInfo.InvariantCulture)
                + "; OutDeadZone=" + _joystick.ZoomOutDeadZone.ToString("0.00", CultureInfo.InvariantCulture)
                + "; OutSpeed=" + _joystick.ZoomOutSpeed.ToString("0.00", CultureInfo.InvariantCulture));
            text.AppendLine("MouseWheel=" + _mouseWheel.Enabled + "; Invert=" + _mouseWheel.Invert
                + "; Up=" + _mouseWheel.UpGain.ToString("0.0", CultureInfo.InvariantCulture) + "/" + _mouseWheel.UpSpeed.ToString("0.0", CultureInfo.InvariantCulture)
                + "; Down=" + _mouseWheel.DownGain.ToString("0.0", CultureInfo.InvariantCulture) + "/" + _mouseWheel.DownSpeed.ToString("0.0", CultureInfo.InvariantCulture));
            text.AppendLine("Triggers=LT:" + _joystick.LeftTrigger + "; RT:" + _joystick.RightTrigger);
            text.AppendLine("FixedCamera=" + _stickCamera.GetSnapshot());
            text.AppendLine("VirtualGamepadConnected=" + _gamepadOutput.IsConnected);
            text.AppendLine("GamepadButtonMapping=" + String.Join(",", _stickButtonActions));
            text.AppendLine("GamepadButtons=0x" + ((ushort)_stickButtons.Output).ToString("X4", CultureInfo.InvariantCulture));
            text.AppendLine("JoystickStatus=" + _stickMessage);
            text.AppendLine("Cube=" + _cubeView.GetSnapshot());
            text.AppendLine("MouseSpeed=" + _mouseMotion.Speed.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("MouseDeadZone=" + _mouseMotion.DeadZone);
            text.AppendLine("MouseInvertX=" + _mouseMotion.InvertX);
            text.AppendLine("MouseInvertY=" + _mouseMotion.InvertY);
            text.AppendLine("LeftButtonLocked=" + _mouseButtons.IsLeftHeld);
            text.AppendLine("MouseButtonReleasePending=" + (_mouseOutput != null && _mouseOutput.HasPendingRelease));
            text.AppendLine("MouseStatus=" + _mouseMessage);
            text.AppendLine("MousePhase=" + _mouseSession.GetPhase(_mouseClock.Elapsed.TotalSeconds));
            text.AppendLine("RawCompatibility=" + RawButtonMode());
            text.AppendLine("ButtonPacketCount=" + _buttonPacketCount);
            text.AppendLine("RawButtons=0x" + _buttonState.RawMask.ToString("X8"));
            text.AppendLine("ObservedButtons=0x" + _buttonState.ObservedMask.ToString("X8"));
            bool capsKnown = !_demo && _selected != null && _selected.HasButtonCaps;
            text.AppendLine("HasButtonCaps=" + capsKnown.ToString());
            text.AppendLine("ValidButtonsMask=" + (_demo ? "0xFFFFFFFF [demo]" : capsKnown ? "0x" + _selected.ValidButtonsMask.ToString("X8", CultureInfo.InvariantCulture) : "unknown"));
            string[] names = { "X", "Y", "Z", "Rx", "Ry", "Rz" };
            for (int i = 0; i < 6; i++)
                text.AppendLine(names[i] + "=" + (_axesKnown ? _axes[i].ToString(CultureInfo.InvariantCulture) : "未知"));
            text.AppendLine(_buttonsKnown ? "Buttons=0x" + _buttons.ToString("X8", CultureInfo.InvariantCulture) : "Buttons=unknown");
            for (int i = 0; i < 4; i++)
                text.AppendLine("按键" + (i + 1).ToString(CultureInfo.InvariantCulture) + "：逻辑键" + (_buttonCards[i].LogicalBit + 1).ToString(CultureInfo.InvariantCulture) + "，" + _buttonCards[i].State.Text + "，" + _buttonCards[i].Count.Text + "，鼠标功能=" + (MouseButtonAction)_buttonCards[i].MouseAction.SelectedIndex);
            text.AppendLine("LastReceivedUtc=" + (_lastReceived == DateTime.MinValue ? "unknown" : _lastReceived.ToString("O", CultureInfo.InvariantCulture)));
            text.AppendLine("LastReport=" + (_lastRaw ?? "unknown"));
            text.AppendLine("LastButtonReport=" + (_lastButtonRaw ?? "unknown"));
            text.AppendLine("ButtonHistory:");
            text.AppendLine(String.Join(Environment.NewLine, _buttonHistory.ToArray()));
            text.AppendLine("Status=" + _statusText.Text);
            return text.ToString();
        }

        public void SavePreview(string path)
        {
            _root.UpdateLayout();
            int width = Math.Max(1, (int)Math.Ceiling(_root.ActualWidth));
            int height = Math.Max(1, (int)Math.Ceiling(_root.ActualHeight));
            RenderTargetBitmap bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(_root);
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream stream = File.Create(path))
                encoder.Save(stream);
        }

        private void OnClosed(object sender, EventArgs args)
        {
            _closing = true;
            StopMouseControl("窗口已关闭。");
            StopJoystick("窗口已关闭，右摇杆已回中。");
            SaveJoystickSettings();
            _gamepadOutput.Dispose();
            SaveMouseSettings();
            SaveMouseActions();
            SaveWheelSettings();
            if (_mouseOutput != null) _mouseOutput.Dispose();
            _timer.Stop();
            if (_input != null)
            {
                _input.ReportReceived -= OnReportReceived;
                _input.DevicesChanged -= OnDevicesChanged;
                _input.Dispose();
                _input = null;
            }
            if (_source != null)
            {
                _source.RemoveHook(WindowMessage);
                _source = null;
            }
        }
    }
}
