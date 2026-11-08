using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace SpaceKatMonitor
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct VirtualGamepadReport
    {
        public ushort Buttons;
        public byte LeftTrigger, RightTrigger;
        public short LeftX, LeftY, RightX, RightY;
    }

    internal interface IVirtualGamepadBackend : IDisposable
    {
        bool IsConnected { get; }
        bool TryConnect(out string error);
        bool TryUpdate(VirtualGamepadReport report, out string error);
        bool TryDisconnect(out string error);
    }

    /// <summary>
    /// Creates an Xbox 360 target only after an explicit TryConnect. Every
    /// report keeps buttons and the left stick at neutral. The
    /// constructor, previews and fake-backend tests create no virtual target.
    /// </summary>
    public sealed class VirtualGamepadOutput : IDisposable
    {
        private readonly IVirtualGamepadBackend backend;
        private bool active;
        private bool disposed;

        public VirtualGamepadOutput() : this(new NativeGamepadBackend()) { }

        internal VirtualGamepadOutput(IVirtualGamepadBackend outputBackend)
        {
            if (outputBackend == null) throw new ArgumentNullException("outputBackend");
            backend = outputBackend;
            Status = "虚拟手柄未连接";
        }

        public bool IsConnected { get { return active && backend.IsConnected; } }
        public bool HasPendingStop { get { return !active && backend.IsConnected; } }
        public string Status { get; private set; }
        public string LastError { get; private set; }
        public static int NativeReportSize { get { return Marshal.SizeOf(typeof(VirtualGamepadReport)); } }

        // This opens only a bus connection, never a virtual controller target.
        public static bool TryProbe(out string error)
        {
            using (NativeGamepadBackend probe = new NativeGamepadBackend())
                return probe.TryProbe(out error);
        }

        public bool TryConnect(out string error)
        {
            error = null;
            if (disposed) { error = "虚拟手柄输出已关闭。"; LastError = error; return false; }
            if (IsConnected) return true;
            if (backend.IsConnected)
            {
                Stop();
                if (HasPendingStop) { error = Status; LastError = error; return false; }
            }
            try
            {
                if (!backend.TryConnect(out error))
                {
                    string initial = error ?? "无法连接虚拟手柄。";
                    Stop();
                    error = initial + (HasPendingStop ? " " + Status : "");
                    Status = error;
                    LastError = error;
                    return false;
                }
                // Always begin at center, including when the input stick is
                // already displaced when the user presses Enable.
                if (!backend.TryUpdate(new VirtualGamepadReport(), out error))
                {
                    string initial = error ?? "虚拟手柄初始化回中失败。";
                    Stop();
                    error = initial + (HasPendingStop ? " " + Status : "");
                    Status = error;
                    LastError = error;
                    return false;
                }
                active = true;
                LastError = null;
                Status = "虚拟 Xbox 360 手柄已连接 · 输出右摇杆、扳机和映射按键";
                return true;
            }
            catch (Exception problem)
            {
                string initial = "虚拟手柄连接失败：" + problem.Message;
                Stop();
                error = initial + (HasPendingStop ? " " + Status : "");
                Status = error;
                LastError = error;
                return false;
            }
        }

        public bool TryUpdate(short rx, short ry, out string error)
        {
            return TryUpdate(rx, ry, 0, 0, out error);
        }

        public bool TryUpdate(short rx, short ry, byte leftTrigger, byte rightTrigger, out string error)
        {
            return TryUpdate(rx, ry, leftTrigger, rightTrigger, GamepadButtons.None, out error);
        }

        internal bool TryUpdate(short rx, short ry, byte leftTrigger, byte rightTrigger, GamepadButtons buttons, out string error)
        {
            error = null;
            if (!IsConnected || disposed)
            {
                error = "虚拟手柄尚未启用。";
                LastError = error;
                return false;
            }
            try
            {
                VirtualGamepadReport report = new VirtualGamepadReport
                {
                    Buttons = (ushort)(buttons & GamepadButtonModel.FaceButtons),
                    RightX = rx, RightY = ry,
                    LeftTrigger = leftTrigger, RightTrigger = rightTrigger
                };
                if (backend.TryUpdate(report, out error)) { LastError = null; return true; }
                error = error ?? "虚拟手柄更新失败。";
            }
            catch (Exception problem) { error = "虚拟手柄更新异常：" + problem.Message; }
            string initialError = error;
            Stop();
            error = initialError + (HasPendingStop ? " " + Status : "");
            Status = error;
            LastError = error;
            return false;
        }

        public void Stop()
        {
            active = false;
            string error = null;
            bool stopped = false;
            // Retain a failed target for later Stop retries. An inability to
            // neutralize is not silently represented as successful cleanup.
            for (int attempt = 0; attempt < 3 && !stopped; attempt++)
            {
                try { stopped = backend.TryDisconnect(out error); }
                catch (Exception problem) { error = problem.Message; }
            }
            Status = stopped && !backend.IsConnected ? (error == null ? "虚拟手柄已回中并断开" : "虚拟手柄已移除，回中报文未确认：" + error)
                : "虚拟手柄尚未完全停止，请重试停止：" + (error ?? "设备仍然连接。");
            LastError = stopped && !backend.IsConnected ? error : Status;
        }

        public void Dispose()
        {
            Stop();
            try { backend.Dispose(); }
            catch (Exception problem) { LastError = "关闭虚拟手柄输出失败：" + problem.Message; Status = LastError; }
            disposed = true;
            if (backend.IsConnected) { Status = "虚拟手柄仍待停止，请重试停止。"; LastError = Status; }
        }

        private sealed class NativeGamepadBackend : IVirtualGamepadBackend
        {
            private const uint Success = 0x20000000;
            private const uint NotPluggedIn = 0xE0000007;
            private const uint Uninitialized = 0xE0000006;
            private IntPtr module, client, target;
            private bool attached;
            private AllocDelegate alloc, targetAlloc;
            private FreeDelegate free, disconnect, targetFree;
            private ConnectDelegate connect;
            private TargetDelegate targetAdd, targetRemove;
            private UpdateDelegate update;

            public bool IsConnected { get { return attached; } }

            public bool TryProbe(out string error)
            {
                error = null;
                IntPtr probeClient = IntPtr.Zero;
                try
                {
                    LoadApi();
                    probeClient = alloc();
                    if (probeClient == IntPtr.Zero) { error = "无法分配虚拟手柄总线连接。"; return false; }
                    uint result = connect(probeClient);
                    if (result != Success) { error = Describe(result); return false; }
                    return true;
                }
                catch (Exception problem) { error = problem.Message; return false; }
                finally
                {
                    if (probeClient != IntPtr.Zero)
                    {
                        disconnect(probeClient);
                        free(probeClient);
                    }
                }
            }

            public bool TryConnect(out string error)
            {
                error = null;
                if (attached) return true;
                try
                {
                    LoadApi();
                    client = alloc();
                    if (client == IntPtr.Zero) { error = "无法分配虚拟手柄总线连接。"; return false; }
                    uint result = connect(client);
                    if (result != Success) { error = Describe(result); CleanupDetached(); return false; }
                    target = targetAlloc();
                    if (target == IntPtr.Zero) { error = "无法分配虚拟 Xbox 360 手柄。"; CleanupDetached(); return false; }
                    result = targetAdd(client, target);
                    if (result != Success) { error = Describe(result); CleanupDetached(); return false; }
                    attached = true;
                    return true;
                }
                catch (Exception problem) { error = problem.Message; CleanupDetached(); return false; }
            }

            public bool TryUpdate(VirtualGamepadReport report, out string error)
            {
                error = null;
                if (!attached) { error = "虚拟手柄尚未连接。"; return false; }
                uint result = update(client, target, report);
                if (result == Success) return true;
                error = Describe(result);
                return false;
            }

            public bool TryDisconnect(out string error)
            {
                error = null;
                string neutralError = null;
                if (attached)
                {
                    try
                    {
                        uint neutral = update(client, target, new VirtualGamepadReport());
                        if (neutral != Success) neutralError = Describe(neutral);
                    }
                    catch (Exception problem) { neutralError = problem.Message; }
                    try
                    {
                        uint result = targetRemove(client, target);
                        if (result != Success && result != NotPluggedIn && result != Uninitialized)
                        {
                            error = (neutralError == null ? "" : "回中失败：" + neutralError + " ") + "移除手柄失败：" + Describe(result);
                            return false;
                        }
                        attached = false;
                    }
                    catch (Exception problem) { error = "移除手柄异常：" + problem.Message; return false; }
                }
                CleanupDetached();
                error = neutralError;
                return true;
            }

            private void CleanupDetached()
            {
                if (attached) return;
                if (target != IntPtr.Zero) { targetFree(target); target = IntPtr.Zero; }
                if (client != IntPtr.Zero) { disconnect(client); free(client); client = IntPtr.Zero; }
            }

            private void LoadApi()
            {
                if (module != IntPtr.Zero) return;
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "native", IntPtr.Size == 8 ? "x64" : "x86", "ViGEmClient.dll");
                if (!File.Exists(path)) throw new FileNotFoundException("缺少虚拟手柄客户端，请保留程序目录内的 native 文件夹。", path);
                module = LoadLibraryEx(path, IntPtr.Zero, 0x00000100 | 0x00001000);
                if (module == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法加载虚拟手柄客户端或其运行库。");
                try
                {
                    alloc = Function<AllocDelegate>("vigem_alloc");
                    free = Function<FreeDelegate>("vigem_free");
                    connect = Function<ConnectDelegate>("vigem_connect");
                    disconnect = Function<FreeDelegate>("vigem_disconnect");
                    targetAlloc = Function<AllocDelegate>("vigem_target_x360_alloc");
                    targetFree = Function<FreeDelegate>("vigem_target_free");
                    targetAdd = Function<TargetDelegate>("vigem_target_add");
                    targetRemove = Function<TargetDelegate>("vigem_target_remove");
                    update = Function<UpdateDelegate>("vigem_target_x360_update");
                }
                catch { FreeLibrary(module); module = IntPtr.Zero; throw; }
            }

            private T Function<T>(string name) where T : class
            {
                IntPtr function = GetProcAddress(module, name);
                if (function == IntPtr.Zero) throw new EntryPointNotFoundException("虚拟手柄客户端缺少接口：" + name);
                return Marshal.GetDelegateForFunctionPointer(function, typeof(T)) as T;
            }

            public void Dispose()
            {
                string ignored;
                TryDisconnect(out ignored);
                if (!attached && module != IntPtr.Zero) { FreeLibrary(module); module = IntPtr.Zero; }
            }

            ~NativeGamepadBackend() { try { Dispose(); } catch { } }

            private static string Describe(uint code)
            {
                string detail;
                switch (code)
                {
                    case 0xE0000001: detail = "没有可用的 ViGEmBus 驱动；可使用仅预览模式。"; break;
                    case 0xE0000002: detail = "虚拟手柄插槽已满，请关闭占用的虚拟手柄程序。"; break;
                    case 0xE0000008: detail = "ViGEmBus 驱动版本不兼容。"; break;
                    case 0xE0000009: detail = "无法打开 ViGEmBus 驱动。"; break;
                    case 0xE0000007: detail = "虚拟手柄已断开。"; break;
                    case 0xE0000018: detail = "连接虚拟手柄超时。"; break;
                    default: detail = "虚拟手柄操作未完成。"; break;
                }
                return detail + " (0x" + code.ToString("X8") + ")";
            }

            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr AllocDelegate();
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void FreeDelegate(IntPtr pointer);
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint ConnectDelegate(IntPtr pointer);
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint TargetDelegate(IntPtr client, IntPtr target);
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint UpdateDelegate(IntPtr client, IntPtr target, VirtualGamepadReport report);
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
            [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)] private static extern IntPtr GetProcAddress(IntPtr module, string name);
            [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool FreeLibrary(IntPtr module);
        }
    }
}
