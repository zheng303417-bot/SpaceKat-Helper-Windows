using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace SpaceKatMonitor
{
    /// <summary>Perspective view from the exact centre of a labelled cube room.</summary>
    public sealed class FixedCameraView : FrameworkElement
    {
        private struct V3
        {
            public double X, Y, Z;
            public V3(double x, double y, double z) { X = x; Y = y; Z = z; }
            public static V3 operator +(V3 a, V3 b) { return new V3(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
            public static V3 operator -(V3 a, V3 b) { return new V3(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
            public static V3 operator *(V3 a, double b) { return new V3(a.X * b, a.Y * b, a.Z * b); }
        }
        private sealed class Wall
        {
            public V3 Center, U, V;
            public string Name, Color;
            public Wall(V3 c, V3 u, V3 v, string name, string color) { Center = c; U = u; V = v; Name = name; Color = color; }
        }
        private static readonly Wall[] Walls = {
            new Wall(new V3(0,0,3),new V3(1,0,0),new V3(0,1,0),"前","#F5EBE2"),
            new Wall(new V3(0,0,-3),new V3(-1,0,0),new V3(0,1,0),"后","#ECE9E4"),
            new Wall(new V3(3,0,0),new V3(0,0,-1),new V3(0,1,0),"右","#E3E9EF"),
            new Wall(new V3(-3,0,0),new V3(0,0,1),new V3(0,1,0),"左","#EFE8DB"),
            new Wall(new V3(0,3,0),new V3(1,0,0),new V3(0,0,-1),"上","#F7F5F1"),
            new Wall(new V3(0,-3,0),new V3(1,0,0),new V3(0,0,1),"下","#E3DDD3")
        };
        private static readonly Pen WallEdge = FrozenPen("#CFC7BB", 1.3);
        private static readonly Pen WallGrid = FrozenPen("#DBD4C9", 0.7);
        private static readonly Brush LabelFill = B("#E6FFFFFF");
        private static readonly Brush LabelInk = B("#5F594F");
        private static readonly Brush HudInk = B("#6E685F");
        private static readonly Typeface LabelFace = new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI"),
            FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        private static readonly Typeface HudFace = new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI"),
            FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        private const double Near = 0.08;
        private double yaw, pitch;
        private double fieldOfView = RightStickModel.DefaultFieldOfViewDegrees;
        private bool hasData;
        private V3 right, up, forward;
        private double focal;
        private Point screenCenter;

        public FixedCameraView() { MinHeight = 200; IsHitTestVisible = false; ClipToBounds = true; }
        public void SetView(double yawDegrees, double pitchDegrees, bool receivedData)
        {
            SetView(yawDegrees, pitchDegrees, receivedData, RightStickModel.DefaultFieldOfViewDegrees);
        }
        public void SetView(double yawDegrees, double pitchDegrees, bool receivedData, double fieldOfViewDegrees)
        {
            yaw = Finite(yawDegrees) ? yawDegrees % 360 : 0;
            pitch = Finite(pitchDegrees) ? Math.Max(-89, Math.Min(89, pitchDegrees)) : 0;
            hasData = receivedData;
            fieldOfView = Finite(fieldOfViewDegrees) ? Math.Max(RightStickModel.MinimumFieldOfViewDegrees,
                Math.Min(RightStickModel.MaximumFieldOfViewDegrees, fieldOfViewDegrees)) : RightStickModel.DefaultFieldOfViewDegrees;
            InvalidateVisual();
        }
        public string GetSnapshot() { return "CameraPosition=0,0,0; Roll=0; Yaw=" + yaw.ToString("0.000", CultureInfo.InvariantCulture) + "; Pitch=" + pitch.ToString("0.000", CultureInfo.InvariantCulture) + "; FOV=" + fieldOfView.ToString("0.000", CultureInfo.InvariantCulture); }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            double width = ActualWidth, height = ActualHeight;
            if (width < 10 || height < 10) return;
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, width, height)));
            double y = yaw * Math.PI / 180, p = pitch * Math.PI / 180;
            right = new V3(Math.Cos(y), 0, -Math.Sin(y));
            up = new V3(-Math.Sin(y) * Math.Sin(p), Math.Cos(p), -Math.Cos(y) * Math.Sin(p));
            forward = new V3(Math.Sin(y) * Math.Cos(p), Math.Sin(p), Math.Cos(y) * Math.Cos(p));
            focal = height / (2 * Math.Tan(fieldOfView * Math.PI / 360));
            screenCenter = new Point(width / 2, height / 2);
            dc.PushOpacity(hasData ? 1 : 0.62);

            foreach (Wall wall in Walls)
            {
                List<V3> corners = new List<V3>();
                corners.Add(Camera(wall.Center - wall.U * 3 - wall.V * 3));
                corners.Add(Camera(wall.Center + wall.U * 3 - wall.V * 3));
                corners.Add(Camera(wall.Center + wall.U * 3 + wall.V * 3));
                corners.Add(Camera(wall.Center - wall.U * 3 + wall.V * 3));
                List<V3> clipped = ClipNearPlane(corners);
                if (clipped.Count < 3) continue;
                Point[] points = new Point[clipped.Count];
                for (int i = 0; i < points.Length; i++) points[i] = Project(clipped[i]);
                dc.DrawGeometry(B(wall.Color), WallEdge, Polygon(points));
            }
            foreach (Wall wall in Walls)
            {
                Pen grid = WallGrid;
                for (int i = -2; i <= 2; i++)
                {
                    DrawWorldLine(dc, wall.Center + wall.U * i - wall.V * 3, wall.Center + wall.U * i + wall.V * 3, grid);
                    DrawWorldLine(dc, wall.Center + wall.V * i - wall.U * 3, wall.Center + wall.V * i + wall.U * 3, grid);
                }
                V3 center = Camera(wall.Center);
                if (center.Z > Near)
                {
                    Point point = Project(center);
                    // Never let a nearly edge-on wall create a giant label.
                    if (point.X > 40 && point.X < width - 40 && point.Y > 50 && point.Y < height - 15)
                    {
                        FormattedText name = Text(wall.Name, 14, LabelInk, LabelFace);
                        double side = Math.Max(name.Width + 24, 40);
                        dc.DrawRoundedRectangle(LabelFill, null, new Rect(point.X - side / 2, point.Y - 34, side, 28), 14, 14);
                        dc.DrawText(name, new Point(point.X - name.Width / 2, point.Y - 20 - name.Height / 2));
                    }
                }
            }
            dc.Pop();

            // Reticle stays fixed: the camera never translates or rolls.
            Brush accent = TryFindResource("AccentBrush") as Brush ?? B("#F2661B");
            Pen outline = new Pen(Brushes.White, 3.4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            Pen ink = new Pen(accent, 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            Point a = new Point(screenCenter.X - 10, screenCenter.Y), b = new Point(screenCenter.X + 10, screenCenter.Y);
            Point c = new Point(screenCenter.X, screenCenter.Y - 10), d = new Point(screenCenter.X, screenCenter.Y + 10);
            dc.DrawLine(outline, a, b); dc.DrawLine(outline, c, d);
            dc.DrawLine(ink, a, b); dc.DrawLine(ink, c, d);
            FormattedText hud = Text(hasData ? "固定中心 · 转向与变焦" : "固定中心 · 等待摇杆输入", 11.5, HudInk, HudFace);
            dc.DrawRoundedRectangle(LabelFill, null, new Rect(12, 12, hud.Width + 24, 26), 13, 13);
            dc.DrawText(hud, new Point(24, 25 - hud.Height / 2));
            dc.Pop();
        }

        private V3 Camera(V3 world) { return new V3(Dot(world, right), Dot(world, up), Dot(world, forward)); }
        private Point Project(V3 camera) { return new Point(screenCenter.X + focal * camera.X / camera.Z, screenCenter.Y - focal * camera.Y / camera.Z); }
        private void DrawWorldLine(DrawingContext dc, V3 a, V3 b, Pen pen)
        {
            a = Camera(a); b = Camera(b);
            if (a.Z < Near && b.Z < Near) return;
            if (a.Z < Near) a = Intersect(a, b);
            if (b.Z < Near) b = Intersect(b, a);
            dc.DrawLine(pen, Project(a), Project(b));
        }
        private static List<V3> ClipNearPlane(List<V3> input)
        {
            List<V3> output = new List<V3>();
            V3 previous = input[input.Count - 1];
            bool previousInside = previous.Z >= Near;
            foreach (V3 current in input)
            {
                bool inside = current.Z >= Near;
                if (inside != previousInside) output.Add(Intersect(previous, current));
                if (inside) output.Add(current);
                previous = current; previousInside = inside;
            }
            return output;
        }
        private static V3 Intersect(V3 a, V3 b)
        {
            double t = (Near - a.Z) / (b.Z - a.Z);
            return new V3(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, Near);
        }
        private static double Dot(V3 a, V3 b) { return a.X * b.X + a.Y * b.Y + a.Z * b.Z; }
        private static Geometry Polygon(Point[] points)
        {
            StreamGeometry geometry = new StreamGeometry();
            using (StreamGeometryContext context = geometry.Open())
            {
                context.BeginFigure(points[0], true, true);
                for (int i = 1; i < points.Length; i++) context.LineTo(points[i], true, false);
            }
            geometry.Freeze(); return geometry;
        }
        private FormattedText Text(string text, double size, Brush color, Typeface face)
        {
            return new FormattedText(text, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
                face, size, color, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        }
        private static Pen FrozenPen(string color, double thickness) { Pen pen = new Pen(B(color), thickness); pen.Freeze(); return pen; }
        private static bool Finite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
        private static Brush B(string color) { var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); brush.Freeze(); return brush; }
    }
}
