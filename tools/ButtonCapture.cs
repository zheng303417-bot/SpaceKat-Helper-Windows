using System;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System.Drawing;

namespace SpaceKatMonitor
{
    internal sealed class CaptureWindow : Form
    {
        private readonly RawInputService input;
        private readonly StreamWriter log;
        private long axisReports;
        private long buttonReports;
        private long inputMessages;
        private readonly Timer lifetimeTimer;
        private readonly Timer heartbeatTimer;
        private bool captureDisposed;

        public CaptureWindow(string path)
        {
            log = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write,
                FileShare.ReadWrite), new UTF8Encoding(true));
            log.AutoFlush = true;
            Text = "SpaceKat diagnostic listener";
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-32000, -32000);
            Size = new Size(1, 1);
            Opacity = 0;
            input = new RawInputService(Handle);
            input.ReportReceived += Received;
            input.DevicesChanged += ListDevices;
            Write("START: top-level WinForms HWND; listen only to multi-axis HID; no output reports or serial-port commands");
            ListDevices();
            heartbeatTimer = new Timer { Interval = 10000 };
            heartbeatTimer.Tick += delegate { Write("HEARTBEAT wmInput=" + inputMessages + " axes=" + axisReports + " buttons=" + buttonReports); };
            heartbeatTimer.Start();
            lifetimeTimer = new Timer { Interval = 240000 };
            lifetimeTimer.Tick += delegate { lifetimeTimer.Stop(); Close(); };
            lifetimeTimer.Start();
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        private void Write(string text)
        {
            log.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " " + text);
        }

        private void ListDevices()
        {
            foreach (HidDevice device in RawInputService.EnumerateDevices())
                Write("DEVICE " + device + " declaredMask=" + device.ValidButtonsMask.ToString("X8") + " " + device.Path);
        }

        private void Received(HidDevice device, byte[] report)
        {
            if (report.Length == 0) return;
            if (report[0] == 1)
            {
                axisReports++;
                if (axisReports != 1) return;
            }
            if (report[0] == 3) buttonReports++;
            DecodedReport decoded;
            string buttonMask = ReportDecoder.TryDecode(report, out decoded) && decoded.Buttons.HasValue
                ? " rawMask=" + decoded.Buttons.Value.ToString("X8") : "";
            Write("INPUT vid=" + device.VendorId.ToString("X4") + " pid=" + device.ProductId.ToString("X4") +
                " length=" + report.Length + buttonMask + " bytes=" +
                BitConverter.ToString(report, 0, Math.Min(report.Length, 64)).Replace('-', ' '));
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x00FF) inputMessages++;
            try { if (input != null) input.ProcessMessage(message.Msg, message.WParam, message.LParam); }
            catch (Exception error) { Write("ERROR " + error.Message); }
            base.WndProc(ref message);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !captureDisposed)
            {
                captureDisposed = true;
                if (lifetimeTimer != null) lifetimeTimer.Dispose();
                if (heartbeatTimer != null) heartbeatTimer.Dispose();
                if (input != null) input.Dispose();
                if (log != null)
                {
                    Write("END wmInput=" + inputMessages + " axes=" + axisReports + " buttons=" + buttonReports);
                    log.Dispose();
                }
            }
            base.Dispose(disposing);
        }

        [STAThread]
        private static int Main(string[] args)
        {
            string path = args.Length > 0 ? args[0] : "button-capture.log";
            try
            {
                using (CaptureWindow window = new CaptureWindow(path))
                {
                    Application.Run(window);
                }
                return 0;
            }
            catch (Exception error)
            {
                File.AppendAllText(path, "\r\nFATAL " + error);
                return 1;
            }
        }
    }
}
