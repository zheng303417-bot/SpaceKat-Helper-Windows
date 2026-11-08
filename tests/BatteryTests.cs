using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace SpaceKatMonitor
{
    internal static class BatteryTests
    {
        public static string Run()
        {
            int count = 0;
            Check(BluetoothBattery.DecodeValue(new byte[] { 1, 0, 0, 0, 0 }) == 0, ref count);
            Check(BluetoothBattery.DecodeValue(new byte[] { 1, 0, 0, 0, 100, 0, 0, 0 }) == 100, ref count);
            Check(!BluetoothBattery.DecodeValue(new byte[] { 1, 0, 0, 0, 101 }).HasValue, ref count);
            Check(!BluetoothBattery.DecodeValue(new byte[] { 2, 0, 0, 0, 50, 50 }).HasValue, ref count);
            Check(!BluetoothBattery.DecodeValue(new byte[] { 1, 0, 0, 0 }).HasValue, ref count);
            Type layout = typeof(BluetoothBattery).GetNestedType("GattCharacteristic", BindingFlags.NonPublic);
            Check(Marshal.SizeOf(layout) == 36 && Marshal.OffsetOf(layout, "Readable").ToInt32() == 29, ref count);

            string[] paths = { @"\\?\HID#{00001812-0000-1000-8000-00805f9b34fb}_a", @"\\?\HID#BTHLE_b" };
            TaskCompletionSource<int?> request = null;
            int reads = 0;
            string requestedPath = null;
            BatteryMonitor battery = new BatteryMonitor(delegate(string path)
            {
                requestedPath = path;
                reads++;
                request = new TaskCompletionSource<int?>();
                return request.Task;
            });
            battery.Reset(@"\\?\HID#VID_256F&PID_C638", false);
            battery.Step(0);
            Check(reads == 0 && battery.Text.Contains("USB"), ref count);
            battery.Reset(paths[0], true);
            battery.Step(0);
            Check(reads == 0 && battery.Text.Contains("演示"), ref count);
            battery.Reset(paths[0], false);
            battery.Step(0);
            Check(reads == 1 && battery.Text.Contains("读取中") && requestedPath == paths[0], ref count);
            battery.Reset(paths[1], false);
            battery.Step(1);
            Check(reads == 1 && !battery.Percent.HasValue, ref count);
            request.SetResult(97);
            battery.Step(2);
            Check(reads == 2 && !battery.Percent.HasValue && requestedPath == paths[1], ref count);
            request.SetResult(0);
            battery.Step(3);
            Check(battery.Percent == 0 && battery.Text.Contains("0%"), ref count);
            battery.Step(21);
            Check(reads == 2 && battery.Percent == 0, ref count);
            battery.Step(22);
            Check(reads == 3, ref count);
            request.SetException(new InvalidOperationException("Mock Bluetooth read failure"));
            battery.Step(23);
            Check(!battery.Percent.HasValue && battery.Text.Contains("暂不可用"), ref count);
            battery.Reset(null, false);
            battery.Step(24);
            Check(reads == 3 && battery.Text.Contains("未连接"), ref count);
            return "PASS: " + count + " Windows Bluetooth battery checks (native layout, percentage and device switching)\r\n";
        }

        private static void Check(bool condition, ref int count)
        {
            count++;
            if (!condition) throw new InvalidOperationException("Bluetooth battery assertion " + count + " failed.");
        }
    }
}
