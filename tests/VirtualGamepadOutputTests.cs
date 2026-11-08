using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SpaceKatMonitor
{
    internal static class VirtualGamepadOutputTests
    {
        private sealed class FakeBackend : IVirtualGamepadBackend
        {
            public readonly List<VirtualGamepadReport> Reports = new List<VirtualGamepadReport>();
            public bool IsConnected { get; private set; }
            public bool FailConnect, ThrowConnect, ThrowUpdate, ThrowDisconnect, ThrowDispose;
            public bool WarnNeutralOnRemove;
            public int FailUpdates, FailDisconnects, Connections, Disconnections, Disposes;

            public bool TryConnect(out string error)
            {
                Connections++;
                error = null;
                if (ThrowConnect) throw new InvalidOperationException("fake connection exception");
                if (FailConnect) { error = "fake missing driver"; return false; }
                IsConnected = true;
                return true;
            }

            public bool TryUpdate(VirtualGamepadReport report, out string error)
            {
                Reports.Add(report);
                error = null;
                if (ThrowUpdate) throw new InvalidOperationException("fake update exception");
                if (FailUpdates > 0) { FailUpdates--; error = "fake update failed"; return false; }
                return true;
            }

            public bool TryDisconnect(out string error)
            {
                error = null;
                if (!IsConnected) return true;
                Disconnections++;
                // Match the native boundary: attempt neutral before remove.
                Reports.Add(new VirtualGamepadReport());
                if (ThrowDisconnect) throw new InvalidOperationException("fake remove exception");
                if (FailDisconnects > 0) { FailDisconnects--; error = "fake remove failed"; return false; }
                IsConnected = false;
                if (WarnNeutralOnRemove) error = "fake neutral not confirmed";
                return true;
            }

            public void Dispose()
            {
                Disposes++;
                if (ThrowDispose) throw new InvalidOperationException("fake dispose exception");
                string ignored;
                TryDisconnect(out ignored);
            }
        }

        public static string Run()
        {
            int count = 0;
            string error;
            Check(VirtualGamepadOutput.NativeReportSize == 12, ref count);
            Check(Marshal.OffsetOf(typeof(VirtualGamepadReport), "LeftTrigger").ToInt32() == 2, ref count);
            Check(Marshal.OffsetOf(typeof(VirtualGamepadReport), "RightTrigger").ToInt32() == 3, ref count);
            Check(Marshal.OffsetOf(typeof(VirtualGamepadReport), "RightX").ToInt32() == 8, ref count);
            Check(Marshal.OffsetOf(typeof(VirtualGamepadReport), "RightY").ToInt32() == 10, ref count);
            bool rejected = false;
            try { new VirtualGamepadOutput(null); } catch (ArgumentNullException) { rejected = true; }
            Check(rejected, ref count);

            FakeBackend backend = new FakeBackend();
            VirtualGamepadOutput output = new VirtualGamepadOutput(backend);
            Check(!output.IsConnected && !output.HasPendingStop && backend.Connections == 0, ref count);
            Check(!output.TryUpdate(0, 0, out error) && error != null && backend.Reports.Count == 0, ref count);
            Check(!output.TryUpdate(123, -456, 255, 128, out error) && error != null && backend.Reports.Count == 0, ref count);
            Check(output.TryConnect(out error) && output.IsConnected && error == null && output.LastError == null, ref count);
            Check(backend.Connections == 1 && backend.Reports.Count == 1 && Neutral(backend.Reports[0]), ref count);
            Check(output.TryConnect(out error) && backend.Connections == 1, ref count);
            short[] values = { short.MinValue, -1, 0, 1, short.MaxValue };
            foreach (short x in values)
                foreach (short y in values)
                {
                    Check(output.TryUpdate(x, y, out error), ref count);
                    VirtualGamepadReport report = backend.Reports[backend.Reports.Count - 1];
                    Check(report.RightX == x && report.RightY == y && OnlyRightStick(report), ref count);
                }
            byte[] triggers = { 0, 1, 127, 128, 254, 255 };
            foreach (byte left in triggers)
                foreach (byte right in triggers)
                {
                    Check(output.TryUpdate(-23456, 12345, left, right, out error), ref count);
                    VirtualGamepadReport report = backend.Reports[backend.Reports.Count - 1];
                    Check(report.RightX == -23456 && report.RightY == 12345
                        && report.LeftTrigger == left && report.RightTrigger == right
                        && report.Buttons == 0 && report.LeftX == 0 && report.LeftY == 0, ref count);
                }
            Check(output.TryUpdate(1, -1, out error), ref count);
            Check(OnlyRightStick(backend.Reports[backend.Reports.Count - 1]), ref count);
            Check(output.TryUpdate(123, -321, 255, 192, GamepadButtons.A | GamepadButtons.Y | (GamepadButtons)4, out error), ref count);
            VirtualGamepadReport mapped = backend.Reports[backend.Reports.Count - 1];
            Check(mapped.Buttons == 0x9000 && mapped.LeftTrigger == 255 && mapped.RightTrigger == 192
                && mapped.LeftX == 0 && mapped.LeftY == 0 && mapped.RightX == 123 && mapped.RightY == -321, ref count);
            output.Stop();
            Check(!output.IsConnected && !output.HasPendingStop && output.LastError == null, ref count);
            Check(Neutral(backend.Reports[backend.Reports.Count - 1]), ref count);
            Check(output.TryConnect(out error) && output.IsConnected && backend.Connections == 2, ref count);
            Check(Neutral(backend.Reports[backend.Reports.Count - 1]), ref count);
            Check(output.TryUpdate(short.MaxValue, short.MinValue, 64, 255, out error), ref count);
            output.Dispose();
            Check(!output.IsConnected && !output.HasPendingStop && backend.Disposes == 1, ref count);
            Check(Neutral(backend.Reports[backend.Reports.Count - 1]), ref count);
            Check(!output.TryConnect(out error) && error != null, ref count);
            Check(!output.TryUpdate(1, 2, out error) && error != null, ref count);
            Check(!output.TryUpdate(1, 2, 255, 255, out error) && error != null, ref count);
            output.Dispose();
            Check(!output.HasPendingStop, ref count);

            backend = new FakeBackend { FailConnect = true };
            output = new VirtualGamepadOutput(backend);
            Check(!output.TryConnect(out error) && !output.IsConnected && !output.HasPendingStop, ref count);
            Check(error.Contains("fake missing driver") && output.LastError == error && backend.Reports.Count == 0, ref count);
            backend.FailConnect = false;
            Check(output.TryConnect(out error), ref count);
            backend.FailUpdates = 1;
            Check(!output.TryUpdate(23000, -17000, 133, 244, out error) && !output.IsConnected, ref count);
            Check(backend.Reports[backend.Reports.Count - 2].LeftTrigger == 133
                && backend.Reports[backend.Reports.Count - 2].RightTrigger == 244, ref count);
            Check(!output.HasPendingStop && Neutral(backend.Reports[backend.Reports.Count - 1]) && output.LastError == error, ref count);
            Check(output.TryConnect(out error) && output.LastError == null, ref count);
            Check(Neutral(backend.Reports[backend.Reports.Count - 1]), ref count);
            backend.ThrowUpdate = true;
            Check(!output.TryUpdate(1, 2, 255, 0, out error) && error.Contains("fake update exception") && !output.IsConnected, ref count);
            Check(backend.Reports[backend.Reports.Count - 2].LeftTrigger == 255
                && backend.Reports[backend.Reports.Count - 2].RightTrigger == 0, ref count);
            Check(!output.HasPendingStop && Neutral(backend.Reports[backend.Reports.Count - 1]), ref count);
            backend.ThrowUpdate = false;
            output.Dispose();

            backend = new FakeBackend { FailUpdates = 1 };
            output = new VirtualGamepadOutput(backend);
            Check(!output.TryConnect(out error) && !output.IsConnected && !output.HasPendingStop, ref count);
            Check(backend.Reports.Count == 2 && Neutral(backend.Reports[0]) && Neutral(backend.Reports[1]), ref count);
            output.Dispose();

            backend = new FakeBackend { ThrowConnect = true };
            output = new VirtualGamepadOutput(backend);
            Check(!output.TryConnect(out error) && error.Contains("fake connection exception") && !output.IsConnected, ref count);
            output.Dispose();

            backend = new FakeBackend();
            output = new VirtualGamepadOutput(backend);
            Check(output.TryConnect(out error) && output.TryUpdate(short.MaxValue, short.MinValue, 255, 255, out error), ref count);
            backend.FailDisconnects = 3;
            output.Stop();
            Check(!output.IsConnected && output.HasPendingStop && output.LastError != null, ref count);
            Check(backend.Disconnections == 3 && Neutral(backend.Reports[backend.Reports.Count - 1]), ref count);
            Check(Neutral(backend.Reports[backend.Reports.Count - 2])
                && Neutral(backend.Reports[backend.Reports.Count - 3]), ref count);
            output.Stop();
            Check(!output.HasPendingStop && output.LastError == null && backend.Disconnections == 4, ref count);
            Check(output.TryConnect(out error), ref count);
            backend.FailDisconnects = 20;
            output.Stop();
            Check(!output.IsConnected && output.HasPendingStop, ref count);
            int connections = backend.Connections;
            Check(!output.TryConnect(out error) && output.HasPendingStop && backend.Connections == connections, ref count);
            backend.FailDisconnects = 0;
            output.Stop();
            Check(!output.HasPendingStop && output.LastError == null, ref count);
            Check(output.TryConnect(out error), ref count);
            backend.ThrowDisconnect = true;
            output.Stop();
            Check(output.HasPendingStop && output.LastError.Contains("fake remove exception"), ref count);
            backend.ThrowDisconnect = false;
            output.Stop();
            Check(!output.HasPendingStop && output.LastError == null, ref count);
            backend.ThrowDispose = true;
            output.Dispose();
            Check(output.LastError != null && output.Status.Contains("fake dispose exception"), ref count);

            backend = new FakeBackend { WarnNeutralOnRemove = true };
            output = new VirtualGamepadOutput(backend);
            Check(output.TryConnect(out error), ref count);
            output.Stop();
            Check(!output.IsConnected && !output.HasPendingStop && output.LastError == "fake neutral not confirmed", ref count);
            Check(output.Status.Contains("回中报文未确认"), ref count);
            output.Dispose();

            return "PASS: " + count + " virtual gamepad output checks (fake backend only)\r\n" +
                "No target before enable; neutral startup/stop; right stick, LT/RT and A/B/X/Y; reserved bits masked\r\n" +
                "Missing driver, initialization/update exceptions and failed-remove retries remain visible\r\n";
        }

        private static bool OnlyRightStick(VirtualGamepadReport report)
        {
            return report.Buttons == 0 && report.LeftTrigger == 0 && report.RightTrigger == 0 && report.LeftX == 0 && report.LeftY == 0;
        }
        private static bool Neutral(VirtualGamepadReport report) { return OnlyRightStick(report) && report.RightX == 0 && report.RightY == 0; }
        private static void Check(bool condition, ref int count)
        {
            count++;
            if (!condition) throw new InvalidOperationException("Virtual gamepad assertion " + count + " failed.");
        }
    }
}
