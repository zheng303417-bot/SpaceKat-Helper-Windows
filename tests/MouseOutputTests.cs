using System;

namespace SpaceKatMonitor
{
    internal static class MouseOutputTests
    {
        public static string Run()
        {
            int count = 0;
            Check(MouseOutput.NativeInputSize == (IntPtr.Size == 8 ? 40 : 28), ref count);
            bool rejected = false;
            try { new MouseOutput(IntPtr.Zero); }
            catch (ArgumentException) { rejected = true; }
            Check(rejected, ref count);
            // Do not Arm: these checks cannot register a shortcut or move the
            // user's cursor. A placeholder HWND exercises disarmed behavior.
            MouseOutput output = new MouseOutput(new IntPtr(1));
            string error;
            Check(!output.StopHotkeyAvailable, ref count);
            Check(!output.TryMove(1, -1, out error) && error != null, ref count);
            Check(output.TryMove(0, 0, out error) && error == null, ref count);
            Check(output.IsStopMessage(0x0312, new IntPtr(0x534B)), ref count);
            Check(!output.IsStopMessage(0x00FF, new IntPtr(0x534B)), ref count);
            Check(!output.IsStopMessage(0x0312, new IntPtr(1)), ref count);
            output.Disarm();
            output.Dispose();
            output.Dispose();
            Check(!output.TryMove(-1, 1, out error) && !output.StopHotkeyAvailable, ref count);
            return "PASS: " + count + " native output guard checks (no cursor movement)\r\n";
        }

        private static void Check(bool condition, ref int count)
        {
            count++;
            if (!condition) throw new InvalidOperationException("Mouse output assertion " + count + " failed.");
        }
    }
}
