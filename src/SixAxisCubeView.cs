using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace SpaceKatMonitor
{
    /// <summary>A lightweight isometric 3D cube driven by all six HID axes.</summary>
    public sealed class SixAxisCubeView : FrameworkElement
    {
        private struct V3
        {
            public double X, Y, Z;
            public V3(double x, double y, double z) { X = x; Y = y; Z = z; }
            public static V3 operator +(V3 a, V3 b) { return new V3(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
        }

        private sealed class Face
        {
            public Point[] Points;
            public double Depth;
            public Brush Fill;
        }

        private static readonly Brush AxisX = Brush("#E76F65");
        private static readonly Brush AxisY = Brush("#45A77B");
        private static readonly Brush AxisZ = Brush("#5D8DDA");
        private static readonly Brush Muted = Brush("#789098");
        private static readonly Brush Teal = Brush("#008F87");
        private static readonly Typeface LabelFace = new Typeface("Segoe UI");
        private static readonly Typeface CaptionFace = new Typeface("Microsoft YaHei UI");
        private readonly CubeMotionModel motion = new CubeMotionModel();

        public SixAxisCubeView()
        {
            MinHeight = 270;
            ClipToBounds = true;
            SnapsToDevicePixels = true;
            IsHitTestVisible = false;
        }

        public void SetInput(short[] axes, bool hasData, double monotonicSeconds)
        {
            motion.SetInput(axes, hasData, monotonicSeconds);
            InvalidateVisual();
        }

        public void Advance(double nowSeconds)
        {
            motion.Advance(nowSeconds);
            InvalidateVisual();
        }

        public void ResetView()
        {
            motion.ResetView();
            InvalidateVisual();
        }

        public string GetSnapshot() { return motion.GetSnapshot(); }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            double width = ActualWidth, height = ActualHeight;
            if (width < 10 || height < 10) return;
            dc.DrawRoundedRectangle(Brush("#F5FAFB"), null, new Rect(0, 0, width, height), 10, 10);
            Point center = new Point(width * 0.5, height * 0.45);
            double scale = FitScale(width, height, center, motion.Pose);

            DrawFloor(dc, center, scale);
            DrawWorldAxes(dc, center, scale);
            dc.PushOpacity(motion.HasData ? 1 : 0.48);
            DrawCube(dc, center, scale, motion.Pose);
            dc.Pop();

            Brush stateBrush = motion.IsInputFresh ? Teal : Muted;
            dc.DrawEllipse(stateBrush, null, new Point(17, 20), 3, 3);
            DrawText(dc, !motion.HasData ? "等待六轴数据" : motion.IsInputFresh ? "实时六轴操控" : "数据已暂停 · 姿态保持",
                new Point(27, 11), 11, stateBrush, false);
            DrawGizmo(dc, new Point(41, height - 37));
            DrawText(dc, "松手保持姿态 · 重置回到中心", new Point(Math.Max(95, width - 211), height - 26), 10, Muted, false);
        }

        private static double FitScale(double width, double height, Point center, CubePose pose)
        {
            double scale = Math.Min(width / 7.5, height / 5.4);
            Point offset = Project(new V3(pose.X, pose.Y, pose.Z), new Point(), 1);
            // Every rotated vertex lies within radius sqrt(3)*0.61, and the
            // longest body axis is 1.25. Reserve room for labels and captions
            // while gently widening the view as the cube approaches an edge.
            const double radius = 1.25;
            if (radius - offset.X > 0) scale = Math.Min(scale, (center.X - 30) / (radius - offset.X));
            if (radius + offset.X > 0) scale = Math.Min(scale, (width - center.X - 30) / (radius + offset.X));
            if (radius - offset.Y > 0) scale = Math.Min(scale, (center.Y - 46) / (radius - offset.Y));
            if (radius + offset.Y > 0) scale = Math.Min(scale, (height - center.Y - 64) / (radius + offset.Y));
            return Math.Max(1, scale);
        }

        private static void DrawFloor(DrawingContext dc, Point center, double scale)
        {
            Pen grid = new Pen(Brush("#DDE9EB"), 0.8);
            const double floor = -1.65;
            for (int i = -4; i <= 4; i++)
            {
                double n = i * 0.45;
                dc.DrawLine(grid, Project(new V3(n, -1.8, floor), center, scale), Project(new V3(n, 1.8, floor), center, scale));
                dc.DrawLine(grid, Project(new V3(-1.8, n, floor), center, scale), Project(new V3(1.8, n, floor), center, scale));
            }
            // A soft, fixed reference shadow makes translations easy to see.
            dc.DrawEllipse(Brush("#E4EFF0"), null, Project(new V3(0, 0, floor), center, scale), scale * 0.84, scale * 0.27);
        }

        private static void DrawWorldAxes(DrawingContext dc, Point center, double scale)
        {
            V3 origin = new V3(0, 0, -1.65);
            dc.PushOpacity(0.48);
            DrawArrow(dc, Project(origin, center, scale), Project(origin + new V3(2.0, 0, 0), center, scale), AxisX, 1.2, 5);
            DrawArrow(dc, Project(origin, center, scale), Project(origin + new V3(0, 2.0, 0), center, scale), AxisY, 1.2, 5);
            DrawArrow(dc, Project(origin, center, scale), Project(new V3(0, 0, 1.65), center, scale), AxisZ, 1.2, 5);
            dc.Pop();
        }

        private void DrawCube(DrawingContext dc, Point center, double scale, CubePose pose)
        {
            const double h = 0.61;
            V3[] local = {
                new V3(-h,-h,-h), new V3(h,-h,-h), new V3(h,h,-h), new V3(-h,h,-h),
                new V3(-h,-h,h), new V3(h,-h,h), new V3(h,h,h), new V3(-h,h,h)
            };
            V3 position = new V3(pose.X, pose.Y, pose.Z);
            V3[] world = new V3[local.Length];
            for (int i = 0; i < local.Length; i++) world[i] = Rotate(local[i], pose) + position;
            int[][] indices = { new[] {0,3,2,1}, new[] {4,5,6,7}, new[] {0,1,5,4},
                new[] {3,7,6,2}, new[] {0,4,7,3}, new[] {1,2,6,5} };
            string[] colors = { "#B5D9D5", "#D8F0EA", "#8FC6BE", "#ABD8D1", "#79B8B1", "#A5DAD2" };
            List<Face> faces = new List<Face>();
            for (int faceIndex = 0; faceIndex < indices.Length; faceIndex++)
            {
                Point[] points = new Point[4];
                double depth = 0;
                for (int vertex = 0; vertex < 4; vertex++)
                {
                    V3 v = world[indices[faceIndex][vertex]];
                    points[vertex] = Project(v, center, scale);
                    depth += v.X + v.Y + v.Z;
                }
                faces.Add(new Face { Points = points, Depth = depth, Fill = Brush(colors[faceIndex]) });
            }
            faces.Sort(delegate(Face a, Face b) { return a.Depth.CompareTo(b.Depth); });
            Pen edge = new Pen(Brush("#75ABA5"), 1.3);
            foreach (Face face in faces) dc.DrawGeometry(face.Fill, edge, Polygon(face.Points));

            // Body-fixed colored axes make each rotation directly visible.
            Point projectedOrigin = Project(position, center, scale);
            DrawLocalAxis(dc, projectedOrigin, Project(Rotate(new V3(1.25, 0, 0), pose) + position, center, scale), AxisX, "X");
            DrawLocalAxis(dc, projectedOrigin, Project(Rotate(new V3(0, 1.25, 0), pose) + position, center, scale), AxisY, "Y");
            DrawLocalAxis(dc, projectedOrigin, Project(Rotate(new V3(0, 0, 1.25), pose) + position, center, scale), AxisZ, "Z");
            dc.DrawEllipse(Brush("#FFFFFF"), new Pen(Brush("#7CA9A8"), 1), projectedOrigin, 2.5, 2.5);
        }

        private static V3 Rotate(V3 v, CubePose pose)
        {
            double rx = pose.Rx * Math.PI / 180, ry = pose.Ry * Math.PI / 180, rz = pose.Rz * Math.PI / 180;
            double y = v.Y * Math.Cos(rx) - v.Z * Math.Sin(rx);
            double z = v.Y * Math.Sin(rx) + v.Z * Math.Cos(rx);
            double x = v.X * Math.Cos(ry) + z * Math.Sin(ry);
            z = -v.X * Math.Sin(ry) + z * Math.Cos(ry);
            return new V3(x * Math.Cos(rz) - y * Math.Sin(rz), x * Math.Sin(rz) + y * Math.Cos(rz), z);
        }

        private static Point Project(V3 v, Point center, double scale)
        {
            // Orthonormal isometric camera; +Z points up in screen space.
            return new Point(center.X + (v.X - v.Y) * 0.7071067812 * scale,
                center.Y + ((v.X + v.Y) * 0.4082482905 - v.Z * 0.8164965809) * scale);
        }

        private void DrawLocalAxis(DrawingContext dc, Point from, Point to, Brush color, string label)
        {
            DrawArrow(dc, from, to, color, 2.0, 6);
            Vector direction = to - from;
            if (direction.Length > 0.01) direction.Normalize();
            Point labelPoint = to + direction * 11;
            DrawText(dc, label, new Point(labelPoint.X - 4, labelPoint.Y - 9), 13, color, true);
        }

        private void DrawGizmo(DrawingContext dc, Point center)
        {
            DrawArrow(dc, center, new Point(center.X + 20, center.Y + 11), AxisX, 1.8, 4);
            DrawArrow(dc, center, new Point(center.X - 20, center.Y + 11), AxisY, 1.8, 4);
            DrawArrow(dc, center, new Point(center.X, center.Y - 24), AxisZ, 1.8, 4);
            DrawText(dc, "X", new Point(center.X + 24, center.Y + 5), 10, AxisX, true);
            DrawText(dc, "Y", new Point(center.X - 32, center.Y + 5), 10, AxisY, true);
            DrawText(dc, "Z", new Point(center.X - 3, center.Y - 38), 10, AxisZ, true);
        }

        private static void DrawArrow(DrawingContext dc, Point from, Point to, Brush color, double thickness, double head)
        {
            Vector direction = to - from;
            if (direction.Length < 0.1) return;
            direction.Normalize();
            Vector perpendicular = new Vector(-direction.Y, direction.X);
            dc.DrawLine(new Pen(color, thickness), from, to);
            Point foot = to - direction * head;
            dc.DrawGeometry(color, null, Polygon(new[] { to, foot + perpendicular * head * 0.45, foot - perpendicular * head * 0.45 }));
        }

        private static Geometry Polygon(Point[] points)
        {
            StreamGeometry geometry = new StreamGeometry();
            using (StreamGeometryContext context = geometry.Open())
            {
                context.BeginFigure(points[0], true, true);
                for (int i = 1; i < points.Length; i++) context.LineTo(points[i], true, false);
            }
            geometry.Freeze();
            return geometry;
        }

        private static Brush Brush(string hex)
        {
            SolidColorBrush brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }

        private void DrawText(DrawingContext dc, string text, Point at, double size, Brush color, bool axisLabel)
        {
            FormattedText formatted = new FormattedText(text, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
                axisLabel ? LabelFace : CaptionFace, size, color, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(formatted, at);
        }
    }
}
