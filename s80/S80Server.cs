using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using SpaceKatMonitor;

namespace SpaceKat.FusionS80;

internal sealed class S80Server(string prefix, Func<long, MotionSample?> readInput, FusionNativeBridge input)
{
    private readonly Dictionary<long, S80Session> sessions = [];
    private readonly HashSet<int> unhandled = [];
    private readonly HashSet<(int Transport, int Operation, int Item)> unhandledSync = [];
    private readonly LiveNavigation live = new();
    private long nextHandle = 0x10000;
    private uint eventMessage;
    private S80Session? grabbedSession;
    private bool connected;
    private int buttonCursor;

    public void Run(CancellationToken cancellationToken, Action? ready = null)
    {
        string windowClass = prefix + S80Protocol.DriverWindowClass;
        if (NativeWindow.FindWindowW(windowClass, 0) != 0)
            throw new InvalidOperationException("导航接口已被占用。请先退出 3DxWare 或其他导航程序。");

        using var driverLock = CreateMutex(prefix + S80Protocol.DriverLock);
        using var appLock = CreateMutex(prefix + S80Protocol.AppLock);
        using var driverPoke = CreateSemaphore(prefix + S80Protocol.DriverPoke);
        using var appPoke = CreateSemaphore(prefix + S80Protocol.AppPoke);
        using var mapping = MemoryMappedFile.CreateNew(prefix + S80Protocol.ControlMapping,
            S80Protocol.PayloadOffset + S80Protocol.ControlPayloadCapacity);
        using var control = mapping.CreateViewAccessor();
        using var window = new NativeWindow(windowClass, prefix + S80Protocol.DriverWindowTitle);

        eventMessage = NativeWindow.RegisterWindowMessageW(prefix + S80Protocol.EventMessage);
        if (eventMessage == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        control.Write(0, BinaryPrimitives.ReadInt64LittleEndian(RandomNumberGenerator.GetBytes(8)) | 1);
        Console.WriteLine("STATUS READY");
        ready?.Invoke();

        try
        {
            WaitHandle[] waits = [cancellationToken.WaitHandle, driverPoke];
            var liveClock = Stopwatch.StartNew();
            long nextLiveFrame = 0;
            long nextCleanup = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                int timeout = (int)Math.Max(0, nextLiveFrame - liveClock.ElapsedMilliseconds);
                int signaled = WaitHandle.WaitAny(waits, timeout);
                if (signaled == 0)
                    break;
                if (signaled == 1)
                    Reply(control, appPoke);
                if (liveClock.ElapsedMilliseconds >= nextLiveFrame)
                {
                    uint foreground = NativeWindow.GetOwnerProcessId(NativeWindow.GetForegroundWindow());
                    S80Session? target = grabbedSession is { } captured && IsLiveTarget(captured, foreground)
                        ? captured : null;
                    long now = FusionNativeBridge.GetTickCount64();
                    live.Update(target, readInput(now), eventMessage, now);
                    ForwardButtons(foreground);
                    nextLiveFrame = liveClock.ElapsedMilliseconds + LiveNavigation.FrameInterval;
                }
                if (liveClock.ElapsedMilliseconds >= nextCleanup)
                {
                    RemoveClosedWindows();
                    nextCleanup = liveClock.ElapsedMilliseconds + 1000;
                }
            }
        }
        finally
        {
            live.Stop(eventMessage);
            foreach (S80Session session in sessions.Values)
                session.Dispose();
            sessions.Clear();
        }
    }

    private void ForwardButtons(uint foreground)
    {
        int head = input.ButtonHead;
        if (buttonCursor == head) return;
        // Read the clock after the published head so a concurrent press cannot look like a future sample.
        long now = FusionNativeBridge.GetTickCount64();
        if (!input.TryReadActiveButtons(now, out int discardedThrough)) return;
        // Skip taps from before the stop even if the helper resumed between two frames.
        if (unchecked(discardedThrough - head) > 0) return;
        if (unchecked(discardedThrough - buttonCursor) > 0) buttonCursor = discardedThrough;
        if (buttonCursor == head) return;
        if (unchecked((uint)(head - buttonCursor)) > FusionNativeBridge.ButtonCapacity)
            buttonCursor = unchecked(head - FusionNativeBridge.ButtonCapacity);

        // View commands are also valid while the puck is centered and no motion session is grabbed.
        S80Session? target = grabbedSession is { } captured && IsLiveTarget(captured, foreground) ? captured :
            sessions.Values.LastOrDefault(session => IsLiveTarget(session, foreground));
        while (buttonCursor != head)
        {
            int next = unchecked(buttonCursor + 1);
            if (target is not null && input.TryReadButton(next, out int command, out uint owner, out long time) &&
                owner == target.OwnerProcessId && time >= target.OpenedAt && time <= now &&
                now - time < FusionNativeBridge.SampleTimeout &&
                (FusionViewCommand)command is FusionViewCommand.Fit or FusionViewCommand.Top or
                    FusionViewCommand.Right or FusionViewCommand.Front)
            {
                if (prefix.Length == 0 &&
                    !IsLiveTarget(target, NativeWindow.GetOwnerProcessId(NativeWindow.GetForegroundWindow())))
                    break;
                if (!target.SendCommand(eventMessage, command)) break;
            }
            buttonCursor = next;
        }
    }

    private void Reply(MemoryMappedViewAccessor control, Semaphore appPoke)
    {
        int operation = control.ReadInt32(S80Protocol.OperationOffset);
        int length = control.ReadInt32(S80Protocol.LengthOffset);
        int replyRequested = control.ReadInt32(S80Protocol.ReplyRequestedOffset);
        int result = operation;
        byte[] response = [];
        if (length < 0 || length > S80Protocol.ControlPayloadCapacity || (uint)replyRequested > 1)
        {
            result = S80Protocol.InvalidLength;
        }
        else
        {
            var request = new byte[length];
            if (length != 0)
                control.ReadArray(S80Protocol.PayloadOffset, request, 0, length);
            (result, response) = Dispatch(operation, request);
        }

        if (response.Length != 0)
            control.WriteArray(S80Protocol.PayloadOffset, response, 0, response.Length);
        control.Write(S80Protocol.LengthOffset, response.Length);
        control.Write(S80Protocol.OperationOffset, result);
        if (operation is S80Protocol.GetBufferSizes or S80Protocol.Initialize or S80Protocol.GetDeviceCount or
            S80Protocol.OpenSession or S80Protocol.CloseSession)
            Console.WriteLine($"S80 请求 0x{operation:X}，回复状态 {result}，{response.Length} 字节");
        try
        {
            appPoke.Release();
        }
        catch (SemaphoreFullException)
        {
            Console.Error.WriteLine("上一个请求的回复尚未被读取。");
        }
    }

    private (int Status, byte[] Response) Dispatch(int operation, byte[] request)
    {
        switch (operation)
        {
            case S80Protocol.GetBufferSizes:
                return (operation, S80Protocol.Integers(S80Protocol.ControlPayloadCapacity, S80Protocol.EventCapacity));
            case S80Protocol.Initialize:
                if (request.Length < 8)
                    return (S80Protocol.InvalidLength, []);
                bool supported = BinaryPrimitives.ReadInt32LittleEndian(request) == 1 &&
                    BinaryPrimitives.ReadInt32LittleEndian(request.AsSpan(4)) == 0;
                return (operation, S80Protocol.Integers(supported ? 0 : -1));
            case S80Protocol.Terminate:
                return request.Length >= 4 ? (operation, S80Protocol.Integers(0)) : (S80Protocol.InvalidLength, []);
            case S80Protocol.GetDeviceCount:
                return (operation, S80Protocol.Integers(1));
            case S80Protocol.GetDriverInfo:
                return (operation, S80Protocol.DriverInfo());
            case S80Protocol.GetDevicePort:
            case S80Protocol.GetDeviceInfo:
                if (request.Length < sizeof(long))
                    return (S80Protocol.InvalidLength, []);
                sessions.TryGetValue(BinaryPrimitives.ReadInt64LittleEndian(request), out S80Session? deviceSession);
                return (operation, operation == S80Protocol.GetDeviceInfo
                    ? S80Protocol.DeviceInfo(deviceSession is not null)
                    : S80Protocol.DevicePort(deviceSession));
            case S80Protocol.CloseSession:
                return Close(request);
            case S80Protocol.OpenSession:
                return Open(request);
            case S80Protocol.GrabDevice:
            case S80Protocol.ReleaseDevice:
                return ChangeDeviceGrab(operation, request);
            case S80Protocol.SyncSend:
            case S80Protocol.SyncQuery:
                return TraceSync(operation, request);
            default:
                if (unhandled.Add(operation))
                    Console.WriteLine($"尚未实现的 S80 请求 0x{operation:X}，{request.Length} 字节");
                return (S80Protocol.Unsupported, []);
        }
    }

    private (int Status, byte[] Response) Open(byte[] request)
    {
        if (!S80Protocol.TryParseOpen(request, out OpenRequest open))
            return (S80Protocol.InvalidLength, []);

        uint owner = NativeWindow.GetOwnerProcessId(open.Window);
        if (!IsSupportedClient(owner, open.Application))
        {
            Console.WriteLine($"跳过会话 {open.Application}（PID {owner}）");
            return (S80Protocol.OpenSession, S80Protocol.OpenReply(1, 0, -1, -1, 0));
        }

        RemoveClosedWindows();

        S80Session? existing = sessions.Values.FirstOrDefault(session =>
            session.Request.Window == open.Window && session.Request.Device == open.Device);
        if (existing is not null)
            return SessionReply(existing);

        for (int number = 0; number < 100; number++)
        {
            if (sessions.Values.Any(session => session.MapNumber == number))
                continue;

            try
            {
                var session = new S80Session(prefix, number, nextHandle++, open, owner);
                sessions.Add(session.Handle, session);
                Console.WriteLine($"已登记 {open.Application}（PID {owner}，队列 {number:D2}）");
                ReportConnection();
                return SessionReply(session);
            }
            catch (IOException error) when ((error.HResult & 0xffff) is 80 or 183)
            {
                // A previous client may still hold a handle to the old queue.
            }
        }

        return (S80Protocol.OpenSession, S80Protocol.OpenReply(1, 0, -1, -1, 0));
    }

    private (int Status, byte[] Response) SessionReply(S80Session session) =>
        (S80Protocol.OpenSession, S80Protocol.OpenReply(0, session.Handle, session.Request.Device,
            session.MapNumber, eventMessage));

    private (int Status, byte[] Response) Close(byte[] request)
    {
        if (request.Length < sizeof(long))
            return (S80Protocol.InvalidLength, []);

        long handle = BinaryPrimitives.ReadInt64LittleEndian(request);
        if (!sessions.TryGetValue(handle, out S80Session? session))
            return (S80Protocol.CloseSession, S80Protocol.Integers(4));

        RemoveSession(session);
        ReportConnection();
        return (S80Protocol.CloseSession, S80Protocol.Integers(0));
    }

    private void RemoveClosedWindows()
    {
        foreach (S80Session stale in sessions.Values.Where(session =>
            NativeWindow.GetOwnerProcessId(session.Request.Window) != session.OwnerProcessId).ToArray())
            RemoveSession(stale);
        ReportConnection();
    }

    private void RemoveSession(S80Session session)
    {
        sessions.Remove(session.Handle);
        live.Detach(session, eventMessage);
        if (session == grabbedSession)
            grabbedSession = null;
        session.Dispose();
    }

    private void ReportConnection()
    {
        bool hasSession = sessions.Count != 0;
        if (connected == hasSession)
            return;
        connected = hasSession;
        Console.WriteLine(connected ? "STATUS CONNECTED" : "STATUS READY");
    }

    private (int Status, byte[] Response) ChangeDeviceGrab(int operation, byte[] request)
    {
        bool isGrab = operation == S80Protocol.GrabDevice;
        if (request.Length < (isGrab ? sizeof(long) + sizeof(int) : sizeof(long)))
            return (S80Protocol.InvalidLength, []);

        long handle = BinaryPrimitives.ReadInt64LittleEndian(request);
        if (!sessions.TryGetValue(handle, out S80Session? session))
            return (operation, isGrab ? S80Protocol.Integers(2) : []);

        if (isGrab)
        {
            int mode = BinaryPrimitives.ReadInt32LittleEndian(request.AsSpan(sizeof(long)));
            if (grabbedSession is { } previous && previous != session)
                live.Detach(previous, eventMessage);
            grabbedSession = session;
            Console.WriteLine($"设备已捕获 {session.Request.Application}（模式 {mode}）");
            return (operation, S80Protocol.Integers(0));
        }

        if (grabbedSession == session)
        {
            live.Detach(session, eventMessage);
            grabbedSession = null;
            Console.WriteLine($"设备已释放 {session.Request.Application}");
        }
        return (operation, []);
    }

    private (int Status, byte[] Response) TraceSync(int transport, ReadOnlySpan<byte> request)
    {
        const int headerSize = 16;
        const int sessionSize = sizeof(long);
        if (request.Length < sessionSize + headerSize)
            return (S80Protocol.InvalidLength, []);

        ReadOnlySpan<byte> sync = request[sessionSize..];
        int size = BinaryPrimitives.ReadInt32LittleEndian(sync);
        if (size < headerSize || size > sync.Length)
            return (S80Protocol.InvalidLength, []);

        int operation = BinaryPrimitives.ReadInt32LittleEndian(sync[8..]);
        int item = BinaryPrimitives.ReadInt32LittleEndian(sync[12..]);
        if (unhandledSync.Add((transport, operation, item)))
        {
            Console.WriteLine($"尚未实现的 S80 同步 0x{transport:X}，操作 {operation}，" +
                $"项目 0x{item:X}，同步体 {size} 字节");
        }
        return (S80Protocol.Unsupported, []);
    }

    private bool IsSupportedClient(uint owner, string application)
    {
        if (owner == 0)
            return false;
        if (prefix.Length != 0)
            return owner == Environment.ProcessId;

        try
        {
            using Process process = Process.GetProcessById((int)owner);
            string name = process.ProcessName;
            bool fusion = name.Equals("Fusion360", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Fusion", StringComparison.OrdinalIgnoreCase);
            return fusion && application.Contains("Fusion", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private bool IsLiveTarget(S80Session session, uint foreground) =>
        NativeWindow.GetOwnerProcessId(session.Request.Window) == session.OwnerProcessId &&
        (prefix.Length != 0 || foreground == session.OwnerProcessId);

    private static Mutex CreateMutex(string name)
    {
        var mutex = new Mutex(false, name, out bool created);
        if (created)
            return mutex;

        mutex.Dispose();
        throw new InvalidOperationException("导航接口仍被占用。请退出 Fusion 和 3DxWare，再重新开启导航。");
    }

    private static Semaphore CreateSemaphore(string name)
    {
        var semaphore = new Semaphore(0, 1, name, out bool created);
        if (created)
            return semaphore;

        semaphore.Dispose();
        throw new InvalidOperationException("导航接口仍被占用。请退出 Fusion 和 3DxWare，再重新开启导航。");
    }
}
