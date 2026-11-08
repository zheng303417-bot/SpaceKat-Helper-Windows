using System;
using System.Globalization;

namespace SpaceKatMonitor
{
    internal sealed class JoystickSettings
    {
        public int SourceX = 5, SourceY = 3, OutputMode = 1, ToggleKey;
        public bool InvertX, InvertY = true, ZoomEnabled = true, ZoomInvert, DominantAxisEnabled = true;
        public double GainX = 1, GainY = 1, DeadZone = .08, Gamma = 1.5;
        public double ZoomInGain = 1, ZoomOutGain = 1, ZoomInDeadZone = .08, ZoomOutDeadZone = .08;
        public double ZoomInSpeed = .5, ZoomOutSpeed = .5;
        public double ZoomGain { get { return ZoomInGain; } set { ZoomInGain = ZoomOutGain = value; } }
        public double ZoomDeadZone { get { return ZoomInDeadZone; } set { ZoomInDeadZone = ZoomOutDeadZone = value; } }
        public double ZoomSpeed { get { return ZoomInSpeed; } set { ZoomInSpeed = ZoomOutSpeed = value; } }
        public short[] Centers = new short[6];

        public static bool TryParse(string text, out JoystickSettings result)
        {
            result = null;
            if (String.IsNullOrWhiteSpace(text)) return false;
            string[] p = text.Trim().Split(',');
            if (p.Length != 16 && p.Length != 21 && p.Length != 23 && p.Length != 24 && p.Length != 25) return false;
            JoystickSettings s = new JoystickSettings();
            if (!Integer(p[0], 0, 5, out s.SourceX) || !Integer(p[1], 0, 5, out s.SourceY)
                || !Boolean.TryParse(p[2], out s.InvertX) || !Boolean.TryParse(p[3], out s.InvertY)
                || !Number(p[4], .1, 3, out s.GainX) || !Number(p[5], .1, 3, out s.GainY)
                || !Number(p[6], 0, .4, out s.DeadZone) || !Number(p[7], 1, 3, out s.Gamma)
                || !Integer(p[8], 0, 1, out s.OutputMode) || !Integer(p[9], 0, 4, out s.ToggleKey)) return false;
            for (int i = 0; i < 6; i++)
                if (!Int16.TryParse(p[10 + i], NumberStyles.Integer, CultureInfo.InvariantCulture, out s.Centers[i])) return false;
            if (p.Length >= 21 && (!Boolean.TryParse(p[16], out s.ZoomEnabled)
                || !Boolean.TryParse(p[17], out s.ZoomInvert)
                || !Number(p[18], .1, 3, out s.ZoomInGain)
                || !Number(p[19], 0, .4, out s.ZoomInDeadZone)
                || !Number(p[20], .05, RightStickModel.MaximumZoomSpeed, out s.ZoomInSpeed))) return false;
            if (p.Length >= 23)
            {
                if (!Number(p[21], .1, 3, out s.ZoomOutGain)
                    || !Number(p[22], .05, RightStickModel.MaximumZoomSpeed, out s.ZoomOutSpeed)) return false;
            }
            else
            {
                s.ZoomOutGain = s.ZoomInGain;
                s.ZoomOutSpeed = s.ZoomInSpeed;
            }
            if (p.Length >= 24)
            {
                if (!Number(p[23], 0, .4, out s.ZoomOutDeadZone)) return false;
            }
            else s.ZoomOutDeadZone = s.ZoomInDeadZone;
            if (p.Length == 25 && !Boolean.TryParse(p[24], out s.DominantAxisEnabled)) return false;
            result = s;
            return true;
        }

        public string Serialize()
        {
            string[] p = new string[25];
            p[0] = SourceX.ToString(CultureInfo.InvariantCulture); p[1] = SourceY.ToString(CultureInfo.InvariantCulture);
            p[2] = InvertX.ToString(); p[3] = InvertY.ToString();
            p[4] = GainX.ToString(CultureInfo.InvariantCulture); p[5] = GainY.ToString(CultureInfo.InvariantCulture);
            p[6] = DeadZone.ToString(CultureInfo.InvariantCulture); p[7] = Gamma.ToString(CultureInfo.InvariantCulture);
            p[8] = OutputMode.ToString(CultureInfo.InvariantCulture); p[9] = ToggleKey.ToString(CultureInfo.InvariantCulture);
            for (int i = 0; i < 6; i++) p[10 + i] = Centers[i].ToString(CultureInfo.InvariantCulture);
            p[16] = ZoomEnabled.ToString(); p[17] = ZoomInvert.ToString();
            p[18] = ZoomInGain.ToString(CultureInfo.InvariantCulture);
            p[19] = ZoomInDeadZone.ToString(CultureInfo.InvariantCulture);
            p[20] = ZoomInSpeed.ToString(CultureInfo.InvariantCulture);
            p[21] = ZoomOutGain.ToString(CultureInfo.InvariantCulture);
            p[22] = ZoomOutSpeed.ToString(CultureInfo.InvariantCulture);
            p[23] = ZoomOutDeadZone.ToString(CultureInfo.InvariantCulture);
            p[24] = DominantAxisEnabled.ToString();
            return String.Join(",", p);
        }

        private static bool Integer(string text, int min, int max, out int value)
        {
            return Int32.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= min && value <= max;
        }
        private static bool Number(string text, double min, double max, out double value)
        {
            return Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && !Double.IsNaN(value) && !Double.IsInfinity(value) && value >= min && value <= max;
        }
    }
}
