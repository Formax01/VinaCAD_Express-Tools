using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Tools.Model;

namespace Tools.View.Controls
{
    public sealed class DoorStylePreview : FrameworkElement
    {
        private static readonly Brush Background = CreateBrush(31, 38, 48);
        private static readonly Pen GeometryPen = CreatePen(CreateBrush(51, 255, 51), 1.5);

        public static readonly DependencyProperty DoorStyleProperty = DependencyProperty.Register(
            nameof(DoorStyle),
            typeof(DoorStyleModel),
            typeof(DoorStylePreview),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public DoorStyleModel? DoorStyle
        {
            get => (DoorStyleModel?)GetValue(DoorStyleProperty);
            set => SetValue(DoorStyleProperty, value);
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            Rect viewport = new Rect(0, 0, Math.Max(1, ActualWidth), Math.Max(1, ActualHeight));
            drawingContext.DrawRectangle(Background, null, viewport);

            IReadOnlyList<DoorPreviewPrimitive>? geometry = DoorStyle?.PreviewGeometry;
            if (geometry == null || geometry.Count == 0) return;

            bool hasOpeningBounds = DoorStyle!.OpeningMaximumX - DoorStyle.OpeningMinimumX > 1e-6;
            double minX = hasOpeningBounds ? DoorStyle.OpeningMinimumX : geometry.Min(item => item.MinX);
            double minY = geometry.Min(item => item.MinY);
            double maxX = hasOpeningBounds ? DoorStyle.OpeningMaximumX : geometry.Max(item => item.MaxX);
            double maxY = geometry.Max(item => item.MaxY);
            if (hasOpeningBounds)
            {
                IReadOnlyList<DoorPreviewPrimitive> visible = geometry
                    .Where(item => item.MaxX >= minX && item.MinX <= maxX)
                    .ToList();
                minY = visible.Min(item => item.MinY);
                maxY = visible.Max(item => item.MaxY);
            }
            double naturalWidth = Math.Max(maxX - minX, 1e-6);
            double naturalHeight = Math.Max(maxY - minY, 1e-6);
            double minimumSpan = Math.Max(naturalWidth, naturalHeight) * 0.08;
            ExpandBounds(ref minX, ref maxX, minimumSpan);
            ExpandBounds(ref minY, ref maxY, minimumSpan);
            double sourceWidth = maxX - minX;
            double sourceHeight = maxY - minY;
            double padding = 9.0;
            double scale = Math.Min(
                Math.Max(1, viewport.Width - padding * 2) / sourceWidth,
                Math.Max(1, viewport.Height - padding * 2) / sourceHeight);
            double offsetX = (viewport.Width - sourceWidth * scale) * 0.5;
            double offsetY = (viewport.Height - sourceHeight * scale) * 0.5;

            Point Map(double x, double y) => new Point(
                offsetX + (x - minX) * scale,
                offsetY + (maxY - y) * scale);

            foreach (DoorPreviewPrimitive item in geometry)
            {
                if (item.Kind == DoorPreviewPrimitiveKind.Line)
                {
                    drawingContext.DrawLine(GeometryPen, Map(item.StartX, item.StartY), Map(item.EndX, item.EndY));
                }
                else if (item.Kind == DoorPreviewPrimitiveKind.Circle)
                {
                    drawingContext.DrawEllipse(null, GeometryPen, Map(item.CenterX, item.CenterY), item.Radius * scale, item.Radius * scale);
                }
                else
                {
                    DrawArc(drawingContext, item, scale, Map);
                }
            }
        }

        private static void ExpandBounds(ref double minimum, ref double maximum, double minimumSpan)
        {
            double span = maximum - minimum;
            if (span >= minimumSpan) return;
            double center = (minimum + maximum) * 0.5;
            minimum = center - minimumSpan * 0.5;
            maximum = center + minimumSpan * 0.5;
        }

        private static void DrawArc(
            DrawingContext drawingContext,
            DoorPreviewPrimitive arc,
            double scale,
            Func<double, double, Point> map)
        {
            double delta = arc.EndAngle - arc.StartAngle;
            while (delta < 0) delta += Math.PI * 2;
            Point start = map(
                arc.CenterX + Math.Cos(arc.StartAngle) * arc.Radius,
                arc.CenterY + Math.Sin(arc.StartAngle) * arc.Radius);
            Point end = map(
                arc.CenterX + Math.Cos(arc.EndAngle) * arc.Radius,
                arc.CenterY + Math.Sin(arc.EndAngle) * arc.Radius);
            PathFigure figure = new PathFigure { StartPoint = start };
            figure.Segments.Add(new ArcSegment(
                end,
                new Size(arc.Radius * scale, arc.Radius * scale),
                0,
                delta > Math.PI,
                SweepDirection.Counterclockwise,
                true));
            drawingContext.DrawGeometry(null, GeometryPen, new PathGeometry(new[] { figure }));
        }

        private static SolidColorBrush CreateBrush(byte red, byte green, byte blue)
        {
            SolidColorBrush brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
            brush.Freeze();
            return brush;
        }

        private static Pen CreatePen(Brush brush, double thickness)
        {
            Pen pen = new Pen(brush, thickness);
            pen.Freeze();
            return pen;
        }
    }
}
