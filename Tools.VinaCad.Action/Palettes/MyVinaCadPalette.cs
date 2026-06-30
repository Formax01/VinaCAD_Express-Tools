//using Prima.VinaCAD.ApplicationServices;
//using System;
//using System.Drawing;
//using Tools.View.UI;

//namespace Tools.VinaCad.Action.Palettes
//{
//    public static class MyVinaCadPalette
//    {
//        private static PaletteSet _paletteSet;

//        public static void Show()
//        {
//            if (_paletteSet == null)
//            {
//                CreatePalette();
//            }

//            _paletteSet.DockEnabled = DockSides.Left | DockSides.Right;
//            _paletteSet.Dock = DockSides.Left;

//            _paletteSet.SetAnchored(true);

//            _paletteSet.Show();
//            _paletteSet.Visible = true;

//            _paletteSet.Dock = DockSides.Left;
//            _paletteSet.SetAnchored(true);

//            _paletteSet.Activate();
//            _paletteSet.Focus();
//        }

//        public static void Hide()
//        {
//            if (_paletteSet != null)
//            {
//                _paletteSet.Hide();
//            }
//        }

//        private static void CreatePalette()
//        {
//            _paletteSet = new PaletteSet(
     
//                "XLDM Tools",
//                "XLDM_TOOLS_PALETTE",
//                new Guid("B0B6FCD4-4CC2-44D6-88B2-AB4F9D451001"));

//            _paletteSet.Size = new Size(370, 500);

//            _paletteSet.DockEnabled = DockSides.Left | DockSides.Right;

//            // Vị trí dock mặc định
//            _paletteSet.Dock = DockSides.Left;

//            _paletteSet.Style = PaletteSetStyles.ShowCloseButton;

//            _paletteSet.Content = new XLDMPaneView();

//            _paletteSet.Closing += (s, e) =>
//            {
//                e.Cancel = true;
//                _paletteSet.Hide();
//            };
//        }
//    }
//}