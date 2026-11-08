using System;
using System.Collections.Generic;

namespace SpaceKatMonitor
{
    internal static class MouseButtonTests
    {
        public static string Run()
        {
            int count = 0;
            ModelChecks(ref count);
            ProtocolChecks(ref count);
            OutputChecks(ref count);
            ExpandedActionChecks(ref count);
            return "PASS: " + count + " mouse button mapping and output checks (fake desktop backend)\r\n" +
                "Press edges, single/double clicks, side buttons, wheel steps, toggle hold/release and raw undeclared bits\r\n" +
                "Stop/dispose, partial/zero/throw cleanup, unresolved-release retries; no real mouse input\r\n";
        }

        private static MouseButtonModel NewModel()
        {
            MouseButtonModel model = new MouseButtonModel();
            model.Configure(new[] { 1, 2, 4, 5 }, new[] { MouseButtonAction.LeftClick,
                MouseButtonAction.RightClick, MouseButtonAction.ToggleLeftHold, MouseButtonAction.None });
            return model;
        }

        private static void ModelChecks(ref int count)
        {
            MouseButtonModel model = NewModel();
            Check(!model.Enabled && !model.IsLeftHeld, ref count);
            Check(Signals(model.Process(16)), ref count);
            Check(Signals(model.Disable()), ref count);
            model.Enable(0);
            Check(Signals(model.Process(0)), ref count);
            Check(Signals(model.Process(2), MouseButtonSignal.LeftDown, MouseButtonSignal.LeftUp), ref count);
            Check(Signals(model.Process(2)), ref count);
            Check(Signals(model.Process(0)), ref count);
            Check(Signals(model.Process(4), MouseButtonSignal.RightDown, MouseButtonSignal.RightUp), ref count);
            model.Process(0);
            Check(Signals(model.Process(16), MouseButtonSignal.LeftDown) && model.IsLeftHeld, ref count);
            Check(Signals(model.Process(16)) && model.IsLeftHeld, ref count);
            Check(Signals(model.Process(0)) && model.IsLeftHeld, ref count);
            Check(Signals(model.Process(4), MouseButtonSignal.RightDown, MouseButtonSignal.RightUp) && model.IsLeftHeld, ref count);
            model.Process(0);
            Check(Signals(model.Process(2), MouseButtonSignal.LeftUp, MouseButtonSignal.LeftDown,
                MouseButtonSignal.LeftUp, MouseButtonSignal.LeftDown) && model.IsLeftHeld, ref count);
            model.Process(0);
            Check(Signals(model.Process(16), MouseButtonSignal.LeftUp) && !model.IsLeftHeld && model.Enabled, ref count);
            Check(Signals(model.ReleaseLeftHold()), ref count);
            model.Process(0);
            Check(Signals(model.Process(16), MouseButtonSignal.LeftDown), ref count);
            Check(Signals(model.ReleaseLeftHold(), MouseButtonSignal.LeftUp) && model.Enabled && !model.IsLeftHeld, ref count);
            Check(Signals(model.Process(16)) && !model.IsLeftHeld, ref count);
            model.Process(0);
            model.Process(16);
            Check(Signals(model.Disable(), MouseButtonSignal.LeftUp) && !model.Enabled && !model.IsLeftHeld, ref count);
            Check(Signals(model.Disable()) && Signals(model.Process(2)), ref count);

            model.Enable(16);
            Check(Signals(model.Process(16)) && !model.IsLeftHeld, ref count);
            Check(Signals(model.Process(0)), ref count);
            Check(Signals(model.Process(16), MouseButtonSignal.LeftDown), ref count);
            model.Disable();
            model.Enable(0);
            Check(Signals(model.Process(32)) && !model.IsLeftHeld, ref count);
            model.Disable();

            MouseButtonAction[] duplicateActions = { MouseButtonAction.ToggleLeftHold,
                MouseButtonAction.ToggleLeftHold, MouseButtonAction.None, MouseButtonAction.None };
            model.Configure(new[] { 4, 4, 4, 4 }, duplicateActions);
            model.Enable(0);
            Check(Signals(model.Process(16), MouseButtonSignal.LeftDown) && model.IsLeftHeld, ref count);
            model.Process(0);
            Check(Signals(model.Process(16), MouseButtonSignal.LeftUp) && !model.IsLeftHeld, ref count);
            model.Disable();
            duplicateActions[0] = MouseButtonAction.None;
            model.Configure(new[] { 4, 4, 4, 4 }, duplicateActions);
            model.Enable(0);
            Check(Signals(model.Process(16), MouseButtonSignal.LeftDown), ref count);
            model.Disable();

            int[] bits = { 31, 2, 4, 5 };
            MouseButtonAction[] actions = { MouseButtonAction.LeftClick, MouseButtonAction.None,
                MouseButtonAction.None, MouseButtonAction.None };
            model.Configure(bits, actions);
            bits[0] = 0;
            actions[0] = MouseButtonAction.None;
            model.Enable(0);
            Check(Signals(model.Process(0x80000000), MouseButtonSignal.LeftDown, MouseButtonSignal.LeftUp), ref count);
            Check(Throws<InvalidOperationException>(delegate { model.Configure(bits, actions); }), ref count);
            Check(Throws<InvalidOperationException>(delegate { model.Enable(0); }), ref count);
            model.Disable();
            Check(Throws<ArgumentException>(delegate { model.Configure(null, actions); }), ref count);
            Check(Throws<ArgumentException>(delegate { model.Configure(bits, null); }), ref count);
            Check(Throws<ArgumentException>(delegate { model.Configure(new[] { 1 }, actions); }), ref count);
            Check(Throws<ArgumentException>(delegate { model.Configure(bits, new MouseButtonAction[3]); }), ref count);
            Check(Throws<ArgumentOutOfRangeException>(delegate { model.Configure(new[] { -1, 2, 4, 5 }, actions); }), ref count);
            Check(Throws<ArgumentOutOfRangeException>(delegate { model.Configure(new[] { 32, 2, 4, 5 }, actions); }), ref count);
            Check(Throws<ArgumentOutOfRangeException>(delegate { model.Configure(bits, new[] { (MouseButtonAction)99,
                MouseButtonAction.None, MouseButtonAction.None, MouseButtonAction.None }); }), ref count);
            model.Enable(0);
            Check(Signals(model.Process(0x80000000), MouseButtonSignal.LeftDown, MouseButtonSignal.LeftUp), ref count);
            model.Disable();
        }

        private static void ProtocolChecks(ref int count)
        {
            MouseButtonModel model = NewModel();
            MouseControlSession session = new MouseControlSession();
            session.Enable();
            model.Enable(0);
            Check(session.GetPhase(10) == MouseControlPhase.WaitingForData && session.Enabled, ref count);
            DecodedReport decoded;
            byte[] packet = new byte[13];
            packet[0] = 3;
            packet[1] = 0x10;
            Check(ReportDecoder.TryDecode(packet, out decoded) && decoded.Buttons.HasValue && decoded.Buttons.Value == 0x10, ref count);
            uint raw = ButtonState.DisplayMask(decoded.Buttons.Value, 0x03, true);
            Check(raw == 0x10 && Signals(model.Process(raw), MouseButtonSignal.LeftDown), ref count);
            Check(Signals(model.Process(raw)) && model.IsLeftHeld, ref count);
            packet[1] = 0;
            Check(ReportDecoder.TryDecode(packet, out decoded), ref count);
            Check(Signals(model.Process(ButtonState.DisplayMask(decoded.Buttons.Value, 0x03, true))) && model.IsLeftHeld, ref count);
            packet[1] = 0x10;
            Check(ReportDecoder.TryDecode(packet, out decoded), ref count);
            Check(Signals(model.Process(ButtonState.DisplayMask(decoded.Buttons.Value, 0x03, true)), MouseButtonSignal.LeftUp) && !model.IsLeftHeld, ref count);
            Check(session.GetPhase(20) == MouseControlPhase.WaitingForData && session.Enabled, ref count);
            foreach (byte rawBit in new byte[] { 0x02, 0x04, 0x10, 0x20 })
            {
                packet[1] = rawBit;
                Check(ReportDecoder.TryDecode(packet, out decoded) && ButtonState.DisplayMask(decoded.Buttons.Value, 0x03, true) == rawBit, ref count);
            }
            Check(ButtonState.DisplayMask(0x36, 0x03, false) == 2, ref count);
            model.Disable();
        }

        private static void OutputChecks(ref int count)
        {
            string error;
            FakeBackend fake = new FakeBackend();
            MouseOutput output = new MouseOutput(new IntPtr(1), fake);
            Check(!output.TryButtons(new[] { MouseButtonSignal.LeftDown }, out error) && error != null && fake.Sends.Count == 0, ref count);
            Check(!output.TryButtons(null, out error) && error != null, ref count);
            Check(output.TryButtons(new MouseButtonSignal[0], out error) && fake.Sends.Count == 0, ref count);
            output.Arm();
            Check(output.StopHotkeyAvailable && fake.Registrations == 1, ref count);
            Check(!output.TryButtons(new[] { MouseButtonSignal.LeftDown, (MouseButtonSignal)99 }, out error) && fake.Sends.Count == 0 && !output.StopHotkeyAvailable, ref count);
            output.Arm();
            Check(output.TryButtons(new[] { MouseButtonSignal.LeftDown }, out error) && output.IsLeftDown && output.HasPendingRelease, ref count);
            Check(Flags(fake.Sends[0], 2), ref count);
            output.Disarm();
            Check(Flags(fake.Sends[1], 4) && !output.HasPendingRelease && output.LastReleaseError == null, ref count);
            Check(!output.StopHotkeyAvailable && fake.Unregistrations == 2, ref count);
            output.Disarm();
            Check(fake.Sends.Count == 2 && fake.Unregistrations == 2, ref count);
            output.Arm();
            Check(output.TryButtons(new[] { MouseButtonSignal.LeftDown, MouseButtonSignal.RightDown }, out error) && output.IsLeftDown && output.IsRightDown, ref count);
            output.Dispose();
            Check(!output.HasPendingRelease && Flags(fake.Sends[fake.Sends.Count - 2], 4) && Flags(fake.Sends[fake.Sends.Count - 1], 16), ref count);
            Check(Throws<ObjectDisposedException>(delegate { output.Arm(); }), ref count);
            Check(!output.TryButtons(new[] { MouseButtonSignal.RightDown }, out error), ref count);

            foreach (int outcome in new[] { 0, 1, -1, 9 })
            {
                fake = new FakeBackend();
                output = new MouseOutput(new IntPtr(1), fake);
                output.Arm();
                fake.Outcomes.Enqueue(outcome);
                Check(!output.TryButtons(new[] { MouseButtonSignal.LeftDown, MouseButtonSignal.LeftUp }, out error) && error != null, ref count);
                Check(fake.Sends.Count == 2 && Flags(fake.Sends[1], 4), ref count);
                Check(!output.HasPendingRelease && !output.StopHotkeyAvailable && output.LastReleaseError == null, ref count);
                output.Dispose();
            }

            fake = new FakeBackend();
            output = new MouseOutput(new IntPtr(1), fake);
            output.Arm();
            Check(output.TryButtons(new[] { MouseButtonSignal.LeftDown }, out error), ref count);
            fake.Outcomes.Enqueue(0);
            Check(!output.TryMove(2, 3, out error) && !output.HasPendingRelease && Flags(fake.Sends[fake.Sends.Count - 1], 4), ref count);
            Check(!output.StopHotkeyAvailable, ref count);

            fake = new FakeBackend();
            output = new MouseOutput(new IntPtr(1), fake);
            output.Arm();
            fake.Outcomes.Enqueue(1); // Partial two-button DOWN batch.
            fake.Outcomes.Enqueue(0); fake.Outcomes.Enqueue(0);
            fake.Outcomes.Enqueue(0); fake.Outcomes.Enqueue(0);
            fake.Outcomes.Enqueue(0); fake.Outcomes.Enqueue(0);
            Check(!output.TryButtons(new[] { MouseButtonSignal.LeftDown, MouseButtonSignal.RightDown }, out error), ref count);
            Check(output.HasPendingRelease && output.IsLeftDown && output.IsRightDown && output.LastReleaseError != null, ref count);
            Check(error.Contains(output.LastReleaseError) && fake.Sends.Count == 7 && !output.StopHotkeyAvailable, ref count);
            output.Disarm();
            Check(!output.HasPendingRelease && output.LastReleaseError == null && fake.Sends.Count == 9, ref count);
            output.Arm();
            Check(output.StopHotkeyAvailable, ref count);
            output.Dispose();

            fake = new FakeBackend();
            output = new MouseOutput(new IntPtr(1), fake);
            output.Arm();
            Check(output.TryButtons(new[] { MouseButtonSignal.LeftDown }, out error), ref count);
            fake.Outcomes.Enqueue(-1); fake.Outcomes.Enqueue(-1); fake.Outcomes.Enqueue(-1);
            output.Dispose();
            Check(output.HasPendingRelease && output.IsLeftDown && output.LastReleaseError != null, ref count);
            output.Dispose();
            Check(!output.HasPendingRelease && output.LastReleaseError == null, ref count);

            fake = new FakeBackend();
            fake.RegisterResult = false;
            output = new MouseOutput(new IntPtr(1), fake);
            output.Arm();
            Check(!output.StopHotkeyAvailable && !output.TryButtons(new[] { MouseButtonSignal.LeftDown }, out error) && fake.Sends.Count == 0, ref count);
            output.Dispose();

            // A pending UP prevents re-arm if cleanup still fails.
            fake = new FakeBackend();
            output = new MouseOutput(new IntPtr(1), fake);
            output.Arm();
            output.TryButtons(new[] { MouseButtonSignal.LeftDown }, out error);
            for (int i = 0; i < 6; i++) fake.Outcomes.Enqueue(0);
            output.Disarm();
            output.Arm();
            Check(output.HasPendingRelease && !output.StopHotkeyAvailable && fake.Registrations == 1, ref count);
            output.Disarm();
            Check(!output.HasPendingRelease && output.LastReleaseError == null, ref count);
            output.Dispose();

            fake = new FakeBackend();
            output = new MouseOutput(new IntPtr(1), fake);
            output.Arm();
            Check(output.TryMove(Int32.MaxValue, Int32.MinValue, out error) && fake.Sends[0][0].X == 256 && fake.Sends[0][0].Y == -256 && Flags(fake.Sends[0], 1), ref count);
            Check(!output.HasPendingRelease, ref count);
            Check(output.TryButtons(new[] { MouseButtonSignal.LeftDown, MouseButtonSignal.LeftUp,
                MouseButtonSignal.RightDown, MouseButtonSignal.RightUp }, out error) && !output.HasPendingRelease, ref count);
            Check(Flags(fake.Sends[1], 2, 4, 8, 16), ref count);
            output.Dispose();

            fake = new FakeBackend();
            output = new MouseOutput(new IntPtr(1), fake);
            output.Arm();
            output.TryButtons(new[] { MouseButtonSignal.LeftDown }, out error);
            Check(!output.TryButtons(new[] { (MouseButtonSignal)99 }, out error)
                && !output.HasPendingRelease && !output.StopHotkeyAvailable
                && Flags(fake.Sends[fake.Sends.Count - 1], 4), ref count);
            output.Dispose();

            fake = new FakeBackend();
            output = new MouseOutput(new IntPtr(1), fake);
            output.Arm();
            output.TryButtons(new[] { MouseButtonSignal.LeftDown }, out error);
            for (int i = 0; i < 3; i++) fake.Outcomes.Enqueue(0);
            Check(!output.TryButtons(new[] { (MouseButtonSignal)99 }, out error)
                && output.IsLeftDown && output.HasPendingRelease && !output.StopHotkeyAvailable
                && output.LastReleaseError != null && error.Contains(output.LastReleaseError), ref count);
            output.Disarm();
            Check(!output.HasPendingRelease && output.LastReleaseError == null, ref count);
            output.Dispose();
        }

        private static void ExpandedActionChecks(ref int count)
        {
            CheckMappedAction(MouseButtonAction.LeftDoubleClick, new uint[] { 2, 4, 2, 4 }, new[] { 0, 0, 0, 0 }, ref count);
            CheckMappedAction(MouseButtonAction.MiddleClick, new uint[] { 0x20, 0x40 }, new[] { 0, 0 }, ref count);
            CheckMappedAction(MouseButtonAction.BackClick, new uint[] { 0x80, 0x100 }, new[] { 1, 1 }, ref count);
            CheckMappedAction(MouseButtonAction.ForwardClick, new uint[] { 0x80, 0x100 }, new[] { 2, 2 }, ref count);
            CheckMappedAction(MouseButtonAction.ScrollUp, new uint[] { 0x800 }, new[] { 120 }, ref count);
            CheckMappedAction(MouseButtonAction.ScrollDown, new uint[] { 0x800 }, new[] { -120 }, ref count);

            MouseButtonModel model = new MouseButtonModel();
            model.Configure(new[] { 0, 1, 2, 3 }, new[] { MouseButtonAction.ToggleLeftHold,
                MouseButtonAction.LeftDoubleClick, MouseButtonAction.None, MouseButtonAction.None });
            model.Enable(0);
            Check(Signals(model.Process(1), MouseButtonSignal.LeftDown), ref count);
            Check(Signals(model.Process(2), MouseButtonSignal.LeftUp, MouseButtonSignal.LeftDown,
                MouseButtonSignal.LeftUp, MouseButtonSignal.LeftDown, MouseButtonSignal.LeftUp, MouseButtonSignal.LeftDown)
                && model.IsLeftHeld, ref count);
            Check(Signals(model.Process(1), MouseButtonSignal.LeftUp) && !model.IsLeftHeld, ref count);

            FakeBackend fake = new FakeBackend();
            using (MouseOutput output = new MouseOutput(new IntPtr(1), fake))
            {
                output.Arm();
                fake.Outcomes.Enqueue(1);
                string error;
                Check(!output.TryButtons(new[] { MouseButtonSignal.MiddleDown, MouseButtonSignal.MiddleUp,
                    MouseButtonSignal.BackDown, MouseButtonSignal.BackUp,
                    MouseButtonSignal.ForwardDown, MouseButtonSignal.ForwardUp }, out error)
                    && !output.HasPendingRelease && !output.StopHotkeyAvailable, ref count);
                Check(fake.Sends.Count == 4 && Flags(fake.Sends[1], 0x40)
                    && Flags(fake.Sends[2], 0x100) && fake.Sends[2][0].Data == 1
                    && Flags(fake.Sends[3], 0x100) && fake.Sends[3][0].Data == 2, ref count);
            }
        }

        private static void CheckMappedAction(MouseButtonAction action, uint[] flags, int[] data, ref int count)
        {
            MouseButtonModel model = new MouseButtonModel();
            model.Configure(new[] { 0, 1, 2, 3 }, new[] { action, MouseButtonAction.None, MouseButtonAction.None, MouseButtonAction.None });
            model.Enable(0);
            FakeBackend fake = new FakeBackend();
            using (MouseOutput output = new MouseOutput(new IntPtr(1), fake))
            {
                output.Arm();
                string error;
                Check(output.TryButtons(model.Process(1), out error) && !output.HasPendingRelease
                    && fake.Sends.Count == 1 && Flags(fake.Sends[0], flags), ref count);
                for (int i = 0; i < data.Length; i++) Check(fake.Sends[0][i].Data == data[i], ref count);
                Check(Signals(model.Process(1)) && Signals(model.Process(0)), ref count);
            }
        }

        private sealed class FakeBackend : IMouseOutputBackend
        {
            public bool RegisterResult = true;
            public int Registrations, Unregistrations;
            public readonly Queue<int> Outcomes = new Queue<int>();
            public readonly List<MouseOutputPacket[]> Sends = new List<MouseOutputPacket[]>();
            public int LastError { get { return 0; } }
            public bool RegisterStopHotkey(IntPtr hwnd) { Registrations++; return RegisterResult; }
            public void UnregisterStopHotkey(IntPtr hwnd) { Unregistrations++; }
            public uint Send(MouseOutputPacket[] packets)
            {
                Sends.Add((MouseOutputPacket[])packets.Clone());
                int result = Outcomes.Count == 0 ? packets.Length : Outcomes.Dequeue();
                if (result < 0) throw new InvalidOperationException("Synthetic send failure");
                return (uint)result;
            }
        }

        private static bool Signals(MouseButtonSignal[] actual, params MouseButtonSignal[] expected)
        {
            if (actual.Length != expected.Length) return false;
            for (int i = 0; i < actual.Length; i++) if (actual[i] != expected[i]) return false;
            return true;
        }
        private static bool Flags(MouseOutputPacket[] actual, params uint[] expected)
        {
            if (actual.Length != expected.Length) return false;
            for (int i = 0; i < actual.Length; i++) if (actual[i].Flags != expected[i]) return false;
            return true;
        }
        private static bool Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return true; }
            return false;
        }
        private static void Check(bool condition, ref int count)
        {
            count++;
            if (!condition) throw new InvalidOperationException("Mouse button assertion " + count + " failed.");
        }
    }
}
