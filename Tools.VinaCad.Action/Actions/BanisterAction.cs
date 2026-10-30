using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using PrLogTrackingSystem;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.Model;
using Tools.View.UI;
using Tools.ViewModel;
using Tools.VinaCad.Helper;
using Tools.VinaCad.Helper.Helper;
using Tools.VinaCad.Modeling;
using Application = Prima.VinaCAD.ApplicationServices.Application;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using Tools.Resources.Language;
namespace Tools.VinaCad.Action.Actions
{
    public class BanisterAction : IAciColorPickerService
    {
        private Document _doc;
        private Editor _ed;
        private Database _db;

        public void Execute()
        {
            try
            {
                UpdateCurrentDocument();
                if (_doc == null)
                    return;

                // 1. Form nhập thông số (mở lại nếu người dùng bấm Pick)
                if (!TryShowDialog(out BanisterInput input))
                    return;

                BanisterSettingsStore.Save(input);

                // 2. Vẽ liên tiếp nhiều đoạn
                int segments = DrawSegments(input);

                Finish(segments);
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(BanisterAction), ex);
                throw;
            }
        }

        private void UpdateCurrentDocument()
        {
            _doc = Application.DocumentManager.MdiActiveDocument;
            if (_doc == null)
                return;

            _ed = _doc.Editor;
            _db = _doc.Database;
        }

        // ---------- Form nhập thông số ----------

        // Hiện form; lặp lại khi bấm Pick hoặc khi dữ liệu không hợp lệ.
        // Trả về false nếu người dùng hủy hoặc đóng cửa sổ.
        private bool TryShowDialog(out BanisterInput input)
        {
            input = null;
            var vm = CreateViewModel();

            while (true)
            {
                vm.ResetDialogRequest();
                var window = new BanisterWindow { DataContext = vm };
                BanisterHelper.DrawIllustrations(window.SlopedPreviewCanvas, window.FlatPreviewCanvas);
                Application.ShowModalWindow(window);

                if (vm.PickRequested)
                {
                    if (BanisterHelper.TryPickLayer(_ed, _db, out string layer, out short aci))
                        vm.ApplyPickedLayer(layer, aci);
                    continue;                       // mở lại form với giá trị đã nhập
                }

                if (!vm.AcceptRequested)
                    return false;                   // Cancel / đóng cửa sổ

                if (BanisterInputFactory.TryCreate(vm, out input, out string error))
                    return true;

                MessageBox.Show(window, error, Lang.LG_Title_LanCan, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // Dựng VM và nối logic đồng bộ layer → màu → brush (VM chỉ giữ state)
        private BanisterVM CreateViewModel()
        {
            var vm = new BanisterVM(this);
            var layerColors = BanisterHelper.GetLayerColors(_db);

            void SyncColorFromLayer()
            {
                if (!string.IsNullOrWhiteSpace(vm.LayerName)
                    && layerColors.TryGetValue(vm.LayerName.Trim(), out short layerAci))
                    vm.ColorIndex = layerAci.ToString(CultureInfo.InvariantCulture);
            }

            void UpdateColorBrush()
            {
                if (!AciColorHelper.TryParseIndex(vm.ColorIndex, out short aci))
                {
                    vm.ColorBrush = System.Windows.Media.Brushes.Transparent;
                    return;
                }
                var (r, g, b) = AciColorHelper.ToRgb(aci);
                var brush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(r, g, b));
                brush.Freeze();
                vm.ColorBrush = brush;
            }

            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(BanisterVM.LayerName)) SyncColorFromLayer();
                else if (e.PropertyName == nameof(BanisterVM.ColorIndex)) UpdateColorBrush();
            };

            vm.SetLayers(layerColors.Keys.OrderBy(x => x));
            vm.Load(BanisterSettingsStore.Load());
            SyncColorFromLayer();   // giữ hành vi cũ: layer có sẵn thì lấy màu layer
            UpdateColorBrush();     // bắt buộc: giá trị không đổi thì không phát PropertyChanged
            return vm;
        }

        public short? PickColor(short currentIndex)
        {
            var owner = System.Windows.Application.Current?.Windows
                .OfType<System.Windows.Window>()
                .FirstOrDefault(window => window.IsActive);
            return AciPickerAction.Pick(currentIndex, owner);
        }

        // ---------- Vẽ các đoạn lan can ----------

        // Vẽ liên tiếp cho đến khi người dùng Enter hoặc Esc; trả về số đoạn đã vẽ
        private int DrawSegments(BanisterInput input)
        {
            int segments = 0;
            bool hasLast = false;
            Point3d lastEnd = Point3d.Origin;

            while (true)
            {
                if (!TryGetStartPoint(hasLast, lastEnd, out Point3d start))
                    break;

                PromptPointResult endRes = _ed.GetPoint(
                    new PromptPointOptions(Lang.LG_UI_ChonDiemCuoiLanCan)
                    {
                        UseBasePoint = true,
                        BasePoint = start
                    });
                if (endRes.Status != PromptStatus.OK)
                    break;

                Point3d end = BanisterHelper.NormalizeEnd(start, endRes.Value);
                var ids = BanisterHelper.CreateBanister(_db, input, start, end);
                if (ids.Count == 0)
                {
                    _ed.WriteMessage(Lang.LG_MessDialog_HaiDiemQuaGanHoacDoc);
                    continue;
                }

                segments++;
                lastEnd = end;
                hasLast = true;
                _ed.WriteMessage(string.Format(Lang.LG_MessDialog_DaVeDoan, segments)   );
            }

            return segments;
        }

        
        private bool TryGetStartPoint(bool hasLast, Point3d lastEnd, out Point3d start)
        {
            start = default(Point3d);

            var options = new PromptPointOptions(hasLast
                ? Lang.LG_UI_ChonDiemDauLanCanNoiTiep
                : Lang.LG_UI_ChonDiemDauLanCan)
            {
                AllowNone = hasLast
            };
            if (hasLast)
            {
                options.Keywords.Add("T");
                options.AppendKeywordsToMessage = false;
            }

            PromptPointResult result = _ed.GetPoint(options);

            if (result.Status == PromptStatus.Keyword)
            {
                start = lastEnd;
                return true;
            }

            if (result.Status != PromptStatus.OK)
                return false;                       // Enter (None), Esc hoặc trạng thái khác

            start = result.Value;
            return true;
        }

       

        private void Finish(int segments)
        {
            if (segments > 0)
                _ed.Regen();

            _ed.WriteMessage(string.Format(Lang.LG_MessDialog_KetThucLG, segments));
        }
        
        internal static class AciPickerAction
        {
            public static short? Pick(short current, System.Windows.Window owner)
            {
                var vm = new AciPickerVM();
                var items = new Dictionary<short, AciColorItem>();
                Fill(vm.DarkColors, AciColorHelper.GetDarkPalette(), items);
                Fill(vm.LightColors, AciColorHelper.GetLightPalette(), items);
                Fill(vm.StandardColors, AciColorHelper.GetStandardPalette(), items);

                AciColorItem selected = null;
                vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName != nameof(AciPickerVM.ColorText)) return;

                    bool valid = AciColorHelper.TryParseIndex(vm.ColorText, out short aci);
                    vm.CanAccept = valid;
                    if (!valid) return;

                    if (selected != null) selected.IsSelected = false;
                    selected = items[aci];
                    selected.IsSelected = true;
                    vm.PreviewBrush = selected.ColorBrush;
                    vm.Description = selected.Description;
                };

                // Gán sau khi đã gắn handler để lần đầu cũng chọn đúng ô.
                short start = AciColorHelper.IsValidIndex(current) ? current : AciColorHelper.DefaultAci;
                vm.ColorText = start.ToString(CultureInfo.InvariantCulture);

                var dialog = new AciPickerWindow(vm) { Owner = owner };
                if (dialog.ShowDialog() != true) return null;

                return AciColorHelper.TryParseIndex(vm.ColorText, out short result) ? result : (short?)null;
            }

            private static void Fill(ObservableCollection<AciColorItem> target,
                IEnumerable<short> indices, Dictionary<short, AciColorItem> lookup)
            {
                foreach (short aci in indices)
                {
                    var (r, g, b) = AciColorHelper.ToRgb(aci);
                    var brush = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(r, g, b));
                    brush.Freeze();

                    var item = new AciColorItem(aci, brush, AciColorHelper.Describe(aci));
                    target.Add(item);
                    lookup[aci] = item;
                }
            }
        }

        internal static class BanisterInputFactory
        {
            public static bool TryCreate(BanisterVM vm, out BanisterInput input, out string error)
            {
                input = null;
                error = null;

                string layer = (vm.LayerName ?? string.Empty).Trim();
                if (layer.Length == 0 || layer.IndexOfAny("<>/\\\":;?*|=`".ToCharArray()) >= 0)
                { error = Lang.LG_MessDialog_TenLayerKhongHopLe; return false; }

                if (!AciColorHelper.TryParseIndex(vm.ColorIndex, out short aci))
                { error = Lang.LG_MessDialog_MauLayerKhongHopLe; return false; }

                if (!Parse(vm.TotalHeight, Lang.LG_UI_TotalHeight, true, out double h, ref error) ||
                    !Parse(vm.HandrailDia, Lang.LG_UI_HandrailDia, true, out double hd, ref error) ||
                    !Parse(vm.HandrailExtend, Lang.LG_UI_HandrailExtend, false, out double he, ref error) ||
                    !Parse(vm.RailDia, Lang.LG_UI_RailDia, true, out double rd, ref error) ||
                    !Parse(vm.TopGap, Lang.LG_UI_TopGap, false, out double tg, ref error) ||
                    !Parse(vm.BottomGap, Lang.LG_UI_BottomGap, false, out double bg, ref error) ||
                    !Parse(vm.ColumnDia, Lang.LG_UI_ColumnDia, true, out double cd, ref error) ||
                    !Parse(vm.ColumnGap, Lang.LG_UI_ColumnGap, true, out double cg, ref error) ||
                    !Parse(vm.PoleDia, Lang.LG_UI_PoleDia, true, out double pd, ref error) ||
                    !Parse(vm.PoleGap, Lang.LG_UI_PoleGap, true, out double pg, ref error))
                    return false;

                if (hd + tg + rd + bg >= h)
                { error = Lang.LG_MessDialog_TotalHeightQuaNho; return false; }
                if (cd >= cg)
                { error = Lang.LG_MessDialog_ColumnGapPhaiLonHon; return false; }

                input = new BanisterInput
                {
                    LayerName = layer,
                    ColorIndex = aci,
                    TotalHeight = h,
                    HandrailDia = hd,
                    HandrailExtend = he,
                    RailDia = rd,
                    TopGap = tg,
                    BottomGap = bg,
                    ColumnDia = cd,
                    ColumnGap = cg,
                    HasSideColumns = vm.HasSideColumns,
                    PoleDia = pd,
                    PoleGap = pg,
                    ToGroup = vm.ToGroup
                };
                return true;
            }

            private static bool Parse(string text, string label, bool positive, out double value, ref string error)
            {
                string normalized = (text ?? string.Empty).Trim().Replace(',', '.');

                if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                {
                    error = string.Format(Lang.LG_MessDialog_GiaTriKhongPhaiSo, label);
                    return false;
                }

                if (positive ? value <= 0 : value < 0)
                {
                    error = positive
                        ? string.Format(Lang.LG_MessDialog_PhaiLonHon0, label)
                        : string.Format(Lang.LG_MessDialog_KhongDuocAm, label);
                    return false;
                }

                return true;
            }
        }
    }
}