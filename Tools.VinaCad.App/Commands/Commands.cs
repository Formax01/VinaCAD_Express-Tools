using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.GraphicsInterface;
using Teigha.Runtime;
using Application = Prima.VinaCAD.ApplicationServices.Application;
using System.Windows;
using PrLogTrackingSystem;
using Tools.AutoCad.Action.Actions;
using Tools.Resources.Definitions;
using Tools.VinaCad.Action.Actions;

namespace Tools.AutoCad.App.Commands
{
    public class Commands
    {

        [CommandMethod("Sample")]
        public void SampleCommand()
        {
            try
            {
                SampleAction action = new SampleAction();
                action.Execute();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(ex.Message, StringDefinition.TITLE_ERROR);
                Logger.Info(nameof(SampleCommand), ex);
            }
        }
        [CommandMethod("FDT")]
        public void FindTextCommand()
        {
            try
            {
                TimTextAction action = new TimTextAction();
                action.Execute();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(ex.Message, StringDefinition.TITLE_ERROR);
                Logger.Info(nameof(FindTextCommand), ex);
            }
        }

        [CommandMethod("XLDM")]
        public void ConfigureDrawingListActionCommand()
        {
            try
            {
                XLDMAction action = new XLDMAction();
                action.Execute();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(ex.Message, StringDefinition.TITLE_ERROR);
                Logger.Info(nameof(ConfigureDrawingListActionCommand), ex);
            }
        }

        [CommandMethod("XLSDM")]
        public void XLSDMCommand()
        {
            try
            {
                XLSDMAction action = new XLSDMAction();
                action.Execute();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(ex.Message, StringDefinition.TITLE_ERROR);
                Logger.Info(nameof(XLSDMCommand), ex);
            }
        }

        [CommandMethod("DSHBV")]
        public void NumberingDrawingsCommand()
        {
            try
            {
                NumberingDrawingsAction action = new NumberingDrawingsAction();
                action.Execute();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(ex.Message, StringDefinition.TITLE_ERROR);
                Logger.Info(nameof(NumberingDrawingsCommand), ex);
            }
        }

        [CommandMethod("VBDM")]
        public void DrawingCategoriesCommand()
        {
            try
            {
                DrawingCategoriesAction action = new DrawingCategoriesAction();
                action.Execute();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(ex.Message, StringDefinition.TITLE_ERROR);
                Logger.Info(nameof(DrawingCategoriesCommand), ex);
            }
        }

        [CommandMethod("SDM")]
        public void SDMCommand()
        {
            try
            {
                SDMAction action = new SDMAction();
                action.Execute();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(ex.Message, StringDefinition.TITLE_ERROR);
                Logger.Info(nameof(SDMCommand), ex);
            }
        }

        [CommandMethod("XLCK")]
        public void XLCKCommand()
        {
            try
            {
                XLTKCKAction action = new XLTKCKAction();
                action.Execute();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(ex.Message, StringDefinition.TITLE_ERROR);
                Logger.Info(nameof(XLCKCommand), ex);
            }
        }

        [CommandMethod("TKCK")]
        public void TKCKCommand()
        {
            try
            {
                TKCKAction action = new TKCKAction();
                action.Execute();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(ex.Message, StringDefinition.TITLE_ERROR);
                Logger.Info(nameof(TKCKCommand), ex);
            }
        }

        [CommandMethod("BB")]
        public void TaoBlockCommand()
        {
            try
            {
                TaoBlockAction action = new TaoBlockAction();
                action.Execute();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(ex.Message, StringDefinition.TITLE_ERROR);
                Logger.Info(nameof(TaoBlockCommand), ex);
            }
        }

        [CommandMethod("TDC")]
        public void TaoDoCocCommand()
        {
            try
            {
                ToaDoCocAction action = new ToaDoCocAction();
                action.Execute();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(ex.Message, StringDefinition.TITLE_ERROR);
                Logger.Info(nameof(TaoDoCocCommand), ex);
            }
        }

        [CommandMethod("VC")]
        public void VeCocCommand()
        {
            try
            {
                VeCocAction action = new VeCocAction();
                action.Execute();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(ex.Message, StringDefinition.TITLE_ERROR);
                Logger.Info(nameof(VeCocCommand), ex);
            }
        }

        [CommandMethod("XLTKCT")]
        public void QXLTKCTCommand()
        {
            try
            {
                XLTKCTAction action = new XLTKCTAction();
                action.Execute();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(ex.Message, StringDefinition.TITLE_ERROR);
                Logger.Info(nameof(QXLTKCTCommand), ex);
            }
        }

        [CommandMethod("QS")]
        public void EDITTKCTCommand()
        {
            try
            {
                EditThongKeCotThepAction action = new EditThongKeCotThepAction();
                action.Execute();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(ex.Message, StringDefinition.TITLE_ERROR);
                Logger.Info(nameof(EDITTKCTCommand), ex);
            }
        }

        [CommandMethod("QD")]
        public void TKLTKCTHCommand()
        {
            try
            {
                EditThongKeCotThepHinhAction action = new EditThongKeCotThepHinhAction();
                action.Execute();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(ex.Message, StringDefinition.TITLE_ERROR);
                Logger.Info(nameof(TKLTKCTHCommand), ex);
            }
        }

        [CommandMethod("QF")]
        public void TKLTKCTCommand()
        {
            try
            {
                TongKhoiLuongThepAction action = new TongKhoiLuongThepAction();
                action.Execute();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(ex.Message, StringDefinition.TITLE_ERROR);
                Logger.Info(nameof(TKLTKCTCommand), ex);
            }
        }

        [CommandMethod("TS")]
        public void TKTSCommand()
        {
            try
            {
                DanhSoHieuTKTSAction action = new DanhSoHieuTKTSAction();
                action.Execute();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(ex.Message, StringDefinition.TITLE_ERROR);
                Logger.Info(nameof(TKTSCommand), ex);
            }
        }

        [CommandMethod("TD")]
        public void TKTDCommand()
        {
            try
            {
                DanhSoHieuTKTDAction action = new DanhSoHieuTKTDAction();
                action.Execute(); 
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(ex.Message, StringDefinition.TITLE_ERROR);
                Logger.Info(nameof(TKTDCommand), ex);
            }
        }

        [CommandMethod("DTT")]
        public void BangTraDTCTCommand()
        {
            try
            {
                BangTraDTCTAction action = new BangTraDTCTAction();
                action.Execute();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(ex.Message, StringDefinition.TITLE_ERROR);
                Logger.Info(nameof(BangTraDTCTCommand), ex);
            }
        }

        [CommandMethod("BTCNN")]
        public void BangTieuChuanNeoNoiCTCommand()
        {
            try
            {
                BangTieuChuanNeoNoiCTAction action = new BangTieuChuanNeoNoiCTAction();
                action.Execute();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(ex.Message, StringDefinition.TITLE_ERROR);
                Logger.Info(nameof(BangTieuChuanNeoNoiCTCommand), ex);
            }
        }

        [CommandMethod("BT")]
        public void BangTraCuongDoBTCommand()
        {
            try
            {
                BangTraBeTongAction action = new BangTraBeTongAction();
                action.Execute();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(ex.Message, StringDefinition.TITLE_ERROR);
                Logger.Info(nameof(BangTraCuongDoBTCommand), ex);
            }
        }
        [CommandMethod("NEO")]
        public void BangTraNeoCTCommand()
        {
            try
            {
                BangTraNeoCTAction action = new BangTraNeoCTAction();
                action.Execute();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(ex.Message, StringDefinition.TITLE_ERROR);
                Logger.Info(nameof(BangTraCuongDoBTCommand), ex);
            }
        }

        [CommandMethod("NOI")]
        public void BangTraNoiCTCommand()
        {
            try
            {
                BangTraNoiCTAction action = new BangTraNoiCTAction();
                action.Execute();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(ex.Message, StringDefinition.TITLE_ERROR);
                Logger.Info(nameof(BangTraCuongDoBTCommand), ex);
            }
        }
        [CommandMethod("KTD")]
        public void KiemTraTrungDimCommand()
        {
            try
            {
                KiemTraTrungDimAction action = new KiemTraTrungDimAction();
                action.Execute();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(ex.Message, StringDefinition.TITLE_ERROR);
                Logger.Info(nameof(KiemTraTrungDimCommand), ex);
            }
        }

        [CommandMethod("XLDCD")]
        public void XLKiemTraCongDonDimCommand()
        {
            try
            {
                SettingKiemTraDimCongDonAction action = new SettingKiemTraDimCongDonAction();
                action.Execute();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(ex.Message, StringDefinition.TITLE_ERROR);
                Logger.Info(nameof(XLKiemTraCongDonDimCommand), ex);
            }
        }


        [CommandMethod("DCD")]
        public void KiemTraCongDonDimCommand()
        {
            try
            {
                KiemTraCongDonDimAction action = new KiemTraCongDonDimAction();
                action.Execute();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(ex.Message, StringDefinition.TITLE_ERROR);
                Logger.Info(nameof(KiemTraCongDonDimCommand), ex);
            }
        }

        [CommandMethod("RenameText")]
        public void RenameTextCommand()
        {
            try
            {
                RenameTextAction action = new RenameTextAction();
                action.Execute();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(ex.Message, StringDefinition.TITLE_ERROR);
                Logger.Info(nameof(RenameTextCommand), ex);
            }
        }

        [CommandMethod("RenameLayer")]
        public void RenameLayerCommand()
        {
            try
            {
                RenameLayerAction action = new RenameLayerAction();
                action.Execute();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(ex.Message, StringDefinition.TITLE_ERROR);
                Logger.Info(nameof(RenameLayerCommand), ex);
            }
        }

        [CommandMethod("ABOUTEXPRESS")]
        public void AboutToolsCommand()
        {
            try
            {
                AboutExpressToolAction action = new AboutExpressToolAction();
                action.Execute();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(ex.Message, StringDefinition.TITLE_ERROR);
                Logger.Info(nameof(AboutToolsCommand), ex);
            }
        }

        [CommandMethod("IPT")]
        public void ImportallCommand()
        {
            try
            {
                ImportAllFromTemplatesAction action = new ImportAllFromTemplatesAction();
                action.Execute();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(ex.Message, StringDefinition.TITLE_ERROR);
                Logger.Info(nameof(AboutToolsCommand), ex);
            }
        }
    }
}
