using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using PrLogTrackingSystem;
using PrMVVMCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
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
    public class XLTKCKAction
    {
        private XLTKCKVM _XLTKCKVM;
        private XLTKCKWindow _XLTKCKView;
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

        public XLTKCKAction()
        {
            _XLTKCKVM = new XLTKCKVM()
            {
                OkCmd = new RelayCommand(OkInvoke),
                CancelCmd = new RelayCommand(CancelInvoke),
                PickTextCmd = new RelayCommand(ChoseTextInvoke)
            };
            _XLTKCKView = new XLTKCKWindow() { DataContext = _XLTKCKVM };
        }

        public void Execute()
        {
            try
            {
                UpdateCurrentDocument();
                _XLTKCKView.ShowDialog();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(XLTKCKAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }
        private void ChoseTextInvoke()
        {
            try
            {
                _XLTKCKView.Hide();

                ObjectId? blockId =PickText();

                if (blockId == null)
                    return;
                Dictionary<string, string> tprs = TextHelper.GetTextProperties(_db,blockId.Value);
                _XLTKCKVM.TextHeight = tprs[TextProperTiesName.Height];
                _XLTKCKVM.TextLayer = tprs[TextProperTiesName.Layer];

                blockIdSeleted = blockId.Value;

                _XLTKCKView.Show();
                _XLTKCKView.Activate();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(XLTKCKAction), ex);
                throw new Exception(ex.Message, ex);
            }

        }
        private void CancelInvoke()
        {
            try
            {
                _XLTKCKView.Close();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(XLTKCKAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }
        private void OkInvoke()
        {
            try
            {
                if (blockIdSeleted != ObjectId.Null)
                {
                  

                    XLTKCKSetting.TextHeight = _XLTKCKVM.TextHeight;
                    XLTKCKSetting.TextLayer = _XLTKCKVM.TextLayer;
                    XLTKCKSetting.CheckDistance = _XLTKCKVM.CheckDistance;
                    //XLTKCKSetting.Scale = _XLTKCKVM.Scale;
                    XLTKCKSetting.Scale = int.Parse(_XLTKCKVM.Scale.Split('/')[1]);
                    _XLTKCKView.Close();
                }
                else
                {
                    MessageBox.Show("Vui lòng chọn text của cấu kiện cần thống kê.", StringDefinition.TITLE_MESSAGE);
                }

            }
            catch (Exception ex)
            {
                Logger.Info(nameof(XLTKCKAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }

        public ObjectId? PickText()
        {
            TypedValue[] filterValues =
                            {
                                new TypedValue((int)DxfCode.Start, "TEXT,MTEXT")
                            };

            SelectionFilter filter = new SelectionFilter(filterValues);

            PromptSelectionOptions opt = new PromptSelectionOptions();
            opt.MessageForAdding = "\nChọn text: ";
            opt.SingleOnly = true;
            opt.RejectObjectsOnLockedLayers = true;

            PromptSelectionResult result = _ed.GetSelection(opt, filter);

            if (result.Status != PromptStatus.OK ||
                result.Value == null ||
                result.Value.Count == 0)
            {
                return null;
            }

            return result.Value[0].ObjectId;
        }
    }
}
