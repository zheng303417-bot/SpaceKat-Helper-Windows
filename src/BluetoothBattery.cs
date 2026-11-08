using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SpaceKatMonitor
{
    internal static class BluetoothBattery
    {
        private static readonly Guid BatteryService = new Guid("0000180f-0000-1000-8000-00805f9b34fb");
        private static readonly Guid BatteryLevel = new Guid("00002a19-0000-1000-8000-00805f9b34fb");
        private static readonly Guid LocalMachineContainer = new Guid("00000000-0000-0000-ffff-ffffffffffff");
        private static readonly IntPtr InvalidHandle = new IntPtr(-1);
        private const uint PresentInterfaces = 0x12; // DIGCF_PRESENT | DIGCF_DEVICEINTERFACE
        private const int InsufficientBuffer = 122;
        private const int MoreData = unchecked((int)0x800700EA);
        private const uint ForceReadFromDevice = 4;

        internal static bool IsBluetoothHidPath(string path)
        {
            return !String.IsNullOrEmpty(path) &&
                (path.IndexOf("00001812", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 path.IndexOf("BTHLE", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        internal static int? ReadPercent(string hidPath)
        {
            if (!IsBluetoothHidPath(hidPath)) return null;
            string servicePath = FindBatteryService(hidPath);
            if (servicePath == null) return null;
            using (SafeFileHandle service = CreateFile(servicePath, 0x80000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
            {
                if (service.IsInvalid) return null;
                ushort count;
                if (BluetoothGATTGetCharacteristics(service, IntPtr.Zero, 0, null, out count, 0) != MoreData ||
                    count == 0 || count > 128) return null;
                GattCharacteristic[] characteristics = new GattCharacteristic[count];
                if (BluetoothGATTGetCharacteristics(service, IntPtr.Zero, count, characteristics, out count, 0) != 0 ||
                    count > characteristics.Length) return null;
                for (int i = 0; i < count; i++)
                {
                    GattCharacteristic characteristic = characteristics[i];
                    if (characteristic.Readable == 0 || !characteristic.Uuid.IsBatteryLevel) continue;
                    byte[] value = new byte[32]; // Space for ULONG DataSize and the one-byte Battery Level.
                    ushort required;
                    int result = BluetoothGATTGetCharacteristicValue(service, ref characteristic,
                        (uint)value.Length, value, out required, ForceReadFromDevice);
                    return result == 0 ? DecodeValue(value) : null;
                }
            }
            return null;
        }

        internal static int? DecodeValue(byte[] value)
        {
            if (value == null || value.Length < 5 || BitConverter.ToUInt32(value, 0) != 1 || value[4] > 100)
                return null;
            return value[4];
        }

        // Match the battery service's PnP container to the selected physical HID device.
        // VID/PID alone cannot distinguish two identical controllers.
        private static string FindBatteryService(string hidPath)
        {
            Guid? selected = GetHidContainer(hidPath);
            if (!selected.HasValue) return null;
            Guid service = BatteryService;
            IntPtr devices = SetupDiGetClassDevs(ref service, IntPtr.Zero, IntPtr.Zero, PresentInterfaces);
            if (devices == InvalidHandle) return null;
            try
            {
                string match = null;
                for (uint index = 0; ; index++)
                {
                    DeviceInterfaceData iface = DeviceInterfaceData.Create();
                    if (!SetupDiEnumDeviceInterfaces(devices, IntPtr.Zero, ref service, index, ref iface))
                        return Marshal.GetLastWin32Error() == 259 ? match : null; // ERROR_NO_MORE_ITEMS
                    uint size;
                    if (GetContainer(devices, ref iface, out size) != selected || size < 6 || size > 65536) continue;
                    IntPtr detail = Marshal.AllocHGlobal((int)size);
                    try
                    {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        DeviceInfoData info = DeviceInfoData.Create();
                        if (!SetupDiGetDeviceInterfaceDetail(devices, ref iface, detail, size, out size, ref info))
                            continue;
                        if (match != null) return null; // Ambiguous service ownership.
                        match = Marshal.PtrToStringUni(IntPtr.Add(detail, 4));
                    }
                    finally { Marshal.FreeHGlobal(detail); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(devices); }
        }

        private static Guid? GetHidContainer(string hidPath)
        {
            IntPtr devices = SetupDiCreateDeviceInfoList(IntPtr.Zero, IntPtr.Zero);
            if (devices == InvalidHandle) return null;
            try
            {
                DeviceInterfaceData iface = DeviceInterfaceData.Create();
                if (!SetupDiOpenDeviceInterface(devices, hidPath, 0, ref iface)) return null;
                uint size;
                return GetContainer(devices, ref iface, out size);
            }
            finally { SetupDiDestroyDeviceInfoList(devices); }
        }

        private static Guid? GetContainer(IntPtr devices, ref DeviceInterfaceData iface, out uint size)
        {
            DeviceInfoData info = DeviceInfoData.Create();
            if (SetupDiGetDeviceInterfaceDetail(devices, ref iface, IntPtr.Zero, 0, out size, ref info) ||
                Marshal.GetLastWin32Error() != InsufficientBuffer) return null;
            PropertyKey key = new PropertyKey { Format = new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), Id = 2 };
            uint type, bytes = 16;
            Guid container;
            if (CM_Get_DevNode_Property(info.Instance, ref key, out type, out container, ref bytes, 0) != 0 ||
                type != 13 || bytes != 16 || container == Guid.Empty || container == LocalMachineContainer) return null;
            return container;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PropertyKey
        {
            public Guid Format;
            public uint Id;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DeviceInterfaceData
        {
            public uint Size;
            public Guid InterfaceClass;
            public uint Flags;
            public IntPtr Reserved;
            public static DeviceInterfaceData Create()
            {
                return new DeviceInterfaceData { Size = (uint)Marshal.SizeOf(typeof(DeviceInterfaceData)) };
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DeviceInfoData
        {
            public uint Size;
            public Guid Class;
            public uint Instance;
            public IntPtr Reserved;
            public static DeviceInfoData Create()
            {
                return new DeviceInfoData { Size = (uint)Marshal.SizeOf(typeof(DeviceInfoData)) };
            }
        }

        [StructLayout(LayoutKind.Explicit, Size = 20)]
        private struct BleUuid
        {
            [FieldOffset(0)] public byte IsShort;
            [FieldOffset(4)] public ushort ShortUuid;
            [FieldOffset(4)] public Guid LongUuid;
            public bool IsBatteryLevel { get { return IsShort != 0 ? ShortUuid == 0x2A19 : LongUuid == BatteryLevel; } }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GattCharacteristic
        {
            public ushort ServiceHandle;
            public BleUuid Uuid;
            public ushort AttributeHandle, ValueHandle;
            public byte Broadcastable, Readable, Writable, WritableWithoutResponse,
                SignedWritable, Notifiable, Indicatable, ExtendedProperties;
        }

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern IntPtr SetupDiCreateDeviceInfoList(IntPtr classGuid, IntPtr parent);
        [DllImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr parent, uint flags);
        [DllImport("setupapi.dll", EntryPoint = "SetupDiOpenDeviceInterfaceW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiOpenDeviceInterface(IntPtr devices, string path, uint flags, ref DeviceInterfaceData data);
        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr devices, IntPtr device, ref Guid classGuid, uint index, ref DeviceInterfaceData data);
        [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", SetLastError = true)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr devices, ref DeviceInterfaceData iface, IntPtr detail,
            uint size, out uint required, ref DeviceInfoData info);
        [DllImport("setupapi.dll")]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr devices);
        [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_DevNode_PropertyW")]
        private static extern uint CM_Get_DevNode_Property(uint instance, ref PropertyKey key, out uint type, out Guid value,
            ref uint bytes, uint flags);
        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint mode, uint flags, IntPtr template);
        [DllImport("BluetoothApis.dll")]
        private static extern int BluetoothGATTGetCharacteristics(SafeFileHandle service, IntPtr parent, ushort count,
            [Out] GattCharacteristic[] values, out ushort actual, uint flags);
        [DllImport("BluetoothApis.dll")]
        private static extern int BluetoothGATTGetCharacteristicValue(SafeFileHandle service, ref GattCharacteristic characteristic,
            uint size, [Out] byte[] value, out ushort required, uint flags);
    }
}
