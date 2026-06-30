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
using Tools.Model;
using Tools.Resources.Definitions;
using Tools.View.UI;
using Tools.ViewModel;
using Tools.VinaCad.Helper.Helper;
using Tools.VinaCad.Modeling;
using MessageBox = System.Windows.MessageBox;

namespace Tools.VinaCad.Action.Actions
{
    public class TaoBlockAction
    {
        private TaoBlockVM _TaoBlockVM;
        private TaoBlockWindow _TaoBlockView;
        private Document _doc;
        private Database _db;
        private Editor _ed;

        private void UpdateCurrentDocument()
        {
            _doc = Prima.VinaCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;

            if (_doc == null)
                return;

            _db = _doc.Database;
            _ed = _doc.Editor;
        }


        public TaoBlockAction()
        {
            _TaoBlockVM = new TaoBlockVM()
            {
                TaoBlockCmd = new RelayCommand(TaoBlockInvoke),
                CancelCmd = new RelayCommand(CancelInvoke)
            };
            _TaoBlockView = new TaoBlockWindow() { DataContext = _TaoBlockVM };
        }

        public void Execute()
        {
            try
            {
                UpdateCurrentDocument();
                ObjectId? blockId = BlockHelper.PickBlock(_ed);
                if (blockId == null)
                    return;
                _TaoBlockVM.SelectedBlockName = BlockHelper.GetBlockName(_db,blockId.Value);
                _TaoBlockView.ShowDialog();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(TaoBlockAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }
        private void TaoBlockInvoke()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_TaoBlockVM.NewBlockName))
                {
                    _TaoBlockView.Hide();
                    _ed.SetImpliedSelection(Array.Empty<ObjectId>());
                    _ed.UpdateScreen();

                    List<ObjectId> blockIds = BlockHelper.PickBlocksByBlockName(_db,_ed,_TaoBlockVM.SelectedBlockName);
                    if (blockIds == null || blockIds.Count == 0)
                    {
                        MessageBox.Show("Không tìm thấy block cần copy.", StringDefinition.TITLE_MESSAGE);
                        return;
                    }
                    var count =  BlockHelper.CloneBlocksWithNewName(_db, blockIds,_TaoBlockVM.NewBlockName); 
                    if(count>0)
                    {
                        _ed.WriteMessage($"\nĐã tạo {count} block mới với tên \"{_TaoBlockVM.NewBlockName}\".");
                    }    
                    _TaoBlockView.Close();
                }
                else
                {
                    MessageBox.Show("Bạn cần nhập tên block mới.", StringDefinition.TITLE_MESSAGE);
                }

            }
            catch (Exception ex)
            {
                Logger.Info(nameof(TaoBlockAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }
        private void CancelInvoke()
        {
            try
            {
                _TaoBlockView.Close();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(TaoBlockAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }
    }
}
