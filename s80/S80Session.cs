using System.Buffers.Binary;
using System.IO.MemoryMappedFiles;

namespace SpaceKat.FusionS80;

internal sealed class S80Session : IDisposable
{
    private readonly MemoryMappedFile mapping;
    private readonly MemoryMappedViewAccessor view;
    private ushort sequence = 1;
    private byte notificationCounter;

    public S80Session(string prefix, int mapNumber, long handle, OpenRequest request, uint ownerProcessId)
    {
        MapNumber = mapNumber;
        Handle = handle;
        Request = request;
        OwnerProcessId = ownerProcessId;
        OpenedAt = SpaceKatMonitor.FusionNativeBridge.GetTickCount64();
        mapping = MemoryMappedFile.CreateNew(S80Protocol.EventMapping(prefix, mapNumber),
            S80Protocol.EventHeaderSize + S80Protocol.EventCapacity);
        try
        {
            view = mapping.CreateViewAccessor();
        }
        catch
        {
            mapping.Dispose();
            throw;
        }
    }

    public int MapNumber { get; }
    public long Handle { get; }
    public OpenRequest Request { get; }
    public uint OwnerProcessId { get; }
    public long OpenedAt { get; }

    public bool SendMotion(uint message, MotionAxes axes, int periodMilliseconds, bool limitBacklog = false) =>
        SendEvent(message, S80Protocol.Motion, axes, periodMilliseconds, limitBacklog);

    public bool SendZero(uint message, int periodMilliseconds) =>
        SendEvent(message, S80Protocol.Zero, default, periodMilliseconds, false);

    public bool SendCommand(uint message, int command)
    {
        // Fusion's NavLib uses V3DCMDs with the legacy open mask (0x3f).
        // The original driver enables this command event through the app profile.
        const int frameSize = 4 + S80Protocol.CommandSize;
        if (!TryGetQueue(frameSize + 4 + S80Protocol.MotionSize, out int write, out _))
            return false;

        var frame = new byte[frameSize];
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(2), S80Protocol.CommandSize);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), S80Protocol.Command);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(8), 1); // Pressed
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(12), command);
        PostFrame(message, S80Protocol.Command, write, frame);
        return true;
    }

    private bool SendEvent(uint message, int type, MotionAxes axes, int periodMilliseconds, bool limitBacklog)
    {
        if ((Request.EventMask & (1u << (type - 1))) == 0)
            return false;

        const int frameSize = 4 + S80Protocol.MotionSize;
        // Keep room for the zero event when the client stops draining the queue.
        int required = type == S80Protocol.Motion ? 2 * frameSize : frameSize;
        if (!TryGetQueue(required, out int write, out int free) ||
            (limitBacklog && S80Protocol.EventCapacity - 1 - free >= 2 * frameSize))
            return false;

        var frame = new byte[frameSize];
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(2), S80Protocol.MotionSize);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), type);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(24), axes.X);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(28), axes.Y);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(32), axes.Z);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(36), axes.Rx);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(40), axes.Ry);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(44), axes.Rz);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(48), periodMilliseconds);

        return PostFrame(message, type, write, frame);
    }

    private bool TryGetQueue(int required, out int write, out int free)
    {
        write = view.ReadInt32(0);
        int read = view.ReadInt32(4);
        int capacity = S80Protocol.EventCapacity;
        free = 0;
        if ((uint)read >= (uint)capacity || (uint)write >= (uint)capacity)
            return false;

        free = (read - write + capacity - 1) % capacity;
        return free >= required;
    }

    private bool PostFrame(uint message, int type, int write, byte[] frame)
    {
        int capacity = S80Protocol.EventCapacity;
        BinaryPrimitives.WriteUInt16LittleEndian(frame, sequence);
        int firstLength = Math.Min(frame.Length, capacity - write);
        view.WriteArray(S80Protocol.EventHeaderSize + write, frame, 0, firstLength);
        if (firstLength != frame.Length)
            view.WriteArray(S80Protocol.EventHeaderSize, frame, firstLength, frame.Length - firstLength);

        Thread.MemoryBarrier();
        view.Write(0, (write + frame.Length) % capacity);
        nuint wParam = (nuint)(MapNumber | notificationCounter++ << 8);
        nint lParam = (type << 16) | sequence;
        sequence++;
        if (sequence == 0)
            sequence = 1;

        return NativeWindow.PostMessageW(Request.Window, message, wParam, lParam);
    }

    public void Dispose()
    {
        view.Dispose();
        mapping.Dispose();
    }
}
