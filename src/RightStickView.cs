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
        private static readonly Typeface Face = new Typeface("Segoe UI");

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
            double radius = Math.Max(10, Math.Min(width, height) / 2 - 28);
            Brush teal = B("#008F87"), muted = B("#8CA0A5");
            dc.DrawEllipse(B("#F3F9F8"), new Pen(B("#CFDFDF"), 1), center, radius, radius);
            dc.DrawEllipse(null, new Pen(B("#E0ECEB"), 1), center, radius / 2, radius / 2);
            Pen grid = new Pen(B("#D8E6E5"), 1);
            dc.DrawLine(grid, new Point(center.X - radius, center.Y), new Point(center.X + radius, center.Y));
            dc.DrawLine(grid, new Point(center.X, center.Y - radius), new Point(center.X, center.Y + radius));
            Text(dc, "X", new Point(center.X + radius + 11, center.Y - 7), 11, muted);
            Text(dc, "Y", new Point(center.X - 4, center.Y - radius - 21), 11, muted);
            Text(dc, "−1", new Point(center.X - radius - 9, center.Y + 11), 9, muted);
            Text(dc, "+1", new Point(center.X + radius - 9, center.Y + 11), 9, muted);
            Point point = new Point(center.X + x * radius, center.Y - y * radius);
            Brush active = enabled && fresh ? teal : muted;
            dc.DrawLine(new Pen(active, 2), center, point);
            dc.DrawEllipse(B(enabled && fresh ? "#DCF2EC" : "#EBEFF0"), null, point, 15, 15);
            dc.DrawEllipse(active, new Pen(Brushes.White, 2), point, 7, 7);
            dc.DrawEllipse(B("#A4BBBB"), null, center, 2, 2);
        }
        private void Text(DrawingContext dc, string value, Point point, double size, Brush color)
        {
            dc.DrawText(new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, size, color, VisualTreeHelper.GetDpi(this).PixelsPerDip), point);
        }
        private static bool Finite(double v) { return !Double.IsNaN(v) && !Double.IsInfinity(v); }
        private static Brush B(string color) { var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); brush.Freeze(); return brush; }
    }
}
