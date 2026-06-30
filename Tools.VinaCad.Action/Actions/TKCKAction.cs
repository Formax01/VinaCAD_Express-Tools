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
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.Model;
using Tools.Resources.Definitions;
using Tools.View.UI;
using Tools.ViewModel;
using Tools.VinaCad.Helper.Helper;
using Tools.VinaCad.Modeling;
using Application = Prima.VinaCAD.ApplicationServices.Application;
using MessageBox = System.Windows.MessageBox;

namespace Tools.VinaCad.Action.Actions
{
    public class TKCKAction
    {
        private TKCKVM _TKCKVM;
        private TKCKWindow _TKCKView;

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
        public TKCKAction()
        {
            _TKCKVM = new TKCKVM()
            {
                DrawTableCmd = new RelayCommand(DrawTableInvoke),
                CancelCmd = new RelayCommand(CancelInvoke)
            };
            _TKCKView = new TKCKWindow() { DataContext = _TKCKVM };
        }
        public void Execute()
        {
            try
            {
                UpdateCurrentDocument();
                TextHelper.ClearSelection(_ed);
                // Load block từ file template
                string templatePath = BlockTemplateLoader.GetTemplatePath(StringDefinition.BlockTemplates);
                BlockTemplateLoader.LoadBlocksFromFile(_db, templatePath, new() { StringDefinition.blockNameTKCK_TD, StringDefinition.blockNameTKCK, StringDefinition.blockTKCK_TONG });

                if (XLTKCKSetting.TextHeight != null && XLTKCKSetting.TextLayer != null)
                {
                    List<ObjectId> blockIds = TextHelper.PickTexts(_db,_ed);
                    if (blockIds != null)
                    {
                        RemoveNearTextResult textIds = RemoveNearTextIds(_db, blockIds, XLTKCKSetting.CheckDistance);
                        if(textIds.DuplicateIds.Count()>0)
                        {
                            MessageBoxResult result = MessageBox.Show($"Có {textIds.DuplicateIds.Count} Text bị trùng. Bạn có muốn tiếp tục thống kê không?","Cảnh báo trùng text",MessageBoxButton.YesNo,MessageBoxImage.Warning);

                            if (result == MessageBoxResult.No)
                            {
                                SetTextsColorRed(_db, textIds.DuplicateIds);
                                TextHelper.ClearSelection(_ed);
                                TextHelper.SelectAndZoom(_db, _ed, textIds.DuplicateIds.ToArray());
                                return;
                            }

                        }
                        List<ObjectId> allTextIds = new List<ObjectId>();
                        allTextIds.AddRange(textIds.ResultIds);
                        allTextIds.AddRange(textIds.DuplicateIds);
                        var itemsCaukien = GetCauKienItemsFromTexts(_db, allTextIds);
                        _TKCKVM.CauKienItems = itemsCaukien;
                        _TKCKView.ShowDialog();
                       TextHelper.ClearSelection(_ed);
                    }
                }
                else
                {
                    MessageBoxResult result = MessageBox.Show("Bạn chưa xác lập cấu kiện.", StringDefinition.TITLE_MESSAGE, MessageBoxButton.OK, MessageBoxImage.Warning);

                    if (result == MessageBoxResult.OK)
                    {
                        _doc.SendStringToExecute("XLCK ", true, false, false);
                    }

                    return;
                }    
               
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(DrawingCategoriesAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }
       
        private void CancelInvoke()
        {
            try
            {
                _TKCKView.Close();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(TKCKAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }
        private void DrawTableInvoke()
        {
            try
            {
                using (Transaction tr = _db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = tr.GetObject(_db.BlockTableId, OpenMode.ForRead) as BlockTable;

                    if (!BlockHelper.CheckTableBlocksExist(bt, new List<string> { StringDefinition.blockNameTKCK_TD, StringDefinition.blockNameTKCK, StringDefinition.blockTKCK_TONG }))
                    {
                        _TKCKView.Close();
                        return;
                    }

                    BlockTableRecord blockTD = tr.GetObject(bt[StringDefinition.blockNameTKCK_TD], OpenMode.ForRead) as BlockTableRecord;

                    BlockTableRecord blockDef = tr.GetObject(bt[StringDefinition.blockNameTKCK], OpenMode.ForRead) as BlockTableRecord;

                    BlockTableRecord blockTotal = tr.GetObject(bt[StringDefinition.blockTKCK_TONG], OpenMode.ForRead) as BlockTableRecord;

                    BlockTableRecord modelSpace = tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;

                    Point3d? basePoint = PickInsertPoint(_ed);

                    if (basePoint == null)
                        return;

                    List<TKCKModel> items = _TKCKVM.CauKienItems.ToList();

                    DrawTKCKTable(tr, modelSpace, blockTD, blockDef, blockTotal, basePoint.Value, items);
                    _TKCKView.Close();
                    tr.Commit();
                }
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(TKCKAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }
        private ObservableCollection<TKCKModel> GetCauKienItemsFromTexts(Database db,List<ObjectId> textIds)
        {
            List<string> textContents = new List<string>();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in textIds)
                {
                    Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;

                    if (ent is not DBText dbText)
                        continue;

                    string height = dbText.Height.ToString();
                    string layer = dbText.Layer;

                    if (!string.Equals(height, XLTKCKSetting.TextHeight, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!string.Equals(layer, XLTKCKSetting.TextLayer, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!string.IsNullOrWhiteSpace(dbText.TextString))
                    {
                        textContents.Add(dbText.TextString.Trim());
                    }
                }

                tr.Commit();
            }

            List<TKCKModel> result = textContents
                .GroupBy(x => x)
                .Select((g, index) => new TKCKModel
                {
                    STT = (index + 1).ToString(),
                    TenCauKien = g.Key.ToUpper(),
                    CD = "",
                    CR = "",
                    CC = "",
                    SL = g.Count().ToString()
                })
                .ToList();

            return new ObservableCollection<TKCKModel>(result);
        }
        private void SetTextsColorRed(Database db, List<ObjectId> textIds)
        {
            if (db == null || textIds == null || textIds.Count == 0)
                return;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in textIds)
                {
                    Entity ent = tr.GetObject(id, OpenMode.ForWrite, false) as Entity;

                    if (ent == null)
                        continue;

                    ent.ColorIndex = 1; // 1 = Red trong AutoCAD/VinaCAD
                }

                tr.Commit();
            }
        }
        private Point3d? PickInsertPoint(Editor ed)
        {
            _TKCKView.Hide();

            PromptPointResult pointResult = ed.GetPoint("\nChọn điểm chèn bảng thống kê: ");

            _TKCKView.Show();
            _TKCKView.Activate();

            if (pointResult.Status != PromptStatus.OK)
                return null;

            return pointResult.Value;
        }
        private void DrawTKCKTable(Transaction tr, BlockTableRecord modelSpace, BlockTableRecord blockTD, BlockTableRecord blockDef, BlockTableRecord blockTotal, Point3d basePoint, List<TKCKModel> items)
        {
            InsertBlock( tr,modelSpace,blockTD,basePoint);

            int total = 0;
            Point3d totalPoint = basePoint;

            for (int i = 0; i < items.Count; i++)
            {
                Point3d rowPoint = GetRowPoint(basePoint, i);

                BlockReference rowBlock =InsertBlock(tr, modelSpace, blockDef, rowPoint);

                AddRowAttributes(tr,blockDef,rowBlock,items[i]);

                total += int.Parse(items[i].SL.ToString());

                totalPoint = GetTotalPoint(basePoint, i);
            }

            BlockReference totalBlock =InsertBlock(tr, modelSpace, blockTotal, totalPoint);

            AddTotalAttributes(tr,blockTotal,totalBlock,total);
        }
        private BlockReference InsertBlock(Transaction tr,BlockTableRecord modelSpace,BlockTableRecord blockDef,Point3d insertPoint)
        {
            BlockReference blockRef = new BlockReference(insertPoint, blockDef.ObjectId);

            modelSpace.AppendEntity(blockRef);
            tr.AddNewlyCreatedDBObject(blockRef, true);

            blockRef.ScaleFactors =
                new Scale3d(
                    XLTKCKSetting.Scale,
                    XLTKCKSetting.Scale,
                    XLTKCKSetting.Scale);

            return blockRef;
        }
        private Point3d GetRowPoint(Point3d basePoint, int index)
        {
            return new Point3d(
                basePoint.X,
                basePoint.Y - ((index + 2) * 10 * XLTKCKSetting.Scale),
                basePoint.Z);
        }

        private Point3d GetTotalPoint(Point3d basePoint, int index)
        {
            return new Point3d(
                basePoint.X,
                basePoint.Y - ((index + 3) * 10 * XLTKCKSetting.Scale),
                basePoint.Z);
        }
        private void AddRowAttributes(Transaction tr, BlockTableRecord blockDef, BlockReference blockRef, TKCKModel item)
        {
            foreach (ObjectId id in blockDef)
            {
                DBObject obj = tr.GetObject(id, OpenMode.ForRead);

                if (obj is not AttributeDefinition attDef || attDef.Constant)
                    continue;

                using (AttributeReference attRef = new AttributeReference())
                {
                    attRef.SetAttributeFromBlock(attDef, blockRef.BlockTransform);

                    string tag = attDef.Tag.ToUpper();

                    if (tag == "STT")
                        attRef.TextString = item.STT;
                    else if (tag == "TENCK")
                        attRef.TextString = item.TenCauKien;
                    else if (tag == "DAI")
                        attRef.TextString = "";
                    else if (tag == "RONG")
                        attRef.TextString = "";
                    else if (tag == "CAO")
                        attRef.TextString = "";
                    else if (tag == "SL1")
                        attRef.TextString = item.SL.ToString();
                    else if (tag == "GHICHU")
                        attRef.TextString = "";

                    blockRef.AttributeCollection.AppendAttribute(attRef);
                    tr.AddNewlyCreatedDBObject(attRef, true);
                }
            }
        }
        private void AddTotalAttributes(Transaction tr,BlockTableRecord blockTotal,BlockReference totalBlock,int total)
        {
            foreach (ObjectId id in blockTotal)
            {
                DBObject obj = tr.GetObject(id, OpenMode.ForRead);

                if (obj is not AttributeDefinition attDef || attDef.Constant)
                    continue;

                using (AttributeReference attRef = new AttributeReference())
                {
                    attRef.SetAttributeFromBlock(attDef,totalBlock.BlockTransform);

                    if (attDef.Tag.Equals("SL1", StringComparison.OrdinalIgnoreCase))
                        attRef.TextString = total.ToString();
                    else if (attDef.Tag.Equals("GHICHU", StringComparison.OrdinalIgnoreCase))
                        attRef.TextString = "";

                    totalBlock.AttributeCollection.AppendAttribute(attRef);
                    tr.AddNewlyCreatedDBObject(attRef, true);
                }
            }
        }
        private RemoveNearTextResult RemoveNearTextIds(Database db,List<ObjectId> textIds, double tolerance)
        {
            RemoveNearTextResult result = new RemoveNearTextResult();

            if (db == null || textIds == null || textIds.Count == 0)
                return result;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in textIds)
                {
                    DBText currentText = tr.GetObject(id, OpenMode.ForRead) as DBText;

                    if (currentText == null)
                        continue;

                    bool isNear = false;

                    foreach (ObjectId resultId in result.ResultIds)
                    {
                        DBText resultText = tr.GetObject(resultId, OpenMode.ForRead) as DBText;

                        if (resultText == null)
                            continue;

                        if (!string.Equals(
                                currentText.TextString,
                                resultText.TextString,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        double distance = currentText.Position.DistanceTo(resultText.Position);

                        if (distance <= tolerance)
                        {
                            isNear = true;
                            break;
                        }
                    }

                    if (isNear)
                    {
                        result.DuplicateIds.Add(id);
                    }
                    else
                    {
                        result.ResultIds.Add(id);
                    }
                }

                tr.Commit();
            }

            return result;
        }
        private class RemoveNearTextResult
        {
            public List<ObjectId> ResultIds { get; set; } = new List<ObjectId>();
            public List<ObjectId> DuplicateIds { get; set; } = new List<ObjectId>();
        }
    }
}
