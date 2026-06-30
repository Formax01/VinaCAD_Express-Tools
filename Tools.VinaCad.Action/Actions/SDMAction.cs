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
using Tools.Model;
using Tools.Resources.Definitions;
using Tools.View.UI;
using Tools.ViewModel;
using Tools.VinaCad.Helper.Helper;
using Tools.VinaCad.Modeling;

namespace Tools.VinaCad.Action.Actions
{
    public class SDMAction
    {
        private SDMVM _SDMVM;
        private SDMWindow _SDMView;

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

        public SDMAction()
        {
            _SDMVM = new SDMVM()
            {
                UpdateCmd = new RelayCommand(UpdateInvoke),
                CancelCmd = new RelayCommand(CancelInvoke)
            };
            _SDMView = new SDMWindow() { DataContext = _SDMVM };
        }
        public void Execute()
        {
            try
            {
                UpdateCurrentDocument();
                _SDMView.ShowDialog();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(SDMAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }

        private void UpdateInvoke()
        {
            try
            {
                List<SDMModel> sdms = _SDMVM.CauKienItems.ToList();

                if (!HasValidData(sdms))
                {
                    MessageBox.Show("Vui lòng copy dữ liệu vào bảng.", StringDefinition.TITLE_MESSAGE);
                    return;
                }

                if (string.IsNullOrWhiteSpace(XLSDMSetting.BlockName))
                {
                    MessageBox.Show("Vui lòng chọn tên block.", StringDefinition.TITLE_MESSAGE);
                    return;
                }
                _SDMView.Hide();
                List<ObjectId> blockIds = BlockHelper.PickBlocksByBlockName(_db,_ed,XLSDMSetting.BlockName);

                if (blockIds == null || blockIds.Count == 0)
                {
                    MessageBox.Show("Không tìm thấy block phù hợp.", StringDefinition.TITLE_MESSAGE);
                    return;
                }

                if (sdms.Count > blockIds.Count)
                {
                    MessageBox.Show($"Vui lòng copy bảng thống kê danh mục thêm {sdms.Count - blockIds.Count} dòng",StringDefinition.TITLE_MESSAGE);
                    return;
                }

                if (XLSDMSetting.IsTopToBottom)
                {
                    blockIds = BlockHelper.SortBlocksTopToBottomLeftToRight(_db,blockIds,XLSDMSetting.Tolerance
                    );
                }
                else if (XLSDMSetting.IsLeftToRight)
                {
                    blockIds = BlockHelper.SortBlocksLeftToRightTopToBottom( _db, blockIds, XLSDMSetting.Tolerance );
                }

                UpdateSDMToBlockAttributes(blockIds, sdms);
                _SDMView.Close();
                MessageBox.Show("Cập nhật bảng thống kê thành công.", StringDefinition.TITLE_MESSAGE);
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(SDMAction), ex);
                throw;
            }
        }

        private void CancelInvoke()
        {
            try
            {
                _SDMView.Close();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(SDMAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }

        private bool HasValidData(List<SDMModel> veCocs)
        {
            if (veCocs == null || veCocs.Count == 0)
                return false;

            if (veCocs.Count == 1)
            {
                SDMModel item = veCocs[0];

                if (string.IsNullOrWhiteSpace(item.TBV) && string.IsNullOrWhiteSpace(item.SH))
                {
                    return false;
                }
            }

            return true;
        }

        private void UpdateSDMToBlockAttributes(List<ObjectId> blockIds, List<SDMModel> sdms)
        {
           
            using (Transaction tr = _db.TransactionManager.StartTransaction())
            {
                for (int i = 0; i < sdms.Count; i++)
                {
                    ObjectId blockId = blockIds[i];
                    SDMModel sdm = sdms[i];

                    BlockReference blockRef = tr.GetObject(blockId, OpenMode.ForWrite) as BlockReference;

                    if (blockRef == null)
                        continue;

                    // Thay các TAG bên dưới bằng đúng Tag Attribute trong block của bạn.
                    SetAttributeValue(tr, blockRef, XLSDMSetting.DrawingTitle1Tag.ToString(), sdm.STT);
                    SetAttributeValue(tr, blockRef, XLSDMSetting.DrawingTitle2Tag.ToString(), sdm.TBV);
                    SetAttributeValue(tr, blockRef, XLSDMSetting.DrawingTitle3Tag.ToString(), sdm.SH);

                    blockRef.RecordGraphicsModified(true);
                }

                tr.Commit();
            }

            _doc.Editor.Regen();
        }
        private void SetAttributeValue(Transaction tr,BlockReference blockRef, string tag, string value)
        {
            if (blockRef.AttributeCollection == null)
                return;

            foreach (ObjectId attId in blockRef.AttributeCollection)
            {
                AttributeReference attRef =tr.GetObject(attId, OpenMode.ForWrite) as AttributeReference;

                if (attRef == null)
                    continue;

                if (string.Equals(attRef.Tag, tag, StringComparison.OrdinalIgnoreCase))
                {
                    attRef.TextString = value ?? string.Empty;
                    return;
                }
            }
        }
    }
}
