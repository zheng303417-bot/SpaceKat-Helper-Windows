using System;

namespace SpaceKatMonitor
{
    public enum MouseControlPhase
    {
        Off,
        WaitingForData,
        Active
    }

    /// <summary>
    /// Keeps the user's enabled intent separate from XY sample freshness.
    /// A quiet input stream pauses motion; it never disables the session.
    /// </summary>
    public sealed class MouseControlSession
    {
        private double lastClockSeconds = Double.NaN;

        public bool Enabled { get; private set; }
        public double LastSampleSeconds { get; private set; }

        public MouseControlSession()
        {
            ClearTiming();
        }

        public void Enable()
        {
            Enabled = true;
            ClearTiming();
        }

        public void ObserveXY(double nowSeconds)
        {
            if (!Enabled) return;
            if (!AcceptTime(nowSeconds)) return;
            LastSampleSeconds = nowSeconds;
        }

        public MouseControlPhase GetPhase(double nowSeconds)
        {
            if (!Enabled) return MouseControlPhase.Off;
            if (!AcceptTime(nowSeconds) || Double.IsNaN(LastSampleSeconds))
                return MouseControlPhase.WaitingForData;
            return nowSeconds - LastSampleSeconds <= MouseMotionModel.SampleTimeoutSeconds
                ? MouseControlPhase.Active : MouseControlPhase.WaitingForData;
        }

        public void Disable()
        {
            Enabled = false;
            ClearTiming();
        }

        private bool AcceptTime(double nowSeconds)
        {
            if (Double.IsNaN(nowSeconds) || Double.IsInfinity(nowSeconds) || nowSeconds < 0)
            {
                ClearTiming();
                return false;
            }
            if (!Double.IsNaN(lastClockSeconds) && nowSeconds < lastClockSeconds)
            {
                // Invalidate the old sample while rebasing the clock. Only a
                // subsequent genuine XY observation may make the session active.
                LastSampleSeconds = Double.NaN;
                lastClockSeconds = nowSeconds;
                return false;
            }
            lastClockSeconds = nowSeconds;
            return true;
        }

        private void ClearTiming()
        {
            LastSampleSeconds = Double.NaN;
            lastClockSeconds = Double.NaN;
        }
    }
}
