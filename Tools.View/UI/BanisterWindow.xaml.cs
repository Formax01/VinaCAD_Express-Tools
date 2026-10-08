using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Tools.Model;
using Tools.VinaCad.Helper.Helper;

namespace Tools.View.UI
{
    public partial class BanisterWindow : Window
    {
        public BanisterWindow()
        {
            InitializeComponent();
            DrawIllustrations();
        }

        private void DrawIllustrations()
        {
            var sample = new BanisterInput();
            DrawRailing(SlopedCanvas, sample, 2400, 0.5);
            DrawRailing(FlatCanvas, sample, 2400, 0.0);
        }

        private static void DrawRailing(Canvas canvas, BanisterInput input, double lengthMm, double slope)
        {
            const double px = 0.065;
            const double left = 12;
            const double bottom = 8;

            var stroke = new SolidColorBrush(Color.FromRgb(0x1F, 0xA3, 0xB5));
            var fill = new SolidColorBrush(Color.FromArgb(40, 0x1F, 0xA3, 0xB5));

            Point Q(double x, double xRef, double off) =>
                new Point(left + x * px, canvas.Height - bottom - (slope * xRef + off) * px);

            canvas.Children.Clear();
            foreach (var r in BanisterGeometry.Build(input, lengthMm, slope))
            {
                double x0 = r.X;
                double x1 = r.X + r.W;
                double y0 = r.Y;
                double y1 = r.Y + r.H;
                PointCollection points;

                if (r.Cap > 0)
                {
                    double c = r.Cap;
                    points = new PointCollection
                    {
                        Q(x0 - c, x0, y0), Q(x0, x0, y0), Q(x1, x1, y0), Q(x1 + c, x1, y0),
                        Q(x1 + c, x1, y1), Q(x1, x1, y1), Q(x0, x0, y1), Q(x0 - c, x0, y1)
                    };
                }
                else
                {
                    points = new PointCollection
                    {
                        Q(x0, x0, y0), Q(x1, x1, y0), Q(x1, x1, y1), Q(x0, x0, y1)
                    };
                }

                canvas.Children.Add(new Polygon
                {
                    Points = points,
                    Stroke = stroke,
                    StrokeThickness = 0.7,
                    Fill = fill
                });
            }
        }

    }
}
