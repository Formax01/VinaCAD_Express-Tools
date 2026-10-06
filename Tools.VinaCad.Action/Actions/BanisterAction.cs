using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using Prima.VinaCAD.EditorInput;
using System;
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

namespace Tools.VinaCad.Action.Actions
{
    public class BanisterAction
    {
        public void Execute()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var db = doc.Database;

            // 1. Form (lặp lại nếu người dùng bấm Pick)
            var vm = new BanisterVM();
            vm.Load(BanisterSettingsStore.Load());
            vm.SetLayers(BanisterHelper.GetLayerColors(db));

            BanisterInput input = null;
            while (true)
            {
                var window = new BanisterWindow { DataContext = vm };
                Application.ShowModalWindow(window);

                if (window.PickRequested)
                {
                    if (BanisterHelper.TryPickLayer(ed, db, out string layer, out short aci))
                        vm.ApplyPickedLayer(layer, aci);
                    continue;                       // mở lại form với giá trị đã nhập
                }

                input = window.Input;               // null nếu Cancel hoặc đóng form
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
                ed.WriteMessage($"\nĐã vẽ đoạn {segments} ({ids.Count} đối tượng).");
            }

            if (segments > 0) ed.Regen();
            ed.WriteMessage($"\nKết thúc LG: {segments} đoạn lan can.");
        }
    }
}