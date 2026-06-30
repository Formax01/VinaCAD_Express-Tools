using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using PrLogTrackingSystem;
using PrMVVMCore;
using System.Windows;
using Teigha.DatabaseServices;
using Tools.Resources.Definitions;
using Tools.View.UI;
using Tools.ViewModel;
using Tools.VinaCad.Modeling;
namespace Tools.VinaCad.Action.Actions
{
    public class XLTKCTAction
    {
        private XLTKCTVM _XLTKCTVM;
        private XLTKCTWindow _XLTKCTView;

        private Document _doc;
        private Database _db;
        private Editor _ed;
        ObjectId blockIdSeleted = ObjectId.Null;
        private void UpdateCurrentDocument()
        {
            _doc = Prima.VinaCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;

            if (_doc == null)
                return;

            _db = _doc.Database;
            _ed = _doc.Editor;
        }

        public XLTKCTAction()
        {
            _XLTKCTVM = new XLTKCTVM()
            {
                XacLapCmd = new RelayCommand(XacLapInvoke),
                CancelCmd = new RelayCommand(CancelInvoke),
            };
            _XLTKCTView = new XLTKCTWindow() { DataContext = _XLTKCTVM };
        }
        public void Execute()
        {
            try
            {
                UpdateCurrentDocument();
                _XLTKCTView.ShowDialog();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(XLTKCTAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }
        private void CancelInvoke()
        {
            try
            {
                _XLTKCTView.Close();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(XLTKCTAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }
        private void XacLapInvoke()
        {
            try
            {
              
                    XLTKCTSetting.LapD10 = _XLTKCTVM.LapD10;
                    XLTKCTSetting.LapD10D16 = _XLTKCTVM.LapD10D16;
                    XLTKCTSetting.LapD16 = _XLTKCTVM.LapD16;
                    XLTKCTSetting.DulThem = _XLTKCTVM.DulThem;
                    XLTKCTSetting.HookEarthquake = _XLTKCTVM.HookEarthquake;
                    XLTKCTSetting.HookNormal = _XLTKCTVM.HookNormal;
                    XLTKCTSetting.IsDongDat = _XLTKCTVM.IsDongDat;
                    XLTKCTSetting.IsNotDongDat = _XLTKCTVM.IsNotDongDat;
                   
                    _XLTKCTView.Close();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(XLTKCTAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }
    }
}
