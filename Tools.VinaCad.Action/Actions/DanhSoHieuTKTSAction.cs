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
using System.Windows.Documents;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;
using Tools.Model;
using Tools.Resources.Definitions;
using Tools.View.UI;
using Tools.ViewModel;
using Tools.VinaCad.Helper.Helper;
using Tools.VinaCad.Helper.Utils;
using Tools.VinaCad.Modeling;
using Exception = System.Exception;
using MessageBox = System.Windows.MessageBox;

namespace Tools.VinaCad.Action.Actions
{
    public class DanhSoHieuTKTSAction
    {
        private DanhSoHieuTKTSVM _danhSoHieuTKTSVM;
        private DanhSoHieuTKTSWindow _danhSoHieuTKTSView;
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
        List<ObjectId> blockIdSeleted = new List<ObjectId>();
        private bool _danhSoHieuTuDong = false;
        public double ChieuDaiLopBeTongBaoVe { get; set; } = 100;
        public DanhSoHieuTKTSAction()
        {
            _danhSoHieuTKTSVM = new DanhSoHieuTKTSVM()
            {
                DrawTableCmd = new RelayCommand(DrawTableInvoke),
                DanhSoCmd = new RelayCommand(DanhSoInvoke),
                CancelCmd = new RelayCommand(CancelInvoke)
            };
            _danhSoHieuTKTSView = new DanhSoHieuTKTSWindow() { DataContext = _danhSoHieuTKTSVM };
        }
        public void Execute()
        {
            try
            {
                UpdateCurrentDocument();
                TextHelper.ClearSelection(_ed);
                if (XLTKCTSetting.LapD10 == 0 || XLTKCTSetting.LapD10D16 == 0 || XLTKCTSetting.LapD16 == 0)
                {
                    MessageBoxResult result = MessageBox.Show("Bạn cần xác lập đầy đủ trước khi thống kê thép!", StringDefinition.TITLE_MESSAGE, MessageBoxButton.OK, MessageBoxImage.Warning);

                    if (result == MessageBoxResult.OK)
                    {
                        _doc.SendStringToExecute("XLTKCT ", true, false, false);
                    }
                    return;
                }
                else
                {
                    List<string> strings = new List<string>() { StringDefinition.blockNameTKSB1, StringDefinition.blockNameTKSB1A, StringDefinition.blockNameTKSB1B,
                    StringDefinition.blockNameTKSB2, StringDefinition.blockNameTKSB2A, StringDefinition.blockNameTKSB2B,StringDefinition.blockNameTKSB3, StringDefinition.blockNameTKSB3A, StringDefinition.blockNameTKSB3B,StringDefinition.blockNameTKTKL};
                    string[] blockNames = { "TKT_TD", "TKT_B1", "TKT_B2", "TKT_B3", "TKT_KL" };
                    string templatePath = BlockTemplateLoader.GetTemplatePath(StringDefinition.BlockTemplates);
                    BlockTemplateLoader.LoadBlocksFromFile(_db, templatePath, blockNames.ToList());
                    List<ObjectId> blockIds = BlockHelper.PickBlocksByBlockNameContains(_db,_ed,strings);
                    blockIdSeleted = blockIds;
                    if (blockIds.Count > 0)
                    {
                        _danhSoHieuTKTSView.Show();
                    }
                    TextHelper.ClearSelection(_ed);
                }

            }
            catch (Exception ex)
            {
                Logger.Info(nameof(EditThongKeCotThepAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }
        private void DanhSoInvoke()
        {
            try
            {
                if (string.IsNullOrEmpty(_danhSoHieuTKTSVM.SoHieuStart))
                {
                    MessageBox.Show("Bạn cần nhập số hiệu bắt đầu để đánh số!", StringDefinition.TITLE_MESSAGE);
                    return;
                }
                else
                {

                    if (_danhSoHieuTKTSVM.IsTopToBottom)
                    {
                        blockIdSeleted = BlockHelper.SortBlocksTopToBottomLeftToRight(_db, blockIdSeleted, 0);
                        NumberBlocks(blockIdSeleted);
                        _danhSoHieuTuDong = true;
                        MessageBox.Show("Bạn đã đánh số hiệu tự động thép sàn thành công!", StringDefinition.TITLE_MESSAGE);
                    }
                    else
                    {
                        blockIdSeleted = BlockHelper.SortBlocksLeftToRightTopToBottom(_db, blockIdSeleted, 0);
                        NumberBlocks(blockIdSeleted);
                        _danhSoHieuTuDong = true;
                        MessageBox.Show("Bạn đã đánh số hiệu tự động thép sàn thành công!", StringDefinition.TITLE_MESSAGE);
                    }
                }

            }
            catch (Exception ex)
            {
                Logger.Info(nameof(DanhSoHieuTKTSAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }
        private void DrawTableInvoke()
        {
            try
            {
                if (!ValidateSlabRebarInput(blockIdSeleted))
                    return;

                _danhSoHieuTKTSView.Hide();
                using (Transaction tr = _db.TransactionManager.StartTransaction())
                {
                    List<ThepSan> items = BuildThepSanItems(tr, blockIdSeleted);

                    if (!_danhSoHieuTuDong)
                    {
                        if (!ValidateDuplicateSoHieu(_db,_ed,items, tr))
                            return;
                    }
                        
                    List<ThepSan> tableItems = GroupThepSanItems(items);

                    DrawSlabRebarTable(tr, tableItems);

                    tr.Commit();
                }

                _danhSoHieuTKTSView.Close();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(DanhSoHieuTKTSAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }
        private bool ValidateSlabRebarInput(List<ObjectId> blockIdsThepSan)
        {
            if (blockIdsThepSan == null || blockIdsThepSan.Count == 0)
            {
                MessageBox.Show("Vui lòng quét chọn thép sàn.", StringDefinition.TITLE_MESSAGE);
                return false;
            }

            if (string.IsNullOrWhiteSpace(_danhSoHieuTKTSVM.Scale))
            {
                MessageBox.Show("Vui lòng nhập tỷ lệ bản vẽ.", StringDefinition.TITLE_MESSAGE);
                return false;
            }

            if (_danhSoHieuTKTSVM.IsCoDinh && string.IsNullOrWhiteSpace(_danhSoHieuTKTSVM.HookLength))
            {
                MessageBox.Show("Vui lòng nhập chiều dài bẻ móc cố định.", StringDefinition.TITLE_MESSAGE);
                return false;
            }

            return true;
        }
        private List<ThepSan> BuildThepSanItems(Transaction tr, List<ObjectId> blockIds)
        {
            List<ThepSan> result = new List<ThepSan>();

            foreach (ObjectId id in blockIds)
            {
                BlockReference blockRef = tr.GetObject(id, OpenMode.ForRead) as BlockReference;

                if (blockRef == null)
                    continue;

                ThepSan item = CreateThepSanFromBlock(tr, blockRef);

                if (item != null)
                    result.Add(item);
            }
            result = result
                   .OrderBy(x => x.soHieu, Comparer<string>.Create(ThepSan.CompareSoHieuNatural))
                   .ToList();
            return result;
        }

        private ThepSan CreateThepSanFromBlock(Transaction tr, BlockReference blockRef)
        {
            ThepSan item = new ThepSan();

            item.blockThep = blockRef;

            ReadSlabRebarAttributes(tr, blockRef, item);

            string type = ReadXDataBlockThepSan(blockRef, tr);

            FillSlabRebarShapeData(blockRef, item, type);

            if (item.khoangCachPhanBo > 0)
                item.soLuong = ((int)(item.chieuDaiPhanBo / item.khoangCachPhanBo) + 1) * item.soLopThep;

            return item;
        }
        private void ReadSlabRebarAttributes(Transaction tr, BlockReference blockRef, ThepSan item)
        {
            foreach (ObjectId attId in blockRef.AttributeCollection)
            {
                AttributeReference attRef = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;

                if (attRef == null)
                    continue;

                string tag = attRef.Tag.ToUpper();
                string value = attRef.TextString ?? "";

                if (tag == "SH")
                    item.soHieu = value;

                if (tag == "DK")
                    item.duongKinh = ParseDiameter(value);

                if (tag == "AKC")
                    item.khoangCachPhanBo = ParseSpacing(value);
            }
        }
        private void FillSlabRebarShapeData(BlockReference blockRef, ThepSan item, string type)
        {
            if (type == "1" || type == "11")
                FillDang01(blockRef, item, type);

            if (type == "2" || type == "22")
                FillDang02(blockRef, item, type);

            if (type == "3" || type == "33")
                FillDang03(blockRef, item, type);
        }

        private void FillDang01(BlockReference blockRef, ThepSan item, string type)
        {
            item.dangThep = "dang01";
            item.soLopThep = type == "11" ? 2 : 1;

            foreach (DynamicBlockReferenceProperty prop in blockRef.DynamicBlockReferencePropertyCollection)
            {
                if (prop.PropertyName == "Distance1")
                    item.chieuDaiThanhThep = MathUtils.LamTronXuongBoiCua5(Math.Round(MathUtils.ToDouble(prop.Value?.ToString()), 0));

                if (prop.PropertyName == "Distance2")
                    item.chieuDaiPhanBo = Math.Round(MathUtils.ToDouble(prop.Value?.ToString()), 0);
            }
        }

        private void FillDang02(BlockReference blockRef, ThepSan item, string type)
        {
            item.dangThep = "dang02";
            item.soLopThep = type == "22" ? 2 : 1;

            foreach (DynamicBlockReferenceProperty prop in blockRef.DynamicBlockReferencePropertyCollection)
            {
                if (prop.PropertyName == "Distance1")
                    item.chieuDaiThanhThep = GetDang02Length(prop);

                if (prop.PropertyName == "L2")
                    item.L2 = GetHookValue(prop);

                if (prop.PropertyName == "L3")
                    item.L3 = GetHookValue(prop);

                if (prop.PropertyName == "Distance2")
                    item.chieuDaiPhanBo = Math.Round(MathUtils.ToDouble(prop.Value?.ToString()), 0);
            }
        }

        private void FillDang03(BlockReference blockRef, ThepSan item, string type)
        {
            item.dangThep = "dang03";
            item.soLopThep = type == "33" ? 2 : 1;

            foreach (DynamicBlockReferenceProperty prop in blockRef.DynamicBlockReferencePropertyCollection)
            {
                if (prop.PropertyName == "Distance1")
                    item.chieuDaiThanhThep = GetDang03Length(prop);

                if (prop.PropertyName == "L2")
                    item.L2 = GetHookValue(prop);

                if (prop.PropertyName == "Distance2")
                    item.chieuDaiPhanBo = Math.Round(MathUtils.ToDouble(prop.Value?.ToString()), 0);
            }
        }
        private double GetDang02Length(DynamicBlockReferenceProperty prop)
        {
            double length = MathUtils.LamTronXuongBoiCua5(Math.Round(MathUtils.ToDouble(prop.Value?.ToString()), 0));

            return length - 2 * _danhSoHieuTKTSVM.ChieuDayBTBaoVe;
        }

        private double GetDang03Length(DynamicBlockReferenceProperty prop)
        {
            double length = MathUtils.LamTronXuongBoiCua5(Math.Round(MathUtils.ToDouble(prop.Value?.ToString()), 0));

            return length - _danhSoHieuTKTSVM.ChieuDayBTBaoVe;
        }
        private double GetHookValue(DynamicBlockReferenceProperty prop)
        {
            if (_danhSoHieuTKTSVM.IsCoDinh)
                return MathUtils.ToDouble(_danhSoHieuTKTSVM.HookLength);

            if (_danhSoHieuTKTSVM.IsThucTe)
                return MathUtils.LamTronXuongBoiCua5(Math.Round(MathUtils.ToDouble(prop.Value?.ToString()), 0));

            return 0;
        }

        //private bool ValidateDuplicateSoHieu(List<ThepSan> items)
        //{
        //    items.Sort();

        //    for (int i = 0; i < items.Count; i++)
        //    {
        //        for (int j = i + 1; j < items.Count; j++)
        //        {
        //            if (!SameText(items[i].soHieu, items[j].soHieu))
        //                break;

        //            if (!IsSameRebar(items[i], items[j]))
        //            {
        //                SelectDuplicateBlocks(items[i], items[j]);
        //                return false;
        //            }
        //        }
        //    }

        //    return true;
        //}

        private bool IsSameRebar(ThepSan a, ThepSan b)
        {
            if (a.duongKinh != b.duongKinh)
            {
                MessageBox.Show($"Cùng số hiệu {a.soHieu} nhưng khác đường kính.", "Phát hiện trùng số hiệu");
                return false;
            }

            if (a.dangThep != b.dangThep)
            {
                MessageBox.Show($"Cùng số hiệu {a.soHieu} nhưng khác hình dạng thanh thép.", "Phát hiện trùng số hiệu");
                return false;
            }

            if (a.chieuDaiThanhThep != b.chieuDaiThanhThep || a.L2 != b.L2 || a.L3 != b.L3)
            {
                MessageBox.Show($"Cùng số hiệu {a.soHieu} nhưng khác chiều dài thép hoặc chiều dài bẻ móc.", "Phát hiện trùng số hiệu");
                return false;
            }

            return true;
        }
        private void SelectDuplicateBlocks(ThepSan a, ThepSan b)
        {
            List<ObjectId> ids = new List<ObjectId> { a.blockThep.ObjectId, b.blockThep.ObjectId };
            SelectIds(ids);
        }

        private List<ThepSan> GroupThepSanItems(List<ThepSan> items)
        {
            return items
                .GroupBy(x => x.soHieu)
                .Select(g =>
                {
                    ThepSan first = g.First();

                    return new ThepSan
                    {
                        soHieu = first.soHieu,
                        dangThep = first.dangThep,
                        duongKinh = first.duongKinh,
                        soLuong = g.Sum(x => x.soLuong),
                        chieuDaiThanhThep = first.chieuDaiThanhThep,
                        L2 = first.L2,
                        L3 = first.L3
                    };
                })
                .ToList();
        }

        private void DrawSlabRebarTable(Transaction tr, List<ThepSan> items)
        {
            BlockTable bt = tr.GetObject(_db.BlockTableId, OpenMode.ForRead) as BlockTable;

            string[] blockNames = { "TKT_TD", "TKT_B1", "TKT_B2", "TKT_B3", "TKT_KL" };

            if (!BlockTemplateLoader.CheckTableBlocksExist(bt, blockNames))
                return;

            BlockTableRecord modelSpace = tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;

            Point3d? basePoint = PickInsertPoint();

            if (basePoint == null)
                return;

            double scale = GetDrawingScale();

            InsertTableBlocks(tr, bt, modelSpace, basePoint.Value, items, scale);
        }
        private Point3d? PickInsertPoint()
        {
            PromptPointResult result = _ed.GetPoint("\nChọn điểm chèn bảng thống kê thép: ");

            if (result.Status != PromptStatus.OK)
                return null;

            return result.Value;
        }
        private void SelectIds(List<ObjectId> ids)
        {
            if (ids == null || !ids.Any())
                return;

            ObjectId[] objectIds = ids
                .Where(x => !x.IsNull && x.IsValid)
                .Distinct()
                .ToArray();

            if (!objectIds.Any())
                return;

            _ed.SetImpliedSelection(Array.Empty<ObjectId>());
            _ed.SetImpliedSelection(objectIds);

            _ed.WriteMessage($"\nĐã chọn {objectIds.Length} đối tượng.");

            _ed.UpdateScreen();
        }
        private void InsertTableBlocks(Transaction tr, BlockTable bt, BlockTableRecord modelSpace, Point3d basePoint, List<ThepSan> items, double scale)
        {
            BlockTableRecord blockTD = tr.GetObject(bt["TKT_TD"], OpenMode.ForRead) as BlockTableRecord;

            InsertBlockTD(tr, modelSpace, blockTD, basePoint, scale);

            Point3d rowBasePoint = MovePointY(basePoint, 20 * scale);

            RebarWeightTotal total = InsertThepSanRows(tr, bt, modelSpace, rowBasePoint, items, scale);

            Point3d totalPoint = MovePointY(rowBasePoint, items.Count * 10 * scale);

            InsertTotalWeightBlock(tr, bt, modelSpace, totalPoint, total, scale);
        }
        private void InsertBlockTD(Transaction tr, BlockTableRecord modelSpace, BlockTableRecord blockDef, Point3d point, double scale)
        {
            BlockReference blockRef =
                new BlockReference(point, blockDef.ObjectId);

            blockRef.ScaleFactors =
                new Scale3d(scale, scale, scale);

            modelSpace.AppendEntity(blockRef);
            tr.AddNewlyCreatedDBObject(blockRef, true);

            foreach (ObjectId id in blockDef)
            {
                AttributeDefinition attDef =
                    tr.GetObject(id, OpenMode.ForRead) as AttributeDefinition;

                if (attDef == null || attDef.Constant)
                    continue;

                AttributeReference attRef =
                    new AttributeReference();

                attRef.SetAttributeFromBlock(
                    attDef,
                    blockRef.BlockTransform);

                blockRef.AttributeCollection.AppendAttribute(attRef);

                tr.AddNewlyCreatedDBObject(attRef, true);
            }
        }
        private RebarWeightTotal InsertThepSanRows(Transaction tr, BlockTable bt, BlockTableRecord modelSpace, Point3d basePoint, List<ThepSan> items, double scale)
        {
            RebarWeightTotal total = new RebarWeightTotal();

            for (int i = 0; i < items.Count; i++)
            {
                ThepSan item = items[i];

                string blockName = GetTKTBlockName(item.dangThep);
                List<string> values = GetRowValues(item);

                if (string.IsNullOrWhiteSpace(blockName))
                    continue;

                BlockTableRecord blockDef = tr.GetObject(bt[blockName], OpenMode.ForRead) as BlockTableRecord;
                Point3d point = MovePointY(basePoint, i * 10 * scale);

                List<double> weights = InsertBlockTKT(tr, modelSpace, blockDef, point, scale, values);

                total.D10 += weights[0];
                total.D10D18 += weights[1];
                total.D18 += weights[2];
            }

            return total;
        }
        private List<double> InsertBlockTKT(Transaction tr, BlockTableRecord modelSpace, BlockTableRecord blockDef, Point3d point, double scale, List<string> values)
        {
            List<double> weights = new List<double> { 0, 0, 0 };

            BlockReference blockRef =
                new BlockReference(point, blockDef.ObjectId);

            blockRef.ScaleFactors =
                new Scale3d(scale, scale, scale);

            modelSpace.AppendEntity(blockRef);

            tr.AddNewlyCreatedDBObject(blockRef, true);

            foreach (ObjectId id in blockDef)
            {
                AttributeDefinition attDef =
                    tr.GetObject(id, OpenMode.ForRead) as AttributeDefinition;

                if (attDef == null || attDef.Constant)
                    continue;

                AttributeReference attRef =
                    new AttributeReference();

                attRef.SetAttributeFromBlock(
                    attDef,
                    blockRef.BlockTransform);

                SetInitialAttributeValue(attRef, values);

                blockRef.AttributeCollection.AppendAttribute(attRef);

                tr.AddNewlyCreatedDBObject(attRef, true);
            }

            Dictionary<string, AttributeReference> atts =
               BlockHelper.GetAttributeReferences(blockRef, tr);

            string blockName = blockDef.Name;

            weights = UpdateInsertedTKTBlock(blockName, atts);

            return weights;
        }
        private void SetInitialAttributeValue(AttributeReference attRef, List<string> values)
        {
            if (attRef == null || values == null)
                return;

            string tag = attRef.Tag.ToUpper();

            if (tag == "SH" && values.Count > 0)
                attRef.TextString = values[0];

            if (tag == "DK" && values.Count > 1)
                attRef.TextString = values[1];

            if (tag == "SL1" && values.Count > 2)
                attRef.TextString = values[2];

            if (tag == "L1" && values.Count > 3)
                attRef.TextString = values[3];

            if (tag == "L2" && values.Count > 4)
                attRef.TextString = values[4];

            if (tag == "L3" && values.Count > 5)
                attRef.TextString = values[5];

            if (tag == "TL1" && values.Count > 0)
                attRef.TextString = values[0];

            if (tag == "TL2" && values.Count > 1)
                attRef.TextString = values[1];

            if (tag == "TL3" && values.Count > 2)
                attRef.TextString = values[2];

            if (tag == "TKL" && values.Count > 3)
                attRef.TextString = values[3];
        }
        private List<double> UpdateInsertedTKTBlock(string blockName, Dictionary<string, AttributeReference> atts)
        {
            switch (blockName)
            {
                case "TKT_B1":
                    return UpdateInsertedB1(atts);

                case "TKT_B2":
                    return UpdateInsertedB2(atts);

                case "TKT_B3":
                    return UpdateInsertedB3(atts);

                default:
                    return new List<double> { 0, 0, 0 };
            }
        }
        private List<double> UpdateInsertedB1(Dictionary<string, AttributeReference> atts)
        {
            double l1 = GetAttrDouble(atts, "L1");
            double d = GetAttrDouble(atts, "DK");
            double sl = GetAttrDouble(atts, "SL1");

            double dai = Math.Round(l1 + ChieuDaiNoiThep(d, l1), 0);
            double cd = sl * dai / 1000.0;

            SetAttr(atts, "DAI", MathUtils.FormatNumber(dai));
            SetAttr(atts, "SLTB", MathUtils.FormatNumber(sl));
            SetAttr(atts, "CD", MathUtils.FormatNumber(cd));
            SetAttr(atts, "DT", MathUtils.FormatNumber(cd));

            return SetWeightAttributes(atts, d, cd);
        }
        private List<double> UpdateInsertedB2(Dictionary<string, AttributeReference> atts)
        {
            double l1 = GetAttrDouble(atts, "L1");
            double l2 = GetAttrDouble(atts, "L2");
            double l3 = GetAttrDouble(atts, "L3");
            double d = GetAttrDouble(atts, "DK");
            double sl = GetAttrDouble(atts, "SL1");

            double length = l1 + l2 + l3;
            double dai = Math.Round(length + ChieuDaiNoiThep(d, length), 0);
            double cd = sl * dai / 1000.0;

            SetAttr(atts, "DAI", MathUtils.FormatNumber(dai));
            SetAttr(atts, "SLTB", MathUtils.FormatNumber(sl));
            SetAttr(atts, "CD", MathUtils.FormatNumber(cd));
            SetAttr(atts, "DT", MathUtils.FormatNumber(cd));

            return SetWeightAttributes(atts, d, cd);
        }
        private List<double> UpdateInsertedB3(Dictionary<string, AttributeReference> atts)
        {
            double l1 = GetAttrDouble(atts, "L1");
            double l2 = GetAttrDouble(atts, "L2");
            double d = GetAttrDouble(atts, "DK");
            double sl = GetAttrDouble(atts, "SL1");

            double length = l1 + l2;
            double dai = Math.Round(length + ChieuDaiNoiThep(d, length), 0);
            double cd = sl * dai / 1000.0;

            SetAttr(atts, "DAI", MathUtils.FormatNumber(dai));
            SetAttr(atts, "SLTB", MathUtils.FormatNumber(sl));
            SetAttr(atts, "CD", MathUtils.FormatNumber(cd));
            SetAttr(atts, "DT", MathUtils.FormatNumber(cd));

            return SetWeightAttributes(atts, d, cd);
        }
        private List<double> SetWeightAttributes(Dictionary<string, AttributeReference> atts, double d, double cd)
        {
            List<double> result = new List<double> { 0, 0, 0 };

            double weight = Math.Round(TongKLThep(cd, d), 2);

            if (d <= 10)
            {
                SetAttr(atts, "TL1", MathUtils.FormatNumber(weight));
                SetAttr(atts, "TL2", "");
                SetAttr(atts, "TL3", "");
                result[0] = weight;
            }
            else if (d <= 18)
            {
                SetAttr(atts, "TL1", "");
                SetAttr(atts, "TL2", MathUtils.FormatNumber(weight));
                SetAttr(atts, "TL3", "");
                result[1] = weight;
            }
            else
            {
                SetAttr(atts, "TL1", "");
                SetAttr(atts, "TL2", "");
                SetAttr(atts, "TL3", MathUtils.FormatNumber(weight));
                result[2] = weight;
            }

            return result;
        }
        private double TongKLThep(double chieuDai, double duongKinhThep)
        {
            double kLThep = Math.PI * (duongKinhThep / 2) * (duongKinhThep / 2) * chieuDai * 7850 / 1000000;
            return kLThep;
        }
        private double ChieuDaiNoiThep(double duongKinhThep, double chieuDaiThanhThep)
        {
            int chieuDaiNoi = 0;

            if (chieuDaiThanhThep <= 11700)
                return 0;

            if (duongKinhThep < 10)
                chieuDaiNoi = LamTronXuongBoiCua5((Convert.ToInt32(chieuDaiThanhThep) / 11700) * XLTKCTSetting.LapD10 * Convert.ToInt32(duongKinhThep));

            if (duongKinhThep >= 10 && duongKinhThep <= 16)
                chieuDaiNoi = LamTronXuongBoiCua5((Convert.ToInt32(chieuDaiThanhThep) / 11700) * XLTKCTSetting.LapD10D16 * Convert.ToInt32(duongKinhThep));

            if (duongKinhThep > 16)
                chieuDaiNoi = LamTronXuongBoiCua5((Convert.ToInt32(chieuDaiThanhThep) / 11700) * XLTKCTSetting.LapD16 * Convert.ToInt32(duongKinhThep));

            if (duongKinhThep == 5 || duongKinhThep == 7 || duongKinhThep == 12.7 || duongKinhThep == 13)
                chieuDaiNoi = 0;

            return chieuDaiNoi;
        }
        private int LamTronXuongBoiCua5(double value)
        {
            return Convert.ToInt32(Math.Round(value / 5.0) * 5);
        }
        private double GetAttrDouble(Dictionary<string, AttributeReference> atts, string tag)
        {
            if (atts == null || !atts.TryGetValue(tag, out AttributeReference attRef))
                return 0;

            return MathUtils.ToDouble(attRef.TextString);
        }
        private void SetAttr(Dictionary<string, AttributeReference> atts, string tag, string value)
        {
            if (atts == null)
                return;

            if (atts.TryGetValue(tag, out AttributeReference attRef))
                attRef.TextString = value ?? "";
        }
        private void InsertTotalWeightBlock(Transaction tr, BlockTable bt, BlockTableRecord modelSpace, Point3d point, RebarWeightTotal total, double scale)
        {
            BlockTableRecord blockDef = tr.GetObject(bt["TKT_KL"], OpenMode.ForRead) as BlockTableRecord;

            List<string> values = new List<string>
                    {
                        total.D10 != 0 ? MathUtils.FormatNumber(total.D10) : "",
                        total.D10D18 != 0 ? MathUtils.FormatNumber(total.D10D18) : "",
                        total.D18 != 0 ? MathUtils.FormatNumber(total.D18) : "",
                        MathUtils.FormatNumber(total.D10 + total.D10D18 + total.D18)
                    };

            InsertBlockTKT(tr, modelSpace, blockDef, point, scale, values);
        }
        private string GetTKTBlockName(string dangThep)
        {
            if (dangThep == "dang01")
                return "TKT_B1";

            if (dangThep == "dang02")
                return "TKT_B2";

            if (dangThep == "dang03")
                return "TKT_B3";

            return "";
        }
        private List<string> GetRowValues(ThepSan item)
        {
            if (item.dangThep == "dang01")
                return new List<string> { item.soHieu, item.duongKinh.ToString(), item.soLuong.ToString(), item.chieuDaiThanhThep.ToString(), "", "" };

            if (item.dangThep == "dang02")
                return new List<string> { item.soHieu, item.duongKinh.ToString(), item.soLuong.ToString(), item.chieuDaiThanhThep.ToString(), item.L2.ToString(), item.L3.ToString() };

            return new List<string> { item.soHieu, item.duongKinh.ToString(), item.soLuong.ToString(), item.chieuDaiThanhThep.ToString(), item.L2.ToString(), "" };
        }
        private class RebarWeightTotal
        {
            public double D10 { get; set; }
            public double D10D18 { get; set; }
            public double D18 { get; set; }
        }

        private double ParseDiameter(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return 0;

            string[] arr = value.ToUpper().Split('C');

            if (arr.Length < 2)
                return MathUtils.ToDouble(value);

            return MathUtils.ToDouble(arr[1]);
        }

        private int ParseSpacing(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return 0;

            string[] arr = value.ToUpper().Split('A');

            if (arr.Length < 2)
                return 0;

            return (int)MathUtils.ToDouble(arr[1]);
        }
        private string ReadXDataBlockThepSan(BlockReference blockRef, Transaction tr)
        {
            Entity ent = tr.GetObject(blockRef.ObjectId, OpenMode.ForRead) as Entity;

            if (ent == null)
                return "";

            ResultBuffer rb = ent.GetXDataForApplication("Thanh");

            if (rb == null)
                return "";

            string result = "";

            foreach (TypedValue value in rb)
            {
                if (value.Value != null)
                    result = value.Value.ToString();
            }

            return result;
        }
        private double GetDrawingScale()
        {
            string value = _danhSoHieuTKTSVM.Scale;

            if (value.Contains("/"))
            {
                string[] arr = value.Split('/');
                return MathUtils.ToDouble(arr[1]);
            }

            return MathUtils.ToDouble(value);
        }
        private bool SameText(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private Point3d MovePointY(Point3d point, double distance)
        {
            return new Point3d(point.X, point.Y - distance, point.Z);
        }


        private void CancelInvoke()
        {
            try
            {
                _danhSoHieuTKTSView.Close();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(DanhSoHieuTKTSAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }
        private void NumberBlocks(List<ObjectId> blockIds)
        {
            if (blockIds == null || blockIds.Count == 0)
                return;

            string prefix = _danhSoHieuTKTSVM.SoHieuStart?.Trim();

            using (Transaction tr = _db.TransactionManager.StartTransaction())
            {
                for (int i = 0; i < blockIds.Count; i++)
                {
                    BlockReference blockRef = tr.GetObject(blockIds[i], OpenMode.ForRead) as BlockReference;

                    if (blockRef == null)
                        continue;

                    Dictionary<string, AttributeReference> atts = BlockHelper.GetAttributeReferences(blockRef, tr);

                    BlockHelper.SetAttr(atts, "SH", $"{prefix}{i + 1}");
                }

                tr.Commit();
            }
        }
        private bool ValidateDuplicateSoHieu(Database db, Editor ed, List<ThepSan> items, Transaction tr)
        {
            if (items == null || items.Count == 0)
                return true;

            ObjectIdCollection duplicateIds = new ObjectIdCollection();
            List<string> messages = new List<string>();

            bool canContinue = true;
            int step = 1;
            int sameCount = 1;

            for (int i = 0; i < items.Count; i = i + step)
            {
                sameCount = CountSameSoHieu(items, i);

                CheckSameSoHieuGroup(items, i, sameCount, duplicateIds, messages, ref canContinue);

                step = sameCount;
            }
           
            if (canContinue)
                return true;

            foreach (string message in messages)
            {
                MessageBox.Show(message, "Phát Hiện Trùng Số Hiệu!!!");
            }

            if (duplicateIds.Count > 0)
            {
                ObjectId[] ids = new ObjectId[duplicateIds.Count];

                for (int i = 0; i < duplicateIds.Count; i++)
                {
                    ids[i] = duplicateIds[i];
                }
                TextHelper.SelectAndZoom(db, ed, ids);
            }
            return false;
        }
        private int CountSameSoHieu(List<ThepSan> items, int startIndex)
        {
            int count = 1;

            for (int i = startIndex + 1; i < items.Count; i++)
            {
                if (items[startIndex].soHieu == items[i].soHieu)
                    count++;
                else
                    break;
            }

            return count;
        }
        private void CheckSameSoHieuGroup(List<ThepSan> items, int startIndex, int sameCount, ObjectIdCollection duplicateIds, List<string> messages, ref bool canContinue)
        {
            for (int j = startIndex + 1; j < startIndex + sameCount; j++)
            {
                if (!CheckOneDuplicatePair(items[startIndex], items[j], duplicateIds, messages))
                {
                    canContinue = false;
                    break;
                }
            }
        }
        private bool CheckOneDuplicatePair(ThepSan first, ThepSan second, ObjectIdCollection duplicateIds, List<string> messages)
        {
            if (first.duongKinh != second.duongKinh)
            {
                AddDuplicateError(first, second, duplicateIds, messages, "khác đường kính");
                return false;
            }

            if (first.dangThep != second.dangThep)
            {
                AddDuplicateError(first, second, duplicateIds, messages, "khác hình dạng thanh thép");
                return false;
            }

            if (first.chieuDaiThanhThep != second.chieuDaiThanhThep)
            {
                AddDuplicateError(first, second, duplicateIds, messages, "khác chiều dài thép");
                return false;
            }

            if (first.L2 != second.L2)
            {
                AddDuplicateError(first, second, duplicateIds, messages, "khác chiều dài bẻ móc");
                return false;
            }

            if (first.L3 != second.L3)
            {
                AddDuplicateError(first, second, duplicateIds, messages, "khác chiều dài bẻ móc");
                return false;
            }

            return true;
        }
        private void AddDuplicateError(ThepSan first, ThepSan second, ObjectIdCollection duplicateIds, List<string> messages, string reason)
        {
            messages.Add("Cùng số hiệu " + first.soHieu.ToUpper() + " nhưng lại " + reason + ". Bạn cần dừng lại để kiểm tra!");

            if (first.blockThep != null)
                duplicateIds.Add(first.blockThep.ObjectId);

            if (second.blockThep != null)
                duplicateIds.Add(second.blockThep.ObjectId);
        }
        private void ZoomAndSelectDuplicateBlocks(ObjectIdCollection duplicateIds, Transaction tr)
        {
            if (duplicateIds == null || duplicateIds.Count == 0)
                return;

            ObjectId[] ids = new ObjectId[duplicateIds.Count];

            for (int i = 0; i < duplicateIds.Count; i++)
            {
                ids[i] = duplicateIds[i];
            }
            ZoomToObjects(ids);
            SelectIds(ids.ToList());

        }
        private void ZoomToObjects(ObjectId[] objectIds)
        {
            Extents3d? totalExtents = null;

            using (Transaction tr = _db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in objectIds)
                {
                    Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;

                    if (ent == null)
                        continue;

                    try
                    {
                        Extents3d ext = ent.GeometricExtents;

                        if (totalExtents == null)
                            totalExtents = ext;
                        else
                        {
                            Extents3d temp = totalExtents.Value;
                            temp.AddExtents(ext);
                            totalExtents = temp;
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }

                tr.Commit();
            }

            if (totalExtents == null)
                return;

            Extents3d ex = totalExtents.Value;

            Point3d min = ex.MinPoint;
            Point3d max = ex.MaxPoint;

            Point3d center3d = new Point3d(
                (min.X + max.X) / 2.0,
                (min.Y + max.Y) / 2.0,
                (min.Z + max.Z) / 2.0);

            double width = Math.Max((max.X - min.X) * 1.5, 1000);
            double height = Math.Max((max.Y - min.Y) * 1.5, 1000);

            using (ViewTableRecord view = _ed.GetCurrentView())
            {
                view.Target = center3d;
                view.CenterPoint = new Point2d(0, 0);
                view.Width = width;
                view.Height = height;

                _ed.SetCurrentView(view);
            }

            _ed.Regen();
        }


    }
}
