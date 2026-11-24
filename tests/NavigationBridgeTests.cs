using System;
using System.IO;
using System.IO.Compression;
using System.IO.MemoryMappedFiles;
using System.Windows.Media.Media3D;

namespace SpaceKatMonitor
{
    internal static class NavigationBridgeTests
    {
        internal static string Run()
        {
            float[] axes = new float[6];
            NavigationBridge.Shape(new short[] { 512, -512, 15, 512, -512, Int16.MinValue }, 16, 1 << 4, 2, 1.5, axes);
            Require(axes[0] == 2 && axes[1] == -2 && axes[2] == 0 && axes[3] == 1.5f && axes[4] == 1.5f && axes[5] == -1.5f,
                "六轴并行处理、死区、反转及限幅");
            string name = @"Local\SpaceKat.Navigation.test." + Guid.NewGuid().ToString("N");
            using (var writer = new NavigationBridge(name))
            using (var map = MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.Read))
            using (var reader = map.CreateViewAccessor(0, NavigationBridge.Size, MemoryMappedFileAccess.Read))
            {
                long now = NavigationBridge.GetTickCount64();
                writer.Publish(NavigationTarget.Blender, now, axes);
                Require((reader.ReadInt32(0) & 1) == 0 && reader.ReadInt32(4) == NavigationBridge.Magic && reader.ReadInt32(8) == 1,
                    "本机只读映射、版本和目标");
                Require(reader.ReadInt64(24) > now && reader.ReadInt64(24) <= now + NavigationBridge.SampleTimeoutMs &&
                    reader.ReadSingle(32) == 2 && reader.ReadSingle(52) == -1.5f, "有效报告及到期时间");
                writer.Publish(NavigationTarget.FreeCAD, now - NavigationBridge.SampleTimeoutMs, axes);
                Require(reader.ReadInt32(8) == 3 && reader.ReadInt64(16) > now && reader.ReadInt64(24) == 0 &&
                    reader.ReadSingle(32) == 0 && reader.ReadSingle(52) == 0, "旧输入归零但保留目标");
                writer.Publish(NavigationTarget.None, 0, null);
                Require(reader.ReadInt32(8) == 0 && reader.ReadInt64(16) == 0, "停止输出立即使帧失效");
            }
            CheckPackage(NavigationTarget.Blender, "spacekat_navigation/", "__init__.py", "spacekat_bridge.py");
            CheckPackage(NavigationTarget.FreeCAD, "SpaceKatNavigation-FreeCAD/", "SpaceKatNavigation.FCMacro", "spacekat_bridge.py");
            CheckPackage(NavigationTarget.Rhino, "SpaceKatNavigation-Rhino/", "SpaceKatNavigation.py", "spacekat_bridge.py");
            CheckPackage(NavigationTarget.FusionNative, "SpaceKatFusion/", "SpaceKatFusion.py", "SpaceKatFusion.manifest", "camera.py");
            NavigationBridge.Shape(new short[] { 512, 0, 0, 512, 0, 0 }, 16, 0, .01, .01, axes);
            Require(axes[0] == .01f && axes[3] == .01f, "0.01× 最低倍率保留平移和旋转输入");
            VerifySolidWorksRotation(new Vector3D(1, 0, 0), new Vector3D(0, 1, 0), new Vector3D(0, 0, 1));
            VerifySolidWorksRotation(new Vector3D(-1, 0, 0), new Vector3D(0, 1, 0), new Vector3D(0, 0, -1));
            VerifySolidWorksRotation(new Vector3D(0, 1, 0), new Vector3D(0, 0, 1), new Vector3D(1, 0, 0));
            VerifySolidWorksRotation(new Vector3D(0, 0, 1), new Vector3D(1, 0, 0), new Vector3D(0, 1, 0));
            VerifySolidWorksPrecision();
            return "3D navigation: 6 axes, 0.01x sensitivity, local transport, expiry, stop and four add-in packages passed.\r\n" +
                "SOLIDWORKS rotation: forward and backward pitch, yaw and roll through 360 degrees, preserving orbit center and scale.\r\n";
        }

        private static void VerifySolidWorksRotation(Vector3D axis, Vector3D before, Vector3D afterQuarterTurn)
        {
            // Start from a non-default view that pans the model away from the viewport center.
            double[] basis = { 0, 1, 0, -1, 0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0 };
            double[] offset = { -4, -8, -15 };
            const double viewScale = 5;
            var center = new Vector3D(2, -1, 3);
            var pivot = new Vector3D(1, 2, 0);
            using (var pose = new SolidWorksViewApi.ViewPose(null, null, basis, offset, viewScale, 10, false))
            {
                double[] bounds = { 1.9, -1.1, 2.9, 2.1, -.9, 3.1 };
                Require((pose.RotationCenter(bounds, 100, 100) - pivot).Length < 1e-10, "SOLIDWORKS 模型完整可见时绕模型中心旋转");
                Require(pose.RotationCenter(bounds, 10, 10).Length < 1e-10, "SOLIDWORKS 放大后绕视图中心旋转");
            }
            Vector3D step = axis * (Math.PI / 120);
            Vector3D[] checkpoints = { afterQuarterTurn, -before, -afterQuarterTurn, before };
            for (int quarter = 0; quarter < checkpoints.Length; quarter++)
            {
                for (int i = 0; i < 60; i++) SolidWorksViewApi.RotateViewTransform(basis, offset, step, pivot);

                // Convert the initial view-space direction to model space, then back through the new orientation.
                Vector3D direction = SolidWorksViewApi.Project(basis, new Vector3D(before.Y, -before.X, before.Z));
                Require((direction - checkpoints[quarter]).Length < 1e-10, "SOLIDWORKS 绕屏幕轴连续旋转 " + (quarter + 1) * 90);
                Vector3D projected = SolidWorksViewApi.Project(basis, center) * viewScale +
                    new Vector3D(offset[0], offset[1], offset[2]);
                Require((projected - pivot).Length < 1e-10 && basis[12] == 1, "SOLIDWORKS 旋转中心和缩放保持稳定");
            }
        }

        private static void VerifySolidWorksPrecision()
        {
            float[] axes = { .06f, -.08f, 0, 0, .3f, 0 };
            SolidWorksNavigationOutput.ApplyPrecisionCurve(axes, .5, .3);
            Require(Math.Abs(axes[0] - .0336f) < 1e-6 && Math.Abs(axes[1] + .0448f) < 1e-6 &&
                axes[2] == 0 && axes[3] == 0 && axes[4] == .3f && axes[5] == 0,
                "SOLIDWORKS 小幅输入降速并保留方向，满幅输入和回中保持原值");
        }

        private static void CheckPackage(NavigationTarget target, string path, params string[] files)
        {
            using (var bytes = new MemoryStream())
            {
                NavigationBridge.ExportAddIn(bytes, target);
                bytes.Position = 0;
                using (var zip = new ZipArchive(bytes, ZipArchiveMode.Read))
                {
                    foreach (string file in files)
                    {
                        ZipArchiveEntry entry = zip.GetEntry(path + file);
                        Require(entry != null && entry.Length > 0, target + " 适配器文件 " + file);
                    }
                }
            }
        }

        private static void Require(bool passed, string description)
        {
            if (!passed) throw new InvalidOperationException("3D 导航自测失败：" + description);
        }
    }
}
