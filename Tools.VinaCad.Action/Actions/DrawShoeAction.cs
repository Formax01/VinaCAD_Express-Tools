using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using PrLogTrackingSystem;
using System;
using System.Runtime.InteropServices;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.Resources.Language;
using Tools.VinaCad.Helper;
using Tools.VinaCad.Helper.Helper;
using Tools.VinaCad.Modeling;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCad.Action.Actions
{
    public class DrawShoeAction
    {
        private Document _doc;
        private Editor _ed;
        private Database _db;
        private Matrix3d _ucsToWcs;

        private static class NativeMouse
        {
            [DllImport("user32.dll")] public static extern IntPtr GetCapture();
            [DllImport("user32.dll")] public static extern bool ReleaseCapture();
            [DllImport("user32.dll")] public static extern IntPtr SetFocus(IntPtr hWnd);
            [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

            public const uint WM_LBUTTONUP = 0x0202;
        }

        public void Execute()
        {
            try
            {
                UpdateCurrentDocument();
                if (_doc == null)
                    return;

                ShoeModel model = new ShoeModel();

                // 1. Điểm đặt
                PromptPointResult startResult = _ed.GetPoint(Lang.B3_UI_ChonDiemDatGotGiay);
                if (startResult.Status != PromptStatus.OK)
                    return;
                model.StartPoint = startResult.Value;

                // 2. Chiều cao
                if (!TryGetDistance(model.StartPoint, Lang.B3_UI_NhapChieuCao, out double height))
                    return;
                model.Height = height;

                // 3. Cạnh bé
                if (!TryGetDistance(model.StartPoint, Lang.B3_UI_NhapCanhBe, out double smallSide))
                    return;
                model.SmallSide = smallSide;

                // 4. Cạnh đáy (Enter = cao + cạnh bé)
                if (!TryGetBaseSide(model, out double baseSide))
                    return;
                model.BaseSide = baseSide;

                // 5. Bên đặt cạnh đáy (Enter = Trái)
                if (!TryGetMirror(out bool mirror))
                    return;
                model.Mirror = mirror;

                // 6. Ướm hướng bằng Jig (F = đảo bên)
                if (!TryPlaceWithJig(model))
                    return;

                // 7. Vẽ
                Point3d[] corners = ShoeHelper.GetCorners(
                    model.StartPoint, model.Direction,
                    model.Height, model.SmallSide, model.BaseSide, model.Mirror);

                DuctHelper.CreateClosedPolyline(_db, corners, model.Layer, _ucsToWcs);

                Finish(model);
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(DrawShoeAction), ex);
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
            _ucsToWcs = _ed.CurrentUserCoordinateSystem;
        }

        // Nhả mouse capture còn sót sau click để menu keyword nhận chuột
        private static void ReleaseMouse()
        {
            for (int i = 0; i < 5; i++)
            {
                IntPtr cap = NativeMouse.GetCapture();
                if (cap == IntPtr.Zero)
                    break;

                NativeMouse.PostMessage(cap, NativeMouse.WM_LBUTTONUP, IntPtr.Zero, IntPtr.Zero);
                NativeMouse.ReleaseCapture();
            }

            IntPtr main = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            if (main != IntPtr.Zero)
                NativeMouse.SetFocus(main);
        }

        private bool TryGetDistance(Point3d basePoint, string message, out double value)
        {
            PromptDistanceOptions options = new PromptDistanceOptions(message)
            {
                BasePoint = basePoint,
                UseBasePoint = true,
                AllowNegative = false,
                AllowZero = false
            };

            PromptDoubleResult result = _ed.GetDistance(options);
            value = result.Status == PromptStatus.OK ? result.Value : 0;
            return result.Status == PromptStatus.OK;
        }

        private bool TryGetBaseSide(ShoeModel model, out double baseSide)
        {
            double autoBase = ShoeHelper.GetAutoBase(model.Height, model.SmallSide);
            baseSide = autoBase;

            PromptDistanceOptions options = new PromptDistanceOptions(
                $"{Lang.B3_UI_NhapCanhDay} <{autoBase:0.##}>")
            {
                BasePoint = model.StartPoint,
                UseBasePoint = true,
                AllowNegative = false,
                AllowZero = false,
                AllowNone = true
            };

            PromptDoubleResult result = _ed.GetDistance(options);

            if (result.Status == PromptStatus.None)
                return true;                  // Enter: dùng giá trị tự tính

            if (result.Status != PromptStatus.OK)
                return false;

            baseSide = result.Value;
            return true;
        }

        // mirror = true nếu chọn Phải
        private bool TryGetMirror(out bool mirror)
        {
            mirror = false;
            ReleaseMouse();

            PromptKeywordOptions options = new PromptKeywordOptions("")
            {
                AllowNone = true
            };
            options.SetMessageAndKeywords(
                Lang.B3_UI_ChonBenDatCanhDay,
                $"{Lang.B3_UI_KeywordTrai} {Lang.B3_UI_KeywordPhai}");

            PromptResult result = _ed.GetKeywords(options);

            if (result.Status == PromptStatus.None)
                return true;                  // Enter: mặc định Trái

            if (result.Status != PromptStatus.OK)
                return false;

            mirror = result.StringResult == Lang.B3_UI_KeywordPhai;
            return true;
        }

        private bool TryPlaceWithJig(ShoeModel model)
        {
            ShoePlacementJig jig = new ShoePlacementJig(model, _ucsToWcs);

            PromptResult dragResult;
            do
            {
                dragResult = _ed.Drag(jig);
            }
            while (jig.FlipRequested);       // F: đảo bên rồi ướm lại

            if (dragResult.Status != PromptStatus.OK)
                return false;

            model.Direction = jig.Direction;
            model.Mirror = jig.Mirror;
            return true;
        }

        private void Finish(ShoeModel model)
        {
            _ed.Regen();
            _ed.WriteMessage(string.Format(
                Lang.B3_MessDialog_DaVeGotGiay,
                model.Height.ToString("0.##"),
                model.SmallSide.ToString("0.##"),
                model.BaseSide.ToString("0.##")));
        }
    }
}