using System;

namespace SpaceKatMonitor
{
    public static class MouseControlSessionTests
    {
        public static string Run()
        {
            int checks = 0;
            MouseControlSession session = new MouseControlSession();
            Check(!session.Enabled && Double.IsNaN(session.LastSampleSeconds), ref checks);
            Check(session.GetPhase(0) == MouseControlPhase.Off, ref checks);
            session.ObserveXY(10);
            Check(!session.Enabled && Double.IsNaN(session.LastSampleSeconds), ref checks);

            session.Enable();
            Check(session.Enabled && session.GetPhase(10) == MouseControlPhase.WaitingForData, ref checks);
            session.ObserveXY(10);
            Check(session.GetPhase(10) == MouseControlPhase.Active, ref checks);
            Check(session.GetPhase(10.25) == MouseControlPhase.Active, ref checks);
            Check(session.GetPhase(10.251) == MouseControlPhase.WaitingForData && session.Enabled, ref checks);
            Check(session.GetPhase(80) == MouseControlPhase.WaitingForData && session.Enabled, ref checks);
            session.ObserveXY(80.01);
            Check(session.GetPhase(80.01) == MouseControlPhase.Active && session.Enabled, ref checks);

            session.Disable();
            Check(!session.Enabled && Double.IsNaN(session.LastSampleSeconds), ref checks);
            session.ObserveXY(81);
            Check(session.GetPhase(81) == MouseControlPhase.Off && !session.Enabled, ref checks);
            session.Enable();
            Check(session.GetPhase(81) == MouseControlPhase.WaitingForData, ref checks);
            session.ObserveXY(82);
            Check(session.GetPhase(82) == MouseControlPhase.Active, ref checks);
            session.Enable();
            Check(Double.IsNaN(session.LastSampleSeconds) && session.GetPhase(82) == MouseControlPhase.WaitingForData, ref checks);

            foreach (double invalid in new double[] { Double.NaN, Double.PositiveInfinity, Double.NegativeInfinity, -1 })
            {
                session.Enable();
                session.ObserveXY(10);
                session.ObserveXY(invalid);
                Check(session.Enabled && Double.IsNaN(session.LastSampleSeconds), ref checks);
                Check(session.GetPhase(11) == MouseControlPhase.WaitingForData, ref checks);
                session.ObserveXY(12);
                Check(session.GetPhase(12) == MouseControlPhase.Active, ref checks);
                Check(session.GetPhase(invalid) == MouseControlPhase.WaitingForData && session.Enabled, ref checks);
                Check(session.GetPhase(13) == MouseControlPhase.WaitingForData, ref checks);
                session.ObserveXY(14);
                Check(session.GetPhase(14) == MouseControlPhase.Active, ref checks);
            }

            session.Enable();
            session.ObserveXY(10);
            Check(session.GetPhase(9) == MouseControlPhase.WaitingForData && session.Enabled, ref checks);
            Check(session.GetPhase(10) == MouseControlPhase.WaitingForData, ref checks);
            session.ObserveXY(11);
            Check(session.GetPhase(11) == MouseControlPhase.Active, ref checks);
            session.ObserveXY(10);
            Check(Double.IsNaN(session.LastSampleSeconds) && session.Enabled, ref checks);
            Check(session.GetPhase(10) == MouseControlPhase.WaitingForData, ref checks);
            session.ObserveXY(10);
            Check(session.GetPhase(10) == MouseControlPhase.Active, ref checks);

            // Regression for the reported failure: quiet data pauses the
            // motion model while the user's enabled intent stays latched.
            MouseMotionModel motion = new MouseMotionModel();
            session.Enable();
            session.ObserveXY(0);
            motion.Update(512, 0, 0);
            Check(session.GetPhase(0) == MouseControlPhase.Active && Zero(motion.Step(0)), ref checks);
            Check(motion.Step(0.02).X > 0, ref checks);
            Check(session.GetPhase(0.251) == MouseControlPhase.WaitingForData && session.Enabled, ref checks);
            motion.Reset();
            Check(Zero(motion.Step(0.251)) && Zero(motion.Step(10)), ref checks);
            Check(session.GetPhase(10) == MouseControlPhase.WaitingForData && session.Enabled, ref checks);
            session.ObserveXY(10.1);
            motion.Update(512, 0, 10.1);
            Check(session.GetPhase(10.1) == MouseControlPhase.Active, ref checks);
            Check(Zero(motion.Step(10.1)), ref checks);
            Check(motion.Step(10.12).X > 0 && session.Enabled, ref checks);
            session.Disable();
            motion.Reset();
            Check(session.GetPhase(10.13) == MouseControlPhase.Off && Zero(motion.Step(10.13)), ref checks);

            return "PASS: " + checks + " mouse session checks\r\n" +
                "Keep enabled intent through 250ms/long silence; resume only on fresh XY\r\n" +
                "Explicit stop and restart discard old samples; invalid clocks pause without disabling\r\n" +
                "Integration: motion -> pause -> ten seconds quiet -> automatic fresh-data recovery without jumps\r\n";
        }

        private static bool Zero(MouseDelta delta)
        {
            return delta.X == 0 && delta.Y == 0;
        }

        private static void Check(bool condition, ref int count)
        {
            count++;
            if (!condition) throw new InvalidOperationException("Mouse session assertion " + count + " failed.");
        }
    }
}
