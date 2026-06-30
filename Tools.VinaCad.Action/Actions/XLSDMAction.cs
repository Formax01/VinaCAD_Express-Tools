using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using PrLogTrackingSystem;
using PrMVVMCore;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teigha.DatabaseServices;
using Tools.AutoCad.Action.Actions;
using Tools.Resources.Definitions;
using Tools.View.UI;
using Tools.ViewModel;
using Tools.VinaCad.Helper.Helper;
using Tools.VinaCad.Modeling;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCad.Action.Actions
{
    public class XLSDMAction
    {
        private XLSDMVM _XLSDMVM;
        private XLSDMWindow _XLSDMView;
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

        public XLSDMAction()
        {
            _XLSDMVM = new XLSDMVM()
            {
                ChoseBlockCmd = new RelayCommand(ChoseBlockInvoke),
                OkCmd = new RelayCommand(OkInvoke),
                CancelCmd = new RelayCommand(CancelInvoke)
            };
            _XLSDMView = new XLSDMWindow() { DataContext = _XLSDMVM };
        }

        public void Execute()
        {
            try
            {
                UpdateCurrentDocument();
                LoadNumberingDrawingsSettingToViewModel();
                _XLSDMView.ShowDialog();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(SampleAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }
        private void ChoseBlockInvoke()
        {
            try
            {
                _XLSDMView.Hide();

                ObjectId? blockId = BlockHelper.PickBlock(_ed);

                if (blockId == null)
                    return;

                LoadAttributeItems(blockId.Value);
                _XLSDMVM.BlockName = BlockHelper.GetBlockName(_db,blockId.Value);
                blockIdSeleted = blockId.Value;

                _XLSDMView.Show();
                _XLSDMView.Activate();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(SampleAction), ex);
                throw new Exception(ex.Message, ex);
            }

        }

        private void OkInvoke()
        {
            try
            {
                if (blockIdSeleted != ObjectId.Null)
                {
                    Dictionary<string, string> atts = BlockHelper.GetAttributes(_db, blockIdSeleted);
                    XLSDMSetting.BlockName = _XLSDMVM.BlockName;
                    XLSDMSetting.DrawingTitle1Tag = atts.FirstOrDefault(x => x.Value == _XLSDMVM.TenBanVe1Selected?.ToString()).Key;
                    XLSDMSetting.DrawingTitle2Tag = atts.FirstOrDefault(x => x.Value == _XLSDMVM.TenBanVe2Selected?.ToString()).Key;
                    XLSDMSetting.DrawingTitle3Tag = atts.FirstOrDefault(x => x.Value == _XLSDMVM.TenBanVe3Selected?.ToString()).Key;
                    XLSDMSetting.Tolerance = _XLSDMVM.Tolerance;
                    XLSDMSetting.IsLeftToRight = _XLSDMVM.IsLeftToRight;
                    XLSDMSetting.IsTopToBottom = _XLSDMVM.IsTopToBottom;
                    _XLSDMView.Close();
                }

                else
                {
                    MessageBox.Show("Vui lòng chọn block khung tên.", StringDefinition.TITLE_MESSAGE);
                }

            }
            catch (Exception ex)
            {
                Logger.Info(nameof(SampleAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }

        private void CancelInvoke()
        {
            try
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                _XLSDMView.Close();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(SampleAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }
        private void LoadAttributeItems(ObjectId blockId)
        {


            Dictionary<string, string> atts = BlockHelper.GetAttributes(_db, blockId);

            ObservableCollection<object> items =
                new ObservableCollection<object>(atts.Values);

            _XLSDMVM.TenBanVe1Items = items;
            _XLSDMVM.TenBanVe2Items = items;
            _XLSDMVM.TenBanVe3Items = items;
        }
        private void LoadNumberingDrawingsSettingToViewModel()
        {
            if (XLSDMSetting.Tolerance > 0)
            {
                _XLSDMVM.Tolerance = XLSDMSetting.Tolerance;
            }

            _XLSDMVM.IsLeftToRight = XLSDMSetting.IsLeftToRight;
            _XLSDMVM.IsTopToBottom = XLSDMSetting.IsTopToBottom;
            if (XLSDMSetting.IsLeftToRight == false && XLSDMSetting.IsTopToBottom == false)
            {
                _XLSDMVM.IsTopToBottom = true;
            }
        }
    }
}
