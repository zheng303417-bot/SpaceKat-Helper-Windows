using System;
using System.Globalization;
using System.Threading.Tasks;

namespace SpaceKatMonitor
{
    internal sealed class BatteryMonitor
    {
        private readonly Func<string, Task<int?>> read;
        private Task<int?> pending;
        private string path;
        private bool demo, bluetooth, hasRead;
        private int generation, requestGeneration;
        private double nextRead;

        public BatteryMonitor(Func<string, Task<int?>> read)
        {
            this.read = read;
        }

        public int? Percent { get; private set; }
        public string Text
        {
            get
            {
                if (Percent.HasValue) return "电量 " + Percent.Value.ToString(CultureInfo.InvariantCulture) + "%";
                if (demo) return "电量 — · 演示模式";
                if (String.IsNullOrEmpty(path)) return "电量 — · 未连接";
                if (!bluetooth) return "电量 — · USB / HID 无读数";
                return hasRead ? "电量 — · 暂不可用" : "电量 — · 读取中";
            }
        }

        public void Reset(string selectedPath, bool isDemo)
        {
            generation++;
            path = selectedPath;
            demo = isDemo;
            bluetooth = !demo && BluetoothBattery.IsBluetoothHidPath(path);
            Percent = null;
            hasRead = false;
            nextRead = 0;
        }

        public void RefreshSoon()
        {
            if (pending == null) nextRead = 0;
        }

        public void Step(double now)
        {
            if (pending != null && pending.IsCompleted)
            {
                int? value = null;
                try { value = pending.GetAwaiter().GetResult(); }
                catch (Exception) { } // The battery query must not interrupt device input.
                pending = null;
                if (requestGeneration == generation)
                {
                    Percent = value >= 0 && value <= 100 ? value : null;
                    hasRead = true;
                }
            }
            if (!bluetooth || pending != null || now < nextRead) return;
            nextRead = now + 20;
            requestGeneration = generation;
            pending = read(path);
        }
    }
}
