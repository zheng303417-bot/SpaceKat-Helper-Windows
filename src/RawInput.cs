using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SpaceKatMonitor
{
    public class HidDevice
    {
        public IntPtr Handle;
        public string Path;
        public string Name;
        public ushort VendorId;
        public ushort ProductId;
        public ushort UsagePage;
        public ushort Usage;
        public uint ValidButtonsMask = UInt32.MaxValue;
        public bool HasButtonCaps;

        public override string ToString()
        {
            return String.Format("{0}  [VID {1:X4} / PID {2:X4}]",
                String.IsNullOrWhiteSpace(Name) ? "六轴控制器" : Name, VendorId, ProductId);
        }
    }

    /// <summary>
    /// Receives the Generic Desktop / Multi-axis Controller collection only.
    /// All calls and events belong to the window's message thread.
    /// The owning WPF hook must leave WM_INPUT unhandled so DefWindowProc can
    /// perform the foreground input cleanup required by Windows.
    /// </summary>
    public sealed class RawInputService : IDisposable
    {
        private const int WmInput = 0x00FF;
        private const int WmInputDeviceChange = 0x00FE;
        private const uint RidInput = 0x10000003;
        private const uint RidiDeviceName = 0x20000007;
        private const uint RidiDeviceInfo = 0x2000000B;
        private const uint RidiPreparsedData = 0x20000005;
        private const uint RimTypeHid = 2;
        private const uint RidevRemove = 0x00000001;
        private const uint RidevInputSink = 0x00000100;
        private const uint RidevDevNotify = 0x00002000;
        private const uint NativeFailure = 0xFFFFFFFF;
        private const uint MaxRawInputBytes = 1024 * 1024;
        private const int ErrorInsufficientBuffer = 122;
        private const int HidpStatusSuccess = 0x00110000;

        private readonly Dictionary<IntPtr, HidDevice> devices =
            new Dictionary<IntPtr, HidDevice>();
        private bool disposed;

        public event Action<HidDevice, byte[]> ReportReceived;
        public event Action DevicesChanged;

        public RawInputService(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                throw new ArgumentException("需要已创建的窗口句柄。", "hwnd");

            // Read metadata before registering; a failure cannot leave an
            // otherwise unowned raw-input registration behind.
            foreach (HidDevice device in EnumerateDevices())
                devices[device.Handle] = device;

            RawInputDevice[] registrations = new RawInputDevice[]
            {
                new RawInputDevice
                {
                    UsagePage = 0x01,
                    Usage = 0x08,
                    Flags = RidevInputSink | RidevDevNotify,
                    Target = hwnd
                }
            };
            if (!RegisterRawInputDevices(registrations, 1,
                (uint)Marshal.SizeOf(typeof(RawInputDevice))))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "无法监听六轴控制器输入。");
            }
        }

        public static List<HidDevice> EnumerateDevices()
        {
            int entrySize = Marshal.SizeOf(typeof(RawInputDeviceList));

            // A device can arrive between the count query and the list read.
            for (int attempt = 0; attempt < 4; attempt++)
            {
                uint count = 0;
                uint result = GetRawInputDeviceList(IntPtr.Zero, ref count, (uint)entrySize);
                if (result == NativeFailure)
                    throw new Win32Exception(Marshal.GetLastWin32Error(),
                        "无法读取输入设备列表。");

                if (count == 0)
                    return new List<HidDevice>();
                if (count > 65536)
                    throw new InvalidOperationException("输入设备列表大小异常。");

                IntPtr buffer = Marshal.AllocHGlobal(checked((int)count * entrySize));
                try
                {
                    uint capacity = count;
                    result = GetRawInputDeviceList(buffer, ref count, (uint)entrySize);
                    if (result == NativeFailure)
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (error == ErrorInsufficientBuffer)
                            continue;
                        throw new Win32Exception(error, "无法读取输入设备列表。");
                    }
                    if (result > capacity)
                        continue;

                    List<HidDevice> list = new List<HidDevice>();
                    for (uint index = 0; index < result; index++)
                    {
                        IntPtr entryAddress = IntPtr.Add(buffer, checked((int)index * entrySize));
                        RawInputDeviceList entry = (RawInputDeviceList)Marshal.PtrToStructure(
                            entryAddress, typeof(RawInputDeviceList));
                        if (entry.Type != RimTypeHid)
                            continue;
                        HidDevice device = ReadDevice(entry.Device);
                        if (device != null)
                            list.Add(device);
                    }
                    list.Sort(delegate(HidDevice first, HidDevice second)
                    {
                        return StringComparer.OrdinalIgnoreCase.Compare(first.ToString(), second.ToString());
                    });
                    return list;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            throw new InvalidOperationException("输入设备列表正在变化，请重新扫描设备。");
        }

        public void ProcessMessage(int msg, IntPtr wParam, IntPtr lParam)
        {
            if (disposed)
                return;

            if (msg == WmInputDeviceChange)
            {
                // GIDC_REMOVAL = 2. The removed handle is no longer queryable.
                if (wParam.ToInt64() == 2)
                    devices.Remove(lParam);
                else
                {
                    HidDevice added = ReadDevice(lParam);
                    if (added != null)
                        devices[added.Handle] = added;
                }
                Action changed = DevicesChanged;
                if (changed != null)
                    changed();
                return;
            }
            if (msg != WmInput || lParam == IntPtr.Zero)
                return;

            uint headerSize = (uint)Marshal.SizeOf(typeof(RawInputHeader));
            uint required = 0;
            if (GetRawInputData(lParam, RidInput, IntPtr.Zero, ref required, headerSize) == NativeFailure)
                return;
            if (required < headerSize + 8 || required > MaxRawInputBytes)
                return;

            IntPtr input = Marshal.AllocHGlobal(checked((int)required));
            try
            {
                uint capacity = required;
                uint copied = GetRawInputData(lParam, RidInput, input, ref required, headerSize);
                if (copied == NativeFailure || copied > capacity || copied < headerSize + 8)
                    return;

                RawInputHeader header = (RawInputHeader)Marshal.PtrToStructure(input,
                    typeof(RawInputHeader));
                if (header.Type != RimTypeHid || header.Device == IntPtr.Zero)
                    return;
                if (header.Size < headerSize + 8 || header.Size > copied)
                    return;

                HidDevice device;
                if (!devices.TryGetValue(header.Device, out device))
                {
                    device = ReadDevice(header.Device);
                    if (device == null)
                        return;
                    devices[device.Handle] = device;
                }

                int dataHeader = (int)headerSize;
                uint reportSize = unchecked((uint)Marshal.ReadInt32(input, dataHeader));
                uint reportCount = unchecked((uint)Marshal.ReadInt32(input, dataHeader + 4));
                ulong totalBytes = (ulong)reportSize * reportCount;
                uint available = header.Size - headerSize - 8;
                if (reportSize == 0 || reportCount == 0 || totalBytes > available)
                    return;

                Action<HidDevice, byte[]> received = ReportReceived;
                if (received == null)
                    return;

                int offset = dataHeader + 8;
                for (uint index = 0; index < reportCount; index++)
                {
                    byte[] report = new byte[(int)reportSize];
                    Marshal.Copy(IntPtr.Add(input, offset), report, 0, report.Length);
                    received(device, report);
                    offset += report.Length;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(input);
            }
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            RawInputDevice[] registrations = new RawInputDevice[]
            {
                new RawInputDevice { UsagePage = 0x01, Usage = 0x08, Flags = RidevRemove, Target = IntPtr.Zero }
            };
            RegisterRawInputDevices(registrations, 1, (uint)Marshal.SizeOf(typeof(RawInputDevice)));
            devices.Clear();
        }

        private static HidDevice ReadDevice(IntPtr deviceHandle)
        {
            if (deviceHandle == IntPtr.Zero)
                return null;

            uint infoSize = (uint)Marshal.SizeOf(typeof(RidDeviceInfo));
            IntPtr infoBuffer = Marshal.AllocHGlobal((int)infoSize);
            try
            {
                RidDeviceInfo info = new RidDeviceInfo { Size = infoSize };
                Marshal.StructureToPtr(info, infoBuffer, false);
                uint received = GetRawInputDeviceInfo(deviceHandle, RidiDeviceInfo, infoBuffer, ref infoSize);
                if (received == NativeFailure || received < 24)
                    return null;
                info = (RidDeviceInfo)Marshal.PtrToStructure(infoBuffer, typeof(RidDeviceInfo));
                if (info.Type != RimTypeHid || info.UsagePage != 0x01 || info.Usage != 0x08)
                    return null;

                string path = ReadDevicePath(deviceHandle);
                string product = ReadProductName(path);
                HidDevice device = new HidDevice
                {
                    Handle = deviceHandle,
                    Path = path,
                    Name = String.IsNullOrWhiteSpace(product) ? "六轴控制器" : product,
                    VendorId = unchecked((ushort)info.VendorId),
                    ProductId = unchecked((ushort)info.ProductId),
                    UsagePage = info.UsagePage,
                    Usage = info.Usage
                };
                ReadButtonCapabilities(device);
                return device;
            }
            finally
            {
                Marshal.FreeHGlobal(infoBuffer);
            }
        }

        private static string ReadDevicePath(IntPtr handle)
        {
            uint characters = 0;
            if (GetRawInputDeviceInfo(handle, RidiDeviceName, IntPtr.Zero, ref characters) == NativeFailure
                || characters == 0 || characters > 32767)
                return String.Empty;

            IntPtr buffer = Marshal.AllocHGlobal(checked(((int)characters + 1) * 2));
            try
            {
                // A terminating character is added even if an unusual device
                // returns a character count without its terminating NUL.
                Marshal.WriteInt16(buffer, checked((int)characters * 2), 0);
                uint copied = GetRawInputDeviceInfo(handle, RidiDeviceName, buffer, ref characters);
                if (copied == NativeFailure)
                    return String.Empty;
                return Marshal.PtrToStringUni(buffer) ?? String.Empty;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static string ReadProductName(string path)
        {
            if (String.IsNullOrEmpty(path))
                return String.Empty;

            // No read/write access is requested and no reports are sent.
            // Sharing allows ordinary controller drivers to keep their handles.
            using (SafeFileHandle handle = CreateFile(path, 0, 0x00000001 | 0x00000002,
                IntPtr.Zero, 3, 0, IntPtr.Zero))
            {
                if (handle.IsInvalid)
                    return String.Empty;
                IntPtr buffer = Marshal.AllocHGlobal(512);
                try
                {
                    Marshal.WriteInt16(buffer, 0);
                    if (!HidD_GetProductString(handle, buffer, 512))
                        return String.Empty;
                    Marshal.WriteInt16(buffer, 510, 0);
                    return Marshal.PtrToStringUni(buffer) ?? String.Empty;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }

        private static void ReadButtonCapabilities(HidDevice device)
        {
            uint required = 0;
            if (GetRawInputDeviceInfo(device.Handle, RidiPreparsedData, IntPtr.Zero,
                ref required) == NativeFailure || required == 0 || required > MaxRawInputBytes)
                return;

            IntPtr preparsed = Marshal.AllocHGlobal((int)required);
            IntPtr capabilities = Marshal.AllocHGlobal(64);
            try
            {
                uint capacity = required;
                uint copied = GetRawInputDeviceInfo(device.Handle, RidiPreparsedData,
                    preparsed, ref required);
                if (copied == NativeFailure || copied == 0 || copied > capacity)
                    return;
                if (HidP_GetCaps(preparsed, capabilities) != HidpStatusSuccess)
                    return;

                // HIDP_CAPS is 64 bytes; NumberInputButtonCaps is ushort 23.
                ushort number = unchecked((ushort)Marshal.ReadInt16(capabilities, 46));
                if (number == 0)
                {
                    device.ValidButtonsMask = 0;
                    device.HasButtonCaps = true;
                    return;
                }

                // HIDP_BUTTON_CAPS remains 72 bytes in both parser API versions.
                const int buttonCapsSize = 72;
                IntPtr buttonCaps = Marshal.AllocHGlobal((int)number * buttonCapsSize);
                try
                {
                    ushort allocated = number;
                    if (HidP_GetButtonCaps(0, buttonCaps, ref number, preparsed) != HidpStatusSuccess
                        || number > allocated)
                        return;

                    uint mask = 0;
                    for (int index = 0; index < number; index++)
                    {
                        IntPtr item = IntPtr.Add(buttonCaps, index * buttonCapsSize);
                        ushort page = unchecked((ushort)Marshal.ReadInt16(item, 0));
                        byte reportId = Marshal.ReadByte(item, 2);
                        if (page != 0x09 || reportId != 3)
                            continue;

                        bool isRange = Marshal.ReadByte(item, 12) != 0;
                        int first = unchecked((ushort)Marshal.ReadInt16(item, 56));
                        int last = isRange ? unchecked((ushort)Marshal.ReadInt16(item, 58)) : first;
                        first = Math.Max(first, 1);
                        last = Math.Min(last, 32);
                        for (int usage = first; usage <= last; usage++)
                            mask |= 1u << (usage - 1);
                    }
                    device.ValidButtonsMask = mask;
                    device.HasButtonCaps = true;
                }
                finally
                {
                    Marshal.FreeHGlobal(buttonCaps);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(capabilities);
                Marshal.FreeHGlobal(preparsed);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RawInputDeviceList
        {
            public IntPtr Device;
            public uint Type;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RawInputDevice
        {
            public ushort UsagePage;
            public ushort Usage;
            public uint Flags;
            public IntPtr Target;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RawInputHeader
        {
            public uint Type;
            public uint Size;
            public IntPtr Device;
            public IntPtr WParam;
        }

        // Largest union member is RID_DEVICE_INFO_KEYBOARD (24 bytes).
        // RID_DEVICE_INFO_HID occupies the first 16 bytes of that union.
        [StructLayout(LayoutKind.Explicit, Size = 32)]
        private struct RidDeviceInfo
        {
            [FieldOffset(0)] public uint Size;
            [FieldOffset(4)] public uint Type;
            [FieldOffset(8)] public uint VendorId;
            [FieldOffset(12)] public uint ProductId;
            [FieldOffset(16)] public uint Version;
            [FieldOffset(20)] public ushort UsagePage;
            [FieldOffset(22)] public ushort Usage;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterRawInputDevices(
            [In] RawInputDevice[] devices, uint number, uint size);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRawInputDeviceList(IntPtr list, ref uint number, uint size);

        [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", CharSet = CharSet.Unicode,
            SetLastError = true)]
        private static extern uint GetRawInputDeviceInfo(IntPtr device, uint command,
            IntPtr data, ref uint size);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRawInputData(IntPtr input, uint command,
            IntPtr data, ref uint size, uint headerSize);

        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode,
            SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string path, uint desiredAccess,
            uint shareMode, IntPtr securityAttributes, uint creationDisposition,
            uint flagsAndAttributes, IntPtr template);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool HidD_GetProductString(SafeFileHandle device,
            IntPtr buffer, uint bytes);

        [DllImport("hid.dll")]
        private static extern int HidP_GetCaps(IntPtr preparsedData, IntPtr capabilities);

        [DllImport("hid.dll")]
        private static extern int HidP_GetButtonCaps(int reportType, IntPtr buttonCapabilities,
            ref ushort buttonCapabilitiesLength, IntPtr preparsedData);
    }
}
