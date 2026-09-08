using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using Tools.Model;
using Tools.View.UI;
using Tools.VinaCad.Helper.Helper;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCAD.Action.Actions
{
    public sealed class DoorStylePickerAction
    {
        public DoorStyleSelection? Execute(DoorStyleSelection? initialSelection = null)
        {
            DoorStyleCatalog catalog = DoorStyleCatalogLoader.LoadDefault();
            DoorStylePickerWindow styleWindow = new DoorStylePickerWindow(catalog, initialSelection?.Style);
            Application.ShowModalWindow(styleWindow);
            if (styleWindow.DialogResult != true || styleWindow.SelectedStyle == null) return null;

            return ShowParameters(styleWindow.SelectedStyle, initialSelection, catalog.DefaultWallThickness);
        }

        public DoorStyleSelection? EditParameters(DoorStyleSelection selection)
        {
            return ShowParameters(selection.Style, selection, selection.WallThickness);
        }

        private static DoorStyleSelection? ShowParameters(
            DoorStyleModel style,
            DoorStyleSelection? initialSelection,
            double defaultWallThickness)
        {
            DoorOpeningSizeWindow window = new DoorOpeningSizeWindow(
                initialSelection,
                style.DefaultWidth,
                style.DefaultHeight,
                defaultWallThickness > 0 ? defaultWallThickness : 200);
            Application.ShowModalWindow(window);
            if (window.DialogResult != true) return null;

            DoorStyleSelection result = new DoorStyleSelection
            {
                Style = style,
                Width = window.HoleWidth,
                Height = window.DoorHeight,
                WallThickness = window.WallThickness,
                UseEdgeDistance = !window.PlaceAtWallCenter,
                EdgeDistance = window.PierWidth,
                PlaceAtWallCenter = window.PlaceAtWallCenter,
                ReverseAlongWall = window.ReverseAlongWall,
                MirrorAcrossWall = window.MirrorAcrossWall,
                HingeSide = initialSelection?.HingeSide ?? HingeSide.Left,
                OpeningDirection = initialSelection?.OpeningDirection ?? OpeningDirection.Inside
            };

            Document? document = Application.DocumentManager.MdiActiveDocument;
            Editor? editor = document?.Editor;
            editor?.WriteMessage(
                $"\nĐã chọn {result.Style.DisplayName}: W={result.Width:0.##}, H={result.Height:0.##}, " +
                $"T={result.WallThickness:0.##} mm, " +
                (result.PlaceAtWallCenter ? "đặt giữa tường" : $"Pier={result.EdgeDistance:0.##}") +
                (result.ReverseAlongWall ? ", đảo trái/phải" : string.Empty) +
                (result.MirrorAcrossWall ? ", lật vào/ra" : string.Empty) + ".");
            return result;
        }
    }
}
