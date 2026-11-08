using System;
using System.Globalization;
using System.Threading;

namespace SpaceKatMonitor
{
    internal static class MouseWheelSettingsTests
    {
        public static string Run()
        {
            int count = 0;
            MouseWheelSettings s = new MouseWheelSettings();
            Check(s.Enabled && !s.Invert && s.UpGain == 1 && s.DownGain == 1 && s.UpSpeed == 6 && s.DownSpeed == 6
                && s.UpDeadZone == .08 && s.DownDeadZone == .08 && s.Center == 0 && s.DominantAxisEnabled, ref count);
            const string legacy = "True,True,2.4,0.6,25,3,0.12,-97,0.03";
            const string independent = legacy + ",False";
            Check(MouseWheelSettings.TryParse(independent, out s), ref count);
            Check(s.Enabled && s.Invert && s.UpGain == 2.4 && s.DownGain == .6 && s.UpSpeed == 25 && s.DownSpeed == 3
                && s.UpDeadZone == .12 && s.DownDeadZone == .03 && s.Center == -97 && !s.DominantAxisEnabled, ref count);
            Check(s.Serialize() == independent, ref count);
            Check(MouseWheelSettings.TryParse(legacy, out s) && s.DominantAxisEnabled
                && s.Serialize() == legacy + ",True", ref count);
            Check(MouseWheelSettings.TryParse("\r\n " + independent + " \r\n", out s), ref count);
            Check(MouseWheelSettings.TryParse("False,False,1,2,5,8,0.17,120", out s)
                && !s.Enabled && !s.Invert && s.UpDeadZone == .17 && s.DownDeadZone == .17 && s.Center == 120
                && s.DominantAxisEnabled, ref count);
            Check(s.Serialize().Split(',').Length == 10, ref count);
            foreach (string text in new[] {
                "True,False,0.1,3,1,120,0,-32768,0.4,True", "False,True,3,0.1,120,1,0.4,32767,0,False"
            })
                Check(MouseWheelSettings.TryParse(text, out s) && s.Serialize() == text, ref count);
            string[] fields = independent.Split(',');
            string[][] invalidValues = {
                new[] { "yes", "1", "" }, new[] { "no", "0", "" },
                new[] { "0.09", "3.1", "NaN", "Infinity", "-Infinity", "" },
                new[] { "0", "3.1", "NaN", "Infinity", "-Infinity", "" },
                new[] { "0.9", "120.1", "NaN", "Infinity", "-Infinity", "" },
                new[] { "0", "121", "NaN", "Infinity", "-Infinity", "" },
                new[] { "-0.01", "0.41", "NaN", "Infinity", "-Infinity", "" },
                new[] { "-32769", "32768", "1.5", "NaN", "" },
                new[] { "-0.01", "0.41", "NaN", "Infinity", "-Infinity", "" },
                new[] { "0", "yes", "" }
            };
            for (int i = 0; i < invalidValues.Length; i++)
                foreach (string invalid in invalidValues[i])
                {
                    string[] values = (string[])fields.Clone();
                    values[i] = invalid;
                    Check(!MouseWheelSettings.TryParse(String.Join(",", values), out s) && s == null, ref count);
                }
            foreach (string invalid in new[] { null, "", " ", "True", "True,False,1,1,6,6,0.08", independent + ",0" })
                Check(!MouseWheelSettings.TryParse(invalid, out s) && s == null, ref count);
            CultureInfo previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
                Check(MouseWheelSettings.TryParse(independent, out s) && s.Serialize() == independent, ref count);
                Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("zh-CN");
                Check(MouseWheelSettings.TryParse(independent, out s) && s.Serialize() == independent, ref count);
            }
            finally { Thread.CurrentThread.CurrentCulture = previous; }
            return "PASS: " + count + " mouse wheel settings checks (independent up/down and invariant persistence)\r\n";
        }

        private static void Check(bool condition, ref int count)
        {
            count++;
            if (!condition) throw new InvalidOperationException("Mouse wheel settings check failed: " + count);
        }
    }
}
