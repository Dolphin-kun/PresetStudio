using System.Windows;
using System.Windows.Media;

namespace PresetStudio.View
{
    internal sealed class WaveformView : FrameworkElement
    {
        public static readonly DependencyProperty PeaksProperty = DependencyProperty.Register(
            nameof(Peaks), typeof(IReadOnlyList<double>), typeof(WaveformView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
            nameof(Fill), typeof(Brush), typeof(WaveformView), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

        public IReadOnlyList<double>? Peaks
        {
            get => (IReadOnlyList<double>?)GetValue(PeaksProperty);
            set => SetValue(PeaksProperty, value);
        }

        public Brush Fill
        {
            get => (Brush)GetValue(FillProperty);
            set => SetValue(FillProperty, value);
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
            if (Peaks is not { Count: > 1 } peaks || ActualWidth <= 0 || ActualHeight <= 0)
                return;

            var middle = ActualHeight / 2;
            var smoothed = Smooth(peaks);
            var step = ActualWidth / (smoothed.Length - 1);
            var top = smoothed.Select((peak, i) => new Point(i * step, middle - Math.Max(0.5, peak * middle))).ToList();
            var bottom = top.Select(point => new Point(point.X, middle * 2 - point.Y)).Reverse().ToList();

            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(top[0], true, true);
                AddCurve(context, top);
                context.LineTo(bottom[0], false, false);
                AddCurve(context, bottom);
            }

            geometry.Freeze();
            drawingContext.DrawGeometry(Fill, null, geometry);
        }

        private static double[] Smooth(IReadOnlyList<double> peaks)
        {
            var result = new double[peaks.Count];
            for (var i = 0; i < peaks.Count; i++)
            {
                var sum = 0.0;
                var weight = 0.0;
                for (var k = -2; k <= 2; k++)
                {
                    var index = i + k;
                    if (index < 0 || index >= peaks.Count)
                        continue;

                    var w = 3 - Math.Abs(k);
                    sum += peaks[index] * w;
                    weight += w;
                }

                result[i] = sum / weight;
            }

            return result;
        }

        private static void AddCurve(StreamGeometryContext context, List<Point> points)
        {
            for (var i = 0; i < points.Count - 1; i++)
            {
                var control = points[i];
                var next = points[i + 1];
                context.QuadraticBezierTo(control, new Point((control.X + next.X) / 2, (control.Y + next.Y) / 2), true, true);
            }

            context.LineTo(points[^1], true, true);
        }
    }
}
