using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SpaceKatMonitor
{
    // Injectable boundary: tests never touch the desktop or global shortcut.
    internal struct MouseOutputPacket
    {
        public int X, Y, Data;
        public uint Flags;
    }

    internal interface IMouseOutputBackend
    {
        bool RegisterStopHotkey(IntPtr hwnd);
        void UnregisterStopHotkey(IntPtr hwnd);
        uint Send(MouseOutputPacket[] packets);
        int LastError { get; }
    }

    /// <summary>
    /// Sends relative motion, vertical wheel and mouse button changes on the HWND thread.
    /// DOWN states remain tracked until successful UP, including stop failures.
    /// </summary>
    public sealed class MouseOutput : IDisposable
    {
        private const int StopHotkeyId = 0x534B;
        private const int WmHotkey = 0x0312;
        private const uint ModAlt = 0x0001;
        private const uint ModControl = 0x0002;
        private const uint ModNoRepeat = 0x4000;
        private const uint VkF8 = 0x77;
        private const uint InputMouse = 0;
        private const uint MouseEventMove = 0x0001;
        private const uint MouseEventLeftDown = 0x0002;
        private const uint MouseEventLeftUp = 0x0004;
        private const uint MouseEventRightDown = 0x0008;
        private const uint MouseEventRightUp = 0x0010;
        private const uint MouseEventMiddleDown = 0x0020;
        private const uint MouseEventMiddleUp = 0x0040;
        private const uint MouseEventXDown = 0x0080;
        private const uint MouseEventXUp = 0x0100;
        private const uint MouseEventWheel = 0x0800;
        private const int XButton1 = 1;
        private const int XButton2 = 2;
        private const int MaxStep = 256;
        private const int MaxWheelStep = 1200;
        private const int ReleaseAttempts = 3;

        private readonly IntPtr windowHandle;
        private readonly IMouseOutputBackend backend;
        private bool armed;
        private bool disposed;
        private bool leftDown;
        private bool rightDown;
        private bool middleDown;
        private bool backDown;
        private bool forwardDown;

        public MouseOutput(IntPtr hwnd) : this(hwnd, new NativeBackend()) { }

        internal MouseOutput(IntPtr hwnd, IMouseOutputBackend inputBackend)
        {
            if (hwnd == IntPtr.Zero)
                throw new ArgumentException("需要已创建的窗口句柄。", "hwnd");
            if (inputBackend == null) throw new ArgumentNullException("inputBackend");
            windowHandle = hwnd;
            backend = inputBackend;
        }

        public bool StopHotkeyAvailable { get; private set; }
        public bool HasPendingRelease { get { return leftDown || rightDown || middleDown || backDown || forwardDown; } }
        public bool IsLeftDown { get { return leftDown; } }
        public bool IsRightDown { get { return rightDown; } }
        public string LastReleaseError { get; private set; }

        public static int NativeInputSize
        {
            get { return Marshal.SizeOf(typeof(NativeInput)); }
        }

        public void Arm()
        {
            if (disposed) throw new ObjectDisposedException("MouseOutput");
            if (armed) return;
            if (HasPendingRelease)
            {
                Disarm();
                if (HasPendingRelease) return;
            }
            StopHotkeyAvailable = backend.RegisterStopHotkey(windowHandle);
            armed = StopHotkeyAvailable;
        }

        public bool IsStopMessage(int msg, IntPtr wParam)
        {
            return msg == WmHotkey && wParam.ToInt64() == StopHotkeyId;
        }

        public bool TryMove(int dx, int dy, out string error)
        {
            error = null;
            if (dx == 0 && dy == 0) return true;
            if (!CanSend(out error)) return false;
            dx = Math.Max(-MaxStep, Math.Min(MaxStep, dx));
            dy = Math.Max(-MaxStep, Math.Min(MaxStep, dy));
            return TrySend(new[] { new MouseOutputPacket { X = dx, Y = dy, Flags = MouseEventMove } }, out error);
        }

        public bool TryScroll(int delta, out string error)
        {
            error = null;
            if (delta == 0) return true;
            if (!CanSend(out error)) return false;
            delta = Math.Max(-MaxWheelStep, Math.Min(MaxWheelStep, delta));
            return TrySend(new[] { new MouseOutputPacket { Data = delta, Flags = MouseEventWheel } }, out error);
        }

        public bool TryButtons(MouseButtonSignal[] signals, out string error)
        {
            error = null;
            if (signals == null)
            {
                return RejectButtonRequest("鼠标按键动作为空。", out error);
            }
            if (signals.Length == 0) return true;
            if (!CanSend(out error)) return false;
            MouseOutputPacket[] packets = new MouseOutputPacket[signals.Length];
            for (int i = 0; i < signals.Length; i++)
            {
                uint flag;
                switch (signals[i])
                {
                    case MouseButtonSignal.LeftDown: flag = MouseEventLeftDown; break;
                    case MouseButtonSignal.LeftUp: flag = MouseEventLeftUp; break;
                    case MouseButtonSignal.RightDown: flag = MouseEventRightDown; break;
                    case MouseButtonSignal.RightUp: flag = MouseEventRightUp; break;
                    case MouseButtonSignal.MiddleDown: flag = MouseEventMiddleDown; break;
                    case MouseButtonSignal.MiddleUp: flag = MouseEventMiddleUp; break;
                    case MouseButtonSignal.BackDown: flag = MouseEventXDown; packets[i].Data = XButton1; break;
                    case MouseButtonSignal.BackUp: flag = MouseEventXUp; packets[i].Data = XButton1; break;
                    case MouseButtonSignal.ForwardDown: flag = MouseEventXDown; packets[i].Data = XButton2; break;
                    case MouseButtonSignal.ForwardUp: flag = MouseEventXUp; packets[i].Data = XButton2; break;
                    case MouseButtonSignal.ScrollUp: flag = MouseEventWheel; packets[i].Data = MouseWheelModel.WheelDelta; break;
                    case MouseButtonSignal.ScrollDown: flag = MouseEventWheel; packets[i].Data = -MouseWheelModel.WheelDelta; break;
                    default:
                        return RejectButtonRequest("未知的鼠标按键动作。", out error);
                }
                packets[i].Flags = flag;
            }
            return TrySend(packets, out error);
        }

        private bool RejectButtonRequest(string message, out string error)
        {
            Disarm();
            error = message;
            if (LastReleaseError != null) error += " " + LastReleaseError;
            return false;
        }

        private bool CanSend(out string error)
        {
            error = null;
            if (!disposed && armed) return true;
            error = "鼠标模拟尚未开启，或已停止。";
            return false;
        }

        private bool TrySend(MouseOutputPacket[] packets, out string error)
        {
            error = null;
            try
            {
                uint sent = backend.Send(packets);
                if (sent == packets.Length)
                {
                    ApplySuccessfulPackets(packets);
                    return true;
                }
                // A partial SendInput count is not an identity guarantee.
                // Conservatively UP all possibly sent DOWNs, even on 0/throw.
                TrackPossibleDowns(packets);
                error = FailureMessage("系统未完整接收鼠标输入", backend.LastError);
            }
            catch (Exception problem)
            {
                TrackPossibleDowns(packets);
                error = "鼠标输入发送异常：" + problem.Message;
            }
            Disarm();
            if (LastReleaseError != null) error += " " + LastReleaseError;
            return false;
        }

        private void ApplySuccessfulPackets(MouseOutputPacket[] packets)
        {
            foreach (MouseOutputPacket packet in packets)
            {
                TrackPossibleDown(packet);
                uint flag = packet.Flags;
                if ((flag & MouseEventLeftUp) != 0) leftDown = false;
                if ((flag & MouseEventRightUp) != 0) rightDown = false;
                if ((flag & MouseEventMiddleUp) != 0) middleDown = false;
                if ((flag & MouseEventXUp) != 0 && packet.Data == XButton1) backDown = false;
                if ((flag & MouseEventXUp) != 0 && packet.Data == XButton2) forwardDown = false;
            }
            if (!HasPendingRelease) LastReleaseError = null;
        }

        private void TrackPossibleDowns(MouseOutputPacket[] packets)
        {
            foreach (MouseOutputPacket packet in packets)
                TrackPossibleDown(packet);
        }

        private void TrackPossibleDown(MouseOutputPacket packet)
        {
            if ((packet.Flags & MouseEventLeftDown) != 0) leftDown = true;
            if ((packet.Flags & MouseEventRightDown) != 0) rightDown = true;
            if ((packet.Flags & MouseEventMiddleDown) != 0) middleDown = true;
            if ((packet.Flags & MouseEventXDown) != 0 && packet.Data == XButton1) backDown = true;
            if ((packet.Flags & MouseEventXDown) != 0 && packet.Data == XButton2) forwardDown = true;
        }

        public void Disarm()
        {
            bool wasArmed = armed;
            armed = false;
            StopHotkeyAvailable = false;
            // Cleanup remains retryable after a failed stop or even Dispose.
            ReleaseButtons();
            if (wasArmed)
            {
                try { backend.UnregisterStopHotkey(windowHandle); }
                catch (Exception) { }
            }
        }

        private void ReleaseButtons()
        {
            if (!HasPendingRelease)
            {
                LastReleaseError = null;
                return;
            }
            for (int attempt = 0; attempt < ReleaseAttempts && HasPendingRelease; attempt++)
            {
                // A single UP per call has an unambiguous success count.
                if (leftDown) TryRelease(MouseEventLeftUp, ref leftDown);
                if (rightDown) TryRelease(MouseEventRightUp, ref rightDown);
                if (middleDown) TryRelease(MouseEventMiddleUp, ref middleDown);
                if (backDown) TryRelease(MouseEventXUp, ref backDown, XButton1);
                if (forwardDown) TryRelease(MouseEventXUp, ref forwardDown, XButton2);
            }
            if (!HasPendingRelease) LastReleaseError = null;
        }

        private void TryRelease(uint flag, ref bool isDown, int data = 0)
        {
            try
            {
                if (backend.Send(new[] { new MouseOutputPacket { Flags = flag, Data = data } }) == 1)
                {
                    isDown = false;
                    return;
                }
                LastReleaseError = FailureMessage("仍有鼠标按键未能释放，请点“重试释放”或按一下实际鼠标对应按键", backend.LastError);
            }
            catch (Exception problem)
            {
                LastReleaseError = "鼠标按键释放异常，请点“重试释放”或按一下实际鼠标对应按键：" + problem.Message;
            }
        }

        private static string FailureMessage(string message, int code)
        {
            return code == 0 ? message + "。" : message + "：" + new Win32Exception(code).Message;
        }

        public void Dispose()
        {
            Disarm();
            disposed = true;
        }

        private sealed class NativeBackend : IMouseOutputBackend
        {
            public int LastError { get; private set; }
            public bool RegisterStopHotkey(IntPtr hwnd)
            {
                return RegisterHotKey(hwnd, StopHotkeyId, ModControl | ModAlt | ModNoRepeat, VkF8);
            }
            public void UnregisterStopHotkey(IntPtr hwnd)
            {
                UnregisterHotKey(hwnd, StopHotkeyId);
            }
            public uint Send(MouseOutputPacket[] packets)
            {
                NativeInput[] inputs = new NativeInput[packets.Length];
                for (int i = 0; i < inputs.Length; i++)
                {
                    inputs[i] = ToNativeInput(packets[i]);
                }
                uint sent = SendInput((uint)inputs.Length, inputs, NativeInputSize);
                LastError = Marshal.GetLastWin32Error();
                return sent;
            }
        }

        private static NativeInput ToNativeInput(MouseOutputPacket packet)
        {
            NativeInput input = new NativeInput();
            input.Type = InputMouse;
            input.Data.Mouse = new NativeMouseInput { X = packet.X, Y = packet.Y, Flags = packet.Flags,
                MouseData = (packet.Flags & (MouseEventWheel | MouseEventXDown | MouseEventXUp)) != 0
                    ? unchecked((uint)packet.Data) : 0 };
            return input;
        }

        // Complete native union: INPUT is 40 bytes on x64 and 28 on x86.
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeInput
        {
            public uint Type;
            public NativeInputUnion Data;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct NativeInputUnion
        {
            [FieldOffset(0)] public NativeMouseInput Mouse;
            [FieldOffset(0)] public NativeKeyboardInput Keyboard;
            [FieldOffset(0)] public NativeHardwareInput Hardware;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeMouseInput
        {
            public int X;
            public int Y;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public UIntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeKeyboardInput
        {
            public ushort VirtualKey;
            public ushort ScanCode;
            public uint Flags;
            public uint Time;
            public UIntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeHardwareInput
        {
            public uint Message;
            public ushort ParameterLow;
            public ushort ParameterHigh;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint numberOfInputs, [In] NativeInput[] inputs, int inputSize);
    }
}
