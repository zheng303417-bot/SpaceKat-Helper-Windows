using System;
using System.Windows;
using System.Windows.Media;

namespace SpaceKatMonitor
{
    /// <summary>
    /// Bipolar ring gauge for one HID axis. Zero sits at twelve o'clock; the arc
    /// grows clockwise for positive values and counter-clockwise for negative ones.
    /// </summary>
    public sealed class AxisGauge : FrameworkElement
    {
        private const double Span = 135;
        private const double Thickness = 8;
        private static readonly Pen TrackPen = RoundPen(Rgb(0xEC, 0xE8, 0xE1), Thickness);
        private static readonly Pen TickPen = RoundPen(Rgb(0xD9, 0xD3, 0xCA), 1.3);
        private static readonly Pen CenterTickPen = RoundPen(Rgb(0xA3, 0x9D, 0x94), 1.3);
        private static readonly Brush IdleDot = Freeze(new SolidColorBrush(Rgb(0xC9, 0xC2, 0xB7)));
        private readonly Pen _arcPen;
        private double _fraction;
        private bool _known;

        public AxisGauge(Brush color)
        {
            _arcPen = new Pen(color, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            _arcPen.Freeze();
            Width = 104;
            Height = 104;
            IsHitTestVisible = false;
            SnapsToDevicePixels = false;
        }

        public void SetValue(short value, bool known)
        {
            double fraction = !known ? 0 : value < 0 ? Math.Max(-1, value / 512.0) : Math.Min(1, value / 511.0);
            if (known == _known && Math.Abs(fraction - _fraction) < 0.0005) return;
            _fraction = fraction;
            _known = known;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double size = Math.Min(ActualWidth, ActualHeight);
            if (size < 20) return;
            Point center = new Point(ActualWidth / 2, ActualHeight / 2);
            double radius = size / 2 - 11;
            dc.DrawGeometry(null, TrackPen, Arc(center, radius, -Span, Span));
            for (int i = -4; i <= 4; i++)
            {
                double angle = i * Span / 4;
                dc.DrawLine(i == 0 ? CenterTickPen : TickPen, At(center, radius + 7.5, angle), At(center, radius + 10, angle));
            }
            if (_known && Math.Abs(_fraction) > 0.003)
            {
                double end = _fraction * Span;
                dc.DrawGeometry(null, _arcPen, Arc(center, radius, 0, end));
                dc.DrawEllipse(Brushes.White, null, At(center, radius, end), 1.9, 1.9);
            }
            else
            {
                dc.DrawEllipse(IdleDot, null, At(center, radius, 0), 2.2, 2.2);
            }
        }

        private static Geometry Arc(Point center, double radius, double from, double to)
        {
            StreamGeometry geometry = new StreamGeometry();
            using (StreamGeometryContext context = geometry.Open())
            {
                context.BeginFigure(At(center, radius, from), false, false);
                context.ArcTo(At(center, radius, to), new Size(radius, radius), 0, Math.Abs(to - from) > 180,
                    to > from ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true, false);
            }
            geometry.Freeze();
            return geometry;
        }

        private static Point At(Point center, double radius, double degrees)
        {
            double radians = (degrees - 90) * Math.PI / 180;
            return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
        }

        private static Color Rgb(byte r, byte g, byte b) { return Color.FromRgb(r, g, b); }

        private static Pen RoundPen(Color color, double thickness)
        {
            Pen pen = new Pen(Freeze(new SolidColorBrush(color)), thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            pen.Freeze();
            return pen;
        }

        private static Brush Freeze(SolidColorBrush brush) { brush.Freeze(); return brush; }
    }
}
