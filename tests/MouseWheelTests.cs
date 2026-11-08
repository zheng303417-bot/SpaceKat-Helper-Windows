using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;

namespace SpaceKatMonitor
{
    internal static class MouseWheelTests
    {
        public static string Run()
        {
            int count = 0;
            MappingChecks(ref count);
            TimingChecks(ref count);
            ConfigurationChecks(ref count);
            OutputChecks(ref count);
            return "PASS: " + count + " Z-axis wheel model and output checks (fake desktop backend)\r\n" +
                "Independent up/down force, speed and dead zones; inversion, calibration and whole detents\r\n" +
                "Neutral/reversal/stale resets, clock and frame-rate guards; signed native payload and held-button cleanup\r\n";
        }

        private static MouseWheelModel Linear()
        {
            return new MouseWheelModel { DeadZone = 0, UpSpeed = 10, DownSpeed = 10 };
        }

        private static void MappingChecks(ref int count)
        {
            MouseWheelModel m = new MouseWheelModel();
            Check(m.Enabled && !m.Invert && m.Center == 0 && m.Input == 0, ref count);
            Check(m.UpGain == 1 && m.DownGain == 1 && m.UpSpeed == 6 && m.DownSpeed == 6, ref count);
            Check(m.UpDeadZone == .08 && m.DownDeadZone == .08 && m.Step(0) == 0 && m.Step(10) == 0, ref count);
            foreach (short z in new short[] { -512, 512, Int16.MinValue, Int16.MaxValue })
            {
                m = Linear(); m.Update(z, 0);
                Check(m.Input == Math.Sign(z), ref count);
                Check(m.Step(0) == 0 && m.Step(.05) == 0 && m.Step(.1) == Math.Sign(z) * 120, ref count);
            }
            m = Linear(); m.UpDeadZone = .125; m.DownDeadZone = .25;
            foreach (short z in new short[] { -128, -64, -1, 0, 1, 64 })
            {
                m.Update(z, 0);
                Check(m.Input == 0, ref count);
            }
            m.Update(65, 0); Check(m.Input > 0, ref count);
            m.Update(-129, 0); Check(m.Input < 0, ref count);
            m.UpGain = 3; m.Update(64, 0);
            Check(m.Input == 0, ref count);
            m.Update(128, 0);
            Check(Near(m.Input, Math.Pow((.25 - .125) / .875 * 3, 1.5)), ref count);

            m = Linear(); m.UpGain = 2; m.DownGain = .5; m.UpSpeed = 12; m.DownSpeed = 4;
            m.Update(256, 0); Check(m.Input == 1, ref count);
            m.Update(-256, 0); Check(Near(m.Input, -.125), ref count);
            m.Update(256, 0); m.Step(0); m.Step(.05);
            Check(m.Step(.1) == 120, ref count);
            double input = m.Input;
            m.UpSpeed = 1;
            Check(m.Input == 0, ref count);
            m.Update(256, .11); Check(m.Input == input, ref count);
            Check(Travel(m, 60, 1, 256) == 120, ref count);
            Check(Travel(m, 60, 2, -256) == -120, ref count);

            // Inversion selects action settings after the sign is changed.
            m = Linear(); m.UpGain = 2; m.DownGain = .5; m.Invert = true;
            m.Update(256, 0); Check(Near(m.Input, -.125), ref count);
            m.Update(-256, 0); Check(m.Input == 1, ref count);
            m.UpDeadZone = .125; m.DownDeadZone = .25;
            m.Update(128, 0); Check(m.Input == 0, ref count);
            m.Update(-128, 0); Check(m.Input > 0, ref count);

            m = Linear(); m.Center = 123;
            m.Update(123, 0); Check(m.Input == 0, ref count);
            m.Update(635, 0); Check(m.Input == 1, ref count);
            m.Update(-389, 0); Check(m.Input == -1, ref count);
            m.Center = Int16.MinValue; m.Update(Int16.MaxValue, 0);
            Check(m.Input == 1, ref count);
            m.Center = Int16.MaxValue; m.Update(Int16.MinValue, 0);
            Check(m.Input == -1, ref count);
        }

        private static void TimingChecks(ref int count)
        {
            MouseWheelModel m = Linear(); m.UpSpeed = 30;
            m.Update(512, 0);
            Check(m.Step(0) == 0 && m.Step(.05) == 120 && m.Step(.05) == 0 && m.Step(.1) == 240, ref count);
            m = Linear(); m.UpSpeed = 120; m.DownSpeed = 90;
            m.Update(512, 0); m.Step(0);
            Check(m.Step(.05) == 6 * MouseWheelModel.WheelDelta, ref count);
            m.Update(-512, .05);
            Check(m.Step(.1) == -4 * MouseWheelModel.WheelDelta, ref count);
            foreach (int rate in new[] { 30, 60, 100, 120 })
            {
                m = Linear(); m.UpSpeed = 6; m.DownSpeed = 9;
                Check(Travel(m, rate, 2, 512) == 1440 && Travel(m, rate, 2, -512) == -2160, ref count);
            }
            m = Linear(); m.UpSpeed = m.DownSpeed = 6;
            m.Update(512, 0); m.Step(0);
            Check(m.Step(.05) == 0 && m.Step(.1) == 0 && m.Step(.15) == 0, ref count);
            m.Update(0, .16); Check(m.Input == 0 && m.Step(.16) == 0, ref count);
            m.Update(512, .17);
            Check(m.Step(.17) == 0 && m.Step(.22) == 0 && m.Step(.27) == 0, ref count);
            m = Linear(); m.UpSpeed = m.DownSpeed = 6;
            m.Update(512, 0); m.Step(0); m.Step(.05); m.Step(.1); m.Step(.15);
            m.Update(-512, .16);
            Check(m.Step(.16) == 0 && m.Step(.21) == 0 && m.Step(.26) == 0 && m.Step(.31) == 0, ref count);
            Check(m.Step(.36) == -120, ref count);

            m = Linear(); m.UpSpeed = 30;
            m.Update(512, 0); m.Step(0);
            Check(m.Step(.2) == 120 && m.Step(.21) == 0, ref count);
            Check(m.Step(.251) == 0 && m.Input == 0 && m.Enabled, ref count);
            Check(m.Step(10) == 0, ref count);
            m.Update(512, 10);
            Check(m.Step(10) == 0 && m.Step(10.05) == 120, ref count);
            // Receiving a fresh frame after a gap must itself invalidate old fractions.
            m = Linear(); m.UpSpeed = 6;
            m.Update(512, 0); m.Step(0); m.Step(.1);
            m.Update(512, 10);
            Check(m.Step(10) == 0 && m.Step(10.05) == 0 && m.Step(10.1) == 0, ref count);

            foreach (double bad in new[] { Double.NaN, Double.PositiveInfinity, Double.NegativeInfinity, -1.0 })
            {
                m = Linear(); m.Update(512, 0); m.Step(0); m.Step(.05);
                Check(m.Step(bad) == 0 && m.Input == 0 && m.Step(.1) == 0, ref count);
                m.Update(512, 1); m.Step(1);
                m.Update(512, bad);
                Check(m.Input == 0 && m.Step(1.05) == 0, ref count);
            }
            m = Linear(); m.Update(512, 2); m.Step(2);
            Check(m.Step(1.9) == 0 && m.Input == 0, ref count);
            m.Update(512, 3); m.Step(3); m.Update(512, 2.9);
            Check(m.Input == 0 && m.Step(3.05) == 0, ref count);
            m.Update(512, 5);
            Check(m.Step(4.9) == 0 && m.Input == 0, ref count);
        }

        private static void ConfigurationChecks(ref int count)
        {
            MouseWheelModel m = Linear(); m.Update(512, 0); m.Step(0); m.Step(.05);
            // Repeated UI assignments leave the fractional detent intact.
            m.Enabled = m.Enabled; m.Invert = m.Invert; m.Center = m.Center;
            m.UpGain = m.UpGain; m.DownGain = m.DownGain;
            m.UpDeadZone = m.UpDeadZone; m.DownDeadZone = m.DownDeadZone;
            m.UpSpeed = m.UpSpeed; m.DownSpeed = m.DownSpeed;
            Check(m.Input == 1 && m.Step(.1) == 120, ref count);
            m.DownSpeed = 20;
            Check(m.Input == 0 && m.Step(.11) == 0, ref count);
            m.Update(512, .12); m.Step(.12);
            Check(m.Step(.17) == 0, ref count);
            m.Enabled = false;
            m.Update(512, .18);
            Check(m.Input == 0 && m.Step(.2) == 0, ref count);
            m.Enabled = true; Check(m.Step(1) == 0, ref count);
            m.Update(512, 1); Check(m.Step(1) == 0 && m.Step(1.05) == 0 && m.Step(1.1) == 120, ref count);

            m.UpGain = Double.MaxValue; m.DownGain = -1;
            m.UpSpeed = Double.MaxValue; m.DownSpeed = -1;
            m.UpDeadZone = Double.MaxValue; m.DownDeadZone = -1;
            Check(m.UpGain == 3 && m.DownGain == .1 && m.UpSpeed == 120 && m.DownSpeed == 1
                && m.UpDeadZone == .4 && m.DownDeadZone == 0, ref count);
            foreach (double bad in new[] { Double.NaN, Double.PositiveInfinity, Double.NegativeInfinity })
            {
                m.UpGain = m.DownGain = bad; m.UpSpeed = m.DownSpeed = bad;
                m.UpDeadZone = m.DownDeadZone = bad;
                Check(m.UpGain == 1 && m.DownGain == 1 && m.UpSpeed == 6 && m.DownSpeed == 6
                    && m.UpDeadZone == .08 && m.DownDeadZone == .08, ref count);
            }
            m.DeadZone = .2; Check(m.UpDeadZone == .2 && m.DownDeadZone == .2, ref count);
            m.Center = 42; m.Invert = true; m.UpGain = 2; m.DownSpeed = 15;
            m.Update(512, 0); m.Reset();
            Check(m.Input == 0 && m.Center == 42 && m.Invert && m.UpGain == 2 && m.DownSpeed == 15
                && m.Enabled && m.Step(10) == 0, ref count);
        }

        private static void OutputChecks(ref int count)
        {
            FakeBackend fake = new FakeBackend();
            MouseOutput output = new MouseOutput(new IntPtr(1), fake);
            string error;
            Check(output.TryScroll(0, out error) && error == null && fake.Calls.Count == 0, ref count);
            Check(!output.TryScroll(120, out error) && error != null && fake.Calls.Count == 0, ref count);
            output.Arm();
            foreach (int request in new[] { 120, -120, 1, -1, 1200, -1200, Int32.MaxValue, Int32.MinValue })
            {
                Check(output.TryScroll(request, out error) && error == null && output.StopHotkeyAvailable, ref count);
                MouseOutputPacket p = fake.Last;
                Check(p.Data == Math.Max(-1200, Math.Min(1200, request)) && p.Flags == 0x0800 && p.X == 0 && p.Y == 0, ref count);
                Check(!output.HasPendingRelease, ref count);
            }
            Check(output.TryMove(3, -4, out error) && fake.Last.Data == 0 && fake.Last.Flags == 1, ref count);
            Check(output.TryButtons(new[] { MouseButtonSignal.LeftDown }, out error) && output.IsLeftDown, ref count);
            Check(output.TryScroll(-120, out error) && output.IsLeftDown && output.HasPendingRelease, ref count);
            output.Disarm();
            Check(!output.HasPendingRelease && fake.Last.Flags == 4 && fake.Last.Data == 0, ref count);
            output.Dispose();
            Check(!output.TryScroll(-120, out error), ref count);

            fake = new FakeBackend { RegisterAllowed = false };
            output = new MouseOutput(new IntPtr(1), fake); output.Arm();
            Check(!output.StopHotkeyAvailable && !output.TryScroll(120, out error) && fake.Calls.Count == 0, ref count);
            output.Dispose();

            foreach (bool throws in new[] { false, true })
            {
                fake = new FakeBackend(); output = new MouseOutput(new IntPtr(1), fake); output.Arm();
                output.TryButtons(new[] { MouseButtonSignal.LeftDown }, out error);
                fake.Results.Enqueue(throws ? (object)new InvalidOperationException("fake wheel exception") : (object)(uint)0);
                Check(!output.TryScroll(120, out error) && error != null && !output.StopHotkeyAvailable, ref count);
                Check(!output.HasPendingRelease && fake.Last.Flags == 4 && fake.Last.Data == 0 && fake.UnregisterCount == 1, ref count);
                Check(!output.TryScroll(120, out error), ref count);
                output.Dispose();
            }
            fake = new FakeBackend(); output = new MouseOutput(new IntPtr(1), fake); output.Arm();
            output.TryButtons(new[] { MouseButtonSignal.LeftDown }, out error);
            fake.Results.Enqueue((uint)0); fake.Results.Enqueue((uint)0); fake.Results.Enqueue((uint)0); fake.Results.Enqueue((uint)0);
            Check(!output.TryScroll(-120, out error) && output.HasPendingRelease && output.LastReleaseError != null, ref count);
            output.Disarm(); Check(!output.HasPendingRelease && output.LastReleaseError == null, ref count);
            output.Dispose();

            // A stationary XY input must not suppress the independent wheel path.
            fake = new FakeBackend(); output = new MouseOutput(new IntPtr(1), fake); output.Arm();
            MouseMotionModel xy = new MouseMotionModel();
            MouseWheelModel wheel = Linear();
            xy.Update(0, 0, 0); wheel.Update(512, 0); xy.Step(0); wheel.Step(0);
            MouseDelta delta = xy.Step(.05); int scroll = wheel.Step(.05);
            delta = xy.Step(.1); scroll += wheel.Step(.1);
            Check(delta.X == 0 && delta.Y == 0 && scroll == 120, ref count);
            Check(output.TryScroll(scroll, out error) && fake.Last.Flags == 0x0800 && fake.Last.Data == 120
                && fake.Last.X == 0 && fake.Last.Y == 0, ref count);
            output.Dispose();

            NativePayloadChecks(ref count);
        }

        private static void NativePayloadChecks(ref int count)
        {
            MethodInfo convert = typeof(MouseOutput).GetMethod("ToNativeInput", BindingFlags.Static | BindingFlags.NonPublic);
            Check(convert != null, ref count);
            Type nativeType = convert.ReturnType;
            int unionOffset = Marshal.OffsetOf(nativeType, "Data").ToInt32();
            Check(MouseOutput.NativeInputSize == (IntPtr.Size == 8 ? 40 : 28) && unionOffset == (IntPtr.Size == 8 ? 8 : 4), ref count);
            IntPtr memory = Marshal.AllocHGlobal(MouseOutput.NativeInputSize);
            try
            {
                foreach (int delta in new[] { 120, -120, 1200, -1200 })
                {
                    object native = convert.Invoke(null, new object[] { new MouseOutputPacket { Flags = 0x0800, Data = delta } });
                    Marshal.StructureToPtr(native, memory, false);
                    Check(Marshal.ReadInt32(memory, 0) == 0 && Marshal.ReadInt32(memory, unionOffset) == 0
                        && Marshal.ReadInt32(memory, unionOffset + 4) == 0, ref count);
                    Check(Marshal.ReadInt32(memory, unionOffset + 8) == delta
                        && Marshal.ReadInt32(memory, unionOffset + 12) == 0x0800, ref count);
                }
                object move = convert.Invoke(null, new object[] { new MouseOutputPacket { X = 3, Y = -4, Flags = 1, Data = -120 } });
                Marshal.StructureToPtr(move, memory, false);
                Check(Marshal.ReadInt32(memory, unionOffset) == 3 && Marshal.ReadInt32(memory, unionOffset + 4) == -4
                    && Marshal.ReadInt32(memory, unionOffset + 8) == 0 && Marshal.ReadInt32(memory, unionOffset + 12) == 1, ref count);
                foreach (uint flag in new uint[] { 0x80, 0x100 })
                {
                    object side = convert.Invoke(null, new object[] { new MouseOutputPacket { Flags = flag, Data = 2 } });
                    Marshal.StructureToPtr(side, memory, false);
                    Check(Marshal.ReadInt32(memory, unionOffset + 8) == 2
                        && Marshal.ReadInt32(memory, unionOffset + 12) == flag, ref count);
                }
            }
            finally { Marshal.FreeHGlobal(memory); }
        }

        private sealed class FakeBackend : IMouseOutputBackend
        {
            public readonly List<MouseOutputPacket[]> Calls = new List<MouseOutputPacket[]>();
            public readonly Queue<object> Results = new Queue<object>();
            public bool RegisterAllowed = true;
            public int UnregisterCount;
            public int LastError { get { return 5; } }
            public MouseOutputPacket Last { get { MouseOutputPacket[] packets = Calls[Calls.Count - 1]; return packets[packets.Length - 1]; } }
            public bool RegisterStopHotkey(IntPtr hwnd) { return RegisterAllowed; }
            public void UnregisterStopHotkey(IntPtr hwnd) { UnregisterCount++; }
            public uint Send(MouseOutputPacket[] packets)
            {
                Calls.Add((MouseOutputPacket[])packets.Clone());
                if (Results.Count == 0) return (uint)packets.Length;
                object result = Results.Dequeue();
                Exception exception = result as Exception;
                if (exception != null) throw exception;
                return (uint)result;
            }
        }

        private static long Travel(MouseWheelModel model, int rate, double duration, short z)
        {
            model.Reset(); model.Update(z, 0); model.Step(0);
            long total = 0;
            for (int i = 1; i <= (int)(rate * duration); i++)
            {
                double now = i / (double)rate;
                model.Update(z, now);
                total += model.Step(now);
            }
            return total;
        }
        private static bool Near(double a, double b) { return Math.Abs(a - b) < 1e-9; }
        private static void Check(bool value, ref int count)
        {
            count++;
            if (!value) throw new InvalidOperationException("Mouse wheel assertion " + count + " failed.");
        }
    }
}
