using System;
using System.IO;
using System.IO.Compression;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading;

namespace SpaceKatMonitor
{
    // Value 2 was the retired Fusion add-in. Keep the remaining IDs stable for settings and adapters.
    internal enum NavigationTarget { None = 0, Blender = 1, FreeCAD = 3, Rhino = 4, Global = 5, SolidWorks = 6, FusionNative = 7 }

    internal sealed class NavigationBridge : IDisposable
    {
        internal const string MapName = @"Local\SpaceKat.Navigation.v1";
        internal const int Size = 56;
        internal const int Magic = 0x314E4B53; // SKN1
        internal const int SampleTimeoutMs = 150;
        private const int HeartbeatMs = 125;
        private readonly MemoryMappedFile map;
        private readonly MemoryMappedViewAccessor view;
        private int sequence;

        [DllImport("kernel32.dll")]
        internal static extern long GetTickCount64();

        internal NavigationBridge(string name = MapName)
        {
            // Keep the Windows token's default DACL, including its logon and restricted
            // identity entries. The Local namespace scopes the mapping to this session.
            map = MemoryMappedFile.CreateNew(name, Size, MemoryMappedFileAccess.ReadWrite);
            try { view = map.CreateViewAccessor(0, Size, MemoryMappedFileAccess.ReadWrite); }
            catch { map.Dispose(); throw; }
        }

        internal void Publish(NavigationTarget target, long sampleTime, float[] axes)
        {
            long now = GetTickCount64();
            long expires = target == NavigationTarget.None ? 0 : now + HeartbeatMs;
            long sampleEnd = axes == null || sampleTime <= 0 || sampleTime > now || now - sampleTime >= SampleTimeoutMs
                ? 0 : Math.Min(expires, sampleTime + SampleTimeoutMs);
            // A reader accepts only a matching even sequence before and after the payload.
            view.Write(0, unchecked(++sequence));
            Thread.MemoryBarrier();
            view.Write(4, Magic);
            view.Write(8, (int)target);
            view.Write(16, expires);
            view.Write(24, sampleEnd);
            for (int i = 0; i < 6; i++) view.Write(32 + i * 4, sampleEnd == 0 ? 0f : axes[i]);
            Thread.MemoryBarrier();
            view.Write(0, unchecked(++sequence));
        }

        public void Dispose()
        {
            try { view.Dispose(); }
            finally { map.Dispose(); }
        }

        internal static void Shape(short[] raw, int deadZone, int invertMask, double linear, double angular, float[] axes)
        {
            for (int i = 0; i < 6; i++)
            {
                int value = raw[i];
                double magnitude = Math.Max(0, Math.Min(512, Math.Abs(value)) - deadZone) / (double)(512 - deadZone);
                double sign = Math.Sign(value) * ((invertMask & (1 << i)) == 0 ? 1 : -1);
                axes[i] = (float)(sign * magnitude * (i < 3 ? linear : angular));
            }
        }

        internal static void ExportAddIn(Stream output, NavigationTarget target)
        {
            string directory;
            string resources;
            string[] files;
            switch (target)
            {
                case NavigationTarget.FusionNative:
                    directory = "SpaceKatFusion/";
                    resources = "fusion.SpaceKatFusion.";
                    files = new[] { "SpaceKatFusion.py", "SpaceKatFusion.manifest", "camera.py" };
                    break;
                case NavigationTarget.Blender:
                    directory = "spacekat_navigation/";
                    resources = "blender.";
                    files = new[] { "__init__.py", "spacekat_bridge.py" };
                    break;
                case NavigationTarget.FreeCAD:
                    directory = "SpaceKatNavigation-FreeCAD/";
                    resources = "freecad.";
                    files = new[] { "SpaceKatNavigation.FCMacro", "spacekat_bridge.py" };
                    break;
                case NavigationTarget.Rhino:
                    directory = "SpaceKatNavigation-Rhino/";
                    resources = "rhino.";
                    files = new[] { "SpaceKatNavigation.py", "spacekat_bridge.py" };
                    break;
                default:
                    throw new ArgumentOutOfRangeException("target");
            }
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
                foreach (string file in files)
                {
                    using (Stream data = OpenAddInFile(resources, file))
                    using (Stream entry = zip.CreateEntry(directory + file).Open())
                        data.CopyTo(entry);
                }
        }

        private static Stream OpenAddInFile(string prefix, string file)
        {
            string name = "Navigation." + (file == "spacekat_bridge.py" ? file : prefix + file);
            Stream data = typeof(NavigationBridge).Assembly.GetManifestResourceStream(name);
            if (data == null) throw new InvalidOperationException("缺少适配器文件：" + file);
            return data;
        }
    }
}
