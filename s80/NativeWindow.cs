using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SpaceKat.FusionS80;

internal sealed class NativeWindow : IDisposable
{
    private const uint WmClose = 0x0010;
    private readonly Thread thread;
    private readonly WindowProcedure procedure;
    private int disposed;

    public NativeWindow(string className, string title, Action<uint, nuint, nint>? onMessage = null,
        Func<nint, IDisposable>? registerInput = null)
    {
        var started = new TaskCompletionSource<nint>(TaskCreationOptions.RunContinuationsAsynchronously);
        procedure = (window, message, wParam, lParam) =>
        {
            if (message == WmClose)
            {
                PostQuitMessage(0);
                return 0;
            }

            onMessage?.Invoke(message, wParam, lParam);
            return DefWindowProcW(window, message, wParam, lParam);
        };
        thread = new Thread(() => Run(className, title, started, registerInput)) { IsBackground = true, Name = title };
        thread.Start();
        Handle = started.Task.GetAwaiter().GetResult();
    }

    public nint Handle { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        PostMessageW(Handle, WmClose, 0, 0);
        thread.Join(TimeSpan.FromSeconds(3));
    }

    private void Run(string className, string title, TaskCompletionSource<nint> started,
        Func<nint, IDisposable>? registerInput)
    {
        nint instance = GetModuleHandleW(null);
        ushort atom = 0;
        nint window = 0;
        try
        {
            var definition = new WindowClass
            {
                Instance = instance,
                Procedure = Marshal.GetFunctionPointerForDelegate(procedure),
                ClassName = className
            };
            atom = RegisterClassW(ref definition);
            if (atom == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            window = CreateWindowExW(0, className, title, 0, 0, 0, 0, 0, 0, 0, instance, 0);
            if (window == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            using var input = registerInput?.Invoke(window);
            started.SetResult(window);
            while (GetMessageW(out WindowMessage message, 0, 0, 0) > 0)
                DispatchMessageW(in message);
        }
        catch (Exception error)
        {
            started.TrySetException(error);
        }
        finally
        {
            if (window != 0)
                DestroyWindow(window);
            if (atom != 0)
                UnregisterClassW(className, instance);
            GC.KeepAlive(procedure);
        }
    }

    public static uint GetOwnerProcessId(nint window)
    {
        GetWindowThreadProcessId(window, out uint processId);
        return processId;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Style;
        public nint Procedure;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public string? MenuName;
        public string ClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowMessage
    {
        public nint Window;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint GetModuleHandleW(string? name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern ushort RegisterClassW(ref WindowClass definition);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern bool UnregisterClassW(string className, nint instance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern nint CreateWindowExW(uint extendedStyle, string className, string title,
        uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint data);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int GetMessageW(out WindowMessage message, nint window, uint min, uint max);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint DispatchMessageW(in WindowMessage message);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint DefWindowProcW(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    internal static extern uint RegisterWindowMessageW(string name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern nint FindWindowW(string className, nint title);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessageW(nint window, uint message, nuint wParam, nint lParam);
}
