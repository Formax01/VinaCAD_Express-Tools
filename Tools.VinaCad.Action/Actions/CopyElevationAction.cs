using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using System;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.VinaCad.Helper.Helper;
using Application = Prima.VinaCAD.ApplicationServices.Application;
using MessageBox = System.Windows.MessageBox;

namespace Tools.VinaCAD.Action.Actions
{
    public class CopyElevationAction
    {
        private Document? _document;
        private Editor? _editor;
        private Database? _database;

        public void Execute()
        {
            try
            {
                _document = Application.DocumentManager.MdiActiveDocument;
                _editor = _document?.Editor;
                _database = _document?.Database;

                if (_editor == null || _database == null)
                    throw new Exception("Không có tài liệu hoạt động");

                ObjectId sourceBlockId = GetSourceBlock(out ObjectId attributeId);
                if (sourceBlockId == ObjectId.Null)
                {
                    _editor.WriteMessage("\nĐối tượng chọn không phải block cao độ hợp lệ.");
                    return;
                }

                Point3d oldPosition = GetBlockPosition(sourceBlockId);
                string oldText = GetAttributeText(attributeId);

                if (!ElevationBlockHelper.TryParseElevationText(oldText, out string prefix, out double oldElevation))
                {
                    _editor.WriteMessage($"\nKhông đọc được giá trị cao độ từ \"{oldText}\".");
                    return;
                }

                double unitToMeter = ElevationBlockHelper.GetUnitToMeterFactor(_database);

                _editor.WriteMessage($"\nCao độ gốc: {oldText}. Chọn điểm đặt mới, Enter để kết thúc.");

                while (true)
                {
                    PromptPointOptions ppo = new PromptPointOptions("\nĐiểm đặt mới <Kết thúc>: ")
                    {
                        BasePoint = oldPosition,
                        UseBasePoint = true
                    };

                    PromptPointResult pr = _editor.GetPoint(ppo);
                    if (pr.Status != PromptStatus.OK)
                        break;

                    Point3d newPosition = pr.Value;

                    double deltaY_drawingUnit = newPosition.Y - oldPosition.Y;
                    double deltaY_meter = deltaY_drawingUnit * unitToMeter;
                    double newElevation = oldElevation + deltaY_meter;

                    string newText = ElevationBlockHelper.BuildElevationText(prefix, newElevation);

                    Vector3d offset = newPosition - oldPosition;
                    ObjectId newBlockId = ElevationBlockHelper.CloneBlockWithOffset(_database, sourceBlockId, offset);
                    ElevationBlockHelper.SetSingleAttributeValue(_database, newBlockId, newText);

                    _editor.WriteMessage($"\nĐã đặt block mới — {newText}");
                }

                _editor.UpdateScreen();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi: {ex.Message}");
            }
        }

        private ObjectId GetSourceBlock(out ObjectId attributeId)
        {
            attributeId = ObjectId.Null;

            PromptSelectionResult psr = _editor!.SelectImplied();
            ObjectId candidateId;

            if (psr.Status == PromptStatus.OK && psr.Value.Count > 0)
            {
                _editor.SetImpliedSelection(Array.Empty<ObjectId>());
                candidateId = psr.Value[0].ObjectId;
            }
            else
            {
                PromptEntityOptions peo = new PromptEntityOptions("\nChọn block cao độ: ");
                peo.SetRejectMessage("\nChỉ được chọn block.");
                peo.AddAllowedClass(typeof(BlockReference), true);

                PromptEntityResult per = _editor.GetEntity(peo);
                if (per.Status != PromptStatus.OK)
                    return ObjectId.Null;

                candidateId = per.ObjectId;
            }

            if (!ElevationBlockHelper.IsCaoDoBlock(_database!, candidateId, out attributeId))
                return ObjectId.Null;

            return candidateId;
        }

        private Point3d GetBlockPosition(ObjectId blockId)
        {
            using (Transaction tr = _database!.TransactionManager.StartTransaction())
            {
                BlockReference blockRef = (BlockReference)tr.GetObject(blockId, OpenMode.ForRead);
                Point3d pos = blockRef.Position;
                tr.Commit();
                return pos;
            }
        }

        private string GetAttributeText(ObjectId attributeId)
        {
            using (Transaction tr = _database!.TransactionManager.StartTransaction())
            {
                AttributeReference attRef = (AttributeReference)tr.GetObject(attributeId, OpenMode.ForRead);
                string text = attRef.TextString;
                tr.Commit();
                return text;
            }
        }
    }
}