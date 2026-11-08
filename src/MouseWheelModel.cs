using System;

namespace SpaceKatMonitor
{
    /// <summary>Maps Z deflection to whole vertical wheel detents without desktop APIs.</summary>
    public sealed class MouseWheelModel
    {
        public const int WheelDelta = 120;
        internal const int MaximumSpeed = 120;
        public const double SampleTimeoutSeconds = 0.25;
        public const double MaximumStepSeconds = 0.05;
        private bool enabled = true, invert;
        private double upGain = 1, downGain = 1, upSpeed = 6, downSpeed = 6;
        private double upDeadZone = .08, downDeadZone = .08;
        private short center;
        private bool hasSample, hasStepTime;
        private double lastSampleTime, lastStepTime, remainder;
        private int direction;

        public bool Enabled { get { return enabled; } set { if (enabled == value) return; enabled = value; Reset(); } }
        public bool Invert { get { return invert; } set { if (invert == value) return; invert = value; Reset(); } }
        public short Center { get { return center; } set { if (center == value) return; center = value; Reset(); } }
        public double UpGain { get { return upGain; } set { SetNumber(ref upGain, value, .1, 3, 1); } }
        public double DownGain { get { return downGain; } set { SetNumber(ref downGain, value, .1, 3, 1); } }
        public double UpSpeed { get { return upSpeed; } set { SetNumber(ref upSpeed, value, 1, MaximumSpeed, 6); } }
        public double DownSpeed { get { return downSpeed; } set { SetNumber(ref downSpeed, value, 1, MaximumSpeed, 6); } }
        public double UpDeadZone { get { return upDeadZone; } set { SetNumber(ref upDeadZone, value, 0, .4, .08); } }
        public double DownDeadZone { get { return downDeadZone; } set { SetNumber(ref downDeadZone, value, 0, .4, .08); } }
        public double DeadZone { get { return UpDeadZone; } set { UpDeadZone = value; DownDeadZone = value; } }
        public double Input { get; private set; }

        public void Update(short z, double nowSeconds)
        {
            if (!enabled || !ValidTime(nowSeconds)
                || (hasSample && nowSeconds < lastSampleTime)
                || (hasStepTime && nowSeconds < lastStepTime))
            { Reset(); return; }
            if (!hasSample || nowSeconds - lastSampleTime > SampleTimeoutSeconds)
            {
                // A recovery starts a new clock and never replays old quiet time.
                hasStepTime = false;
                remainder = 0;
            }
            double value = Clamp((z - (double)center) / 512, -1, 1) * (invert ? -1 : 1);
            double magnitude = Math.Abs(value);
            double dead = value < 0 ? downDeadZone : upDeadZone;
            double gain = value < 0 ? downGain : upGain;
            int nextDirection = magnitude <= dead ? 0 : Math.Sign(value);
            if (nextDirection == 0 || nextDirection != direction) remainder = 0;
            direction = nextDirection;
            Input = nextDirection == 0 ? 0
                : nextDirection * Math.Pow(Clamp((magnitude - dead) / (1 - dead) * gain, 0, 1), 1.5);
            lastSampleTime = nowSeconds;
            hasSample = true;
        }

        public int Step(double nowSeconds)
        {
            if (!enabled || !ValidTime(nowSeconds)
                || (hasSample && nowSeconds < lastSampleTime)
                || (hasStepTime && nowSeconds < lastStepTime))
            { Reset(); return 0; }
            if (!hasSample) return 0;
            if (nowSeconds - lastSampleTime > SampleTimeoutSeconds)
            { Reset(); return 0; }
            if (!hasStepTime)
            {
                hasStepTime = true;
                lastStepTime = nowSeconds;
                return 0;
            }
            double elapsed = Math.Min(nowSeconds - lastStepTime, MaximumStepSeconds);
            lastStepTime = nowSeconds;
            if (Input == 0) { remainder = 0; return 0; }
            if (elapsed <= 0) return 0;
            double accumulated = remainder + Input * (Input > 0 ? upSpeed : downSpeed) * elapsed;
            // Tiny binary floating-point errors must not lose a detent at exact seconds.
            int detents = (int)Math.Floor(Math.Abs(accumulated) + 1e-12) * Math.Sign(accumulated);
            remainder = accumulated - detents;
            if (Math.Abs(remainder) < 1e-12) remainder = 0;
            return detents * WheelDelta;
        }

        public void Reset()
        {
            hasSample = hasStepTime = false;
            lastSampleTime = lastStepTime = remainder = 0;
            direction = 0;
            Input = 0;
        }

        private void SetNumber(ref double field, double value, double minimum, double maximum, double fallback)
        {
            double next = IsFinite(value) ? Clamp(value, minimum, maximum) : fallback;
            if (field == next) return;
            field = next;
            Reset();
        }
        private static bool IsFinite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
        private static bool ValidTime(double value) { return IsFinite(value) && value >= 0; }
        private static double Clamp(double value, double minimum, double maximum) { return Math.Max(minimum, Math.Min(maximum, value)); }
    }
}
