using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SpaceKatMonitor
{
    // Compatibility mode uses each application's default mouse navigation controls.
    // Only one gesture is active at a time, and only over the recognized foreground window.
    internal sealed class GlobalNavigationOutput : IDisposable
    {
        [Flags]
        private enum Chord { None = 0, Ctrl = 1, Shift = 2, Alt = 4, Middle = 8, Left = 16, Right = 32 }
        private enum Motion { None, Pan, Orbit, Zoom }

        private sealed class Profile
        {
            internal readonly string Name;
            internal readonly Chord Pan, Orbit;

            internal Profile(string name, Chord pan, Chord orbit)
            {
                Name = name;
                Pan = pan;
                Orbit = orbit;
            }
        }

        private const Chord Middle = Chord.Middle;
        private const Chord ShiftMiddle = Chord.Shift | Middle;
        private static readonly Chord[] PressOrder = { Chord.Ctrl, Chord.Shift, Chord.Alt, Middle, Chord.Left, Chord.Right };
        private static readonly int[] PressKeys = { 0xA2, 0xA0, 0xA4, 4, 1, 2 };
        private static readonly int[] WatchedKeys = { 1, 2, 4, 5, 6, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C };
        private const uint InputMouse = 0;
        private const uint InputKeyboard = 1;
        private const uint MouseMove = 1;
        private const uint Wheel = 0x0800;
        private const uint KeyUp = 2;
        private const int WheelDetent = 120;
        private const double PixelsPerSecond = 500;
        private const double WheelStepsPerSecond = 9;
        private const double DeliberateZoomStrength = 0.55;
        private const double DeliberateZoomSeconds = 0.10;
        private const double GestureSwitchRatio = 1.20;
        private static readonly int InputSize = Marshal.SizeOf(typeof(NativeInput));
        private readonly NativeInput[] packet = new NativeInput[PressOrder.Length + 1];
        private readonly HookProcedure keyboardProcedure;
        private readonly HookProcedure mouseProcedure;
        private IntPtr keyboardHook, mouseHook;
        private IntPtr window;
        private uint processId, threadId;
        private Profile profile;
        private Motion motion;
        private Chord held;
        private int physical;
        private long lastTime, hookRetryAfter;
        private double fractionX, fractionY, fractionWheel;
        private double wheelIntent;
        private int wheelDirection;
        private bool wheelStarted;
        private bool disposed;

        internal GlobalNavigationOutput()
        {
            keyboardProcedure = ObserveKeyboard;
            mouseProcedure = ObserveMouse;
        }

        internal string CurrentProfile { get { return profile == null ? null : profile.Name; } }
        internal bool HasPendingRelease { get { return motion == Motion.None && held != Chord.None; } }

        internal void Update(float[] axes, bool fresh, double linearGain = 1)
        {
            if (disposed) return;
            IntPtr foreground = GetForegroundWindow();
            uint pid = 0;
            uint thread = foreground == IntPtr.Zero ? 0 : GetWindowThreadProcessId(foreground, out pid);
            if (foreground != window || pid != processId)
            {
                ResetMotion();
                window = foreground;
                processId = pid;
                threadId = thread;
                profile = ResolveProfile(foreground, pid);
                // Wait a frame so the previous application's releases are delivered first.
                return;
            }

            if (!fresh || axes == null || axes.Length < 5 || profile == null || !IsOverForegroundClient())
            {
                ResetMotion();
                return;
            }

            double x = Axis(axes[0]), y = -Axis(axes[1]), z = Axis(axes[2]);
            double rx = Axis(axes[3]), ry = Axis(axes[4]);
            double panStrength = Math.Sqrt(x * x + y * y);
            double orbitStrength = Math.Sqrt(rx * rx + ry * ry);
            double zoomStrength = Math.Abs(z);
            Motion next = DominantMotion(motion, panStrength, orbitStrength, zoomStrength);
            long now = Stopwatch.GetTimestamp();
            if (next == Motion.None || !EnsureInputGuards(now) || UserControlsActive())
            {
                ResetMotion();
                return;
            }

            if (next != motion)
            {
                ResetMotion();
                if (HasPendingRelease) return;
                motion = next;
            }
            double elapsed = lastTime == 0 ? 0 : (now - lastTime) / (double)Stopwatch.Frequency;
            lastTime = now;
            if (elapsed > 0.12)
            {
                ResetMotion();
                return;
            }
            if (elapsed <= 0) return;
            double dt = Math.Min(elapsed, 0.035);
            if (motion == Motion.Zoom)
            {
                int detents = ZoomDetents(z, linearGain, dt);
                if (detents != 0) SendMotion(Chord.None, MousePacket(Wheel, 0, 0, detents * WheelDetent));
                return;
            }

            bool orbit = motion == Motion.Orbit;
            int dx = Integrate(ref fractionX, (orbit ? ry : x) * PixelsPerSecond * dt);
            int dy = Integrate(ref fractionY, (orbit ? rx : y) * PixelsPerSecond * dt);
            if (dx != 0 || dy != 0)
                SendMotion(orbit ? profile.Orbit : profile.Pan, MousePacket(MouseMove, dx, dy, 0));
        }

        private static double Axis(float value)
        {
            return Single.IsNaN(value) || Single.IsInfinity(value) ? 0 : Math.Max(-3, Math.Min(3, value));
        }

        private static int Integrate(ref double fraction, double amount)
        {
            if (Math.Sign(fraction) != Math.Sign(amount)) fraction = 0;
            fraction += amount;
            int whole = (int)fraction;
            fraction -= whole;
            return whole;
        }

        private static Motion DominantMotion(Motion current, double pan, double orbit, double zoom)
        {
            double strongest = Math.Max(pan, Math.Max(orbit, zoom));
            if (strongest == 0) return Motion.None;
            double previous = current == Motion.Pan ? pan : current == Motion.Orbit ? orbit : current == Motion.Zoom ? zoom : 0;
            if (previous > 0 && previous * GestureSwitchRatio >= strongest) return current;
            if (pan == strongest) return Motion.Pan;
            return orbit == strongest ? Motion.Orbit : Motion.Zoom;
        }

        private int ZoomDetents(double z, double linearGain, double dt)
        {
            int direction = Math.Sign(z);
            if (direction != wheelDirection)
            {
                wheelDirection = direction;
                fractionWheel = wheelIntent = 0;
                wheelStarted = false;
            }
            int detents = Integrate(ref fractionWheel, z * WheelStepsPerSecond * dt);
            if (detents != 0) wheelStarted = true;
            if (wheelStarted) return detents;

            // A wheel click is the smallest zoom step these apps accept. Allow one
            // click on a sustained, deliberate push, then keep repeats gain-scaled.
            if (Math.Abs(z) < Math.Max(0.01, linearGain) * DeliberateZoomStrength)
            {
                wheelIntent = 0;
                return 0;
            }
            wheelIntent += dt;
            if (wheelIntent < DeliberateZoomSeconds) return 0;
            wheelStarted = true;
            fractionWheel = 0;
            return direction;
        }

        private static Profile ResolveProfile(IntPtr hwnd, uint pid)
        {
            if (pid == 0) return null;
            var windowClass = new StringBuilder(64);
            if (GetClassName(hwnd, windowClass, windowClass.Capacity) == 0 || windowClass.ToString() == "#32770")
                return null;
            try
            {
                using (Process app = Process.GetProcessById((int)pid)) return ProfileForName(app.ProcessName);
            }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
            catch (System.ComponentModel.Win32Exception) { return null; }
        }

        private static Profile ProfileForName(string executable)
        {
            switch (executable.ToLowerInvariant())
            {
                case "blender": return new Profile("Blender", ShiftMiddle, Middle);
                case "fusion360": return new Profile("Autodesk Fusion", Middle, ShiftMiddle);
                case "sldworks": return new Profile("SOLIDWORKS", Chord.Ctrl | Middle, Middle);
                case "inventor": return new Profile("Inventor", Middle, ShiftMiddle);
                case "rhino": return new Profile("Rhino", Chord.Shift | Chord.Right, Chord.Right);
                case "sketchup": return new Profile("SketchUp", Middle | Chord.Left, Middle);
                case "freecad": return new Profile("FreeCAD", Middle, Middle | Chord.Right);
                case "maya": return new Profile("Maya", Chord.Alt | Middle, Chord.Alt | Chord.Left);
                case "3dsmax": return new Profile("3ds Max", Middle, Chord.Alt | Middle);
                case "acad": return new Profile("AutoCAD", Middle, ShiftMiddle);
                case "revit": return new Profile("Revit", Middle, ShiftMiddle);
                default: return null;
            }
        }

        private bool IsOverForegroundClient()
        {
            if (window == IntPtr.Zero || GetForegroundWindow() != window || GetWindow(window, 4) != IntPtr.Zero)
                return false;
            var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf(typeof(GuiThreadInfo)) };
            // No menu loops, moving/sizing, or capture by another application.
            if (!GetGUIThreadInfo(threadId, ref info) || (info.Flags & 0x1E) != 0 ||
                (info.Capture != IntPtr.Zero && GetAncestor(info.Capture, 2) != window)) return false;
            Point cursor;
            Rect rect;
            if (!GetCursorPos(out cursor) || GetAncestor(WindowFromPoint(cursor), 2) != window ||
                !GetClientRect(window, out rect) || !ScreenToClient(window, ref cursor)) return false;
            return cursor.X >= rect.Left && cursor.X < rect.Right && cursor.Y >= rect.Top && cursor.Y < rect.Bottom;
        }

        private bool EnsureInputGuards(long now)
        {
            if (keyboardHook != IntPtr.Zero && mouseHook != IntPtr.Zero) return true;
            if (now < hookRetryAfter) return false;
            IntPtr module = GetModuleHandle(null);
            keyboardHook = SetWindowsHookEx(13, keyboardProcedure, module, 0);
            mouseHook = SetWindowsHookEx(14, mouseProcedure, module, 0);
            if (keyboardHook == IntPtr.Zero || mouseHook == IntPtr.Zero)
            {
                RemoveInputGuards();
                hookRetryAfter = now + 2 * Stopwatch.Frequency;
                return false;
            }
            physical = 0;
            for (int i = 0; i < WatchedKeys.Length; i++)
                if ((GetAsyncKeyState(WatchedKeys[i]) & 0x8000) != 0) physical |= 1 << i;
            return true;
        }

        private bool UserControlsActive()
        {
            if (physical != 0) return true;
            // The asynchronous check covers an input arriving around a timer tick
            // and also guards against a held key from another input source.
            foreach (int key in WatchedKeys)
            {
                int ours = Array.IndexOf(PressKeys, key);
                if (ours >= 0 && (held & PressOrder[ours]) != 0) continue;
                if ((GetAsyncKeyState(key) & 0x8000) != 0) return true;
            }
            return false;
        }

        private IntPtr ObserveKeyboard(int code, IntPtr message, IntPtr data)
        {
            // Watch modifiers only. No text or unrelated keys are stored.
            if (code >= 0 && (Marshal.ReadInt32(data, 8) & 0x10) == 0)
                UpdatePhysical(Marshal.ReadInt32(data), message.ToInt64() == 0x100 || message.ToInt64() == 0x104);
            return CallNextHookEx(IntPtr.Zero, code, message, data);
        }

        private IntPtr ObserveMouse(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0 && (Marshal.ReadInt32(data, 12) & 1) == 0)
            {
                switch (message.ToInt64())
                {
                    case 0x201: UpdatePhysical(1, true); break;
                    case 0x202: UpdatePhysical(1, false); break;
                    case 0x204: UpdatePhysical(2, true); break;
                    case 0x205: UpdatePhysical(2, false); break;
                    case 0x207: UpdatePhysical(4, true); break;
                    case 0x208: UpdatePhysical(4, false); break;
                    case 0x20B:
                    case 0x20C: UpdatePhysical((Marshal.ReadInt32(data, 8) >> 16) == 1 ? 5 : 6, message.ToInt64() == 0x20B); break;
                }
            }
            return CallNextHookEx(IntPtr.Zero, code, message, data);
        }

        private void UpdatePhysical(int key, bool down)
        {
            int index = Array.IndexOf(WatchedKeys, key);
            if (index < 0) return;
            int bit = 1 << index;
            if (down) physical |= bit;
            else physical &= ~bit;
        }

        private void SendMotion(Chord desired, NativeInput movement)
        {
            if (GetForegroundWindow() != window || UserControlsActive())
            {
                ResetMotion();
                return;
            }
            if (held != Chord.None && held != desired)
            {
                Release();
                if (held != Chord.None) return;
            }
            int count = 0;
            if (held != desired)
                for (int i = 0; i < PressOrder.Length; i++)
                    if ((desired & PressOrder[i]) != 0) packet[count++] = ChordPacket(i, true);
            packet[count++] = movement;
            // On partial writes release any DOWN that might have reached the system.
            held |= desired;
            if (SendInput((uint)count, packet, InputSize) != count) ResetMotion();
        }

        private void ResetMotion()
        {
            motion = Motion.None;
            lastTime = 0;
            fractionX = fractionY = fractionWheel = 0;
            wheelDirection = 0;
            wheelIntent = 0;
            wheelStarted = false;
            Release();
        }

        private void Release()
        {
            for (int i = PressOrder.Length - 1; i >= 0; i--)
            {
                Chord flag = PressOrder[i];
                if ((held & flag) == 0) continue;
                int physicalBit = 1 << Array.IndexOf(WatchedKeys, PressKeys[i]);
                if ((physical & physicalBit) != 0)
                {
                    // The user took over this control and will release it physically.
                    held &= ~flag;
                    continue;
                }
                packet[0] = ChordPacket(i, false);
                for (int attempt = 0; attempt < 3; attempt++)
                    if (SendInput(1, packet, InputSize) == 1)
                    {
                        held &= ~flag;
                        break;
                    }
            }
        }

        internal void Stop()
        {
            ResetMotion();
            window = IntPtr.Zero;
            processId = threadId = 0;
            profile = null;
            if (!HasPendingRelease) RemoveInputGuards();
        }

        public void Dispose()
        {
            Stop();
            RemoveInputGuards();
            disposed = true;
        }

        private void RemoveInputGuards()
        {
            if (keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(keyboardHook);
            if (mouseHook != IntPtr.Zero) UnhookWindowsHookEx(mouseHook);
            keyboardHook = mouseHook = IntPtr.Zero;
            physical = 0;
        }

        private static NativeInput ChordPacket(int index, bool down)
        {
            if (index < 3)
            {
                var input = new NativeInput { Type = InputKeyboard };
                input.Data.Keyboard = new NativeKeyboardInput { Key = (ushort)PressKeys[index], Flags = down ? 0 : KeyUp };
                return input;
            }
            uint flag = index == 3 ? 0x20u : index == 4 ? 0x02u : 0x08u;
            return MousePacket(down ? flag : flag << 1, 0, 0, 0);
        }

        private static NativeInput MousePacket(uint flags, int x, int y, int data)
        {
            var input = new NativeInput { Type = InputMouse };
            input.Data.Mouse = new NativeMouseInput { X = x, Y = y, Flags = flags, Data = unchecked((uint)data) };
            return input;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeInput { internal uint Type; internal NativeInputUnion Data; }
        [StructLayout(LayoutKind.Explicit)]
        private struct NativeInputUnion
        {
            [FieldOffset(0)] internal NativeMouseInput Mouse;
            [FieldOffset(0)] internal NativeKeyboardInput Keyboard;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeMouseInput
        {
            internal int X, Y;
            internal uint Data, Flags, Time;
            internal UIntPtr Extra;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeKeyboardInput
        {
            internal ushort Key, Scan;
            internal uint Flags, Time;
            internal UIntPtr Extra;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct Point { internal int X, Y; }
        [StructLayout(LayoutKind.Sequential)]
        private struct Rect { internal int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct GuiThreadInfo
        {
            internal uint Size, Flags;
            internal IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
            internal Rect CaretRect;
        }
        private delegate IntPtr HookProcedure(int code, IntPtr message, IntPtr data);

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder className, int capacity);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint relation);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint relation);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr hwnd, ref Point point);
        [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int virtualKey);
        [DllImport("user32.dll")] private static extern uint SendInput(uint count, [In] NativeInput[] inputs, int size);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SetWindowsHookEx(int type, HookProcedure hook, IntPtr module, uint thread);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string module);
    }
}
