using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace SpaceKatMonitor
{
    /// <summary>A normalized gamepad stick disk; positive Y points upwards.</summary>
    public sealed class RightStickView : FrameworkElement
    {
        private double x, y;
        private bool enabled, fresh;
        private static readonly Typeface Face = new Typeface(new FontFamily("Segoe UI Variable Display, Segoe UI"),
            FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        private static readonly Brush Well = Gradient("#EFECE7", "#E5E1D9");
        private static readonly Pen Rim = FrozenPen("#D8D2C8", 1);
        private static readonly Pen Grid = FrozenPen("#DCD6CC", 1);
        private static readonly Pen Tick = FrozenPen("#C9C2B7", 1.2);
        private static readonly Brush Label = B("#8C867D");
        private static readonly Brush Idle = B("#B9B2A7");
        private static readonly Brush Shadow1 = B("#143C2D1E");
        private static readonly Brush Shadow2 = B("#0C3C2D1E");
        private static readonly Pen CapEdge = FrozenPen("#DDD7CE", 1);
        private static readonly Brush Cap = Gradient("#FFFFFF", "#F1EDE7");

        public RightStickView() { MinHeight = 180; IsHitTestVisible = false; ClipToBounds = true; }

        public void SetValue(double valueX, double valueY, bool isEnabled, bool isFresh)
        {
            x = Finite(valueX) ? Math.Max(-1, Math.Min(1, valueX)) : 0;
            y = Finite(valueY) ? Math.Max(-1, Math.Min(1, valueY)) : 0;
            enabled = isEnabled; fresh = isFresh;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            double width = ActualWidth, height = ActualHeight;
            if (width < 10 || height < 10) return;
            Point center = new Point(width / 2, height / 2);
            double radius = Math.Max(10, Math.Min(width, height) / 2 - 26);
            Brush accent = TryFindResource("AccentBrush") as Brush ?? B("#F2661B");
            bool live = enabled && fresh;

            dc.DrawEllipse(Well, Rim, center, radius, radius);
            dc.DrawEllipse(null, Grid, center, radius * 0.5, radius * 0.5);
            dc.DrawLine(Grid, new Point(center.X - radius, center.Y), new Point(center.X + radius, center.Y));
            dc.DrawLine(Grid, new Point(center.X, center.Y - radius), new Point(center.X, center.Y + radius));
            for (int i = 0; i < 16; i++)
            {
                double a = i * Math.PI / 8;
                Vector d = new Vector(Math.Cos(a), Math.Sin(a));
                dc.DrawLine(Tick, center + d * (radius + 5), center + d * (radius + (i % 4 == 0 ? 10 : 8)));
            }
            Text(dc, "X", new Point(center.X + radius + 18, center.Y), 11.5);
            Text(dc, "Y", new Point(center.X, center.Y - radius - 18), 11.5);
            Text(dc, "−1", new Point(center.X - radius + 12, center.Y + 12), 10);
            Text(dc, "+1", new Point(center.X + radius - 12, center.Y + 12), 10);

            Point point = new Point(center.X + x * radius, center.Y - y * radius);
            Pen vector = new Pen(live ? accent : Idle, 2.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawLine(vector, center, point);
            dc.DrawEllipse(live ? accent : Idle, null, center, 3, 3);
            // The knob is a small white cap resting on the disk.
            dc.DrawEllipse(Shadow2, null, new Point(point.X, point.Y + 3), 18, 18);
            dc.DrawEllipse(Shadow1, null, new Point(point.X, point.Y + 1.5), 16, 16);
            dc.DrawEllipse(Cap, CapEdge, point, 15, 15);
            dc.DrawEllipse(live ? accent : Idle, null, point, 5.5, 5.5);
        }

        private void Text(DrawingContext dc, string value, Point middle, double size)
        {
            FormattedText text = new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, size, Label,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text, new Point(middle.X - text.Width / 2, middle.Y - text.Height / 2));
        }

        private static bool Finite(double v) { return !Double.IsNaN(v) && !Double.IsInfinity(v); }
        private static Brush B(string color) { var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); brush.Freeze(); return brush; }

        private static Brush Gradient(string top, string bottom)
        {
            var brush = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(top), (Color)ColorConverter.ConvertFromString(bottom), 90);
            brush.Freeze();
            return brush;
        }

        private static Pen FrozenPen(string color, double thickness)
        {
            Pen pen = new Pen(B(color), thickness);
            pen.Freeze();
            return pen;
        }
    }
}
