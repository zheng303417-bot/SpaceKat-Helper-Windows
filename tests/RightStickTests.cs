using System;

namespace SpaceKatMonitor
{
    internal static class RightStickTests
    {
        public static string Run()
        {
            int count = 0;
            RightStickModel m = new RightStickModel();
            Check(!m.Enabled && !m.IsFresh && !m.HasData && Zero(m), ref count);
            Check(m.SourceX == 5 && m.SourceY == 3 && !m.InvertX && m.InvertY, ref count);
            Check(Near(m.DeadZone, .08) && Near(m.Gamma, 1.5) && Near(m.GainX, 1) && Near(m.GainY, 1), ref count);
            m.Update(Axes(5, 512), 0);
            m.Step(.02);
            Check(Zero(m) && Near(m.YawDegrees, 0), ref count);
            m.Enable();
            Check(m.Enabled && !m.IsFresh && Zero(m), ref count);
            m.Update(Axes(5, 512), 0);
            Check(m.IsFresh && m.HasData && Near(m.StickX, 1) && Near(m.StickY, 0), ref count);
            Check(m.OutputX == Int16.MaxValue && m.OutputY == 0, ref count);
            m.Step(.02);
            Check(Near(m.YawDegrees, 2.4) && Near(m.PitchDegrees, 0), ref count);
            double yaw = m.YawDegrees;
            m.Update(new short[6], .02);
            m.Step(.04);
            Check(Zero(m) && Near(m.YawDegrees, yaw), ref count);
            m.Update(Axes(5, -512), .04);
            Check(m.OutputX == Int16.MinValue && Near(m.StickX, -1), ref count);

            // Every field can be chosen, and each sign controls the selected
            // channel without letting the other four report values leak in.
            for (int axis = 0; axis < 6; axis++)
            {
                foreach (short sign in new short[] { 512, -512 })
                {
                    m = Linear(); m.SourceX = axis; m.SourceY = (axis + 1) % 6;
                    m.Enable(); m.Update(Axes(axis, sign), 0);
                    Check(Near(m.StickX, Math.Sign(sign)) && Near(m.StickY, 0), ref count);
                    m.SourceY = axis; m.SourceX = (axis + 1) % 6;
                    m.Enable(); m.Update(Axes(axis, sign), 0);
                    Check(Near(m.StickY, Math.Sign(sign)) && Near(m.StickX, 0), ref count);
                }
            }
            m = Linear(); m.Enable();
            m.Update(new short[] { 512, -512, 512, 0, 512, 0 }, 0);
            Check(Zero(m), ref count);
            m.InvertX = true; m.InvertY = true;
            m.Update(new short[] { 0, 0, 0, 256, 0, 256 }, .01);
            Check(m.StickX < 0 && m.StickY < 0, ref count);

            // The radial limiter preserves the direction of diagonals.
            m = Linear(); m.Enable();
            m.Update(new short[] { 0, 0, 0, 512, 0, 512 }, 0);
            Check(Near(m.StickX, 1 / Math.Sqrt(2)) && Near(m.StickY, 1 / Math.Sqrt(2)), ref count);
            Check(Near(m.StickX * m.StickX + m.StickY * m.StickY, 1), ref count);
            m.Update(new short[] { 0, 0, 0, 256, 0, 512 }, .01);
            Check(Near(m.StickX / m.StickY, 2), ref count);
            m.Update(new short[] { 0, 0, 0, Int16.MinValue, 0, Int16.MaxValue }, .02);
            Check(Near(m.StickX, -m.StickY) && m.StickX > 0, ref count);
            m.DeadZone = .125; m.Gamma = 1;
            m.Update(Axes(5, 64), .03);
            Check(Zero(m), ref count);
            m.Update(Axes(5, 288), .04);
            Check(Near(m.StickX, .5), ref count);
            m.Gamma = 2;
            m.Update(Axes(5, 288), .05);
            Check(Near(m.StickX, .25), ref count);
            m = Linear(); m.GainX = 2; m.GainY = .5; m.Enable();
            m.Update(Axes(5, 256), 0);
            Check(Near(m.StickX, 1), ref count);
            m.Update(Axes(3, 256), .01);
            Check(Near(m.StickY, .25), ref count);

            // Centres are copied, cover all six sources and survive enable.
            m = Linear(); short[] center = { 10, 20, 30, 40, 50, 60 };
            m.Calibrate(center); center[5] = 0; m.Enable();
            m.Update(new short[] { 10, 20, 30, 40, 50, 60 }, 0);
            Check(Zero(m) && m.Centers[5] == 60, ref count);
            short[] copy = m.Centers; copy[5] = 512;
            Check(m.Centers[5] == 60, ref count);
            m.Update(new short[] { 10, 20, 30, 40, 50, 316 }, .01);
            Check(Near(m.StickX, .5), ref count);
            m.ResetCalibration();
            Check(Zero(m), ref count);
            m.Update(Axes(5, 256), .02);
            Check(Near(m.StickX, .5), ref count);
            bool rejected = false;
            try { m.Calibrate(new short[2]); } catch (ArgumentException) { rejected = true; }
            Check(rejected, ref count);

            // Output reaches zero on disable, invalid input or stale data;
            // restoring a fresh sample never replays the silent interval.
            m = Linear(); m.Enable(); m.Update(Axes(5, 512), 1);
            m.Step(1.04); Check(m.IsFresh && m.OutputX == 32767, ref count);
            yaw = m.YawDegrees;
            m.Step(1.2);
            Check(m.Enabled && !m.IsFresh && Zero(m) && Near(m.YawDegrees, yaw), ref count);
            m.Step(10);
            Check(m.Enabled && Zero(m) && Near(m.YawDegrees, yaw), ref count);
            m.Update(Axes(5, 512), 10.01);
            Check(m.IsFresh && Near(m.YawDegrees, yaw), ref count);
            m.Step(10.03);
            Check(Near(m.YawDegrees, yaw + 2.4), ref count);
            m.Disable(); yaw = m.YawDegrees;
            Check(!m.Enabled && !m.IsFresh && Zero(m), ref count);
            m.Step(11); m.Update(Axes(5, 512), 11);
            Check(Zero(m) && Near(m.YawDegrees, yaw), ref count);
            m.Enable(); m.Step(20);
            Check(Zero(m) && Near(m.YawDegrees, yaw), ref count);
            m.Update(Axes(5, 512), 20); m.Step(20.14);
            Check(Near(m.YawDegrees, yaw + 6), ref count); // 50ms cap.
            foreach (double invalid in new[] { Double.NaN, Double.PositiveInfinity, -1.0, 1.0 })
            {
                m.Update(Axes(5, 512), 21); yaw = m.YawDegrees;
                m.Step(invalid);
                Check(Zero(m) && !m.IsFresh && m.Enabled && Near(m.YawDegrees, yaw), ref count);
            }
            m.Update(Axes(5, 512), 30); m.Update(null, 30.01);
            Check(Zero(m) && !m.IsFresh, ref count);
            m.Update(Axes(5, 512), 30.02); m.Update(new short[5], 30.03);
            Check(Zero(m), ref count);
            m.Update(Axes(5, 512), 31); m.Update(Axes(5, 512), 30);
            Check(Zero(m), ref count);

            // Time based camera speed, fixed position and no roll.
            double expected = Simulate(30, 1, false);
            Check(Near(expected, 120), ref count);
            foreach (int rate in new[] { 60, 100, 120, 200 })
                Check(Near(Simulate(rate, 1, false), expected), ref count);
            Check(Near(Simulate(100, 4, false), 120), ref count); // yaw wraps.
            m = Linear(); m.Enable();
            for (int i = 0; i <= 400; i++) { double t = i / 100.0; m.Update(Axes(3, 512), t); m.Step(t); }
            Check(Near(m.PitchDegrees, 89), ref count);
            for (int i = 401; i <= 800; i++) { double t = i / 100.0; m.Update(Axes(3, -512), t); m.Step(t); }
            Check(Near(m.PitchDegrees, -89), ref count);
            string snapshot = m.GetSnapshot();
            Check(snapshot.Contains("CameraPosition=0,0,0") && snapshot.Contains("CameraRoll=0"), ref count);
            m.ResetView();
            Check(Zero(m) && Near(m.YawDegrees, 0) && Near(m.PitchDegrees, 0) && m.Enabled, ref count);
            m.Step(100);
            Check(Zero(m) && Near(m.YawDegrees, 0), ref count);

            // Persisted settings cannot inject NaN, infinity or bad indices.
            m.SourceX = -4; m.SourceY = 10;
            m.DeadZone = Double.NaN; m.Gamma = Double.PositiveInfinity;
            m.GainX = -20; m.GainY = 20;
            Check(m.SourceX == 0 && m.SourceY == 5, ref count);
            Check(Near(m.DeadZone, .08) && Near(m.Gamma, 1.5), ref count);
            Check(Near(m.GainX, 0) && Near(m.GainY, 4), ref count);
            m.DeadZone = 1; m.Gamma = -10;
            Check(Near(m.DeadZone, .95) && Near(m.Gamma, .25), ref count);
            m.Enable(); m.Update(new short[] { Int16.MinValue, 0, 0, 0, 0, Int16.MaxValue }, 0);
            Check(Finite(m.StickX) && Finite(m.StickY) && m.StickX * m.StickX + m.StickY * m.StickY <= 1.000001, ref count);
            RunZoomChecks(ref count);
            RunDirectionalZoomChecks(ref count);
            RunDominantAxisChecks(ref count);
            return "PASS: " + count + " right-stick mapping and fixed-camera checks (no desktop output)\r\n" +
                "Axis selection, radial dead zone/curve, inversion, calibration, unit circle and signed int16\r\n" +
                "150ms neutral guard, fresh-data recovery, fixed camera rates and pitch/clock limits\r\n" +
                "Independent Z dead zone, force sensitivity, trigger speed/limits, inversion and FOV zoom\r\n" +
                "Separate zoom-in/out force, speed and dead zones stay attached to actions after inversion\r\n" +
                "Grouped filtering suppresses cross-talk, rearms at center and switches live\r\n";
        }
        private static void RunZoomChecks(ref int count)
        {
            RightStickModel m = new RightStickModel();
            Check(m.ZoomEnabled && !m.ZoomInvert && Near(m.ZoomGain, 1) && Near(m.ZoomDeadZone, .08) && Near(m.ZoomSpeed, .5), ref count);
            Check(ZoomZero(m) && Near(m.FieldOfViewDegrees, 60), ref count);
            m.Update(Axes(2, 512), 0); m.Step(.02);
            Check(ZoomZero(m) && Near(m.FieldOfViewDegrees, 60), ref count);
            m.Enable(); m.Update(Axes(2, 512), 0);
            Check(Near(m.ZoomInput, 1) && m.LeftTrigger == 0 && m.RightTrigger == 128 && Zero(m), ref count);
            m.Step(.05);
            double fov = 60 - 128 / 255.0 * 60 * .05;
            Check(Near(m.FieldOfViewDegrees, fov) && Near(m.YawDegrees, 0) && Near(m.PitchDegrees, 0), ref count);
            m.Update(new short[6], .05); m.Step(.1);
            Check(ZoomZero(m) && Near(m.FieldOfViewDegrees, fov), ref count);
            m.Update(Axes(2, -512), .1);
            Check(Near(m.ZoomInput, -1) && m.LeftTrigger == 128 && m.RightTrigger == 0, ref count);
            m.Step(.15);
            Check(Near(m.FieldOfViewDegrees, 60), ref count);
            m.Update(Axes(2, 512), .15);
            Check(m.LeftTrigger == 0 && m.RightTrigger == 128, ref count);
            m.Update(Axes(2, -512), .16);
            Check(m.LeftTrigger == 128 && m.RightTrigger == 0, ref count);

            // The dead zone is applied before force sensitivity, so raising
            // sensitivity cannot make a resting offset cross the threshold.
            m = Zoom(); m.ZoomDeadZone = .125; m.Enable();
            foreach (short z in new short[] { 0, 32, -32, 64, -64 })
            {
                m.Update(Axes(2, z), 0);
                Check(ZoomZero(m), ref count);
            }
            m.ZoomGain = 3; m.Update(Axes(2, 64), 0);
            Check(ZoomZero(m), ref count);
            m.ZoomGain = 1; m.Update(Axes(2, 288), 0);
            Check(Near(m.ZoomInput, Math.Pow(.5, 1.5)) && m.RightTrigger == 90 && m.LeftTrigger == 0, ref count);
            m.Update(Axes(2, -288), .01);
            Check(Near(m.ZoomInput, -Math.Pow(.5, 1.5)) && m.LeftTrigger == 90 && m.RightTrigger == 0, ref count);
            m.Update(Axes(2, 65), .02);
            Check(m.ZoomInput > 0 && m.ZoomInput < .001, ref count);
            m.Update(Axes(2, Int16.MaxValue), .03);
            Check(Near(m.ZoomInput, 1) && m.RightTrigger == 255 && m.LeftTrigger == 0, ref count);
            m.Update(Axes(2, Int16.MinValue), .04);
            Check(Near(m.ZoomInput, -1) && m.LeftTrigger == 255 && m.RightTrigger == 0, ref count);

            // Force sensitivity changes how far the cap must move. Speed
            // independently scales the output ceiling, not the measured force.
            m = Zoom(); m.Enable(); m.Update(Axes(2, 256), 0);
            double partial = m.ZoomInput;
            Check(Near(partial, Math.Pow(.5, 1.5)) && m.RightTrigger == 90, ref count);
            m.ZoomGain = 2;
            Check(ZoomZero(m) && m.Enabled, ref count);
            m.Update(Axes(2, 256), .01);
            Check(Near(m.ZoomInput, 1) && m.RightTrigger == 255, ref count);
            m.ZoomSpeed = .5;
            Check(ZoomZero(m) && m.Enabled, ref count);
            m.Update(Axes(2, 256), .02);
            Check(Near(m.ZoomInput, 1) && m.RightTrigger == 128, ref count);
            m.ZoomGain = 1; m.Update(Axes(2, 256), .03);
            Check(Near(m.ZoomInput, partial) && m.RightTrigger == 45, ref count);
            m.ZoomSpeed = .05; m.Update(Axes(2, 512), .04);
            Check(Near(m.ZoomInput, 1) && m.RightTrigger == 13, ref count);
            m.ZoomInvert = true;
            Check(ZoomZero(m), ref count);
            m.Update(Axes(2, 512), .05);
            Check(Near(m.ZoomInput, -1) && m.LeftTrigger == 13 && m.RightTrigger == 0, ref count);
            m.Update(Axes(2, -512), .06);
            Check(Near(m.ZoomInput, 1) && m.LeftTrigger == 0 && m.RightTrigger == 13, ref count);
            m.ZoomEnabled = false; m.Update(Axes(2, 512), .07);
            Check(ZoomZero(m) && m.Enabled && m.IsFresh, ref count);
            m.ZoomEnabled = true;
            Check(ZoomZero(m), ref count);
            m.Update(Axes(2, 512), .08);
            Check(m.LeftTrigger == 13 && m.RightTrigger == 0, ref count);

            // Zoom settings update live without resetting the original stick
            // or accepting an old trigger value under the new setting.
            m = Zoom(); m.Enable();
            m.Update(new short[] { 0, 0, 512, 256, 0, 256 }, 0);
            double sx = m.StickX, sy = m.StickY;
            m.ZoomEnabled = m.ZoomEnabled; m.ZoomInvert = m.ZoomInvert;
            m.ZoomGain = m.ZoomGain; m.ZoomDeadZone = m.ZoomDeadZone; m.ZoomSpeed = m.ZoomSpeed;
            Check(Near(m.StickX, sx) && Near(m.StickY, sy) && m.RightTrigger == 255 && m.IsFresh, ref count);
            m.ZoomDeadZone = .1;
            Check(ZoomZero(m) && Near(m.StickX, sx) && Near(m.StickY, sy) && m.IsFresh && m.Enabled, ref count);
            m.Step(.02);
            Check(Near(m.FieldOfViewDegrees, 60) && m.YawDegrees > 0 && m.PitchDegrees > 0, ref count);
            m.Update(new short[] { 0, 0, 512, 256, 0, 256 }, .02);
            Check(m.RightTrigger == 255 && Near(m.StickX, sx) && Near(m.StickY, sy), ref count);
            m.SourceX = 5;
            Check(Zero(m) && ZoomZero(m) && !m.IsFresh && m.Enabled, ref count);

            // XY steering and Z zoom use independent normalization. Even the
            // other unused channels cannot influence either trigger.
            RightStickModel a = Linear(), b = Linear(); a.Enable(); b.Enable();
            a.Update(new short[] { 0, 0, 0, 256, 0, 512 }, 0);
            b.Update(new short[] { 512, -512, 512, 256, 512, 512 }, 0);
            Check(Near(a.StickX, b.StickX) && Near(a.StickY, b.StickY) && ZoomZero(a) && b.RightTrigger == 128, ref count);
            m = Zoom(); m.Enable(); m.Update(new short[] { 512, -512, 0, 512, 512, -512 }, 0);
            Check(ZoomZero(m), ref count);

            // Centre correction applies to Z, including asymmetric raw values.
            m = Zoom(); m.Calibrate(new short[] { 10, 20, 30, 40, 50, 60 }); m.Enable();
            m.Update(new short[] { 10, 20, 30, 40, 50, 60 }, 0);
            Check(ZoomZero(m) && Zero(m), ref count);
            m.Update(new short[] { 10, 20, 542, 40, 50, 60 }, .01);
            Check(m.RightTrigger == 255 && m.LeftTrigger == 0 && Zero(m), ref count);
            m.Calibrate(new short[] { 0, 0, -100, 0, 0, 0 });
            Check(ZoomZero(m), ref count);
            m.Update(Axes(2, -612), .02);
            Check(m.LeftTrigger == 255 && m.RightTrigger == 0, ref count);
            m.ResetCalibration();
            Check(ZoomZero(m) && m.Centers[2] == 0, ref count);

            // Every path that neutralizes the stick also releases triggers;
            // stale input holds the FOV and cannot replay a disconnected gap.
            m = Zoom(); m.Enable(); m.Update(Axes(2, 512), 1); m.Step(1.04);
            fov = m.FieldOfViewDegrees;
            m.Step(1.2);
            Check(m.Enabled && !m.IsFresh && ZoomZero(m) && Near(m.FieldOfViewDegrees, fov), ref count);
            m.Step(10);
            Check(ZoomZero(m) && Near(m.FieldOfViewDegrees, fov), ref count);
            m.Update(Axes(2, 512), 10.01);
            Check(m.IsFresh && m.RightTrigger == 255 && Near(m.FieldOfViewDegrees, fov), ref count);
            m.Step(10.03);
            Check(Near(m.FieldOfViewDegrees, fov - 1.2), ref count);
            m.Disable(); fov = m.FieldOfViewDegrees;
            Check(!m.Enabled && !m.IsFresh && ZoomZero(m), ref count);
            m.Step(11); m.Update(Axes(2, -512), 11);
            Check(ZoomZero(m) && Near(m.FieldOfViewDegrees, fov), ref count);
            m.Enable();
            Check(ZoomZero(m) && Near(m.FieldOfViewDegrees, fov), ref count);
            m.Update(Axes(2, -512), 20); m.Step(20.14);
            Check(Near(m.FieldOfViewDegrees, fov + 3), ref count);
            foreach (double invalid in new[] { Double.NaN, Double.PositiveInfinity, -1.0, 1.0 })
            {
                m.Update(Axes(2, 512), 21); fov = m.FieldOfViewDegrees; m.Step(invalid);
                Check(ZoomZero(m) && !m.IsFresh && m.Enabled && Near(m.FieldOfViewDegrees, fov), ref count);
            }
            m.Update(Axes(2, 512), 30); m.Update(null, 30.01);
            Check(ZoomZero(m) && !m.IsFresh, ref count);
            m.Update(Axes(2, 512), 30.02); m.Update(new short[5], 30.03);
            Check(ZoomZero(m), ref count);
            m.Update(Axes(2, 512), 31); m.Update(Axes(2, -512), 30);
            Check(ZoomZero(m), ref count);

            // Zoom integrates per second, clamps at both useful FOV bounds,
            // and releasing or stopping holds the current magnification.
            double expectedFov = SimulateZoom(30, .5, 512, 1);
            Check(Near(expectedFov, 30), ref count);
            foreach (int rate in new[] { 60, 100, 120, 200 })
                Check(Near(SimulateZoom(rate, .5, 512, 1), expectedFov), ref count);
            Check(Near(SimulateZoom(100, .5, -512, 1), 90), ref count);
            Check(Near(SimulateZoom(100, 1, 512, .5), 60 - 128 / 255.0 * 60), ref count);
            m = Zoom(); m.Enable();
            for (int i = 0; i <= 200; i++) m.Update(Axes(2, 512), i / 100.0);
            Check(Near(m.FieldOfViewDegrees, 20) && m.RightTrigger == 255, ref count);
            m.Update(new short[6], 2); m.Step(2.02);
            Check(ZoomZero(m) && Near(m.FieldOfViewDegrees, 20), ref count);
            for (int i = 203; i <= 403; i++) m.Update(Axes(2, -512), i / 100.0);
            Check(Near(m.FieldOfViewDegrees, 100) && m.LeftTrigger == 255, ref count);
            m.Disable(); m.Step(5);
            Check(ZoomZero(m) && Near(m.FieldOfViewDegrees, 100), ref count);
            m.ResetView();
            Check(ZoomZero(m) && Near(m.FieldOfViewDegrees, 60) && Near(m.YawDegrees, 0) && Near(m.PitchDegrees, 0), ref count);
            string snapshot = m.GetSnapshot();
            Check(snapshot.Contains("ZoomTriggers=0,0") && snapshot.Contains("CameraFieldOfView=60.000"), ref count);

            // Invalid configuration never creates nonfinite or out-of-range
            // trigger values, and all numeric setters use consistent bounds.
            m.ZoomGain = Double.NaN; m.ZoomDeadZone = Double.PositiveInfinity; m.ZoomSpeed = Double.NegativeInfinity;
            Check(Near(m.ZoomGain, 1) && Near(m.ZoomDeadZone, .08) && Near(m.ZoomSpeed, .5), ref count);
            m.ZoomGain = -1; m.ZoomDeadZone = -1; m.ZoomSpeed = -1;
            Check(Near(m.ZoomGain, .1) && Near(m.ZoomDeadZone, 0) && Near(m.ZoomSpeed, .05), ref count);
            m.ZoomGain = 100; m.ZoomDeadZone = 1; m.ZoomSpeed = 100;
            Check(Near(m.ZoomGain, 3) && Near(m.ZoomDeadZone, .4) && Near(m.ZoomSpeed, 3), ref count);
            m.Enable(); m.Update(Axes(2, Int16.MaxValue), 0);
            Check(Finite(m.ZoomInput) && m.RightTrigger == 255 && m.LeftTrigger == 0 && Finite(m.FieldOfViewDegrees), ref count);
        }
        private static void RunDirectionalZoomChecks(ref int count)
        {
            RightStickModel m = new RightStickModel();
            Check(Near(m.ZoomInGain, 1) && Near(m.ZoomOutGain, 1)
                && Near(m.ZoomInSpeed, .5) && Near(m.ZoomOutSpeed, .5)
                && Near(m.ZoomInDeadZone, .08) && Near(m.ZoomOutDeadZone, .08), ref count);

            // The two actions can require different movement and have different
            // maximum rates, with no trigger leakage into the other direction.
            m = Zoom(); m.ZoomInGain = 2; m.ZoomOutGain = .5;
            m.ZoomInSpeed = .75; m.ZoomOutSpeed = .25; m.Enable();
            m.Update(Axes(2, 256), 0);
            Check(Near(m.ZoomInput, 1) && m.RightTrigger == 191 && m.LeftTrigger == 0, ref count);
            m.Update(Axes(2, -256), .01);
            Check(Near(m.ZoomInput, -.125) && m.LeftTrigger == 8 && m.RightTrigger == 0, ref count);
            m.ZoomOutGain = 3; m.ZoomOutSpeed = 1;
            m.Update(Axes(2, 256), .02);
            Check(Near(m.ZoomInput, 1) && m.RightTrigger == 191 && m.LeftTrigger == 0, ref count);
            m.Update(Axes(2, -256), .03);
            Check(Near(m.ZoomInput, -1) && m.LeftTrigger == 255 && m.RightTrigger == 0, ref count);
            m.ZoomInGain = .5; m.ZoomInSpeed = .1;
            m.Update(Axes(2, -512), .04);
            Check(Near(m.ZoomInput, -1) && m.LeftTrigger == 255 && m.RightTrigger == 0, ref count);
            m.Update(Axes(2, 512), .05);
            Check(Near(m.ZoomInput, Math.Pow(.5, 1.5)) && m.RightTrigger == 9 && m.LeftTrigger == 0, ref count);

            // Inversion changes the physical movement assigned to an action;
            // the action's gain and speed remain its own settings.
            m.ZoomInvert = true;
            Check(ZoomZero(m) && m.Enabled, ref count);
            m.Update(Axes(2, 512), .06);
            Check(Near(m.ZoomInput, -1) && m.LeftTrigger == 255 && m.RightTrigger == 0, ref count);
            m.Update(Axes(2, -512), .07);
            Check(Near(m.ZoomInput, Math.Pow(.5, 1.5)) && m.RightTrigger == 9 && m.LeftTrigger == 0, ref count);

            // Resting thresholds are independent too, and are selected after
            // inversion before gain, so sensitivity cannot bypass a dead zone.
            m = Zoom(); m.ZoomInDeadZone = .25; m.ZoomOutDeadZone = 0;
            m.Enable(); m.Update(Axes(2, 64), 0);
            Check(ZoomZero(m), ref count);
            m.Update(Axes(2, -64), .01);
            Check(m.LeftTrigger == 11 && m.RightTrigger == 0, ref count);
            m.ZoomInGain = 3; m.Update(Axes(2, 64), .02);
            Check(ZoomZero(m), ref count);
            m.ZoomInGain = 1; m.ZoomInvert = true;
            m.Update(Axes(2, 64), .03);
            Check(m.LeftTrigger == 11 && m.RightTrigger == 0, ref count);
            m.Update(Axes(2, -64), .04);
            Check(ZoomZero(m), ref count);
            m.ZoomOutDeadZone = .3; m.ZoomInDeadZone = 0;
            m.Update(Axes(2, -64), .05);
            Check(m.RightTrigger == 11 && m.LeftTrigger == 0, ref count);
            m.Update(Axes(2, 64), .06);
            Check(ZoomZero(m), ref count);

            // Live direction settings neutralize only zoom. Reapplying an
            // unchanged configuration does not clear a current trigger.
            m = Zoom(); m.Enable();
            m.Update(new short[] { 0, 0, 512, 256, 0, 256 }, 0);
            double sx = m.StickX, sy = m.StickY;
            m.ZoomInGain = m.ZoomInGain; m.ZoomOutGain = m.ZoomOutGain;
            m.ZoomInSpeed = m.ZoomInSpeed; m.ZoomOutSpeed = m.ZoomOutSpeed;
            m.ZoomInDeadZone = m.ZoomInDeadZone; m.ZoomOutDeadZone = m.ZoomOutDeadZone;
            Check(m.RightTrigger == 255 && Near(m.StickX, sx) && Near(m.StickY, sy), ref count);
            m.ZoomOutSpeed = .2;
            Check(ZoomZero(m) && m.Enabled && m.IsFresh && Near(m.StickX, sx) && Near(m.StickY, sy), ref count);
            m.Update(new short[] { 0, 0, -512, 256, 0, 256 }, .01);
            Check(m.LeftTrigger == 51 && m.RightTrigger == 0 && Near(m.StickX, sx) && Near(m.StickY, sy), ref count);
            m.ZoomInDeadZone = .1;
            Check(ZoomZero(m) && m.Enabled && m.IsFresh && Near(m.StickX, sx) && Near(m.StickY, sy), ref count);
            m.Update(Axes(2, -512), .02); m.Step(.2);
            Check(ZoomZero(m) && !m.IsFresh && m.Enabled, ref count);
            m.Update(Axes(2, -512), 1);
            Check(m.LeftTrigger == 51 && m.RightTrigger == 0 && m.IsFresh, ref count);
            m.Disable();
            Check(ZoomZero(m) && !m.Enabled, ref count);

            // The camera consumes the actual direction-specific trigger rate.
            m = Zoom(); m.ZoomInSpeed = .25; m.ZoomOutSpeed = .75; m.Enable();
            m.Update(Axes(2, 512), 0); m.Step(.05);
            double fov = 60 - 64 / 255.0 * 60 * .05;
            Check(Near(m.FieldOfViewDegrees, fov), ref count);
            m.Update(Axes(2, -512), .05); m.Step(.1);
            fov += 191 / 255.0 * 60 * .05;
            Check(Near(m.FieldOfViewDegrees, fov), ref count);
            m.Update(new short[6], .1); m.Step(.12);
            Check(Near(m.FieldOfViewDegrees, fov) && ZoomZero(m), ref count);
            m.ZoomInvert = true; m.Update(Axes(2, 512), .12); m.Step(.14);
            Check(Near(m.FieldOfViewDegrees, fov + 191 / 255.0 * 60 * .02), ref count);

            m = Zoom(); m.ZoomInSpeed = 3; m.ZoomOutSpeed = 2.4; m.Enable();
            m.Update(Axes(2, 256), 0); m.Step(.05);
            fov = 60 - Math.Pow(.5, 1.5) * 3 * 60 * .05;
            Check(m.RightTrigger == 255 && m.LeftTrigger == 0 && Near(m.FieldOfViewDegrees, fov), ref count);
            m.Update(Axes(2, -512), .05); m.Step(.1);
            Check(m.LeftTrigger == 255 && m.RightTrigger == 0 && Near(m.FieldOfViewDegrees, fov + 7.2), ref count);

            // Old shared setters deliberately initialize both directions;
            // each new setter clamps without modifying the opposite setting.
            m.ZoomGain = 2; m.ZoomSpeed = .3; m.ZoomDeadZone = .12;
            Check(Near(m.ZoomInGain, 2) && Near(m.ZoomOutGain, 2)
                && Near(m.ZoomInSpeed, .3) && Near(m.ZoomOutSpeed, .3)
                && Near(m.ZoomInDeadZone, .12) && Near(m.ZoomOutDeadZone, .12), ref count);
            m.ZoomInGain = Double.NaN; m.ZoomOutGain = Double.PositiveInfinity;
            m.ZoomInSpeed = Double.NegativeInfinity; m.ZoomOutSpeed = Double.NaN;
            m.ZoomInDeadZone = Double.NaN; m.ZoomOutDeadZone = Double.PositiveInfinity;
            Check(Near(m.ZoomInGain, 1) && Near(m.ZoomOutGain, 1)
                && Near(m.ZoomInSpeed, .5) && Near(m.ZoomOutSpeed, .5)
                && Near(m.ZoomInDeadZone, .08) && Near(m.ZoomOutDeadZone, .08), ref count);
            m.ZoomInGain = -1; m.ZoomOutGain = 100;
            m.ZoomInSpeed = -1; m.ZoomOutSpeed = 100;
            m.ZoomInDeadZone = -1; m.ZoomOutDeadZone = 100;
            Check(Near(m.ZoomInGain, .1) && Near(m.ZoomOutGain, 3)
                && Near(m.ZoomInSpeed, .05) && Near(m.ZoomOutSpeed, 3)
                && Near(m.ZoomInDeadZone, 0) && Near(m.ZoomOutDeadZone, .4), ref count);
            m.ZoomInGain = 2;
            Check(Near(m.ZoomOutGain, 3) && Near(m.ZoomOutSpeed, 3) && Near(m.ZoomOutDeadZone, .4), ref count);
        }
        private static void RunDominantAxisChecks(ref int count)
        {
            RightStickModel m = Zoom();
            m.DominantAxisEnabled = true;
            m.ZoomInGain = 3;
            m.Enable();

            // Physical XY selects movement even when neither axis is mapped to the stick.
            m.Update(new short[] { 300, -270, 100, 0, 0, 0 }, 0);
            Check(m.DominantGroup == DominantAxisGroup.Movement && ZoomZero(m), ref count);
            m.Update(new short[] { 300, -270, 512, 160, 0, 175 }, .02);
            m.Step(.03);
            Check(m.DominantGroup == DominantAxisGroup.Movement && m.OutputX > 0 && m.OutputY > 0
                && ZoomZero(m) && Near(m.FieldOfViewDegrees, 60), ref count);

            m.Update(new short[6], .04);
            m.Update(new short[6], .09);
            Check(m.DominantGroup == DominantAxisGroup.None && Zero(m) && ZoomZero(m), ref count);
            short[] zoomWithCrossTalk = { 24, 0, 448, 80, 0, 96 };
            m.Update(zoomWithCrossTalk, .10);
            Check(m.DominantGroup == DominantAxisGroup.None && Zero(m) && ZoomZero(m), ref count);
            m.Update(zoomWithCrossTalk, .14);
            double yaw = m.YawDegrees;
            m.Step(.16);
            Check(m.DominantGroup == DominantAxisGroup.Z && Zero(m) && m.RightTrigger > 0
                && m.LeftTrigger == 0 && Near(m.YawDegrees, yaw) && m.FieldOfViewDegrees < 60, ref count);
            m.Update(Axes(5, 512), .17);
            Check(m.DominantGroup == DominantAxisGroup.Z && Zero(m) && ZoomZero(m), ref count);
            double fov = m.FieldOfViewDegrees;
            m.Step(.34);
            Check(m.DominantGroup == DominantAxisGroup.None && m.Enabled && !m.IsFresh
                && Zero(m) && ZoomZero(m) && Near(m.FieldOfViewDegrees, fov), ref count);
            m.Update(Axes(5, 512), .35);
            Check(m.DominantGroup == DominantAxisGroup.Movement && m.OutputX > 0, ref count);

            m.DominantAxisEnabled = false;
            Check(m.Enabled && Zero(m) && ZoomZero(m) && Near(m.FieldOfViewDegrees, fov), ref count);
            m.Update(zoomWithCrossTalk, .36);
            Check(m.OutputX > 0 && m.OutputY > 0 && m.RightTrigger > 0, ref count);
            m.DominantAxisEnabled = true;
            m.ZoomEnabled = false;
            m.Update(Axes(5, 20), .37);
            Check(m.DominantGroup == DominantAxisGroup.None && m.OutputX > 0 && ZoomZero(m), ref count);

            m = Zoom();
            m.DominantAxisEnabled = true;
            m.ZoomInDeadZone = .4;
            m.Enable();
            m.Update(new short[] { 0, 0, 176, 0, 0, 90 }, 0);
            Check(m.DominantGroup == DominantAxisGroup.Movement && m.OutputX > 0 && ZoomZero(m), ref count);

            // Calibrated offsets and a duplicate Z steering binding cannot win movement.
            m.SourceX = 2;
            m.Calibrate(new short[] { 450, 400, 128, 0, 0, 0 });
            short[] calibratedZoom = { 450, 400, 384, 0, 0, 0 };
            m.Update(calibratedZoom, .02);
            m.Update(calibratedZoom, .06);
            Check(m.DominantGroup == DominantAxisGroup.Z && Zero(m) && m.RightTrigger > 0, ref count);
            m.Disable();
            Check(m.DominantGroup == DominantAxisGroup.None && Zero(m) && ZoomZero(m), ref count);
        }

        private static RightStickModel Zoom() { return new RightStickModel { DeadZone = 0, Gamma = 1, InvertY = false, ZoomDeadZone = 0, ZoomSpeed = 1 }; }
        private static bool ZoomZero(RightStickModel m) { return m.ZoomInput == 0 && m.LeftTrigger == 0 && m.RightTrigger == 0; }
        private static double SimulateZoom(int rate, double duration, short z, double speed)
        {
            RightStickModel m = Zoom(); m.ZoomSpeed = speed; m.Enable();
            for (int i = 0; i <= (int)(rate * duration); i++) { double t = (double)i / rate; m.Update(Axes(2, z), t); m.Step(t); }
            return m.FieldOfViewDegrees;
        }
        private static RightStickModel Linear() { return new RightStickModel { DeadZone = 0, Gamma = 1, InvertY = false }; }
        private static short[] Axes(int index, short value) { short[] axes = new short[6]; axes[index] = value; return axes; }
        private static bool Zero(RightStickModel m) { return m.StickX == 0 && m.StickY == 0 && m.OutputX == 0 && m.OutputY == 0; }
        private static bool Near(double a, double b) { return Math.Abs(a - b) < .000001; }
        private static bool Finite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
        private static double Simulate(int rate, double duration, bool vertical)
        {
            RightStickModel m = Linear(); m.Enable();
            for (int i = 0; i <= (int)(rate * duration); i++) { double t = (double)i / rate; m.Update(Axes(vertical ? 3 : 5, 512), t); m.Step(t); }
            return vertical ? m.PitchDegrees : m.YawDegrees;
        }
        private static void Check(bool condition, ref int count) { count++; if (!condition) throw new InvalidOperationException("Right stick assertion " + count + " failed."); }
    }
}
