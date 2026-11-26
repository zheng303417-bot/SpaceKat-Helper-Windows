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
            public static V3 operator -(V3 a, V3 b) { return new V3(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
        }

        private sealed class Face
        {
            public Point[] Points;
            public double Depth;
            public Brush Fill;
        }

        private static readonly Brush AxisX = Brush("#D9483F");
        private static readonly Brush AxisY = Brush("#36996A");
        private static readonly Brush AxisZ = Brush("#3E70D4");
        private static readonly Brush Muted = Brush("#8C867D");
        private static readonly Brush HintInk = Brush("#6E695F");
        private static readonly Brush LiveDot = Brush("#2FAE6B");
        private static readonly Brush LiveText = Brush("#1F8A55");
        private static readonly Brush PillFill = Brush("#C8FFFFFF");
        private static readonly Brush ShadowFill = Brush("#1C3C2D1E");
        private static readonly Pen GridPen = FrozenPen("#DCD6CD", 0.8);
        private static readonly Pen GridAxisPen = FrozenPen("#CFC8BD", 1);
        private static readonly Pen ReferencePen = DashedPen("#BFB8AC", 1);
        private static readonly Pen EdgePen = FrozenPen("#B9B1A5", 1.05);
        private static readonly Pen OriginPen = FrozenPen("#8C867D", 1);
        private static readonly Color FaceLight = Color.FromRgb(0xFF, 0xFF, 0xFF);
        private static readonly Color FaceDark = Color.FromRgb(0xCC, 0xC4, 0xB8);
        // Light comes from the viewer's upper left, so the top face reads brightest.
        private static readonly V3 Light = Normalize(new V3(0.2, 0.8, 1.3));
        private static readonly Typeface LabelFace = new Typeface(new FontFamily("Segoe UI Variable Display, Segoe UI"),
            FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        private static readonly Typeface CaptionFace = new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI"),
            FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
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
            Point center = new Point(width * 0.5, height * 0.47);
            double scale = FitScale(width, height, center, motion.Pose);

            DrawFloor(dc, center, scale, motion.Pose);
            DrawWorldAxes(dc, center, scale);
            dc.PushOpacity(motion.HasData ? 1 : 0.5);
            DrawCube(dc, center, scale, motion.Pose);
            dc.Pop();

            bool fresh = motion.IsInputFresh;
            string state = !motion.HasData ? "等待六轴数据" : fresh ? "实时六轴操控" : "数据已暂停 · 姿态保持";
            FormattedText label = Text(state, 11.5, fresh ? LiveText : Muted, CaptionFace);
            dc.DrawRoundedRectangle(PillFill, null, new Rect(14, 14, label.Width + 36, 26), 13, 13);
            dc.DrawEllipse(fresh ? LiveDot : Muted, null, new Point(28, 27), 3.5, 3.5);
            dc.DrawText(label, new Point(38, 27 - label.Height / 2));

            FormattedText hint = Text("松手保持姿态 · 重置回到中心", 11, HintInk, CaptionFace);
            Rect hintPill = new Rect(width - hint.Width - 38, height - 40, hint.Width + 24, 26);
            dc.DrawRoundedRectangle(PillFill, null, hintPill, 13, 13);
            dc.DrawText(hint, new Point(hintPill.X + 12, hintPill.Y + 13 - hint.Height / 2));
        }

        private static double FitScale(double width, double height, Point center, CubePose pose)
        {
            double scale = Math.Min(width / 6.4, height / 4.7);
            Point offset = Project(new V3(pose.X, pose.Y, pose.Z), new Point(), 1);
            // Every rotated vertex lies within radius sqrt(3)*0.61, and the
            // longest body axis is 1.25. Reserve room for labels and captions
            // while gently widening the view as the cube approaches an edge.
            const double radius = 1.25;
            if (radius - offset.X > 0) scale = Math.Min(scale, (center.X - 30) / (radius - offset.X));
            if (radius + offset.X > 0) scale = Math.Min(scale, (width - center.X - 30) / (radius + offset.X));
            if (radius - offset.Y > 0) scale = Math.Min(scale, (center.Y - 50) / (radius - offset.Y));
            if (radius + offset.Y > 0) scale = Math.Min(scale, (height - center.Y - 40) / (radius + offset.Y));
            return Math.Max(1, scale);
        }

        private static void DrawFloor(DrawingContext dc, Point center, double scale, CubePose pose)
        {
            const double floor = -1.65;
            for (int i = -5; i <= 5; i++)
            {
                double n = i * 0.42;
                Pen pen = i == 0 ? GridAxisPen : GridPen;
                dc.DrawLine(pen, Project(new V3(n, -2.1, floor), center, scale), Project(new V3(n, 2.1, floor), center, scale));
                dc.DrawLine(pen, Project(new V3(-2.1, n, floor), center, scale), Project(new V3(2.1, n, floor), center, scale));
            }
            // A dashed ring marks the centre; the soft shadow follows the cube,
            // so translations read against a fixed reference.
            dc.DrawEllipse(null, ReferencePen, Project(new V3(0, 0, floor), center, scale), scale * 0.86, scale * 0.5);
            double lift = Math.Max(0, Math.Min(1, (pose.Z + 1.2) / 2.4));
            dc.PushOpacity(1 - lift * 0.55);
            dc.DrawEllipse(ShadowFill, null, Project(new V3(pose.X, pose.Y, floor), center, scale),
                scale * (0.78 + lift * 0.2), scale * (0.45 + lift * 0.12));
            dc.Pop();
        }

        private static void DrawWorldAxes(DrawingContext dc, Point center, double scale)
        {
            V3 origin = new V3(0, 0, -1.65);
            dc.PushOpacity(0.42);
            DrawArrow(dc, Project(origin, center, scale), Project(origin + new V3(2.3, 0, 0), center, scale), AxisX, 1.2, 5);
            DrawArrow(dc, Project(origin, center, scale), Project(origin + new V3(0, 2.3, 0), center, scale), AxisY, 1.2, 5);
            DrawArrow(dc, Project(origin, center, scale), Project(new V3(0, 0, 1.75), center, scale), AxisZ, 1.2, 5);
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
            List<Face> faces = new List<Face>();
            for (int faceIndex = 0; faceIndex < indices.Length; faceIndex++)
            {
                Point[] points = new Point[4];
                double depth = 0;
                V3 middle = new V3(0, 0, 0);
                for (int vertex = 0; vertex < 4; vertex++)
                {
                    V3 v = world[indices[faceIndex][vertex]];
                    points[vertex] = Project(v, center, scale);
                    depth += v.X + v.Y + v.Z;
                    middle = middle + v;
                }
                V3 normal = Normalize(new V3(middle.X / 4 - position.X, middle.Y / 4 - position.Y, middle.Z / 4 - position.Z));
                double lit = 0.32 + 0.68 * Math.Max(0, Dot(normal, Light));
                faces.Add(new Face { Points = points, Depth = depth, Fill = Shade(lit) });
            }
            faces.Sort(delegate(Face a, Face b) { return a.Depth.CompareTo(b.Depth); });
            foreach (Face face in faces) dc.DrawGeometry(face.Fill, EdgePen, Polygon(face.Points));

            // Body-fixed coloured axes make each rotation directly visible.
            Point projectedOrigin = Project(position, center, scale);
            DrawLocalAxis(dc, projectedOrigin, Project(Rotate(new V3(1.25, 0, 0), pose) + position, center, scale), AxisX, "X");
            DrawLocalAxis(dc, projectedOrigin, Project(Rotate(new V3(0, 1.25, 0), pose) + position, center, scale), AxisY, "Y");
            DrawLocalAxis(dc, projectedOrigin, Project(Rotate(new V3(0, 0, 1.25), pose) + position, center, scale), AxisZ, "Z");
            dc.DrawEllipse(Brushes.White, OriginPen, projectedOrigin, 2.8, 2.8);
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
            DrawArrow(dc, from, to, color, 2.2, 7);
            Vector direction = to - from;
            if (direction.Length > 0.01) direction.Normalize();
            Point labelPoint = to + direction * 12;
            FormattedText text = Text(label, 13, color, LabelFace);
            dc.DrawText(text, new Point(labelPoint.X - text.Width / 2, labelPoint.Y - text.Height / 2));
        }

        private static void DrawArrow(DrawingContext dc, Point from, Point to, Brush color, double thickness, double head)
        {
            Vector direction = to - from;
            if (direction.Length < 0.1) return;
            direction.Normalize();
            Vector perpendicular = new Vector(-direction.Y, direction.X);
            Point foot = to - direction * head;
            Pen pen = new Pen(color, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Flat };
            dc.DrawLine(pen, from, foot);
            dc.DrawGeometry(color, null, Polygon(new[] { to, foot + perpendicular * head * 0.48, foot - perpendicular * head * 0.48 }));
        }

        private static Brush Shade(double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            SolidColorBrush brush = new SolidColorBrush(Color.FromRgb(
                (byte)(FaceDark.R + (FaceLight.R - FaceDark.R) * t),
                (byte)(FaceDark.G + (FaceLight.G - FaceDark.G) * t),
                (byte)(FaceDark.B + (FaceLight.B - FaceDark.B) * t)));
            brush.Freeze();
            return brush;
        }

        private static double Dot(V3 a, V3 b) { return a.X * b.X + a.Y * b.Y + a.Z * b.Z; }

        private static V3 Normalize(V3 v)
        {
            double length = Math.Sqrt(Dot(v, v));
            return length < 1e-9 ? new V3(0, 0, 1) : new V3(v.X / length, v.Y / length, v.Z / length);
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

        private static Pen FrozenPen(string hex, double thickness)
        {
            Pen pen = new Pen(Brush(hex), thickness) { LineJoin = PenLineJoin.Round };
            pen.Freeze();
            return pen;
        }

        private static Pen DashedPen(string hex, double thickness)
        {
            Pen pen = new Pen(Brush(hex), thickness) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
            pen.Freeze();
            return pen;
        }

        private FormattedText Text(string text, double size, Brush color, Typeface face)
        {
            return new FormattedText(text, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
                face, size, color, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        }
    }
}
