using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using PrLogTrackingSystem;
using PrMVVMCore;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.Resources.Definitions;
using Tools.View.UI;
using Tools.ViewModel;
using Tools.VinaCad.Helper.Helper;
using Tools.VinaCad.Helper.Utils;
using Tools.VinaCad.Modeling;
using MessageBox = System.Windows.MessageBox;

namespace Tools.VinaCad.Action.Actions
{
    public class DanhSoHieuTKTDAction
    {
        private DanhSoHieuTKTDVM _danhSoHieuTKTDVM;
        private DanhSoHieuTKTDWindow _danhSoHieuTKTDView;
        private Document _doc;
        private Database _db;
        private Editor _ed;
       
        List<ObjectId> blockIdRenSeleted = new List<ObjectId>();
        List<ObjectId> blockIdDamSeleted = new List<ObjectId>();
        private bool _danhSoHieuTuDong = false;
        private void UpdateCurrentDocument()
        {
            _doc = Prima.VinaCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;

            if (_doc == null)
                return;

            _db = _doc.Database;
            _ed = _doc.Editor;
        }
        public DanhSoHieuTKTDAction()
        {
            _danhSoHieuTKTDVM = new DanhSoHieuTKTDVM()
            {
                DrawTableCmd = new RelayCommand(DrawTableInvoke),
                DanhSoCmd = new RelayCommand(DanhSoInvoke),
                CancelCmd = new RelayCommand(CancelInvoke)
            };
            _danhSoHieuTKTDView = new DanhSoHieuTKTDWindow() { DataContext = _danhSoHieuTKTDVM };
        }

        public void Execute()
        {
            try
            {
                UpdateCurrentDocument();
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
                    List<string> strings = new List<string>() { StringDefinition.blockNameTD1, StringDefinition.blockNameTD2, StringDefinition.blockNameTD3, StringDefinition.blockNameTKTKL };

                    string templatePath = BlockTemplateLoader.GetTemplatePath(StringDefinition.BlockTemplates);
                    string[] blockNames = { "TKT_TD", "TKT_B1", "TKT_B3", "TKT_KL" };
                    BlockTemplateLoader.LoadBlocksFromFile(_db, templatePath, blockNames.ToList());
                    List<ObjectId> blockIds = BlockHelper.PickBlocksByBlockNameContains(_db,_ed,strings);

                    blockIdRenSeleted = blockIds
                                         .Where(x => BlockHelper.GetBlockName(_db,x)?.Contains(StringDefinition.blockNameTD1) == true)
                                         .ToList();

                    blockIdDamSeleted = blockIds
                                        .Where(x => BlockHelper.GetBlockName(_db,x)?.Contains(StringDefinition.blockNameTD1) != true)
                                        .ToList();
                    if (blockIds.Count > 0)
                    {
                        _danhSoHieuTKTDView.Show();
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
                if (string.IsNullOrEmpty(_danhSoHieuTKTDVM.SoHieuThepRenStart) || string.IsNullOrEmpty(_danhSoHieuTKTDVM.SoHieuThepKhacStart))
                {
                    MessageBox.Show("Bạn cần nhập số hiệu bắt đầu để đánh số!", StringDefinition.TITLE_MESSAGE);
                    return;
                }
                else
                {

                    if (_danhSoHieuTKTDVM.IsTopToBottom)
                    {
                        blockIdRenSeleted = BlockHelper.SortBlocksTopToBottomLeftToRight(_db,blockIdRenSeleted, 0);
                        blockIdDamSeleted = BlockHelper.SortBlocksTopToBottomLeftToRight(_db,blockIdDamSeleted, 0);
                        NumberBlocks(blockIdRenSeleted, _danhSoHieuTKTDVM.SoHieuThepRenStart);
                        NumberBlocks(blockIdDamSeleted, _danhSoHieuTKTDVM.SoHieuThepKhacStart);
                        _danhSoHieuTuDong = true;
                        MessageBox.Show("Bạn đã đánh số hiệu tự động thép dầm thành công!", StringDefinition.TITLE_MESSAGE);
                    }
                    else
                    {
                        blockIdRenSeleted = BlockHelper.SortBlocksLeftToRightTopToBottom(_db, blockIdRenSeleted, 0);
                        blockIdDamSeleted = BlockHelper.SortBlocksLeftToRightTopToBottom(_db,blockIdDamSeleted, 0);
                        NumberBlocks(blockIdRenSeleted, _danhSoHieuTKTDVM.SoHieuThepRenStart);
                        NumberBlocks(blockIdDamSeleted, _danhSoHieuTKTDVM.SoHieuThepKhacStart);
                        _danhSoHieuTuDong = true;
                        MessageBox.Show("Bạn đã đánh số hiệu tự động thép dầm thành công!", StringDefinition.TITLE_MESSAGE);
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
                if (!_danhSoHieuTuDong)
                {
                    if (!ValidateThepDauDamInput())
                        return;
                }
                _danhSoHieuTKTDView.Hide();

                using (Transaction tr = _db.TransactionManager.StartTransaction())
                {
                    List<ThepDauDam> items = BuildThepDauDamItems(tr);
                    if (!_danhSoHieuTuDong)
                    {
                        if (!ValidateDuplicateSoHieu(_db,_ed,items))
                            return;
                    }
                    items = GroupThepDauDamItems(items);
                    DrawThepDauDamTable(tr, items);

                    tr.Commit();
                }

                _danhSoHieuTKTDView.Close();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(DanhSoHieuTKTDAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }
        private bool ValidateThepDauDamInput()
        {
            if ((blockIdRenSeleted == null || blockIdRenSeleted.Count == 0) && (blockIdDamSeleted == null || blockIdDamSeleted.Count == 0))
            {
                MessageBox.Show("Vui lòng quét chọn block thép đầu dầm.", StringDefinition.TITLE_MESSAGE);
                return false;
            }

            if (string.IsNullOrWhiteSpace(_danhSoHieuTKTDVM.Scale))
            {
                MessageBox.Show("Vui lòng nhập tỷ lệ bản vẽ.", StringDefinition.TITLE_MESSAGE);
                return false;
            }

            return true;
        }
        private bool ValidateDuplicateSoHieu(Database db, Editor ed,List<ThepDauDam> items)
        {
            var groups = items.GroupBy(x => x.soHieu, StringComparer.OrdinalIgnoreCase);
            List<ObjectId> duplicateIds = new List<ObjectId>();
            foreach (var group in groups)
            {
                var first = group.First();

                foreach (var item in group.Skip(1))
                {
                    if (first.duongKinh != item.duongKinh ||
                        first.dangThep != item.dangThep ||
                        first.chieuDaiThanhThep != item.chieuDaiThanhThep ||
                        first.chieuDaiBeMoc != item.chieuDaiBeMoc)
                    {
                        MessageBox.Show("Cùng số hiệu " + first.soHieu.ToUpper() + " nhưng lại khác thông số. Bạn cần dừng lại để kiểm tra!", "Phát Hiện Trùng Số Hiệu!!!");

                        if (first.blockThep != null && !first.blockThep.ObjectId.IsNull)
                            duplicateIds.Add(first.blockThep.ObjectId);

                        if (item.blockThep != null && !item.blockThep.ObjectId.IsNull)
                            duplicateIds.Add(item.blockThep.ObjectId);
                    }
                }
            }
            duplicateIds = duplicateIds.Distinct() .ToList();

            if (duplicateIds.Count > 0)
            {
                TextHelper.SelectAndZoom(db, ed, duplicateIds.ToArray());
                return false;
            }

            return true;
        }
        private List<ThepDauDam> BuildThepDauDamItems(Transaction tr)
        {
            List<ThepDauDam> resultRen = new List<ThepDauDam>();
            List<ThepDauDam> resultDam = new List<ThepDauDam>();

            if (blockIdRenSeleted != null)
            {
                foreach (ObjectId id in blockIdRenSeleted)
                {
                    ThepDauDam item = CreateThepDauDamFromBlock(tr, id, true);

                    if (item != null)
                        resultRen.Add(item);
                }
            }

            if (blockIdDamSeleted != null)
            {
                foreach (ObjectId id in blockIdDamSeleted)
                {
                    ThepDauDam item = CreateThepDauDamFromBlock(tr, id, false);

                    if (item != null)
                        resultDam.Add(item);
                }
            }
            resultRen = resultRen
                   .OrderBy(x => x.soHieu, Comparer<string>.Create(ThepDauDam.CompareSoHieuNatural))
                   .ToList();
            resultDam = resultDam
                   .OrderBy(x => x.soHieu, Comparer<string>.Create(ThepDauDam.CompareSoHieuNatural))
                   .ToList();
            resultRen.AddRange(resultDam);
            return resultRen;
        }

        private List<ThepDauDam> GroupThepDauDamItems(List<ThepDauDam> items)
        {
            return items
                .GroupBy(x => x.soHieu?.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    ThepDauDam first = g.First();

                    return new ThepDauDam
                    {
                        soHieu = first.soHieu,
                        soHieuLaSoDeSort = first.soHieuLaSoDeSort,
                        dangThep = first.dangThep,
                        duongKinh = first.duongKinh,
                        soLuong = g.Sum(x => x.soLuong),
                        chieuDaiThanhThep = first.chieuDaiThanhThep,
                        chieuDaiBeMoc = first.chieuDaiBeMoc
                    };
                })
                .ToList();
        }
        private ThepDauDam CreateThepDauDamFromBlock(Transaction tr, ObjectId id, bool isThepRen)
        {
            BlockReference blockRef = tr.GetObject(id, OpenMode.ForRead) as BlockReference;

            if (blockRef == null)
                return null;

            ThepDauDam item = new ThepDauDam();
            item.blockThep = blockRef;
            item.dangThep = isThepRen ? "thepRen" : "thepKhac";

            ReadThepDauDamAttributes(tr, blockRef, item);

            if (isThepRen)
                item.chieuDaiThanhThep += MathUtils.ToDouble(_danhSoHieuTKTDVM.ChieuDaiCongThemTienRen);

            return item;
        }
        private void ReadThepDauDamAttributes(Transaction tr, BlockReference blockRef, ThepDauDam item)
        {
            Dictionary<string, AttributeReference> atts = BlockHelper.GetAttributeReferences(blockRef, tr);

            item.soHieu = GetAttrText(atts, "SH");
            item.soHieuLaSoDeSort = GetNumberFromText(item.soHieu);
            item.duongKinh = GetDiameterFromAttr(atts);
            item.soLuong = GetQuantityFromAttr(atts);
            item.chieuDaiThanhThep = GetLength(blockRef);
            item.chieuDaiBeMoc = GetHook(blockRef);
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
        private string GetAttrText(Dictionary<string, AttributeReference> atts, string tag)
        {
            if (atts == null || !atts.TryGetValue(tag, out AttributeReference attRef))
                return "";

            return attRef.TextString ?? "";
        }
        private double GetDiameterFromAttr(Dictionary<string, AttributeReference> atts)
        {
            string value = GetAttrText(atts, "DK");

            if (value.ToUpper().Contains("C"))
            {
                string[] arr = value.ToUpper().Split('C');

                if (arr.Length > 1)
                    return MathUtils.ToDouble(arr[1]);
            }

            return MathUtils.ToDouble(value);
        }
        private int GetQuantityFromAttr(Dictionary<string, AttributeReference> atts)
        {
            string value = GetAttrText(atts, "DK");

            if (string.IsNullOrWhiteSpace(value))
                value = GetAttrText(atts, "SL");

            if (string.IsNullOrWhiteSpace(value))
                return 0;

            int index = value.IndexOf("%%", StringComparison.OrdinalIgnoreCase);

            if (index > 0)
                return Convert.ToInt32(MathUtils.ToDouble(value.Substring(0, index)));

            return Convert.ToInt32(MathUtils.ToDouble(value));
        }
        private double GetLength(BlockReference blockRef)
        {
            foreach (DynamicBlockReferenceProperty prop in blockRef.DynamicBlockReferencePropertyCollection)
            {
                if (prop.PropertyName == "Distance1")
                {
                    return MathUtils.LamTronXuongBoiCua5(
                        Math.Round(MathUtils.ToDouble(prop.Value?.ToString()), 0));
                }
            }
            return 0;
        }

        private double GetHook(BlockReference blockRef)
        {
            foreach (DynamicBlockReferenceProperty prop in blockRef.DynamicBlockReferencePropertyCollection)
            {
                if (prop.PropertyName == "Distance2")
                {
                    return MathUtils.LamTronXuongBoiCua5(
                        Math.Round(MathUtils.ToDouble(prop.Value?.ToString()), 0));
                }
            }

            return 0;
        }
        private double GetNumberFromText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            string number = new string(text.Where(char.IsDigit).ToArray());

            return MathUtils.ToDouble(number);
        }

        private void DrawThepDauDamTable(Transaction tr, List<ThepDauDam> items)
        {
            if (items == null || items.Count == 0)
                return;

            BlockTable bt = tr.GetObject(_db.BlockTableId, OpenMode.ForRead) as BlockTable;

            string[] blockNames = { "TKT_TD", "TKT_B1", "TKT_B3", "TKT_KL" };

            if (!BlockTemplateLoader.CheckTableBlocksExist(bt, blockNames))
                return;

            BlockTableRecord modelSpace = tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;

            Point3d? basePoint = PickInsertPoint();

            if (basePoint == null)
                return;

            double scale = GetDrawingScale();

            InsertThepDauDamTableBlocks(tr, bt, modelSpace, basePoint.Value, items, scale);
        }
        private Point3d? PickInsertPoint()
        {
            PromptPointResult result = _ed.GetPoint("\nChọn điểm chèn bảng thống kê thép: ");

            if (result.Status != PromptStatus.OK)
                return null;

            return result.Value;
        }
        private void InsertThepDauDamTableBlocks(Transaction tr, BlockTable bt, BlockTableRecord modelSpace, Point3d basePoint, List<ThepDauDam> items, double scale)
        {
            BlockTableRecord blockTD = tr.GetObject(bt["TKT_TD"], OpenMode.ForRead) as BlockTableRecord;

            InsertBlockTD(tr, modelSpace, blockTD, basePoint, scale);

            Point3d rowBasePoint = MovePointY(basePoint, 20 * scale);

            RebarWeightTotal total = InsertThepDauDamRows(tr, bt, modelSpace, rowBasePoint, items, scale);

            Point3d totalPoint = MovePointY(rowBasePoint, items.Count * 10 * scale);

            InsertTotalWeightBlock(tr, bt, modelSpace, totalPoint, total, scale);
        }
        private Point3d MovePointY(Point3d point, double distance)
        {
            return new Point3d(point.X, point.Y - distance, point.Z);
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
        private RebarWeightTotal InsertThepDauDamRows(Transaction tr, BlockTable bt, BlockTableRecord modelSpace, Point3d basePoint, List<ThepDauDam> items, double scale)
        {
            RebarWeightTotal total = new RebarWeightTotal();

            for (int i = 0; i < items.Count; i++)
            {
                ThepDauDam item = items[i];

                string blockName = GetThepDauDamBlockName(item);

                if (!bt.Has(blockName))
                    continue;

                BlockTableRecord blockDef = tr.GetObject(bt[blockName], OpenMode.ForRead) as BlockTableRecord;
                Point3d point = MovePointY(basePoint, i * 10 * scale);
                List<string> values = GetThepDauDamRowValues(item);

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

            BlockReference blockRef = new BlockReference(point, blockDef.ObjectId);
            blockRef.ScaleFactors = new Scale3d(scale, scale, scale);

            modelSpace.AppendEntity(blockRef);
            tr.AddNewlyCreatedDBObject(blockRef, true);

            foreach (ObjectId id in blockDef)
            {
                AttributeDefinition attDef = tr.GetObject(id, OpenMode.ForRead) as AttributeDefinition;

                if (attDef == null || attDef.Constant)
                    continue;

                AttributeReference attRef = new AttributeReference();
                attRef.SetAttributeFromBlock(attDef, blockRef.BlockTransform);

                SetInitialAttributeValue(attRef, values);

                blockRef.AttributeCollection.AppendAttribute(attRef);
                tr.AddNewlyCreatedDBObject(attRef, true);
            }

            Dictionary<string, AttributeReference> atts = BlockHelper.GetAttributeReferences(blockRef, tr);

            weights = UpdateInsertedTKTBlock(blockDef.Name, atts);

            return weights;
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

        private string GetThepDauDamBlockName(ThepDauDam item)
        {
            if (item.chieuDaiBeMoc > 0)
                return "TKT_B3";

            return "TKT_B1";
        }
        private List<string> GetThepDauDamRowValues(ThepDauDam item)
        {
            if (GetThepDauDamBlockName(item) == "TKT_B3")
                return new List<string> { item.soHieu, item.duongKinh.ToString(), item.soLuong.ToString(), item.chieuDaiThanhThep.ToString(), item.chieuDaiBeMoc.ToString(), "" };

            return new List<string> { item.soHieu, item.duongKinh.ToString(), item.soLuong.ToString(), item.chieuDaiThanhThep.ToString(), "", "" };
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

        private double GetDrawingScale()
        {
            string value = _danhSoHieuTKTDVM.Scale;

            if (value.Contains("/"))
            {
                string[] arr = value.Split('/');
                return MathUtils.ToDouble(arr[1]);
            }

            return MathUtils.ToDouble(value);
        }
        private class RebarWeightTotal
        {
            public double D10 { get; set; }
            public double D10D18 { get; set; }
            public double D18 { get; set; }
        }
        private void CancelInvoke()
        {
            try
            {
                _danhSoHieuTKTDView.Close();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(DanhSoHieuTKTSAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }

        private void NumberBlocks(List<ObjectId> blockIds, string prefix)
        {
            if (blockIds == null || blockIds.Count == 0)
                return;

            prefix = prefix?.Trim();

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
    }
}
