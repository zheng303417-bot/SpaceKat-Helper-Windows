using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Text;
using SpaceKatMonitor;

namespace SpaceKat.FusionS80;

internal static class SelfTest
{
    internal static void Run()
    {
        string id = Guid.NewGuid().ToString("N");
        string prefix = $"SpaceKatFusionTest-{id}-";
        string inputName = FusionNativeBridge.NamePrefix + id;
        using var writer = new FusionNativeBridge(inputName, writer: true);
        using var reader = new FusionNativeBridge(inputName, writer: false);
        var motion = new MotionReader(reader);
        float[] axes = [1, .5f, -.25f, -.5f, -.125f, .25f];
        var expected = new MotionAxes(96, -48, 24, -48, 12, -24);
        long now = FusionNativeBridge.GetTickCount64();
        writer.Publish(now, axes);
        Check(motion.Read(now) == new MotionSample(expected, now), "六轴方向、灵敏度与采样时钟");
        Check(motion.Read(now + FusionNativeBridge.SampleTimeout) is null, "超时样本无输出");
        writer.Publish(now, [float.NaN, 0, 0, 0, 0, 0]);
        Check(motion.Read(now) is null, "无效轴无输出");
        writer.Publish(0, null!);
        Check(motion.Read(now) is null, "停止后无样本");

        using var stopping = new CancellationTokenSource();
        using var ready = new ManualResetEventSlim();
        Task serving = Task.Run(() => new S80Server(prefix, motion.Read, reader).Run(stopping.Token, ready.Set));
        try
        {
            if (WaitHandle.WaitAny([ready.WaitHandle, ((IAsyncResult)serving).AsyncWaitHandle], 5000) != 0)
                throw new TimeoutException("隔离导航组件未启动。");
            VerifyExchange(prefix, writer, axes, expected, stopping.Cancel);
        }
        finally
        {
            stopping.Cancel();
            serving.GetAwaiter().GetResult();
        }
        Check(NativeWindow.FindWindowW(prefix + S80Protocol.DriverWindowClass, 0) == 0, "关闭时清理窗口");
        Console.WriteLine("Fusion 原生导航自检通过：六轴运动、归零、同一会话停用与恢复、四键命令和目标窗口校验。");
    }

    private static void VerifyExchange(string prefix, FusionNativeBridge writer, float[] axes, MotionAxes expected, Action stop)
    {
        using var map = MemoryMappedFile.OpenExisting(prefix + S80Protocol.ControlMapping);
        using var control = map.CreateViewAccessor();
        using var requestReady = Semaphore.OpenExisting(prefix + S80Protocol.DriverPoke);
        using var replyReady = Semaphore.OpenExisting(prefix + S80Protocol.AppPoke);
        using var window = new NativeWindow(prefix + "Client", "SpaceKat Fusion Test");
        uint message = NativeWindow.RegisterWindowMessageW(prefix + S80Protocol.EventMessage);

        byte[] Request(int operation, byte[] payload)
        {
            control.Write(S80Protocol.OperationOffset, operation);
            control.Write(S80Protocol.ReplyRequestedOffset, 1);
            control.Write(S80Protocol.LengthOffset, payload.Length);
            if (payload.Length != 0)
                control.WriteArray(S80Protocol.PayloadOffset, payload, 0, payload.Length);
            requestReady.Release();
            Check(replyReady.WaitOne(3000), "控制请求回复");
            Check(control.ReadInt32(S80Protocol.OperationOffset) == operation, "控制请求状态");
            var reply = new byte[control.ReadInt32(S80Protocol.LengthOffset)];
            if (reply.Length != 0)
                control.ReadArray(S80Protocol.PayloadOffset, reply, 0, reply.Length);
            return reply;
        }

        Check(Request(S80Protocol.Initialize, S80Protocol.Integers(1, 0)).SequenceEqual(S80Protocol.Integers(0)), "初始化");
        byte[] session = Request(S80Protocol.OpenSession, MakeOpenRequest(window.Handle));
        Check(session.Length == 24 && BinaryPrimitives.ReadInt32LittleEndian(session) == 0, "登记");
        Check(BinaryPrimitives.ReadUInt32LittleEndian(session.AsSpan(20)) == message, "注册事件消息");
        int mapNumber = BinaryPrimitives.ReadInt32LittleEndian(session.AsSpan(16));
        byte[] handle = session[4..12];
        byte[] grab = [.. handle, .. S80Protocol.Integers(1)];

        using var eventMap = MemoryMappedFile.OpenExisting(S80Protocol.EventMapping(prefix, mapNumber));
        using var events = eventMap.CreateViewAccessor();
        VerifyButtons(writer, events);
        Check(Request(S80Protocol.GrabDevice, grab).SequenceEqual(S80Protocol.Integers(0)), "捕获");

        void AwaitFrame(int type, MotionAxes target, bool publish)
        {
            var timer = Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < 3000)
            {
                if (publish)
                    writer.Publish(FusionNativeBridge.GetTickCount64(), axes);
                int read = events.ReadInt32(4);
                if (read == events.ReadInt32(0))
                {
                    Thread.Sleep(5);
                    continue;
                }

                const int frameSize = 52;
                int ReadField(int offset) => events.ReadInt32(8 + (read + offset) % S80Protocol.EventCapacity);
                int frameType = ReadField(4);
                var frame = new MotionAxes(ReadField(24), ReadField(28), ReadField(32),
                    ReadField(36), ReadField(40), ReadField(44));
                Check(ReadField(48) == LiveNavigation.FrameInterval, "运动周期");
                events.Write(4, (read + frameSize) % S80Protocol.EventCapacity);
                if (frameType == type)
                {
                    Check(frame == target, "S80 六轴帧轴值");
                    return;
                }
                Check(frameType == S80Protocol.Motion && type == S80Protocol.Zero, "S80 事件类型");
            }
            throw new TimeoutException($"未收到预期的 S80 事件 {type}。");
        }

        AwaitFrame(S80Protocol.Motion, expected, publish: true);
        writer.Publish(FusionNativeBridge.GetTickCount64(), new float[6]);
        AwaitFrame(S80Protocol.Zero, default, publish: false);
        AwaitFrame(S80Protocol.Motion, expected, publish: true);
        AwaitFrame(S80Protocol.Zero, default, publish: false); // Writer stopped refreshing.
        AwaitFrame(S80Protocol.Motion, expected, publish: true);
        writer.Publish(0, null!);
        AwaitFrame(S80Protocol.Zero, default, publish: false);
        writer.PublishButton(6, (uint)Environment.ProcessId, FusionNativeBridge.GetTickCount64());
        writer.Publish(0, null!); // A queued tap must not reach the client after a quick resume.
        VerifyButtons(writer, events);
        AwaitFrame(S80Protocol.Motion, expected, publish: true);
        Check(Request(S80Protocol.ReleaseDevice, handle).Length == 0, "释放设备");
        AwaitFrame(S80Protocol.Zero, default, publish: false);
        Check(Request(S80Protocol.GrabDevice, grab).SequenceEqual(S80Protocol.Integers(0)), "重新捕获");
        AwaitFrame(S80Protocol.Motion, expected, publish: true);
        stop();
        AwaitFrame(S80Protocol.Zero, default, publish: false);
    }

    private static void VerifyButtons(FusionNativeBridge writer, MemoryMappedViewAccessor events)
    {
        const int capacity = S80Protocol.EventCapacity;
        // Force the first command to span the end of the S80 ring.
        events.Write(0, capacity - 8);
        events.Write(4, capacity - 8);
        int[] commands = [2, 3, 5, 6];
        long now = FusionNativeBridge.GetTickCount64();
        writer.Publish(0, new float[6]);
        writer.PublishButton(2, 0, now); // A tap outside the Fusion process.
        writer.PublishButton(2, (uint)Environment.ProcessId, now - 1000); // Expired tap.
        foreach (int command in commands)
            writer.PublishButton(command, (uint)Environment.ProcessId, now);

        foreach (int expected in commands)
        {
            Check(SpinWait.SpinUntil(() => events.ReadInt32(4) != events.ReadInt32(0), 1000), "接收按键命令");
            int read = events.ReadInt32(4);
            int ReadField(int offset) => events.ReadInt32(8 + (read + offset) % capacity);
            Check((ReadField(0) >> 16) == S80Protocol.CommandSize, "按键命令长度");
            Check(ReadField(4) == 0x11 && ReadField(8) == 1 && ReadField(12) == expected, "按键命令和顺序");
            events.Write(4, (read + 4 + S80Protocol.CommandSize) % capacity);
        }
        Check(events.ReadInt32(4) == events.ReadInt32(0), "每次按下只执行一次");
    }

    private static byte[] MakeOpenRequest(nint window)
    {
        byte[] name = Encoding.Unicode.GetBytes("SpaceKat Fusion Test\0");
        var request = new byte[4 + name.Length + 20 + 44];
        BinaryPrimitives.WriteInt32LittleEndian(request, name.Length);
        name.CopyTo(request.AsSpan(4));
        int device = 4 + name.Length;
        BinaryPrimitives.WriteInt32LittleEndian(request.AsSpan(device), -1);
        BinaryPrimitives.WriteUInt64LittleEndian(request.AsSpan(device + 4), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(request.AsSpan(device + 12), 3);
        int mode = device + 20;
        BinaryPrimitives.WriteInt32LittleEndian(request.AsSpan(mode), 1);
        BinaryPrimitives.WriteInt64LittleEndian(request.AsSpan(mode + 4), window);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(mode + 20), 0x3f);
        return request;
    }

    private static void Check(bool condition, string stage)
    {
        if (!condition)
            throw new InvalidOperationException($"Fusion 原生六轴隔离自检失败（{stage}）。");
    }
}
