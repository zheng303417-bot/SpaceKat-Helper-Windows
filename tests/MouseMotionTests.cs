using System;
using System.Collections.Generic;

namespace SpaceKatMonitor
{
    public static class MouseMotionTests
    {
        public static string Run()
        {
            int checks = 0;
            List<string> cases = new List<string>();
            MouseMotionModel model = new MouseMotionModel();
            Check(model.Speed == 900 && model.DeadZone == 16 && !model.InvertX && !model.InvertY, ref checks);
            Check(Zero(model.Step(0)) && Zero(model.Step(0.05)), ref checks);
            model.Update(512, 0, 0.05);
            Check(Zero(model.Step(0.1)), ref checks);
            Check(model.Step(0.12).X >= 17, ref checks);
            cases.Add("Use only XY samples; no movement before a sample or on the first output tick");

            foreach (short x in new short[] { -16, -1, 0, 1, 16 })
                foreach (short y in new short[] { -16, 0, 16 })
                {
                    model.Reset();
                    model.Update(x, y, 0);
                    model.Step(0);
                    Check(Zero(model.Step(0.05)), ref checks);
                }
            CheckDelta(512, 0, 45, 0, false, false, ref checks);
            CheckDelta(-512, 0, -45, 0, false, false, ref checks);
            CheckDelta(0, 512, 0, 45, false, false, ref checks);
            CheckDelta(0, -512, 0, -45, false, false, ref checks);
            CheckDelta(512, -512, 45, -45, false, false, ref checks);
            CheckDelta(512, 512, -45, 45, true, false, ref checks);
            CheckDelta(512, 512, 45, -45, false, true, ref checks);
            CheckDelta(512, 512, -45, -45, true, true, ref checks);
            CheckDelta(Int16.MinValue, Int16.MaxValue, -45, 45, false, false, ref checks);
            cases.Add("Inclusive dead zone, signed directions, independent inversion and clamped int16 limits");

            long[] thirty = Travel(30, 512, -256);
            long[] sixty = Travel(60, 512, -256);
            long[] oneTwenty = Travel(120, 512, -256);
            Check(Math.Abs(thirty[0] - 900) <= 1 && Math.Abs(thirty[1] + 435) <= 1, ref checks);
            Check(Math.Abs(thirty[0] - sixty[0]) <= 1 && Math.Abs(thirty[1] - sixty[1]) <= 1, ref checks);
            Check(Math.Abs(sixty[0] - oneTwenty[0]) <= 1 && Math.Abs(sixty[1] - oneTwenty[1]) <= 1, ref checks);
            model = new MouseMotionModel { Speed = 100, DeadZone = 0 };
            model.Update(5, -5, 0);
            model.Step(0);
            int smallX = 0;
            int smallY = 0;
            for (int i = 1; i <= 240; i++)
            {
                double time = i / 120.0;
                model.Update(5, -5, time);
                MouseDelta delta = model.Step(time);
                smallX += delta.X;
                smallY += delta.Y;
            }
            Check(smallX == 1 && smallY == -1, ref checks);
            cases.Add("Comparable travel at 30/60/120 Hz and accumulated subpixel motion");

            model = new MouseMotionModel { Speed = 100, DeadZone = 0 };
            model.Update(128, -128, 0);
            model.Step(0);
            Check(Zero(model.Step(0.02)), ref checks);
            model.Update(-128, 128, 0.02);
            Check(Zero(model.Step(0.04)), ref checks);
            MouseDelta reversed = model.Step(0.06);
            Check(reversed.X == -1 && reversed.Y == 1, ref checks);
            model.Reset();
            model.Update(128, 0, 0);
            model.Step(0);
            Check(Zero(model.Step(0.02)), ref checks);
            model.Update(0, 0, 0.021);
            model.Update(128, 0, 0.022);
            Check(Zero(model.Step(0.04)), ref checks);
            Check(model.Step(0.06).X == 1, ref checks);
            model.Update(0, 0, 0.061);
            Check(Zero(model.Step(0.08)) && Zero(model.Step(0.1)), ref checks);
            cases.Add("Clear residual motion on reversals and neutral, including between output ticks");

            model = new MouseMotionModel();
            model.Update(512, -512, 0);
            model.Step(0);
            Check(model.Step(0.05).X == 45, ref checks);
            Check(model.Step(0.1).X == 45, ref checks);
            Check(Zero(model.Step(0.251)), ref checks);
            Check(Zero(model.Step(0.3)), ref checks);
            model.Update(512, -512, 0.31);
            Check(Zero(model.Step(0.31)), ref checks);
            Check(model.Step(0.36).X >= 44, ref checks);
            model.Update(512, -512, 1);
            MouseDelta bounded = model.Step(1);
            Check(bounded.X == 45 && bounded.Y == -45, ref checks);
            model.Update(512, -512, 1.01);
            Check(model.Step(1.01).X <= 10, ref checks);
            model.Reset();
            Check(Zero(model.Step(2)) && Zero(model.Step(2.1)), ref checks);
            cases.Add("Pause stale motion after 250 ms, recover without jumps and cap stalled ticks at 50 ms");

            foreach (double invalid in new double[] { Double.NaN, Double.PositiveInfinity, Double.NegativeInfinity, -1 })
            {
                model.Reset();
                model.Update(512, 512, invalid);
                Check(Zero(model.Step(0)) && Zero(model.Step(0.01)), ref checks);
                model.Update(512, 512, 0.02);
                model.Step(0.02);
                Check(Zero(model.Step(invalid)) && Zero(model.Step(0.03)), ref checks);
            }
            model.Reset();
            model.Update(512, 512, 5);
            model.Step(5);
            Check(Zero(model.Step(4)) && Zero(model.Step(5.01)), ref checks);
            model.Update(512, 512, 6);
            model.Step(6);
            model.Update(512, 512, 5.9);
            Check(Zero(model.Step(6.01)), ref checks);
            model.Update(512, 512, 7);
            model.Step(7);
            Check(Zero(model.Step(7)), ref checks);
            cases.Add("Reject invalid or backward clocks; duplicate timestamps never create movement");

            foreach (double invalidSpeed in new double[] { Double.NaN, Double.PositiveInfinity, Double.NegativeInfinity, -10, 0 })
            {
                model = new MouseMotionModel { Speed = invalidSpeed };
                model.Update(512, 512, 0);
                model.Step(0);
                Check(Zero(model.Step(0.05)) && model.Speed == 0, ref checks);
            }
            model = new MouseMotionModel { Speed = Double.MaxValue, DeadZone = Int32.MaxValue, InvertX = true };
            Check(model.Speed == 10000 && model.DeadZone == 100, ref checks);
            model.Update(512, 0, 0);
            model.Step(0);
            MouseDelta highSpeed = model.Step(0.05);
            Check(highSpeed.X == -500 && highSpeed.Y == 0, ref checks);
            model.DeadZone = -1;
            Check(model.DeadZone == 0, ref checks);
            model.Reset();
            Check(model.Speed == 10000 && model.DeadZone == 0 && model.InvertX && Zero(model.Step(0.1)), ref checks);
            cases.Add("Bound configuration values and reset input without losing user settings");

            return "PASS: " + checks + " mouse motion checks\r\n" + String.Join("\r\n", cases.ToArray()) + "\r\n";
        }

        private static long[] Travel(int rate, short x, short y)
        {
            MouseMotionModel model = new MouseMotionModel();
            model.Update(x, y, 0);
            model.Step(0);
            long totalX = 0;
            long totalY = 0;
            for (int i = 1; i <= rate; i++)
            {
                double time = i / (double)rate;
                model.Update(x, y, time);
                MouseDelta delta = model.Step(time);
                totalX += delta.X;
                totalY += delta.Y;
            }
            return new long[] { totalX, totalY };
        }

        private static void CheckDelta(short x, short y, int expectedX, int expectedY, bool invertX, bool invertY, ref int checks)
        {
            MouseMotionModel model = new MouseMotionModel { InvertX = invertX, InvertY = invertY };
            model.Update(x, y, 0);
            model.Step(0);
            MouseDelta delta = model.Step(0.05);
            Check(delta.X == expectedX && delta.Y == expectedY, ref checks);
        }

        private static bool Zero(MouseDelta value)
        {
            return value.X == 0 && value.Y == 0;
        }

        private static void Check(bool condition, ref int checks)
        {
            checks++;
            if (!condition) throw new InvalidOperationException("Mouse motion assertion " + checks + " failed.");
        }
    }
}
