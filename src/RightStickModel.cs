using System;
using System.Globalization;

namespace SpaceKatMonitor
{
    /// <summary>Two selected HID axes become a radial logical right stick.</summary>
    public sealed class RightStickModel
    {
        public const double SampleTimeoutSeconds = 0.15;
        public const double MaximumStepSeconds = 0.05;
        public const double YawSpeedDegrees = 120;
        public const double PitchSpeedDegrees = 90;
        public const double DefaultFieldOfViewDegrees = 60;
        public const double MinimumFieldOfViewDegrees = 20;
        public const double MaximumFieldOfViewDegrees = 100;
        public const double ZoomSpeedDegrees = 60;
        internal const double MaximumZoomSpeed = 3;
        private int sourceX = 5, sourceY = 3;
        private bool invertX, invertY = true;
        private double gainX = 1, gainY = 1, deadZone = 0.08, gamma = 1.5;
        private bool zoomEnabled = true, zoomInvert;
        private double zoomInGain = 1, zoomOutGain = 1, zoomInDeadZone = 0.08, zoomOutDeadZone = 0.08;
        private double zoomInSpeed = 0.5, zoomOutSpeed = 0.5;
        private double fieldOfViewDegrees = DefaultFieldOfViewDegrees;
        private readonly short[] centers = new short[6];
        private readonly DominantAxisFilter dominantAxisFilter = new DominantAxisFilter();
        private bool dominantAxisEnabled;
        private bool hasSample;
        private double lastSample = Double.NaN, lastStep = Double.NaN;

        public int SourceX { get { return sourceX; } set { sourceX = Math.Max(0, Math.Min(5, value)); ClearInput(); } }
        public int SourceY { get { return sourceY; } set { sourceY = Math.Max(0, Math.Min(5, value)); ClearInput(); } }
        public bool InvertX { get { return invertX; } set { invertX = value; ClearInput(); } }
        public bool InvertY { get { return invertY; } set { invertY = value; ClearInput(); } }
        public double GainX { get { return gainX; } set { gainX = Bound(value, 0, 4, 1); ClearInput(); } }
        public double GainY { get { return gainY; } set { gainY = Bound(value, 0, 4, 1); ClearInput(); } }
        public double DeadZone { get { return deadZone; } set { deadZone = Bound(value, 0, 0.95, 0.08); ClearInput(); } }
        public double Gamma { get { return gamma; } set { gamma = Bound(value, 0.25, 4, 1.5); ClearInput(); } }
        public bool ZoomEnabled { get { return zoomEnabled; } set { if (zoomEnabled == value) return; zoomEnabled = value; ClearZoomInput(); } }
        public bool ZoomInvert { get { return zoomInvert; } set { if (zoomInvert == value) return; zoomInvert = value; ClearZoomInput(); } }
        public double ZoomInGain { get { return zoomInGain; } set { double next = Bound(value, 0.1, 3, 1); if (zoomInGain == next) return; zoomInGain = next; ClearZoomInput(); } }
        public double ZoomOutGain { get { return zoomOutGain; } set { double next = Bound(value, 0.1, 3, 1); if (zoomOutGain == next) return; zoomOutGain = next; ClearZoomInput(); } }
        public double ZoomGain { get { return zoomInGain; } set { ZoomInGain = value; ZoomOutGain = value; } }
        public double ZoomInDeadZone { get { return zoomInDeadZone; } set { double next = Bound(value, 0, 0.4, 0.08); if (zoomInDeadZone == next) return; zoomInDeadZone = next; ClearZoomInput(); } }
        public double ZoomOutDeadZone { get { return zoomOutDeadZone; } set { double next = Bound(value, 0, 0.4, 0.08); if (zoomOutDeadZone == next) return; zoomOutDeadZone = next; ClearZoomInput(); } }
        public double ZoomDeadZone { get { return zoomInDeadZone; } set { ZoomInDeadZone = value; ZoomOutDeadZone = value; } }
        public double ZoomInSpeed { get { return zoomInSpeed; } set { double next = Bound(value, 0.05, MaximumZoomSpeed, 0.5); if (zoomInSpeed == next) return; zoomInSpeed = next; ClearZoomInput(); } }
        public double ZoomOutSpeed { get { return zoomOutSpeed; } set { double next = Bound(value, 0.05, MaximumZoomSpeed, 0.5); if (zoomOutSpeed == next) return; zoomOutSpeed = next; ClearZoomInput(); } }
        public double ZoomSpeed { get { return zoomInSpeed; } set { ZoomInSpeed = value; ZoomOutSpeed = value; } }
        public bool DominantAxisEnabled
        {
            get { return dominantAxisEnabled; }
            set
            {
                if (dominantAxisEnabled == value) return;
                dominantAxisEnabled = value;
                ClearInput();
            }
        }
        internal DominantAxisGroup DominantGroup { get { return dominantAxisFilter.Group; } }
        public bool Enabled { get; private set; }
        public bool IsFresh { get; private set; }
        public bool HasData { get; private set; }
        public double StickX { get; private set; }
        public double StickY { get; private set; }
        public short OutputX { get { return ToChannel(StickX); } }
        public short OutputY { get { return ToChannel(StickY); } }
        public double YawDegrees { get; private set; }
        public double PitchDegrees { get; private set; }
        public double ZoomInput { get; private set; }
        public byte LeftTrigger { get { return ToTrigger(Math.Max(-ZoomInput, 0) * zoomOutSpeed); } }
        public byte RightTrigger { get { return ToTrigger(Math.Max(ZoomInput, 0) * zoomInSpeed); } }
        public double FieldOfViewDegrees { get { return fieldOfViewDegrees; } }
        public short[] Centers { get { return (short[])centers.Clone(); } }

        public void Enable() { Enabled = true; ClearInput(); }
        public void Disable() { Enabled = false; ClearInput(); }

        public void Update(short[] axes, double now)
        {
            if (!Enabled) return;
            if (axes == null || axes.Length < 6 || !ValidTime(now) ||
                (!Double.IsNaN(lastStep) && now < lastStep) ||
                (hasSample && now < lastSample))
            {
                ClearInput();
                return;
            }
            Step(now);
            double x = Clamp((axes[sourceX] - (double)centers[sourceX]) / 512 * gainX, -1, 1);
            double y = Clamp((axes[sourceY] - (double)centers[sourceY]) / 512 * gainY, -1, 1);
            if (invertX) x = -x;
            if (invertY) y = -y;
            double radius = Math.Sqrt(x * x + y * y);
            if (radius <= deadZone || radius == 0)
                StickX = StickY = 0;
            else
            {
                double amplitude = Math.Pow((Math.Min(radius, 1) - deadZone) / (1 - deadZone), gamma);
                StickX = Clamp(amplitude * x / radius, -1, 1);
                StickY = Clamp(amplitude * y / radius, -1, 1);
            }
            // Z has its own one-dimensional dead zone. It never participates
            // in the right stick's radial limiter or changes its magnitude.
            double z = Clamp((axes[2] - (double)centers[2]) / 512, -1, 1);
            if (zoomInvert) z = -z;
            double zoomAmplitude = Math.Abs(z);
            double directionDeadZone = z > 0 ? zoomInDeadZone : zoomOutDeadZone;
            if (!zoomEnabled || zoomAmplitude <= directionDeadZone)
                ZoomInput = 0;
            else
            {
                double directionGain = z > 0 ? zoomInGain : zoomOutGain;
                double mapped = Clamp((zoomAmplitude - directionDeadZone) / (1 - directionDeadZone) * directionGain, 0, 1);
                ZoomInput = Math.Sign(z) * Math.Pow(mapped, 1.5);
            }
            if (dominantAxisEnabled && zoomEnabled)
            {
                // Physical X/Y can leak into Z even with Rz/Rx selected for steering.
                double movement = Math.Max(AxisStrength(axes, 0), AxisStrength(axes, 1));
                if (sourceX != 2) movement = Math.Max(movement, AxisStrength(axes, sourceX));
                if (sourceY != 2) movement = Math.Max(movement, AxisStrength(axes, sourceY));
                double zoom = zoomAmplitude > directionDeadZone ? zoomAmplitude : 0;
                DominantAxisGroup group = dominantAxisFilter.Update(movement, zoom, now, SampleTimeoutSeconds);
                if (group != DominantAxisGroup.Movement) StickX = StickY = 0;
                if (group != DominantAxisGroup.Z) ZoomInput = 0;
            }
            lastSample = now;
            hasSample = true;
            HasData = IsFresh = true;
        }

        public void Step(double now)
        {
            if (!Enabled) return;
            if (!ValidTime(now) || (!Double.IsNaN(lastStep) && now < lastStep) ||
                (hasSample && now < lastSample))
            {
                ClearInput();
                return;
            }
            if (Double.IsNaN(lastStep))
            {
                lastStep = now;
                return;
            }
            double elapsed = Math.Min(now - lastStep, MaximumStepSeconds);
            lastStep = now;
            IsFresh = hasSample && now - lastSample <= SampleTimeoutSeconds;
            if (!IsFresh)
            {
                hasSample = false;
                StickX = StickY = 0;
                ClearZoomInput();
                return;
            }
            if (elapsed <= 0) return;
            YawDegrees = Wrap(YawDegrees + StickX * YawSpeedDegrees * elapsed);
            PitchDegrees = Clamp(PitchDegrees + StickY * PitchSpeedDegrees * elapsed, -89, 89);
            // Match the XInput triggers within their range. The preview can keep speeding up after they saturate.
            double zoomDemand = ZoomInput * (ZoomInput >= 0 ? zoomInSpeed : zoomOutSpeed);
            double zoomRate = Math.Abs(zoomDemand) > 1 ? zoomDemand : (RightTrigger - LeftTrigger) / 255.0;
            fieldOfViewDegrees = Clamp(fieldOfViewDegrees - zoomRate * ZoomSpeedDegrees * elapsed,
                MinimumFieldOfViewDegrees, MaximumFieldOfViewDegrees);
        }

        public void Calibrate(short[] axes)
        {
            if (axes == null || axes.Length < 6) throw new ArgumentException("需要六轴静止读数。", "axes");
            Array.Copy(axes, centers, 6);
            ClearInput();
        }

        public void SetCalibration(short[] values) { Calibrate(values); }
        public void ResetCalibration() { Array.Clear(centers, 0, centers.Length); ClearInput(); }
        public void ResetView() { YawDegrees = PitchDegrees = 0; fieldOfViewDegrees = DefaultFieldOfViewDegrees; ClearInput(); }

        public string GetSnapshot()
        {
            return "RightStickEnabled=" + Enabled + "\r\nRightStickFresh=" + IsFresh +
                "\r\nRightStick=" + Format(StickX) + "," + Format(StickY) +
                "\r\nRightStickInt16=" + OutputX + "," + OutputY +
                "\r\nCameraYawPitch=" + Format(YawDegrees) + "," + Format(PitchDegrees) +
                "\r\nDominantAxisEnabled=" + DominantAxisEnabled + "\r\nDominantAxisGroup=" + DominantGroup +
                "\r\nZoomEnabled=" + ZoomEnabled + "\r\nZoomInput=" + Format(ZoomInput) +
                "\r\nZoomTriggers=" + LeftTrigger + "," + RightTrigger +
                "\r\nCameraFieldOfView=" + Format(fieldOfViewDegrees) +
                "\r\nCameraPosition=0,0,0\r\nCameraRoll=0\r\n";
        }

        private void ClearInput()
        {
            hasSample = IsFresh = false;
            lastSample = lastStep = Double.NaN;
            StickX = StickY = 0;
            ClearZoomInput();
        }
        private void ClearZoomInput()
        {
            ZoomInput = 0;
            dominantAxisFilter.Reset();
        }
        private double AxisStrength(short[] axes, int index)
        {
            return Math.Min(1, Math.Abs(axes[index] - (double)centers[index]) / 512);
        }
        private static short ToChannel(double value) { return (short)Math.Round(value * (value < 0 ? 32768 : 32767)); }
        private static byte ToTrigger(double value) { return (byte)Math.Round(Clamp(value, 0, 1) * 255); }
        private static bool ValidTime(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value) && value >= 0; }
        private static double Bound(double value, double min, double max, double fallback) { return Double.IsNaN(value) || Double.IsInfinity(value) ? fallback : Clamp(value, min, max); }
        private static double Clamp(double value, double min, double max) { return Math.Max(min, Math.Min(max, value)); }
        private static double Wrap(double value) { value %= 360; return value > 180 ? value - 360 : value < -180 ? value + 360 : value; }
        private static string Format(double value) { return value.ToString("0.000", CultureInfo.InvariantCulture); }
    }
}
