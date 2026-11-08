using System;
using System.Globalization;

namespace SpaceKatMonitor
{
    internal sealed class MouseWheelSettings
    {
        public bool Enabled = true, Invert;
        public bool DominantAxisEnabled = true;
        public double UpGain = 1, DownGain = 1;
        public double UpSpeed = 6, DownSpeed = 6;
        public double UpDeadZone = .08, DownDeadZone = .08;
        public short Center;

        public static bool TryParse(string text, out MouseWheelSettings result)
        {
            result = null;
            if (String.IsNullOrWhiteSpace(text)) return false;
            string[] p = text.Trim().Split(',');
            if (p.Length < 8 || p.Length > 10) return false;
            MouseWheelSettings s = new MouseWheelSettings();
            if (!Boolean.TryParse(p[0], out s.Enabled) || !Boolean.TryParse(p[1], out s.Invert)
                || !Number(p[2], .1, 3, out s.UpGain) || !Number(p[3], .1, 3, out s.DownGain)
                || !Number(p[4], 1, MouseWheelModel.MaximumSpeed, out s.UpSpeed)
                || !Number(p[5], 1, MouseWheelModel.MaximumSpeed, out s.DownSpeed)
                || !Number(p[6], 0, .4, out s.UpDeadZone)
                || !Int16.TryParse(p[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out s.Center)) return false;
            if (p.Length >= 9)
            {
                if (!Number(p[8], 0, .4, out s.DownDeadZone)) return false;
            }
            else s.DownDeadZone = s.UpDeadZone;
            if (p.Length == 10 && !Boolean.TryParse(p[9], out s.DominantAxisEnabled)) return false;
            result = s;
            return true;
        }

        public string Serialize()
        {
            return String.Join(",", new[] {
                Enabled.ToString(), Invert.ToString(),
                UpGain.ToString(CultureInfo.InvariantCulture), DownGain.ToString(CultureInfo.InvariantCulture),
                UpSpeed.ToString(CultureInfo.InvariantCulture), DownSpeed.ToString(CultureInfo.InvariantCulture),
                UpDeadZone.ToString(CultureInfo.InvariantCulture), Center.ToString(CultureInfo.InvariantCulture),
                DownDeadZone.ToString(CultureInfo.InvariantCulture), DominantAxisEnabled.ToString()
            });
        }

        private static bool Number(string text, double min, double max, out double value)
        {
            return Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && !Double.IsNaN(value) && !Double.IsInfinity(value) && value >= min && value <= max;
        }
    }
}
