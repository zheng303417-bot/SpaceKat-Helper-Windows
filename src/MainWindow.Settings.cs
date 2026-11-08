using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace SpaceKatMonitor
{
    public partial class MainWindow
    {
        private const string ThemeFile = "theme-settings.txt";
        private readonly StartupRegistration _startup = new StartupRegistration(
            Environment.GetFolderPath(Environment.SpecialFolder.Startup), AppDomain.CurrentDomain.BaseDirectory);
        private Grid _settingsPage;
        private Button _settingsNav;
        private CheckBox _startupCheck;
        private ComboBox _themePicker;
        private TextBlock _startupStatus, _themeStatus, _configurationStatus;
        private AppTheme _theme;
        private bool _loadingTheme;

        private Brush Accent { get { return (Brush)_root.Resources["AccentBrush"]; } }
        private Brush HighlightBackground { get { return (Brush)_root.Resources["AccentTintBrush"]; } }
        private Brush HighlightBorder { get { return (Brush)_root.Resources["AccentEdgeBrush"]; } }

        private void InitializeSettings()
        {
            _settingsPage = Find<Grid>("SettingsPage");
            _settingsNav = Find<Button>("SettingsNavButton");
            _settingsNav.Click += delegate { ShowPage("settings"); };
            _startupCheck = Find<CheckBox>("StartupCheckBox");
            _startupStatus = Find<TextBlock>("StartupStatusText");
            _startupCheck.Click += delegate { ToggleStartup(); };
            _themePicker = Find<ComboBox>("ThemePicker");
            _themeStatus = Find<TextBlock>("ThemeStatusText");
            _configurationStatus = Find<TextBlock>("ConfigurationStatusText");
            foreach (AppTheme theme in AppTheme.Choices) _themePicker.Items.Add(theme.Name);
            LoadTheme();
            _themePicker.SelectionChanged += delegate { ChangeTheme(); };
            Find<Button>("ImportConfigurationButton").Click += delegate { ImportConfiguration(); };
            Find<Button>("ExportConfigurationButton").Click += delegate { ExportConfiguration(); };
        }

        private void RefreshStartupSetting()
        {
            try
            {
                string target = _startup.RegisteredTarget();
                bool registered = _startup.Matches(target);
                _startupCheck.IsChecked = registered;
                _startupStatus.Text = registered ? "已为当前目录添加 Windows 自启动快捷方式。"
                    : String.IsNullOrEmpty(target) ? "未为当前账号开启。"
                    : "另一目录已设置自启动。勾选可改为从当前目录启动。";
            }
            catch (Exception error)
            {
                _startupCheck.IsChecked = false;
                _startupStatus.Text = "无法检查自启动状态 · " + error.GetBaseException().Message;
            }
        }

        private void ToggleStartup()
        {
            try
            {
                _startup.SetEnabled(_startupCheck.IsChecked == true);
                RefreshStartupSetting();
            }
            catch (Exception error)
            {
                RefreshStartupSetting();
                _startupStatus.Text = "无法修改自启动 · " + error.GetBaseException().Message;
            }
        }

        private void LoadTheme()
        {
            foreach (string candidate in SettingCandidates(ThemeFile))
            {
                AppTheme theme = AppTheme.Find(candidate);
                if (theme == null) continue;
                ApplyTheme(theme);
                return;
            }
        }

        private void ChangeTheme()
        {
            if (_loadingTheme || _themePicker.SelectedIndex < 0) return;
            AppTheme theme = AppTheme.Choices[_themePicker.SelectedIndex];
            try
            {
                ConfigurationProfiles.WriteText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ThemeFile), theme.Id);
                ApplyTheme(theme);
                Render(DateTime.UtcNow);
            }
            catch (IOException error) { ThemeSaveFailed(error); }
            catch (UnauthorizedAccessException error) { ThemeSaveFailed(error); }
        }

        private void ThemeSaveFailed(Exception error)
        {
            ApplyTheme(_theme);
            _themeStatus.Text = "主题保存失败 · " + error.Message;
        }

        private void ApplyTheme(AppTheme theme)
        {
            _theme = theme;
            _root.Resources["AccentBrush"] = ColorBrush(theme.Accent);
            _root.Resources["SidebarBrush"] = ColorBrush(theme.Sidebar);
            _root.Resources["AccentTintBrush"] = ColorBrush(theme.Tint);
            _root.Resources["AccentEdgeBrush"] = ColorBrush(theme.Edge);
            _loadingTheme = true;
            try { _themePicker.SelectedIndex = Array.IndexOf(AppTheme.Choices, theme); }
            finally { _loadingTheme = false; }
            _themeStatus.Text = theme.Accent;
            RenderNavigation();
        }

        private static string ArchiveFolder()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "配置存档");
        }

        private static Dictionary<string, string> CurrentConfiguration()
        {
            Dictionary<string, string> settings = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string name in ConfigurationProfiles.Names)
                foreach (string candidate in SettingCandidates(name))
                {
                    if (!ConfigurationProfiles.IsValid(name, candidate)) continue;
                    settings.Add(name, candidate);
                    break;
                }
            return settings;
        }

        private void ImportConfiguration()
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Title = "选择 SpaceKat 配置", Multiselect = true, CheckFileExists = true,
                Filter = "SpaceKat 配置 (*.zip;*.txt)|*.zip;*.txt",
                InitialDirectory = Directory.Exists(ArchiveFolder()) ? ArchiveFolder() : AppDomain.CurrentDomain.BaseDirectory
            };
            if (dialog.ShowDialog(this) == true) ReplaceConfiguration(dialog.FileNames);
        }

        private void ReplaceConfiguration(string[] paths)
        {
            string backup = null;
            bool replaced = false;
            try
            {
                Dictionary<string, string> settings = ConfigurationProfiles.ReadSelection(paths);
                StopMouseControl("正在替换配置，鼠标模拟已停止。");
                StopJoystick("正在替换配置，摇杆模拟已停止。");
                backup = BackupConfiguration();
                ConfigurationProfiles.Replace(AppDomain.CurrentDomain.BaseDirectory, settings);
                replaced = true;
                ReloadConfiguration();
                _configurationStatus.Text = "已应用 " + settings.Count + " 项配置。备份 · " + backup;
            }
            catch (Exception error)
            {
                _configurationStatus.Text = (replaced ? "配置已替换，重新打开程序后生效 · " : "导入失败 · ") + error.Message +
                    (backup == null ? "" : " 备份 · " + backup);
            }
        }

        private static string BackupConfiguration()
        {
            string directory = ArchiveFolder();
            Directory.CreateDirectory(directory);
            string backup = Path.Combine(directory, "SpaceKat-导入前-" +
                DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) + "-" +
                Guid.NewGuid().ToString("N").Substring(0, 6) + ".zip");
            ConfigurationProfiles.WriteArchive(backup, CurrentConfiguration());
            return backup;
        }

        private void ReloadConfiguration()
        {
            LoadMouseSettings();
            UpdateMouseSettings();
            _loadingWheelSettings = true;
            try { LoadWheelSettings(); ApplyWheelSettings(); }
            finally { _loadingWheelSettings = false; }
            LoadMappings();
            LoadMouseActions();
            _loadingStickSettings = true;
            try
            {
                LoadJoystickSettings();
                LoadStickButtonMappings();
                ApplyJoystickSettings();
                ApplyZoomSettings();
            }
            finally { _loadingStickSettings = false; }
            LoadTheme();
            Render(DateTime.UtcNow);
        }

        private void ExportConfiguration()
        {
            SaveFileDialog dialog = new SaveFileDialog
            {
                Title = "导出 SpaceKat 配置", Filter = "配置 ZIP (*.zip)|*.zip", DefaultExt = ".zip",
                FileName = "SpaceKat-配置-" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture),
                InitialDirectory = Directory.Exists(ArchiveFolder()) ? ArchiveFolder() : AppDomain.CurrentDomain.BaseDirectory
            };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                if (!String.Equals(Path.GetExtension(dialog.FileName), ".zip", StringComparison.OrdinalIgnoreCase))
                    throw new IOException("请选择 .zip 文件名。");
                ConfigurationProfiles.WriteArchive(dialog.FileName, CurrentConfiguration());
                _configurationStatus.Text = "已导出 · " + dialog.FileName;
            }
            catch (Exception error) { _configurationStatus.Text = "导出失败 · " + error.Message; }
        }
    }
}
