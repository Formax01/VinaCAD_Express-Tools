using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using Prima.VinaCAD.EditorInput;
using System;
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

namespace Tools.VinaCad.Action.Actions
{
    public class BanisterAction : IAciColorPickerService
    {
        public void Execute()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var db = doc.Database;

            // 1. Form (lặp lại nếu người dùng bấm Pick)
            var vm = new BanisterVM(this);
            vm.Load(BanisterSettingsStore.Load());
            vm.SetLayers(BanisterHelper.GetLayerColors(db));

            BanisterInput input = null;
            while (true)
            {
                vm.ResetDialogRequest();
                var window = new BanisterWindow { DataContext = vm };
                BanisterHelper.DrawIllustrations(window.SlopedPreviewCanvas, window.FlatPreviewCanvas);
                Application.ShowModalWindow(window);

                if (vm.PickRequested)
                {
                    if (BanisterHelper.TryPickLayer(ed, db, out string layer, out short aci))
                        vm.ApplyPickedLayer(layer, aci);
                    continue;                       // mở lại form với giá trị đã nhập
                }

                if (!vm.AcceptRequested) break; // Cancel/đóng cửa sổ

                if (!BanisterInputFactory.TryCreate(vm, out input, out string error))
                {
                    MessageBox.Show(window, error, "Lan can", MessageBoxButton.OK, MessageBoxImage.Warning);
                    continue;
                }
                break;
            }
            if (input == null) return;

            BanisterSettingsStore.Save(input);

            // 2. Vẽ liên tiếp nhiều đoạn
            int segments = 0;
            bool hasLast = false;
            Point3d lastEnd = Point3d.Origin;

            while (true)
            {
                var startOpt = new PromptPointOptions(hasLast
                    ? "\nChọn điểm đầu lan can [Nối tiếp từ điểm cuối trước (T)] <Enter để kết thúc>: "
                    : "\nChọn điểm đầu lan can: ")
                {
                    AllowNone = hasLast
                };
                if (hasLast)
                {
                    startOpt.Keywords.Add("T");
                    startOpt.AppendKeywordsToMessage = false;
                }

                var r1 = ed.GetPoint(startOpt);
                if (r1.Status == PromptStatus.None) break;
                if (r1.Status != PromptStatus.OK && r1.Status != PromptStatus.Keyword) break;

                Point3d start = r1.Status == PromptStatus.Keyword ? lastEnd : r1.Value;

                var r2 = ed.GetPoint(new PromptPointOptions("\nChọn điểm cuối lan can: ")
                {
                    UseBasePoint = true,
                    BasePoint = start
                });
                if (r2.Status != PromptStatus.OK) break;

                Point3d end = BanisterHelper.NormalizeEnd(start, r2.Value);
                var ids = BanisterHelper.CreateBanister(db, input, start, end);
                if (ids.Count == 0)
                {
                    ed.WriteMessage("\nHai điểm quá gần nhau theo phương ngang hoặc quá dốc (> 70 độ), bỏ qua đoạn này.");
                    continue;
                }

                segments++;
                lastEnd = end;
                hasLast = true;
                ed.WriteMessage($"\nĐã vẽ đoạn {segments} .");
            }

            if (segments > 0) ed.Regen();
            ed.WriteMessage($"\nKết thúc LG: {segments} đoạn lan can.");
        }

        public short? PickColor(short currentIndex)
        {
            var owner = System.Windows.Application.Current?.Windows
                .OfType<System.Windows.Window>()
                .FirstOrDefault(window => window.IsActive);
            var dialog = new AciPickerWindow(currentIndex) { Owner = owner };
            return dialog.ShowDialog() == true ? dialog.Selected : null;
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
            { error = "Tên layer không hợp lệ."; return false; }

            if (!short.TryParse(vm.ColorIndex, out short aci) || aci < 1 || aci > 255)
            { error = "Màu layer (ACI) phải từ 1 đến 255."; return false; }

            if (!Parse(vm.TotalHeight, "Total Height", true, out double h, ref error) ||
                !Parse(vm.HandrailDia, "Handrail Dia", true, out double hd, ref error) ||
                !Parse(vm.HandrailExtend, "Handrail Extend", false, out double he, ref error) ||
                !Parse(vm.RailDia, "Rail Dia", true, out double rd, ref error) ||
                !Parse(vm.TopGap, "Top Gap", false, out double tg, ref error) ||
                !Parse(vm.BottomGap, "Bottom Gap", false, out double bg, ref error) ||
                !Parse(vm.ColumnDia, "Column Dia", true, out double cd, ref error) ||
                !Parse(vm.ColumnGap, "Column Gap", true, out double cg, ref error) ||
                !Parse(vm.PoleDia, "Pole Dia", true, out double pd, ref error) ||
                !Parse(vm.PoleGap, "Pole Gap", true, out double pg, ref error))
                return false;

            if (hd + tg + rd + bg >= h)
            { error = "Total Height quá nhỏ so với Handrail Dia + Top Gap + Rail Dia + Bottom Gap."; return false; }
            if (cd >= cg)
            { error = "Column Gap phải lớn hơn Column Dia."; return false; }

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
            { error = $"{label}: giá trị không phải là số."; return false; }
            if (positive ? value <= 0 : value < 0)
            { error = positive ? $"{label} phải lớn hơn 0." : $"{label} không được âm."; return false; }
            return true;
        }
    }
}
