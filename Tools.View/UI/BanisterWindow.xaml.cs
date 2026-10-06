using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Tools.Model;
using Tools.ViewModel;
using Tools.VinaCad.Helper;
using Tools.VinaCad.Helper.Helper;

namespace Tools.View.UI
{
    // ============================================================
    //  Cửa sổ nhập thông số lan can (LG)
    // ============================================================
    public partial class BanisterWindow : Window
    {
        public BanisterInput Input { get; private set; }
        public bool PickRequested { get; private set; }

        public BanisterWindow()
        {
            InitializeComponent();
            DrawIllustrations();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not BanisterVM vm) return;

            if (!vm.TryBuildInput(out var input, out var error))
            {
                MessageBox.Show(this, error, "Lan can", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;     // giữ form mở để người dùng sửa
            }

            Input = input;
            DialogResult = true;
        }

        // Nút Pick: đóng form để Action cho người dùng chọn một đối tượng lấy layer
        private void PickButton_Click(object sender, RoutedEventArgs e)
        {
            PickRequested = true;
            Close();
        }

        // Nút ô màu: mở bảng chọn màu ACI
        private void ColorButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not BanisterVM vm) return;

            short.TryParse(vm.ColorIndex, out short current);
            if (current < 1 || current > 255) current = 7;

            var dlg = new AciPickerWindow(current) { Owner = this };
            if (dlg.ShowDialog() == true && dlg.Selected.HasValue)
                vm.ColorIndex = dlg.Selected.Value.ToString();
        }

        // ===== Hình minh họa (vẽ bằng code) =====
        private void DrawIllustrations()
        {
            var sample = new BanisterInput();                 // thông số mặc định
            DrawRailing(SlopedCanvas, sample, 2400, 0.5);     // lan can nghiêng
            DrawRailing(FlatCanvas, sample, 2400, 0.0);       // lan can ngang
        }

        private static void DrawRailing(Canvas canvas, BanisterInput input, double lengthMm, double slope)
        {
            const double px = 0.065;                          // pixel trên mỗi mm
            const double left = 12, bottom = 8;               // lề trong canvas (px)

            var stroke = new SolidColorBrush(Color.FromRgb(0x1F, 0xA3, 0xB5));
            var fill = new SolidColorBrush(Color.FromArgb(40, 0x1F, 0xA3, 0xB5));

            // x: vị trí ngang (mm), xRef: vị trí dùng để tính độ dốc, off: độ cao so với nền dốc (mm)
            Point Q(double x, double xRef, double off) =>
                new Point(left + x * px, canvas.Height - bottom - (slope * xRef + off) * px);

            canvas.Children.Clear();
            foreach (var r in BanisterGeometry.Build(input, lengthMm, slope))
            {
                double x0 = r.X, x1 = r.X + r.W, y0 = r.Y, y1 = r.Y + r.H;
                PointCollection pts;

                if (r.Cap > 0)
                {
                    double c = r.Cap;
                    pts = new PointCollection
            {
                Q(x0 - c, x0, y0), Q(x0, x0, y0), Q(x1, x1, y0), Q(x1 + c, x1, y0),
                Q(x1 + c, x1, y1), Q(x1, x1, y1), Q(x0, x0, y1), Q(x0 - c, x0, y1)
            };
                }
                else
                {
                    pts = new PointCollection
            {
                Q(x0, x0, y0), Q(x1, x1, y0), Q(x1, x1, y1), Q(x0, x0, y1)
            };
                }

                canvas.Children.Add(new Polygon
                {
                    Points = pts,
                    Stroke = stroke,
                    StrokeThickness = 0.7,
                    Fill = fill
                });
            }
        }
    }

    // ============================================================
    //  Bảng chọn màu ACI (dùng cho ô màu của form LG)
    // ============================================================
    internal class AciPickerWindow : Window
    {
        public short? Selected { get; private set; }

        private const int Cols = 24;
        private readonly TextBox _tb = new TextBox { Width = 50, Padding = new Thickness(3, 2, 3, 2) };
        private readonly Border _preview = new Border
        {
            Width = 44,
            Height = 26,
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(8, 0, 0, 0)
        };
        private readonly TextBlock _info = new TextBlock
        {
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        private Border _marked;
        private bool _syncing;

        public AciPickerWindow(short current)
        {
            Title = "Chọn màu (ACI)";
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var root = new StackPanel { Margin = new Thickness(12) };

            root.Children.Add(new TextBlock { Text = "AutoCAD Color Index (ACI):", Margin = new Thickness(0, 0, 0, 4) });

            // Khối 1: màu đậm, từ tối lên sáng
            root.Children.Add(BuildBlock(new[] { 8, 6, 4, 2, 0 }));
            // Khối 2: màu nhạt, từ sáng xuống tối
            var block2 = BuildBlock(new[] { 1, 3, 5, 7, 9 });
            block2.Margin = new Thickness(0, 6, 0, 0);
            root.Children.Add(block2);

            // Hàng màu chuẩn 1..9 và 250..255
            var std = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            for (short a = 1; a <= 9; a++) std.Children.Add(MakeCell(a, 26, 22));
            std.Children.Add(new Border { Width = 14 });
            for (short a = 250; a <= 255; a++) std.Children.Add(MakeCell(a, 26, 22));
            root.Children.Add(std);

            // Ô nhập số + xem trước
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            row.Children.Add(new TextBlock { Text = "Index color:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            row.Children.Add(_tb);
            row.Children.Add(_preview);
            row.Children.Add(_info);
            root.Children.Add(row);

            // OK / Cancel
            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var ok = new Button { Content = "OK", Width = 80, Padding = new Thickness(0, 3, 0, 3), IsDefault = true };
            var cancel = new Button { Content = "Cancel", Width = 80, Padding = new Thickness(0, 3, 0, 3), Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
            ok.Click += (s, e) => Accept();
            btns.Children.Add(ok);
            btns.Children.Add(cancel);
            root.Children.Add(btns);

            Content = root;

            _tb.TextChanged += (s, e) =>
            {
                if (_syncing) return;
                if (short.TryParse(_tb.Text, out short v) && v >= 1 && v <= 255) Select(v, false);
            };

            Select(current >= 1 && current <= 255 ? current : (short)7, true);
        }

        // ===== Dựng bảng =====
        private UniformGrid BuildBlock(int[] offsets)
        {
            var grid = new UniformGrid { Columns = Cols, Rows = offsets.Length };
            foreach (int off in offsets)
                for (int n = 0; n < Cols; n++)
                    grid.Children.Add(MakeCell((short)(10 + n * 10 + off), 18, 14));
            return grid;
        }

        private Border MakeCell(short aci, double w, double h)
        {
            var (r, g, b) = AciColorHelper.ToRgb(aci);
            var cell = new Border
            {
                Width = w,
                Height = h,
                Tag = aci,
                Margin = new Thickness(0.5),
                Background = new SolidColorBrush(Color.FromRgb(r, g, b)),
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(2),
                Cursor = Cursors.Hand
            };
            cell.MouseLeftButtonDown += (s, e) =>
            {
                Select(aci, true);
                if (e.ClickCount == 2) Accept();       // nhấp đúp = chọn luôn
            };
            cell.MouseEnter += (s, e) => _info.Text = Describe(aci);
            cell.MouseLeave += (s, e) =>
                _info.Text = short.TryParse(_tb.Text, out short v) ? Describe(v) : "";
            return cell;
        }

        // ===== Chọn / xác nhận =====
        private void Select(short aci, bool updateText)
        {
            if (updateText)
            {
                _syncing = true;
                _tb.Text = aci.ToString();
                _syncing = false;
            }

            var (r, g, b) = AciColorHelper.ToRgb(aci);
            _preview.Background = new SolidColorBrush(Color.FromRgb(r, g, b));
            _info.Text = Describe(aci);

            if (_marked != null) _marked.BorderBrush = Brushes.Transparent;
            _marked = FindCell(Content as Panel, aci);
            if (_marked != null) _marked.BorderBrush = Brushes.Black;
        }

        private static Border FindCell(Panel root, short aci)
        {
            if (root == null) return null;
            foreach (var child in root.Children)
            {
                if (child is Border b && b.Tag is short t && t == aci) return b;
                if (child is Panel p)
                {
                    var found = FindCell(p, aci);
                    if (found != null) return found;
                }
            }
            return null;
        }

        private static string Describe(short aci)
        {
            var (r, g, b) = AciColorHelper.ToRgb(aci);
            return $"ACI {aci}  (R {r}, G {g}, B {b})";
        }

        private void Accept()
        {
            if (short.TryParse(_tb.Text, out short v) && v >= 1 && v <= 255)
            {
                Selected = v;
                DialogResult = true;
            }
            else MessageBox.Show(this, "Nhập số ACI từ 1 đến 255.");
        }
    }
}