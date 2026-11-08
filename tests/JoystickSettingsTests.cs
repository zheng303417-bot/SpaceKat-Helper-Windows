using System;
using System.Globalization;
using System.Threading;

namespace SpaceKatMonitor
{
    internal static class JoystickSettingsTests
    {
        public static string Run()
        {
            int count = 0;
            const string legacy = "5,3,True,True,1.2,0.8,0.09,1.7,0,4,-8,12,23,-40,51,62";
            JoystickSettings s;
            Check(JoystickSettings.TryParse(legacy, out s), ref count);
            Check(s.SourceX == 5 && s.SourceY == 3 && s.InvertX && s.InvertY && s.OutputMode == 0 && s.ToggleKey == 4, ref count);
            Check(s.GainX == 1.2 && s.GainY == .8 && s.DeadZone == .09 && s.Gamma == 1.7, ref count);
            Check(s.Centers[0] == -8 && s.Centers[2] == 23 && s.Centers[5] == 62, ref count);
            Check(s.ZoomEnabled && !s.ZoomInvert && s.DominantAxisEnabled && s.ZoomInGain == 1 && s.ZoomOutGain == 1
                && s.ZoomInDeadZone == .08 && s.ZoomOutDeadZone == .08 && s.ZoomInSpeed == .5 && s.ZoomOutSpeed == .5, ref count);
            s.ZoomEnabled = false; s.ZoomInvert = true; s.ZoomGain = 2.3; s.ZoomDeadZone = .12; s.ZoomSpeed = .35;
            s.DominantAxisEnabled = false;
            string saved = s.Serialize();
            Check(saved.Split(',').Length == 25 && saved.StartsWith(legacy + ",", StringComparison.Ordinal), ref count);
            Check(JoystickSettings.TryParse(saved, out s) && !s.ZoomEnabled && s.ZoomInvert && !s.DominantAxisEnabled
                && s.ZoomInGain == 2.3 && s.ZoomOutGain == 2.3 && s.ZoomInDeadZone == .12
                && s.ZoomOutDeadZone == .12 && s.ZoomInSpeed == .35 && s.ZoomOutSpeed == .35, ref count);
            Check(JoystickSettings.TryParse(" \r\n" + saved + "\r\n", out s), ref count);
            foreach (string suffix in new[] { "True,False,0.1,0,0.05", "True,True,3,0.4,1" })
                Check(JoystickSettings.TryParse(legacy + "," + suffix, out s), ref count);
            foreach (string suffix in new[] {
                "True,False,0.09,0.08,0.5", "True,False,3.1,0.08,0.5", "True,False,NaN,0.08,0.5",
                "True,False,1,-0.01,0.5", "True,False,1,0.41,0.5", "True,False,1,Infinity,0.5",
                "True,False,1,0.08,0.04", "True,False,1,0.08,3.01", "True,False,1,0.08,NaN",
                "yes,False,1,0.08,0.5", "True,no,1,0.08,0.5", "True,False,1,0.08", "True,False,1,0.08,0.5,0" })
                Check(!JoystickSettings.TryParse(legacy + "," + suffix, out s) && s == null, ref count);
            foreach (string invalid in new[] { null, "", "5,3", legacy.Replace("5,3,", "6,3,"), legacy.Replace("1.2", "NaN"),
                legacy.Replace("0,4,-8", "2,4,-8"), legacy.Replace("23,-40", "32768,-40") })
                Check(!JoystickSettings.TryParse(invalid, out s) && s == null, ref count);
            CultureInfo previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
                Check(JoystickSettings.TryParse(saved, out s) && s.Serialize() == saved, ref count);
            }
            finally { Thread.CurrentThread.CurrentCulture = previous; }
            RunDirectionalChecks(legacy, ref count);
            return "PASS: " + count + " joystick settings checks (legacy migration and saved zoom controls)\r\n";
        }

        private static void RunDirectionalChecks(string legacy, ref int count)
        {
            JoystickSettings s;
            // A published 1.7 profile retains every old value and initializes
            // both new actions from its shared force, speed and resting offset.
            string oldProfile = legacy + ",True,False,3,0.08,0.8";
            Check(JoystickSettings.TryParse(oldProfile, out s), ref count);
            Check(s.ZoomInGain == 3 && s.ZoomOutGain == 3 && s.ZoomInSpeed == .8 && s.ZoomOutSpeed == .8
                && s.ZoomInDeadZone == .08 && s.ZoomOutDeadZone == .08, ref count);
            Check(s.Serialize().StartsWith(oldProfile + ",", StringComparison.Ordinal)
                && s.Serialize().Split(',').Length == 25 && s.DominantAxisEnabled, ref count);
            s.ZoomInGain = 2.1; s.ZoomOutGain = .4;
            s.ZoomInSpeed = 3; s.ZoomOutSpeed = 2.4;
            s.ZoomInDeadZone = .03; s.ZoomOutDeadZone = .27;
            s.ZoomInvert = true;
            string saved = s.Serialize();
            Check(JoystickSettings.TryParse(saved, out s) && s.ZoomInvert && s.ZoomEnabled, ref count);
            Check(s.ZoomInGain == 2.1 && s.ZoomOutGain == .4 && s.ZoomInSpeed == 3 && s.ZoomOutSpeed == 2.4
                && s.ZoomInDeadZone == .03 && s.ZoomOutDeadZone == .27, ref count);
            Check(s.SourceX == 5 && s.SourceY == 3 && s.InvertX && s.InvertY && s.OutputMode == 0
                && s.ToggleKey == 4 && s.Centers[0] == -8 && s.Centers[2] == 23 && s.Centers[5] == 62, ref count);
            Check(s.Serialize() == saved, ref count);
            string previousVersion = saved.Substring(0, saved.LastIndexOf(','));
            Check(JoystickSettings.TryParse(previousVersion, out s) && s.DominantAxisEnabled
                && s.ZoomOutDeadZone == .27 && s.ZoomOutSpeed == 2.4 && s.Centers[2] == 23, ref count);
            Check(JoystickSettings.TryParse(oldProfile + ",0.4,0.85", out s)
                && s.ZoomOutGain == .4 && s.ZoomOutSpeed == .85 && s.ZoomOutDeadZone == .08 && s.DominantAxisEnabled, ref count);
            foreach (string suffix in new[] {
                "True,False,0.1,0,0.05,0.1,0.05,0", "True,True,3,0.4,1,3,1,0.4" })
                Check(JoystickSettings.TryParse(legacy + "," + suffix, out s), ref count);
            foreach (int index in new[] { 18, 19, 20, 21, 22, 23 })
            {
                foreach (string invalid in new[] { "NaN", "Infinity", "-0.01", "4" })
                {
                    string[] fields = saved.Split(','); fields[index] = invalid;
                    Check(!JoystickSettings.TryParse(String.Join(",", fields), out s) && s == null, ref count);
                }
            }
            foreach (string invalid in new[] { saved + ",0", oldProfile + ",1", saved.Substring(0, saved.LastIndexOf(',')) + "," })
                Check(!JoystickSettings.TryParse(invalid, out s) && s == null, ref count);
            CultureInfo previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                Check(JoystickSettings.TryParse(saved, out s) && s.Serialize() == saved, ref count);
            }
            finally { Thread.CurrentThread.CurrentCulture = previous; }
        }

        private static void Check(bool condition, ref int count)
        {
            count++;
            if (!condition) throw new InvalidOperationException("Joystick settings check failed: " + count);
        }
    }
}
