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
using System.Windows;
using System.Windows.Documents;
using Teigha.DatabaseServices;
using Tools.AutoCad.Action.Actions;
using Tools.Resources.Definitions;
using Tools.View.UI;
using Tools.ViewModel;
using Tools.VinaCad.Helper.Helper;
using Tools.VinaCad.Modeling;
using Application = Prima.VinaCAD.ApplicationServices.Application;
using MessageBox = System.Windows.MessageBox;

namespace Tools.VinaCad.Action.Actions
{
    public class XLDMAction
    {
        private XLDMVM _numberingDrawingsVM;
        private XLDMWindow _numberingDrawingsView;
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
       
     
        public XLDMAction()
        {
            _numberingDrawingsVM = new XLDMVM()
            {
                ChoseBlockCmd = new RelayCommand(ChoseBlockInvoke),
                OkCmd = new RelayCommand(OkInvoke),
                CancelCmd = new RelayCommand(CancelInvoke)
            };
            _numberingDrawingsView = new XLDMWindow() { DataContext = _numberingDrawingsVM };
        }
        public void Execute()
        {
            try
            {
                UpdateCurrentDocument();
                LoadNumberingDrawingsSettingToViewModel();
                _numberingDrawingsView.ShowDialog();
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
                _numberingDrawingsView.Hide();

                ObjectId? blockId = BlockHelper.PickBlock(_ed);

                if (blockId == null)
                    return;

                LoadAttributeItems(blockId.Value);
                _numberingDrawingsVM.BlockName = BlockHelper.GetBlockName(_db, blockId.Value);
                 blockIdSeleted = blockId.Value;

                _numberingDrawingsView.Show();
                _numberingDrawingsView.Activate();
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
                    NumberingDrawingsSetting.BlockName = _numberingDrawingsVM.BlockName;
                    NumberingDrawingsSetting.DrawingNoTag = atts.FirstOrDefault(x => x.Value == _numberingDrawingsVM.KiHieuBanVeSelected?.ToString()).Key;
                    NumberingDrawingsSetting.DrawingTitle1Tag = atts.FirstOrDefault(x => x.Value == _numberingDrawingsVM.TenBanVe1Selected?.ToString()).Key; 
                    NumberingDrawingsSetting.DrawingTitle2Tag = atts.FirstOrDefault(x => x.Value == _numberingDrawingsVM.TenBanVe2Selected?.ToString()).Key;
                    NumberingDrawingsSetting.DrawingTitle3Tag = atts.FirstOrDefault(x => x.Value == _numberingDrawingsVM.TenBanVe3Selected?.ToString()).Key;
                    NumberingDrawingsSetting.Tolerance = _numberingDrawingsVM.Tolerance;
                    NumberingDrawingsSetting.IsLeftToRight = _numberingDrawingsVM.IsLeftToRight;
                    NumberingDrawingsSetting.IsTopToBottom = _numberingDrawingsVM.IsTopToBottom;
                    NumberingDrawingsSetting.Scale =  int.Parse(_numberingDrawingsVM.Scale.Split('/')[1]);
                    NumberingDrawingsSetting.Prefix = _numberingDrawingsVM.TienTo;
                    NumberingDrawingsSetting.Suffix = _numberingDrawingsVM.HauTo;
                    _numberingDrawingsView.Close();
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
                _numberingDrawingsView.Close();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(SampleAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }
        private void LoadAttributeItems(ObjectId blockId)
        {
          

            Dictionary<string, string> atts =BlockHelper.GetAttributes(_db, blockId);

            ObservableCollection<object> items =
                new ObservableCollection<object>(atts.Values);

            _numberingDrawingsVM.KiHieuBanVeItems = items;
            _numberingDrawingsVM.TenBanVe1Items = items;
            _numberingDrawingsVM.TenBanVe2Items = items;
            _numberingDrawingsVM.TenBanVe3Items = items;
        }
        private void LoadNumberingDrawingsSettingToViewModel()
        {
            if (NumberingDrawingsSetting.Tolerance > 0)
            {
                _numberingDrawingsVM.Tolerance = NumberingDrawingsSetting.Tolerance;
            }

            _numberingDrawingsVM.IsLeftToRight = NumberingDrawingsSetting.IsLeftToRight;
            _numberingDrawingsVM.IsTopToBottom = NumberingDrawingsSetting.IsTopToBottom;
            if(NumberingDrawingsSetting.IsLeftToRight == false && NumberingDrawingsSetting.IsTopToBottom == false)
            {
                _numberingDrawingsVM.IsLeftToRight = true;
            }    
            if (!string.IsNullOrWhiteSpace(NumberingDrawingsSetting.Prefix))
            {
                _numberingDrawingsVM.TienTo = NumberingDrawingsSetting.Prefix;
            }

            if (!string.IsNullOrWhiteSpace(NumberingDrawingsSetting.Suffix))
            {
                _numberingDrawingsVM.HauTo = NumberingDrawingsSetting.Suffix;
            }

            if (NumberingDrawingsSetting.Scale > 0)
            {
                _numberingDrawingsVM.Scale = $"1/{NumberingDrawingsSetting.Scale}";
            }
        }
    }
}
