using System;

namespace SpaceKatMonitor
{
    public struct MouseDelta
    {
        public int X;
        public int Y;
    }

    /// <summary>
    /// Converts only the X/Y controller deflection into relative cursor motion.
    /// Call Update for each real axis report and Step with monotonic seconds.
    /// No desktop APIs are called by this model.
    /// </summary>
    public sealed class MouseMotionModel
    {
        public const double SampleTimeoutSeconds = 0.25;
        public const double MaximumStepSeconds = 0.05;

        private double speed = 900;
        private int deadZone = 16;
        private bool invertX;
        private bool invertY;
        private bool hasSample;
        private bool hasStepTime;
        private double lastSampleTime;
        private double lastStepTime;
        private int sampleX;
        private int sampleY;
        private int directionX;
        private int directionY;
        private double fractionX;
        private double fractionY;

        public double Speed
        {
            get { return speed; }
            set
            {
                // A finite upper bound also bounds every delta after a stall.
                double next = IsFinite(value) ? Math.Max(0, Math.Min(10000, value)) : 0;
                if (next != speed) ClearFractions();
                speed = next;
            }
        }

        public int DeadZone
        {
            get { return deadZone; }
            set
            {
                int next = Math.Max(0, Math.Min(100, value));
                if (next != deadZone) ClearFractions();
                deadZone = next;
            }
        }

        public bool InvertX
        {
            get { return invertX; }
            set
            {
                if (invertX != value) { fractionX = 0; directionX = 0; }
                invertX = value;
            }
        }

        public bool InvertY
        {
            get { return invertY; }
            set
            {
                if (invertY != value) { fractionY = 0; directionY = 0; }
                invertY = value;
            }
        }

        public void Update(short x, short y, double nowSeconds)
        {
            if (!ValidTime(nowSeconds) ||
                (hasSample && nowSeconds < lastSampleTime) ||
                (hasStepTime && nowSeconds < lastStepTime))
            {
                Reset();
                return;
            }

            if (!hasSample)
            {
                // The first Step after startup/recovery establishes its clock;
                // time before this sample is never converted into movement.
                hasStepTime = false;
                ClearFractions();
            }
            sampleX = Math.Max(-512, Math.Min(512, (int)x));
            sampleY = Math.Max(-512, Math.Min(512, (int)y));
            lastSampleTime = nowSeconds;
            hasSample = true;
            UpdateDirections();
        }

        public MouseDelta Step(double nowSeconds)
        {
            if (!ValidTime(nowSeconds))
            {
                Reset();
                return new MouseDelta();
            }
            if (!hasStepTime)
            {
                hasStepTime = true;
                lastStepTime = nowSeconds;
                return new MouseDelta();
            }

            double elapsed = nowSeconds - lastStepTime;
            if (elapsed < 0 || (hasSample && nowSeconds < lastSampleTime))
            {
                Reset();
                return new MouseDelta();
            }
            lastStepTime = nowSeconds;
            if (!hasSample || nowSeconds - lastSampleTime > SampleTimeoutSeconds)
            {
                hasSample = false;
                ClearFractions();
                return new MouseDelta();
            }

            UpdateDirections();
            if (elapsed <= 0 || speed <= 0)
            {
                if (speed <= 0) ClearFractions();
                return new MouseDelta();
            }

            elapsed = Math.Min(elapsed, MaximumStepSeconds);
            double motionX = fractionX + Normalized(sampleX, invertX) * speed * elapsed;
            double motionY = fractionY + Normalized(sampleY, invertY) * speed * elapsed;
            int deltaX = (int)motionX;
            int deltaY = (int)motionY;
            fractionX = motionX - deltaX;
            fractionY = motionY - deltaY;
            return new MouseDelta { X = deltaX, Y = deltaY };
        }

        public void Reset()
        {
            hasSample = false;
            hasStepTime = false;
            lastSampleTime = 0;
            lastStepTime = 0;
            sampleX = 0;
            sampleY = 0;
            ClearFractions();
        }

        private double Normalized(int value, bool inverted)
        {
            int magnitude = Math.Abs(value);
            if (magnitude <= deadZone) return 0;
            double normalized = (magnitude - deadZone) / (512.0 - deadZone);
            return (value < 0 ? -normalized : normalized) * (inverted ? -1 : 1);
        }

        private void UpdateDirections()
        {
            int nextX = Direction(sampleX, invertX);
            int nextY = Direction(sampleY, invertY);
            // Clear on neutral and reversals, including samples which arrive
            // between output ticks, so a fractional remainder cannot cause drift.
            if (nextX != directionX || nextX == 0) fractionX = 0;
            if (nextY != directionY || nextY == 0) fractionY = 0;
            directionX = nextX;
            directionY = nextY;
        }

        private int Direction(int value, bool inverted)
        {
            if (Math.Abs(value) <= deadZone) return 0;
            return (value < 0 ? -1 : 1) * (inverted ? -1 : 1);
        }

        private void ClearFractions()
        {
            fractionX = 0;
            fractionY = 0;
            directionX = 0;
            directionY = 0;
        }

        private static bool IsFinite(double value)
        {
            return !Double.IsNaN(value) && !Double.IsInfinity(value);
        }

        private static bool ValidTime(double value)
        {
            return IsFinite(value) && value >= 0;
        }
    }
}
