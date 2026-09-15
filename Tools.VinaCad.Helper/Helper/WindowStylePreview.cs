using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Tools.Model;

namespace Tools.VinaCad.Helper.Helper
{
    public sealed class WindowStylePreview : FrameworkElement
    {
        private const double ReferenceWidth = 120.0;
        private const double ReferenceHeight = 85.0;
        private static readonly Brush Background = CreateBrush(33, 40, 48);
        private static readonly Pen GeometryPen = CreatePen(CreateBrush(0, 127, 127), 1.0);
        private static readonly Pen WallOriginPen = CreatePen(Brushes.White, 1.0);
        private static readonly double[][] ReferenceLines =
        {
            new double[] { 13,46,107,46, 15,50,105,50, 15,51,105,51, 13,55,107,55, 15,46,15,55, 105,46,105,55 },
            new double[] { 13,46,107,46, 15,50,105,50, 13,55,107,55, 15,46,15,55, 105,46,105,55, 60,44,60,57, 15,48,60,48, 60,52,105,52 },
            new double[] { 13,46,107,46, 15,50,105,50, 13,55,107,55, 15,46,15,55, 105,46,105,55, 45,44,45,57, 75,44,75,57, 15,48,45,48, 75,48,105,48, 45,52,75,52 },
            new double[] { 13,46,107,46, 15,50,105,50, 13,55,107,55, 15,46,15,55, 105,46,105,55, 37,44,37,57, 60,44,60,57, 82,44,82,57, 15,48,37,48, 82,48,105,48, 37,52,82,52 },
            new double[] { 26,53,111,53, 26,59,111,59, 26,53,26,59, 111,53,111,59 },
            new double[] { 12,32,106,32, 12,53,106,53 },
            new double[] { 12,37,60,37, 12,43,106,43, 58,49,106,49, 60,37,60,43, 58,43,58,49 },
            new double[] { 12,53,106,53 },
            new double[] { 3,55,115,55, 3,62,115,62, 3,55,3,62, 59,55,59,62, 115,55,115,62 },
            new double[] { 12,32,106,32, 12,53,106,53, 22,40,42,40, 50,40,70,40, 78,40,98,40, 22,45,42,45, 50,45,70,45, 78,45,98,45 },
            new double[] { 12,32,106,32, 22,55,115,55, 22,62,115,62, 22,55,22,62, 115,55,115,62 },
            new double[] { 12,32,106,32, 12,41,60,41, 12,47,106,47, 58,53,106,53, 60,41,60,47, 58,47,58,53 },
            new double[] { 8,35,111,35, 8,42,42,42, 76,42,111,42, 8,47,111,47, 40,52,78,52, 42,42,42,47, 76,42,76,47, 40,47,40,52, 78,47,78,52 },
            new double[] { 4,38,115,38, 4,43,33,43, 86,43,115,43, 4,46,115,46, 31,50,87,50, 33,43,33,46, 86,43,86,46, 31,46,31,50, 59,46,59,50, 87,46,87,50 },
            new double[] { 7,19,113,19, 10,22,110,22, 13,25,107,25, 7,19,7,49, 10,22,10,49, 13,25,13,49, 107,25,107,49, 110,22,110,49, 113,19,113,49, 14,63,106,63 },
            new double[] { 9,18,110,18, 11,21,108,21, 14,23,105,23, 9,18,9,48, 11,21,11,48, 14,23,14,48, 105,23,105,48, 108,21,108,48, 110,18,110,48, 15,59,104,59 },
            new double[] { 20,21,109,21, 20,24,106,24, 20,27,103,27, 103,27,103,61, 106,24,106,49, 109,21,109,49, 19,61,103,61 },
            new double[] { 32,23,87,23, 33,26,86,26, 34,28,85,28, 32,23,7,49, 33,26,10,49, 34,28,14,49, 87,23,112,49, 86,26,109,49, 85,28,105,49, 14,59,105,59 },
            new double[] { 15,23,83,23, 15,26,82,26, 15,28,81,28, 15,23,15,49, 83,23,114,49, 82,26,110,49, 81,28,105,49, 14,59,105,59 },
            new double[] { 14,33,105,33, 14,44,105,44, 14,54,105,54, 14,33,14,54, 105,33,105,54 },
            new double[] { 13,33,106,33, 13,41,106,41, 13,46,106,46, 13,54,106,54, 13,33,13,54, 106,33,106,54 }
        };
        private static readonly double[][] ReferenceWalls =
        {
            new double[] { 0,46,13,10, 107,46,13,10 }, 
            new double[] { 0,46,13,10, 107,46,13,10 },
            new double[] { 0,46,13,10, 107,46,13,10 },
            new double[] { 0,46,13,10, 107,46,13,10 },
            new double[] { 0,32,18,20, 103,32,17,20 }, 
            new double[] { 0,32,13,22, 106,32,14,22 },
            new double[] { 0,32,13,22, 106,32,14,22 },
            new double[] { 0,32,13,22, 106,32,14,22 },
            new double[] { 0,32,13,22, 106,32,14,22 },
            new double[] { 0,32,13,22, 106,32,14,22 },
            new double[] { 0,32,13,22, 106,32,14,22 },
            new double[] { 0,32,13,22, 106,32,14,22 },
            new double[] { 0,35,9,18, 111,35,9,18 },
            new double[] { 0,38,5,13, 115,38,5,13 },
            new double[] { 0,50,14,14, 107,50,13,14 },
            new double[] { 0,49,15,11, 105,49,15,11 },
            new double[] { 0,21,20,41, 103,50,17,12 }, 
            new double[] { 0,50,15,10, 105,50,15,10 },
            new double[] { 0,23,15,37, 105,50,15,10 }, 
            new double[] { 0,33,14,22, 106,33,14,22 },
            new double[] { 0,33,14,22, 106,33,14,22 }
        };

        public WindowStylePreview()
        {
            ClipToBounds = true;
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
            RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
        }

        public static readonly DependencyProperty WindowStyleProperty = DependencyProperty.Register(
            nameof(WindowStyle),
            typeof(WindowStyleModel),
            typeof(WindowStylePreview),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public WindowStyleModel? WindowStyle
        {
            get => (WindowStyleModel?)GetValue(WindowStyleProperty);
            set => SetValue(WindowStyleProperty, value);
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            Rect viewport = new Rect(0, 0, Math.Max(1, ActualWidth), Math.Max(1, ActualHeight));
            drawingContext.DrawRectangle(Background, null, viewport);

            WindowStyleModel? style = WindowStyle;
            if (style == null) return;
            if (DrawReferencePreview(drawingContext, viewport, style.Id)) return;

            IReadOnlyList<WindowPreviewPrimitive>? geometry = style.PreviewGeometry;
            if (geometry == null || geometry.Count == 0) return;

            double geometryMinX = geometry.Min(item => item.MinX);
            double geometryMaxX = geometry.Max(item => item.MaxX);
            double minX = geometryMinX;
            double maxX = geometryMaxX;
            double sourceWidth = Math.Max(maxX - minX, 1e-6);
            double minY = geometry.Min(item => item.MinY);
            double maxY = geometry.Max(item => item.MaxY);
            double naturalHeight = Math.Max(maxY - minY, 1e-6);
            double minimumSpan = Math.Max(sourceWidth, naturalHeight) * 4;
            ExpandBounds(ref minY, ref maxY, minimumSpan);
            double sourceHeight = maxY - minY;
            const double markerMargin = 2.0;
            const double markerWidth = 12.0;
            const double markerHeight = 8.0;
            double horizontalPadding = markerMargin + markerWidth;
            double verticalPadding = 10.0;
            double scaleX = Math.Max(1, viewport.Width - horizontalPadding * 2) / sourceWidth;
            double scaleY = Math.Min(
                scaleX,
                Math.Max(1, viewport.Height - verticalPadding * 2) / sourceHeight);
            double verticalOffset = (viewport.Height - sourceHeight * scaleY) * 0.5;

            Point Map(double x, double y) => new Point(
                horizontalPadding + (x - minX) * scaleX,
                verticalOffset + (maxY - y) * scaleY);

            drawingContext.PushClip(new RectangleGeometry(new Rect(
                horizontalPadding,
                0,
                Math.Max(0, viewport.Width - horizontalPadding * 2),
                viewport.Height)));
            foreach (WindowPreviewPrimitive item in geometry)
            {
                if (item.Kind == WindowPreviewPrimitiveKind.Line)
                {
                    drawingContext.DrawLine(GeometryPen, Map(item.StartX, item.StartY), Map(item.EndX, item.EndY));
                }
                else if (item.Kind == WindowPreviewPrimitiveKind.Circle)
                {
                    drawingContext.DrawEllipse(null, GeometryPen, Map(item.CenterX, item.CenterY), item.Radius * scaleX, item.Radius * scaleY);
                }
                else
                {
                    DrawArc(drawingContext, item, scaleX, scaleY, Map);
                }
            }
            drawingContext.Pop();

            double originY = viewport.Height * 0.5;
            drawingContext.DrawRectangle(null, WallOriginPen, new Rect(markerMargin, originY - markerHeight * 0.5, markerWidth, markerHeight));
            drawingContext.DrawRectangle(null, WallOriginPen, new Rect(viewport.Width - markerMargin - markerWidth, originY - markerHeight * 0.5, markerWidth, markerHeight));
        }

        private static void ExpandBounds(ref double minimum, ref double maximum, double minimumSpan)
        {
            double span = maximum - minimum;
            if (span >= minimumSpan) return;
            double center = (minimum + maximum) * 0.5;
            minimum = center - minimumSpan * 0.5;
            maximum = center + minimumSpan * 0.5;
        }

        private static bool DrawReferencePreview(DrawingContext drawingContext, Rect viewport, string id)
        {
            if (!TryGetReferenceTemplate(id, out double[] lines, out double[] walls)) return false;

            double scaleX = viewport.Width / ReferenceWidth;
            double scaleY = viewport.Height / ReferenceHeight;
            for (int i = 0; i < lines.Length; i += 4)
            {
                drawingContext.DrawLine(
                    GeometryPen,
                    new Point(lines[i] * scaleX, lines[i + 1] * scaleY),
                    new Point(lines[i + 2] * scaleX, lines[i + 3] * scaleY));
            }

            for (int i = 0; i < walls.Length; i += 4)
            {
                drawingContext.DrawRectangle(
                    null,
                    WallOriginPen,
                    new Rect(
                        walls[i] * scaleX,
                        walls[i + 1] * scaleY,
                        walls[i + 2] * scaleX,
                        walls[i + 3] * scaleY));
            }
            return true;
        }

        internal static bool TryGetReferenceTemplate(string id, out double[] lines, out double[] walls)
        {
            lines = Array.Empty<double>();
            walls = Array.Empty<double>();
            if (!id.StartsWith("WA_B", StringComparison.OrdinalIgnoreCase) ||
                !int.TryParse(id.AsSpan(4), out int number) ||
                number < 1 || number > ReferenceLines.Length) return false;

            lines = ReferenceLines[number - 1];
            walls = ReferenceWalls[number - 1];
            return true;
        }

        private static void DrawArc(
            DrawingContext drawingContext,
            WindowPreviewPrimitive arc,
            double scaleX,
            double scaleY,
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
                new Size(arc.Radius * scaleX, arc.Radius * scaleY),
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
