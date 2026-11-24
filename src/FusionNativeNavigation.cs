using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace SpaceKatMonitor
{
    internal sealed class FusionNativeNavigation : IDisposable
    {
        private const string InputMap = @"Local\SpaceKat.FusionCamera.Input.v1";
        private const string ReadyEvent = @"Local\SpaceKat.FusionCamera.Ready.v1";
        private FusionNativeBridge _input;
        private long _nextStatusCheck;
        private bool _ready;

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        internal string Status(bool freshInput)
        {
            long now = FusionNativeBridge.GetTickCount64();
            if (now >= _nextStatusCheck)
            {
                _nextStatusCheck = now + 250;
                EventWaitHandle ready;
                _ready = EventWaitHandle.TryOpenExisting(ReadyEvent, System.Security.AccessControl.EventWaitHandleRights.Synchronize, out ready);
                if (ready != null)
                {
                    using (ready) _ready = ready.WaitOne(0);
                }
            }
            if (!_ready) return "等待 Fusion 运行 SpaceKatFusion 加载项";
            return freshInput ? "Fusion 已连接 · 切到模型视图即可导航" : "Fusion 已连接 · 等待摇杆六轴数据";
        }

        internal void EnsureStarted()
        {
            if (_input == null) _input = new FusionNativeBridge(InputMap, true);
        }

        internal void Publish(long sampleTime, float[] axes)
        {
            _input.Publish(sampleTime, axes);
        }

        internal void PublishButtons(uint pressed, int[] logicalBits, FusionViewCommand[] commands)
        {
            if (_input == null || pressed == 0) return;
            IntPtr foreground = GetForegroundWindow();
            uint owner;
            if (foreground == IntPtr.Zero || GetWindowThreadProcessId(foreground, out owner) == 0) return;
            long time = FusionNativeBridge.GetTickCount64();
            for (int slot = 0; slot < commands.Length; slot++)
            {
                if (commands[slot] != FusionViewCommand.None && logicalBits[slot] >= 0 &&
                    (pressed & (1u << logicalBits[slot])) != 0)
                    _input.PublishButton((int)commands[slot], owner, time);
            }
        }

        internal void Stop()
        {
            if (_input == null) return;
            try { _input.Publish(0, null); }
            catch (Exception) { } // The last sample expires even if the stop snapshot cannot be written.
        }

        public void Dispose()
        {
            Stop();
            if (_input == null) return;
            _input.Dispose();
            _input = null;
        }
    }
}
