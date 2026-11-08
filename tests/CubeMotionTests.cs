using System;

namespace SpaceKatMonitor
{
    internal static class CubeMotionTests
    {
        public static string Run()
        {
            int count = 0;
            CubeMotionModel model = new CubeMotionModel();
            model.Advance(10);
            Check(!model.HasData && !model.IsInputFresh && Zero(model.Pose), ref count);
            model.SetInput(new short[6], true, 10);
            Check(model.HasData && model.IsInputFresh && Zero(model.Pose), ref count);
            model.Advance(10.02);
            Check(Zero(model.Pose), ref count);

            // Each report field controls only its corresponding visual axis.
            for (int axis = 0; axis < 6; axis++)
            {
                foreach (short sign in new short[] { 512, -512 })
                {
                    model.ResetView();
                    short[] input = new short[6];
                    input[axis] = sign;
                    model.SetInput(input, true, 0);
                    Check(Zero(model.Pose), ref count);
                    model.Advance(0.02);
                    double[] pose = Values(model.Pose);
                    for (int component = 0; component < 6; component++)
                        Check(component == axis ? pose[component] * sign > 0 : Near(pose[component], 0), ref count);
                }
            }

            model.ResetView();
            model.SetInput(new short[] { 12, -12, 0, 12, -12, 0 }, true, 0);
            model.Advance(0.02);
            Check(Zero(model.Pose), ref count);
            model.SetInput(new short[] { 13, -13, 0, 13, -13, 0 }, true, 0.02);
            model.Advance(0.04);
            Check(model.Pose.X > 0 && model.Pose.Y < 0 && model.Pose.Rx > 0 && model.Pose.Ry < 0, ref count);

            // The same one-second input produces the same result at different
            // update/render cadences, while all intervals remain under 50 ms.
            CubePose expected = Simulate(30, 1, new short[] { 512, -256, 128, 256, -512, 64 });
            foreach (int rate in new[] { 60, 120 })
            {
                CubePose actual = Simulate(rate, 1, new short[] { 512, -256, 128, 256, -512, 64 });
                double[] a = Values(actual), b = Values(expected);
                for (int i = 0; i < 6; i++) Check(Near(a[i], b[i]), ref count);
            }

            // Stale samples never move a retained pose. Fresh samples recover
            // from a long pause without replaying the paused interval.
            model.ResetView();
            model.SetInput(new short[] { 512, 0, 0, 0, 0, 512 }, true, 0);
            model.Advance(0.02);
            CubePose beforePause = model.Pose;
            model.Advance(0.3);
            Check(!model.IsInputFresh && model.HasData && Same(beforePause, model.Pose), ref count);
            model.Advance(100);
            Check(Same(beforePause, model.Pose), ref count);
            model.SetInput(new short[] { 512, 0, 0, 0, 0, 512 }, true, 100.01);
            Check(model.IsInputFresh && Same(beforePause, model.Pose), ref count);
            model.Advance(100.03);
            Check(Near(model.Pose.X - beforePause.X, CubeMotionModel.TranslationSpeed * 0.02), ref count);
            Check(Near(model.Pose.Rz - beforePause.Rz, CubeMotionModel.RotationSpeedDegrees * 0.02), ref count);

            // A delayed render can integrate at most 50 ms, even with a sample
            // which is still fresh; neutral reports stop further motion.
            model.ResetView();
            model.SetInput(new short[] { 512, 0, 0, 512, 0, 0 }, true, 0);
            model.Advance(0.2);
            Check(Near(model.Pose.X, CubeMotionModel.TranslationSpeed * 0.05), ref count);
            Check(Near(model.Pose.Rx, CubeMotionModel.RotationSpeedDegrees * 0.05), ref count);
            model.SetInput(new short[6], true, 0.2);
            CubePose neutralPose = model.Pose;
            model.Advance(0.22);
            Check(Same(neutralPose, model.Pose), ref count);

            // Held input cannot send the cube offscreen or grow angles without
            // bound; int16 extremes are clamped to the nominal HID range.
            CubePose held = Simulate(50, 40, new short[] { Int16.MaxValue, Int16.MinValue, 512, 512, -512, 512 });
            Check(Near(held.X, CubeMotionModel.TranslationLimit) && Near(held.Y, -CubeMotionModel.TranslationLimit)
                && Near(held.Z, CubeMotionModel.TranslationLimit), ref count);
            Check(Math.Abs(held.Rx) <= 180 && Math.Abs(held.Ry) <= 180 && Math.Abs(held.Rz) <= 180, ref count);

            model.ResetView();
            Check(!model.HasData && !model.IsInputFresh && Zero(model.Pose), ref count);
            model.Advance(1000);
            Check(Zero(model.Pose), ref count);
            model.SetInput(new short[] { 512, 0, 0, 0, 0, 0 }, true, 1000);
            model.Advance(1000.02);
            CubePose retained = model.Pose;
            foreach (double invalid in new[] { Double.NaN, Double.PositiveInfinity, Double.NegativeInfinity, -1.0 })
            {
                model.Advance(invalid);
                Check(!model.IsInputFresh && Same(retained, model.Pose), ref count);
            }
            model.SetInput(new short[] { 512, 0, 0, 0, 0, 0 }, true, 10);
            model.Advance(9);
            Check(!model.IsInputFresh && Same(retained, model.Pose), ref count);
            model.SetInput(new short[] { 512, 0, 0, 0, 0, 0 }, true, 11);
            model.Advance(11.02);
            Check(model.IsInputFresh && model.Pose.X > retained.X, ref count);
            CubePose validPose = model.Pose;
            model.SetInput(null, false, 12);
            model.Advance(13);
            Check(!model.HasData && !model.IsInputFresh && Same(validPose, model.Pose), ref count);
            model.SetInput(new short[5], true, 14);
            Check(!model.HasData && Same(validPose, model.Pose), ref count);

            return "PASS: " + count + " six-axis cube motion checks (visual model only)\r\n" +
                "Independent signed axes, dead zone, time-based rates, bounded pose and reset\r\n" +
                "Stale/invalid clocks pause; fresh data resumes without replay; no desktop mouse output\r\n";
        }

        private static CubePose Simulate(int rate, int seconds, short[] sample)
        {
            CubeMotionModel model = new CubeMotionModel();
            model.SetInput(sample, true, 0);
            for (int tick = 1; tick <= rate * seconds; tick++)
            {
                double now = tick / (double)rate;
                model.SetInput(sample, true, now);
                model.Advance(now);
            }
            return model.Pose;
        }

        private static double[] Values(CubePose pose)
        {
            return new[] { pose.X, pose.Y, pose.Z, pose.Rx, pose.Ry, pose.Rz };
        }

        private static bool Same(CubePose a, CubePose b)
        {
            double[] first = Values(a), second = Values(b);
            for (int i = 0; i < first.Length; i++) if (!Near(first[i], second[i])) return false;
            return true;
        }

        private static bool Zero(CubePose pose) { return Same(pose, new CubePose()); }
        private static bool Near(double a, double b) { return Math.Abs(a - b) < 0.000001; }
        private static void Check(bool condition, ref int count)
        {
            count++;
            if (!condition) throw new InvalidOperationException("Cube motion assertion " + count + " failed.");
        }
    }
}
