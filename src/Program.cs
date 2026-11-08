using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

[assembly: System.Reflection.AssemblyTitle(SpaceKatMonitor.Program.ProductName)]
[assembly: System.Reflection.AssemblyProduct(SpaceKatMonitor.Program.ProductName)]
[assembly: System.Reflection.AssemblyFileVersion("1.16.0.0")]

namespace SpaceKatMonitor
{
    internal static class Program
    {
        internal const string ProductName = "SpaceKat 小助手";

        [STAThread]
        private static int Main(string[] args)
        {
            bool commandLine = Array.IndexOf(args, "--self-test") >= 0 || Array.IndexOf(args, "--probe") >= 0 || Array.IndexOf(args, "--gamepad-probe") >= 0 || Array.IndexOf(args, "--gamepad-neutral-check") >= 0;
            string output = Option(args, "--output");
            try
            {
                if (Array.IndexOf(args, "--self-test") >= 0)
                {
                    Write(output ?? DefaultFile("test-results.txt"), DecoderTests.Run() + ButtonStateTests.Run()
                        + MouseMotionTests.Run() + MouseOutputTests.Run() + MouseControlSessionTests.Run() + MouseButtonTests.Run() + CubeMotionTests.Run()
                        + RightStickTests.Run() + VirtualGamepadOutputTests.Run() + JoystickSettingsTests.Run()
                        + MouseWheelTests.Run() + MouseWheelSettingsTests.Run() + DominantAxisFilterTests.Run()
                        + BatteryTests.Run() + GamepadButtonTests.Run());
                    return 0;
                }
                if (Array.IndexOf(args, "--gamepad-probe") >= 0)
                {
                    string error;
                    bool ready = VirtualGamepadOutput.TryProbe(out error);
                    Write(output ?? DefaultFile("gamepad-probe.txt"), "ViGEmBusReady=" + ready + "\r\n" +
                        (ready ? "总线连接成功；未创建虚拟手柄，未发送摇杆输入。" : error));
                    return ready ? 0 : 2;
                }
                if (Array.IndexOf(args, "--gamepad-neutral-check") >= 0)
                {
                    using (VirtualGamepadOutput pad = new VirtualGamepadOutput())
                    {
                        string error;
                        bool connected = pad.TryConnect(out error);
                        bool centered = connected && pad.TryUpdate(0, 0, out error);
                        pad.Stop();
                        bool removed = !pad.HasPendingStop && !pad.IsConnected;
                        Write(output ?? DefaultFile("gamepad-neutral-check.txt"),
                            "VirtualControllerCreated=" + connected + "\r\nNeutralReportSent=" + centered +
                            "\r\nVirtualControllerRemoved=" + removed + "\r\n" +
                            (error ?? pad.LastError ?? "创建、中位输出和移除均成功；未发送偏转输入。"));
                        return connected && centered && removed && pad.LastError == null ? 0 : 2;
                    }
                }
                if (Array.IndexOf(args, "--probe") >= 0)
                {
                    StringBuilder report = new StringBuilder();
                    var devices = RawInputService.EnumerateDevices();
                    report.AppendLine("Multi-axis HID devices: " + devices.Count);
                    foreach (HidDevice device in devices)
                    {
                        report.AppendLine(device.ToString());
                        report.AppendLine(device.Path);
                    }
                    Write(output ?? DefaultFile("device-probe.txt"), report.ToString());
                    return 0;
                }

                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnMainWindowClose;
                bool demo = Array.IndexOf(args, "--demo") >= 0;
                MainWindow window = new MainWindow(demo);
                window.ShowPage(Option(args, "--page") ?? "test");
                string previewSize = Option(args, "--preview-size");
                if (previewSize != null)
                {
                    string[] dimensions = previewSize.Split('x');
                    int width, height;
                    if (dimensions.Length == 2 && Int32.TryParse(dimensions[0], out width) && Int32.TryParse(dimensions[1], out height))
                    {
                        window.Width = Math.Max(window.MinWidth, Math.Min(2000, width));
                        window.Height = Math.Max(window.MinHeight, Math.Min(1400, height));
                    }
                }
                string preview = Option(args, "--preview");
                string liveCheck = Option(args, "--live-check");
                if (preview != null || liveCheck != null)
                {
                    window.ContentRendered += delegate
                    {
                        DispatcherTimer timer = new DispatcherTimer();
                        int captureTicks = 0;
                        timer.Interval = TimeSpan.FromMilliseconds(liveCheck != null ? 1000 : 1100);
                        timer.Tick += delegate
                        {
                            if (liveCheck == null) timer.Stop();
                            try
                            {
                                string capturePath = Path.GetFullPath(liveCheck ?? preview);
                                captureTicks++;
                                if (liveCheck == null || captureTicks % 15 == 0) window.SavePreview(capturePath);
                                Write(Path.ChangeExtension(capturePath, ".txt"), window.GetSnapshot());
                            }
                            catch (Exception error)
                            {
                                Write(DefaultFile("preview-error.txt"), error.ToString());
                                Environment.ExitCode = 1;
                            }
                            if (liveCheck == null) window.Close();
                        };
                        timer.Start();
                    };
                }
                app.Run(window);
                return Environment.ExitCode;
            }
            catch (Exception error)
            {
                string errorPath = output ?? DefaultFile("SpaceKatMonitor-error.log");
                try { Write(errorPath, error.ToString()); } catch { }
                if (!commandLine)
                    MessageBox.Show("程序启动失败。详细信息已保存到：\n" + errorPath + "\n\n" + error.Message,
                        ProductName, MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
        }

        private static string Option(string[] args, string name)
        {
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        private static string DefaultFile(string name)
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name);
        }

        private static void Write(string path, string text)
        {
            File.WriteAllText(Path.GetFullPath(path), text, new UTF8Encoding(true));
        }

    }
}
