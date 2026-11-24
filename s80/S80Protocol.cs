using System.Buffers.Binary;
using System.Text;

namespace SpaceKat.FusionS80;

internal static class S80Protocol
{
    public const string DriverWindowClass = "SpaceWare Driver Class";
    public const string DriverWindowTitle = "SpaceWare Driver Window";
    public const string EventMessage = "SpaceWareMessage00";
    public const string ControlMapping = "SpaceWareMemMap";
    public const string DriverLock = "SpaceWareDvrLock";
    public const string AppLock = "SpaceWareAppLock";
    public const string DriverPoke = "SpaceWareDvrPoke";
    public const string AppPoke = "SpaceWareAppPoke";

    public const int OperationOffset = 8;
    public const int ReplyRequestedOffset = 12;
    public const int LengthOffset = 16;
    public const int PayloadOffset = 20;
    public const int ControlPayloadCapacity = 2_200_000;
    public const int EventHeaderSize = 8;
    public const int EventCapacity = 1_800;
    public const int OpenReplySize = 24;
    public const int MotionSize = 48;
    public const int CommandSize = 0x8c;

    public const int GetBufferSizes = 0x02;
    public const int Initialize = 0x03;
    public const int Terminate = 0x04;
    public const int GetDeviceCount = 0x65;
    public const int GetDevicePort = 0x67;
    public const int GetDriverInfo = 0x68;
    public const int CloseSession = 0x72;
    public const int GrabDevice = 0x78;
    public const int ReleaseDevice = 0x79;
    public const int GetDeviceInfo = 0x7A;
    public const int SyncSend = 0x7D;
    public const int SyncQuery = 0x7E;
    public const int OpenSession = 0x86;

    public const int InvalidLength = -13;
    public const int Unsupported = -14;
    public const int Motion = 2;
    public const int Zero = 4;
    public const int Command = 0x11;
    public const int PrototypeDeviceType = 0xC638;

    public static string EventMapping(string prefix, int number) => $"{prefix}{ControlMapping}{number:D2}";

    public static bool TryParseOpen(ReadOnlySpan<byte> payload, out OpenRequest request)
    {
        request = default;
        if (payload.Length < 20)
            return false;

        int nameLength = BinaryPrimitives.ReadInt32LittleEndian(payload);
        if (nameLength < 2 || nameLength > 1024 || (nameLength & 1) != 0 || nameLength > payload.Length - 16)
            return false;

        ReadOnlySpan<byte> name = payload.Slice(4, nameLength);
        if (BinaryPrimitives.ReadUInt16LittleEndian(name[^2..]) != 0)
            return false;

        int deviceOffset = 4 + nameLength;
        int device = BinaryPrimitives.ReadInt32LittleEndian(payload[deviceOffset..]);
        bool hasTypeMask = BinaryPrimitives.ReadUInt64LittleEndian(payload[(deviceOffset + 4)..]) != 0;
        int modeOffset = deviceOffset + (hasTypeMask ? 20 : 12);
        if (payload.Length - modeOffset < 44)
            return false;

        int hintLength = BinaryPrimitives.ReadInt32LittleEndian(payload[(modeOffset + 32)..]);
        if (hintLength < 0 || hintLength > payload.Length - modeOffset - 44)
            return false;

        request = new OpenRequest(
            Encoding.Unicode.GetString(name[..^2]),
            device,
            (nint)BinaryPrimitives.ReadInt64LittleEndian(payload[(modeOffset + 4)..]),
            BinaryPrimitives.ReadUInt32LittleEndian(payload[(modeOffset + 20)..]));
        return request.Application.Length != 0 && request.Window != 0;
    }

    public static byte[] OpenReply(int result, long handle, int device, int mapNumber, uint message)
    {
        var reply = new byte[OpenReplySize];
        BinaryPrimitives.WriteInt32LittleEndian(reply, result);
        BinaryPrimitives.WriteInt64LittleEndian(reply.AsSpan(4), handle);
        BinaryPrimitives.WriteInt32LittleEndian(reply.AsSpan(12), device);
        BinaryPrimitives.WriteInt32LittleEndian(reply.AsSpan(16), mapNumber);
        BinaryPrimitives.WriteUInt32LittleEndian(reply.AsSpan(20), message);
        return reply;
    }

    public static byte[] Integers(params int[] values)
    {
        var reply = new byte[values.Length * sizeof(int)];
        for (int index = 0; index < values.Length; index++)
            BinaryPrimitives.WriteInt32LittleEndian(reply.AsSpan(index * sizeof(int)), values[index]);
        return reply;
    }

    public static byte[] DriverInfo()
    {
        var reply = new byte[0x110];
        BinaryPrimitives.WriteInt32LittleEndian(reply.AsSpan(4), 17);
        BinaryPrimitives.WriteInt32LittleEndian(reply.AsSpan(8), 9);
        BinaryPrimitives.WriteInt32LittleEndian(reply.AsSpan(12), 15);
        Encoding.ASCII.GetBytes("SpaceKat S80 Prototype", reply.AsSpan(16));
        Encoding.ASCII.GetBytes("Local S80 compatibility test", reply.AsSpan(144));
        return reply;
    }

    public static byte[] DeviceInfo(bool found)
    {
        var reply = new byte[0x9c];
        BinaryPrimitives.WriteInt32LittleEndian(reply, found ? 0 : 2);
        if (found)
        {
            BinaryPrimitives.WriteInt32LittleEndian(reply.AsSpan(4), PrototypeDeviceType);
            BinaryPrimitives.WriteInt32LittleEndian(reply.AsSpan(12), 6);
            Encoding.ASCII.GetBytes("S80 prototype", reply.AsSpan(28));
        }
        return reply;
    }

    public static byte[] DevicePort(S80Session? session)
    {
        var reply = new byte[0x194];
        BinaryPrimitives.WriteInt32LittleEndian(reply, session is null ? 2 : 0);
        if (session is not null)
        {
            BinaryPrimitives.WriteInt32LittleEndian(reply.AsSpan(4), session.Request.Device);
            Encoding.ASCII.GetBytes("SpaceKat S80 Prototype", reply.AsSpan(16));
            Encoding.ASCII.GetBytes("SpaceKatS80", reply.AsSpan(144));
        }
        return reply;
    }
}

internal readonly record struct OpenRequest(string Application, int Device, nint Window, uint EventMask);
