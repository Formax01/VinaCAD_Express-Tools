using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using PrLogTrackingSystem;
using System;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.Resources.Language;
using Tools.VinaCad.Helper.Helper;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCAD.Action.Actions
{
    public class CopyElevationAction
    {
        private Document _doc = null!;
        private Editor _ed = null!;
        private Database _db = null!;
        private Matrix3d _ucs;

        public void Execute()
        {
            try
            {
                UpdateCurrentDocument();
                if (_doc == null)
                    throw new Exception(Lang.CPD_MessDialog_KhongCoTaiLieuHoatDong);

                // Bước 1: chọn block cao độ nguồn
                ObjectId sourceBlockId = GetSourceBlock(out ObjectId attributeId);
                if (sourceBlockId == ObjectId.Null)
                {
                    _ed.WriteMessage(Lang.CPD_MessDialog_DoiTuongChonKhongPhaiBlockCaoDoHopLe);
                    return;
                }

                // Bước 2: đọc vị trí và giá trị cao độ hiện tại
                Point3d oldPosition = GetBlockPosition(sourceBlockId);
                string oldText = GetAttributeText(attributeId);

                if (!ElevationBlockHelper.TryParseElevationText(
                        oldText, out string prefix, out double oldElevation))
                {
                    _ed.WriteMessage(string.Format(
                        Lang.CPD_MessDialog_KhongDocDuocGiaTriCaoDoTuOldText, oldText));
                    return;
                }

                double unitToMeter = ElevationBlockHelper.GetUnitToMeterFactor(_db);

                // Bước 3: đặt các bản sao cho đến khi Enter / Esc
                _ed.WriteMessage(Lang.CPD_MessDialog_ChonDiemDatMoiEnterDeKetThuc);
                PlaceCopies(sourceBlockId, oldPosition, prefix, oldElevation, unitToMeter);

                _ed.UpdateScreen();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(CopyElevationAction), ex);
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
            _ucs = _ed.CurrentUserCoordinateSystem;
        }

        // ---------- Đặt các bản sao ----------

        private void PlaceCopies(
            ObjectId sourceBlockId, Point3d oldPosition,
            string prefix, double oldElevation, double unitToMeter)
        {
            while (TryGetNewPosition(oldPosition, out Point3d newPosition))
            {
                double deltaMeter = (newPosition.Y - oldPosition.Y) * unitToMeter;
                string newText = ElevationBlockHelper.BuildElevationText(
                    prefix, oldElevation + deltaMeter);

                Vector3d offset = newPosition - oldPosition;
                ObjectId newBlockId = ElevationBlockHelper.CloneBlockWithOffset(
                    _db, sourceBlockId, offset);
                ElevationBlockHelper.SetSingleAttributeValue(_db, newBlockId, newText);

                _ed.WriteMessage(string.Format(Lang.CPD_MessDialog_DaDatBlockMoiNewText, newText));
            }
        }

        // Chọn điểm đặt mới; Enter hoặc Esc thì trả về false để kết thúc
        private bool TryGetNewPosition(Point3d oldPosition, out Point3d newPosition)
        {
            PromptPointOptions options = new PromptPointOptions(Lang.CPD_UI_DiemDatMoiKetThuc)
            {
                BasePoint = oldPosition.TransformBy(_ucs.Inverse()),   // BasePoint tính theo UCS
                UseBasePoint = true
            };

            PromptPointResult result = _ed.GetPoint(options);
            if (result.Status != PromptStatus.OK)
            {
                newPosition = default(Point3d);
                return false;
            }

            newPosition = result.Value.TransformBy(_ucs);              // đổi về WCS
            return true;
        }

        // ---------- Đọc dữ liệu từ bản vẽ ----------

        private ObjectId GetSourceBlock(out ObjectId attributeId)
        {
            attributeId = ObjectId.Null;

            PromptSelectionResult psr = _ed.SelectImplied();
            ObjectId candidateId;

            if (psr.Status == PromptStatus.OK && psr.Value.Count > 0)
            {
                _ed.SetImpliedSelection(Array.Empty<ObjectId>());
                candidateId = psr.Value[0].ObjectId;
            }
            else
            {
                PromptEntityOptions peo = new PromptEntityOptions(Lang.CPD_UI_ChonBlockCaoDo);
                peo.SetRejectMessage(Lang.CPD_UI_ChiDuocChonBlock);
                peo.AddAllowedClass(typeof(BlockReference), true);

                PromptEntityResult per = _ed.GetEntity(peo);
                if (per.Status != PromptStatus.OK)
                    return ObjectId.Null;

                candidateId = per.ObjectId;
            }

            if (!ElevationBlockHelper.IsCaoDoBlock(_db, candidateId, out attributeId))
                return ObjectId.Null;

            return candidateId;
        }

        private Point3d GetBlockPosition(ObjectId blockId)
        {
            using (Transaction tr = _db.TransactionManager.StartTransaction())
            {
                BlockReference blockRef = (BlockReference)tr.GetObject(blockId, OpenMode.ForRead);
                Point3d pos = blockRef.Position;
                tr.Commit();
                return pos;
            }
        }

        private string GetAttributeText(ObjectId attributeId)
        {
            using (Transaction tr = _db.TransactionManager.StartTransaction())
            {
                AttributeReference attRef = (AttributeReference)tr.GetObject(attributeId, OpenMode.ForRead);
                string text = attRef.TextString;
                tr.Commit();
                return text;
            }
        }
    }
}