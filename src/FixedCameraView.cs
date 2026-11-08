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
            new Wall(new V3(0,0,3),new V3(1,0,0),new V3(0,1,0),"前  /  FRONT","#E1F1EB"),
            new Wall(new V3(0,0,-3),new V3(-1,0,0),new V3(0,1,0),"后  /  BACK","#E9E9F2"),
            new Wall(new V3(3,0,0),new V3(0,0,-1),new V3(0,1,0),"右  /  RIGHT","#E3EFF8"),
            new Wall(new V3(-3,0,0),new V3(0,0,1),new V3(0,1,0),"左  /  LEFT","#F6EADC"),
            new Wall(new V3(0,3,0),new V3(1,0,0),new V3(0,0,-1),"上  /  TOP","#EBF4F7"),
            new Wall(new V3(0,-3,0),new V3(1,0,0),new V3(0,0,1),"下  /  BOTTOM","#DFE9E8")
        };
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
            dc.DrawRectangle(B("#F1F7F7"), null, new Rect(0, 0, width, height));
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
                dc.DrawGeometry(B(wall.Color), new Pen(B("#B1C7C8"), 1.5), Polygon(points));
            }
            foreach (Wall wall in Walls)
            {
                Pen grid = new Pen(B("#C6D8D7"), 0.7);
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
                    if (point.X > 80 && point.X < width - 80 && point.Y > 70 && point.Y < height - 15)
                    {
                        double labelWidth = 152;
                        dc.DrawRoundedRectangle(B("#F7FBFA"), null, new Rect(point.X - labelWidth / 2, point.Y - 43, labelWidth, 30), 6, 6);
                        Text(dc, wall.Name, new Point(point.X - 57, point.Y - 38), 14, B("#547B7A"));
                    }
                }
            }
            dc.Pop();

            // Reticle stays fixed: the camera never translates or rolls.
            Pen outline = new Pen(B("#FFFFFF"), 3), ink = new Pen(B("#008F87"), 1.2);
            Point a = new Point(screenCenter.X - 9, screenCenter.Y), b = new Point(screenCenter.X + 9, screenCenter.Y);
            Point c = new Point(screenCenter.X, screenCenter.Y - 9), d = new Point(screenCenter.X, screenCenter.Y + 9);
            dc.DrawLine(outline, a, b); dc.DrawLine(outline, c, d);
            dc.DrawLine(ink, a, b); dc.DrawLine(ink, c, d);
            dc.DrawRoundedRectangle(B("#F8FCFB"), null, new Rect(10, 10, 168, 27), 6, 6);
            Text(dc, hasData ? "固定中心 · 转向与变焦" : "固定中心 · 等待摇杆输入", new Point(19, 16), 10, B("#547B7A"));
            dc.DrawRoundedRectangle(B("#F8FCFB"), null, new Rect(10, height - 31, 159, 22), 5, 5);
            Text(dc, "位置 (0, 0, 0)  ·  Roll 0°", new Point(19, height - 27), 10, B("#718C91"));
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
        private void Text(DrawingContext dc, string text, Point at, double size, Brush color)
        {
            dc.DrawText(new FormattedText(text, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
                new Typeface("Microsoft YaHei UI"), size, color, VisualTreeHelper.GetDpi(this).PixelsPerDip), at);
        }
        private static bool Finite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
        private static Brush B(string color) { var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); brush.Freeze(); return brush; }
    }
}
