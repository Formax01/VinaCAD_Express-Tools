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
    public class TongKhoiLuongThepAction
    {
       
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

        private TKCTVM _TKCTVM;
        private TKCTWindow _TKCTView;

        public TongKhoiLuongThepAction()
        {
            _TKCTView = new TKCTWindow() { DataContext = _TKCTVM };
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
                    List<ObjectId> blockIds = BlockHelper.PickBlocksByBlockNameContains(_db,_ed,new List<string> { StringDefinition.blockNameTKT, StringDefinition.blockNameTKTH });
                    using (Transaction tr = _db.TransactionManager.StartTransaction())
                    {
                        RebarTotalInfo total = new RebarTotalInfo();

                        List<ObjectId> sortedBlockIds = SortBlockIdsLikeOld(blockIds, tr);


                        for (int i = 0; i < sortedBlockIds.Count - 1; i++)
                        {
                            BlockReference blockRef = tr.GetObject(sortedBlockIds[i], OpenMode.ForWrite) as BlockReference;

                            if (blockRef == null)
                                continue;

                            string blockName = BlockHelper.GetBlockName(_db,sortedBlockIds[i]);

                            Dictionary<string, AttributeReference> atts =
                                GetAttributeReferences(blockRef, tr);

                            if (IsTKTDetailBlock( blockName))
                                UpdateOneTKTBlock(tr, blockRef, blockName, atts, total);

                            else if (IsTKHDetailBlock(blockName))
                                UpdateOneTKHBlock(blockName, atts, total);
                        }

                        UpdateTotalBlock(tr, sortedBlockIds[sortedBlockIds.Count - 1], total);
                        TextHelper.ClearSelection(_ed);
                        tr.Commit();
                    }

                }

            }
            catch (Exception ex)
            {
                Logger.Info(nameof(EditThongKeCotThepAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }
        private void UpdateOneTKTBlock(Transaction tr, BlockReference blockRef, string blockName, Dictionary<string, AttributeReference> atts,RebarTotalInfo total)
        {
            double d = GetAttrDouble(atts, "DK");
            double sltb = GetAttrDouble(atts, "SLTB");

            double dai = GetRebarLength(tr, blockRef, blockName, atts, d);
            double cd = sltb * dai / 1000.0;

            SetAttr(atts, "DAI", MathUtils.FormatNumber(dai));
            SetAttr(atts, "CD", MathUtils.FormatNumber(cd));
            SetAttr(atts, "DT", MathUtils.FormatNumber(cd));

            SetRebarWeight(blockName, atts, d, cd, total);
        }
        private List<ObjectId> SortBlockIdsLikeOld(List<ObjectId> blockIds, Transaction tr)
        {
            var remaining = blockIds
                .Select(id => new
                {
                    Id = id,
                    Block = tr.GetObject(id, OpenMode.ForRead) as BlockReference
                })
                .Where(x => x.Block != null)
                .ToList();

            List<ObjectId> result = new List<ObjectId>();

            while (remaining.Count > 0)
            {
                double xMin = remaining.Min(x => x.Block.Position.X);

                var oneColumn = remaining
                    .Where(x => x.Block.Position.X - xMin <= 100)
                    .OrderByDescending(x => x.Block.Position.Y)
                    .ToList();

                result.AddRange(oneColumn.Select(x => x.Id));

                var removedIds = new HashSet<ObjectId>(oneColumn.Select(x => x.Id));

                remaining = remaining
                    .Where(x => !removedIds.Contains(x.Id))
                    .ToList();
            }

            return result;
        }
        private double GetRebarLength(Transaction tr,BlockReference blockRef,string blockName, Dictionary<string, AttributeReference> atts, double d)
        {
            double l1 = GetAttrDouble(atts, "L1");
            double l2 = GetAttrDouble(atts, "L2");
            double l3 = GetAttrDouble(atts, "L3");
            double l4 = GetAttrDouble(atts, "L4");
            double l5 = GetAttrDouble(atts, "L5");

            double length;

            switch (blockName)
            {
                case "TKT_B0":
                    return GetAttrDouble(atts, "DAI");

                case "TKT_B1":
                    length = l1;
                    break;

                case "TKT_B2":
                case "TKT_B23":
                    length = l1 + l2 + l3;
                    break;

                case "TKT_B3":
                    length = l1 + l2;
                    break;

                case "TKT_B4":
                    double ld = GetHookLength(d);
                    SetAllAttr(blockRef, tr, "LD", MathUtils.FormatNumber(ld));
                    length = l1 + 2 * ld;
                    break;

                case "TKT_B5":
                    double hook5 = GetHookLength(d);
                    SetAttr(atts, "L3", MathUtils.FormatNumber(hook5));
                    length = 2 * l1 + 2 * l2 + 2 * hook5;
                    break;

                case "TKT_B6":
                    length = 2 * Math.PI * (l1 / 2.0);
                    break;

                case "TKT_B7":
                    length = l1 + 2 * l2 + l3 + l4 + 2 * l5;
                    break;

                case "TKT_B8":
                    length = Math.PI * l1 + 2 * l2 + 2 * l3;
                    return MathUtils.LamTronBoiCua5(length + ChieuDaiNoiThep(d, length));

                case "TKT_B24":
                    length = l1 + 2 * l2 + 2 * l3;
                    break;

                case "TKT_B25":
                case "TKT_B30":
                    length = l1 + l2 + l3 + l4;
                    break;

                case "TKT_B26":
                    length = l1 + l2 + l3 + l4 + l5;
                    break;

                case "TKT_B99":
                    length = GetLengthB99(atts, d);
                    break;

                case "TKT_B102":
                    double hook102 = GetHookLength(d);
                    SetAttr(atts, "L3", MathUtils.FormatNumber(hook102));
                    length = 2 * l1 + l2 + 2 * hook102;
                    break;

                default:
                    length = l1 + l2 + l3;
                    break;
            }

             return Math.Round(length + ChieuDaiNoiThep(d, length), 0);
        }
        private double GetLengthB99(Dictionary<string, AttributeReference> atts, double d)
        {
            double buoc = GetAttrDouble(atts, "L1");
            double chieuCao = GetAttrDouble(atts, "L2");
            double soBuoc = GetAttrDouble(atts, "L4");

            double duongCheo =
                Math.Round(Math.Sqrt(chieuCao * chieuCao + (buoc / 2) * (buoc / 2)), 0);

            double beMoc = GetHookLength(d);

            SetAttr(atts, "L3", MathUtils.FormatNumber(duongCheo));
            SetAttr(atts, "L5", MathUtils.FormatNumber(beMoc));

            double length = 2 * soBuoc * duongCheo + 2 * beMoc;

            return length;
        }
        private void SetRebarWeight(string blockName, Dictionary<string, AttributeReference> atts, double d, double cd, RebarTotalInfo total)
        {
            double weight = GetRebarWeight(blockName, atts, d, cd);

            if (d <= 10)
            {
                SetAttr(atts, "TL1", MathUtils.FormatNumber(weight));
                SetAttr(atts, "TL2", "");
                SetAttr(atts, "TL3", "");

                if (blockName == "TKT_B1" && IsT5T7(atts))
                    total.T5T7 += weight;
                else
                    total.D10 += weight;
            }
            else if (d <= 18)
            {
                SetAttr(atts, "TL1", "");
                SetAttr(atts, "TL2", MathUtils.FormatNumber(weight));
                SetAttr(atts, "TL3", "");

                if (blockName == "TKT_B1" && IsT127OrT15(atts))
                    total.T127 += weight;
                else
                    total.D10D18 += weight;
            }
            else
            {
                SetAttr(atts, "TL1", "");
                SetAttr(atts, "TL2", "");
                SetAttr(atts, "TL3", MathUtils.FormatNumber(weight));

                total.D18 += weight;
            }
        }

        private double GetRebarWeight(string blockName, Dictionary<string, AttributeReference> atts, double d, double cd)
        {
            string tenThep = GetAttrText(atts, "L2").Trim().ToUpper();

            if (blockName == "TKT_B1")
            {
                if (tenThep == "T12.7" || tenThep == "T13")
                    return Math.Round(cd * 0.785, 2);

                if (tenThep == "T15.24" || tenThep == "T15")
                    return Math.Round(cd * 1.102, 2);
            }

            return Math.Round(TongKLThep(cd, d), 2);
        }

        private void UpdateOneTKHBlock(string blockName,Dictionary<string, AttributeReference> atts, RebarTotalInfo total)
        {
            string qc = GetAttrText(atts, "QC");

            double chieuDai = GetAttrDouble(atts, "DAI");
            double tongSoLuong = GetAttrDouble(atts, "SLTB");

            if (IsLinearTKHBlock(blockName))
            {
                double tongChieuDai = tongSoLuong * chieuDai / 1000.0;

                double kl1Thanh = GetTKHWeightOne(blockName, qc, chieuDai);
                double tongKhoiLuong = kl1Thanh * tongSoLuong;

                double tongKhoiLuongLamTron = Math.Round(tongKhoiLuong, 3);

                SetAttr(atts, "CD", MathUtils.FormatNumber(tongChieuDai));
                SetAttr(atts, "DT", MathUtils.FormatNumber(tongChieuDai));

                SetAttr(atts, "TL1", MathUtils.FormatNumber(kl1Thanh));
                SetAttr(atts, "TL2", MathUtils.FormatNumber(tongKhoiLuongLamTron));

                total.Hinh += tongKhoiLuongLamTron;
            }
            else if (IsPlateTKHBlock(blockName))
            {
                double kl1BanMa = GetTKHWeightOne(blockName, qc, 0);
                double tongKhoiLuong = kl1BanMa * tongSoLuong;

                double tongKhoiLuongLamTron = Math.Round( tongKhoiLuong, 3);

                SetAttr(atts, "TL1", MathUtils.FormatNumber(kl1BanMa));
                SetAttr(atts, "TL2", MathUtils.FormatNumber(tongKhoiLuongLamTron));

                total.Hinh += tongKhoiLuongLamTron;
            }
        }
        private bool IsLinearTKHBlock(string blockName)
        {
            switch (blockName)
            {
                case "TKH_B1":
                case "TKH_B2":
                case "TKH_B3":
                case "TKH_B5":
                case "TKH_B6":
                case "TKH_B7":
                case "TKH_B8":
                case "TKH_B14":
                    return true;

                default:
                    return false;
            }
        }

        private bool IsPlateTKHBlock(string blockName)
        {
            switch (blockName)
            {
                case "TKH_B9":
                case "TKH_B10":
                case "TKH_B11":
                case "TKH_B12":
                case "TKH_B13":
                    return true;

                default:
                    return false;
            }
        }
        private void SetAllAttr(BlockReference blockRef, Transaction tr, string tag, string value)
        {
            foreach (ObjectId attId in blockRef.AttributeCollection)
            {
                AttributeReference attRef =
                    tr.GetObject(attId, OpenMode.ForWrite) as AttributeReference;

                if (attRef == null)
                    continue;

                if (string.Equals(attRef.Tag, tag, StringComparison.OrdinalIgnoreCase))
                    attRef.TextString = value ?? "";
            }
        }
        private bool IsTKTDetailBlock(string blockName)
        {
            switch (blockName)
            {
                case "TKT_B0":
                case "TKT_B1":
                case "TKT_B2":
                case "TKT_B3":
                case "TKT_B4":
                case "TKT_B5":
                case "TKT_B6":
                case "TKT_B7":
                case "TKT_B8":
                case "TKT_B23":
                case "TKT_B24":
                case "TKT_B25":
                case "TKT_B26":
                case "TKT_B30":
                case "TKT_B99":
                case "TKT_B102":
                    return true;

                default:
                    return false;
            }
        }

        private bool IsTKHDetailBlock(string blockName)
        {
            switch (blockName)
            {
                case "TKH_B1":
                case "TKH_B2":
                case "TKH_B3":
                case "TKH_B5":
                case "TKH_B6":
                case "TKH_B7":
                case "TKH_B8":
                case "TKH_B9":
                case "TKH_B10":
                case "TKH_B11":
                case "TKH_B12":
                case "TKH_B13":
                case "TKH_B14":
                    return true;

                default:
                    return false;
            }
        }
        private void SetWeightTKHAttributes(Dictionary<string, AttributeReference> atts, string blockName, string qc, double chieuDai, double tongSoLuong)
        {
            double kl1Thanh = GetTKHWeightOne(blockName, qc, chieuDai);
            double tongKhoiLuong = kl1Thanh * tongSoLuong;

            SetAttr(atts, "TL1", MathUtils.FormatNumber(kl1Thanh));
            SetAttr(atts, "TL2", MathUtils.FormatNumber(tongKhoiLuong));
        }
        private double GetTKHWeightOne(string blockName, string qc, double chieuDai)
        {
            switch (blockName)
            {
                case "TKH_B1":
                    return DTThepHinhLDeu(qc) * chieuDai * 7850 / 1000.0;

                case "TKH_B2":
                    return DTThepHinhIDeu(qc) * chieuDai * 7850 / 1000.0;

                case "TKH_B3":
                    return DTThepHinhLKhongDeu(qc) * chieuDai * 7850 / 1000.0;

                case "TKH_B5":
                    return DTThepHinhCDeu(qc) * chieuDai * 7850 / 1000.0;

                case "TKH_B6":
                    return DTThepHinhHop(qc) * chieuDai * 7850 / 1000.0;

                case "TKH_B7":
                    return DTThepHinhXaGoC(qc) * chieuDai * 7850 / 1000.0;

                case "TKH_B8":
                    return DTThepHinhXaGoZ(qc) * chieuDai * 7850 / 1000.0;

                case "TKH_B14":
                    return DTThepOng(qc) * chieuDai * 7850 / 1000.0;

                case "TKH_B9":
                case "TKH_B11":
                    return KLBanMaChuNhat(qc);

                case "TKH_B10":
                    return KLBanMaTamGiac(qc);

                case "TKH_B12":
                    return KLBanMaTron(qc);

                case "TKH_B13":
                    return KLBanMaHinhThang(qc);

                default:
                    return 0;
            }
        }
        private void UpdateTotalBlock(Transaction tr, ObjectId blockId, RebarTotalInfo total)
        {
            BlockReference blockRef =
                tr.GetObject(blockId, OpenMode.ForWrite) as BlockReference;

            if (blockRef == null)
                return;

            string blockName = BlockHelper.GetBlockName(_db, blockId);

            Dictionary<string, AttributeReference> atts =
                GetAttributeReferences(blockRef, tr);

            if (blockName == "TKT_KL")
            {
                UpdateTKTKLTotal(atts, total);
            }
            else if (blockName == "TKT_KLN")
            {
                UpdateTKTKLNTotal(blockRef, tr, total);
            }
            else if (blockName == "TKH_KLN")
            {
                SetAttr(atts, "TL1", MathUtils.FormatNumber(total.Hinh));
            }
            else
            {
                MessageBox.Show(
                    "Block bạn chọn không có Block Thống Kê Tổng Mẫu, bạn cần xem lại.",
                    StringDefinition.TITLE_MESSAGE);
            }
        }
        private void UpdateTKTKLTotal(Dictionary<string, AttributeReference> atts, RebarTotalInfo total)
        {
            SetAttr(atts, "TL1", total.D10 != 0 ? MathUtils.FormatNumber(total.D10) : "");
            SetAttr(atts, "TL2", total.D10D18 != 0 ? MathUtils.FormatNumber(total.D10D18) : "");
            SetAttr(atts, "TL3", total.D18 != 0 ? MathUtils.FormatNumber(total.D18) : "");

            double sum = total.D10 + total.D10D18 + total.D18;

            SetAttr(atts, "TKL", MathUtils.FormatNumber(sum));
        }
        private void UpdateTKTKLNTotal(BlockReference blockRef, Transaction tr, RebarTotalInfo total)
        {
            foreach (ObjectId attId in blockRef.AttributeCollection)
            {
                AttributeReference attRef = tr.GetObject(attId, OpenMode.ForWrite) as AttributeReference;

                if (attRef == null)
                    continue;

                string tag = attRef.Tag.ToUpper();
                string value = (attRef.TextString ?? "").ToUpper();

                if (tag == "TL1")
                {
                    if (value.Contains("T5+T7"))
                        attRef.TextString = "T5+T7 =" + MathUtils.FormatNumber(total.T5T7);
                    else
                        attRef.TextString = total.D10 != 0 ? MathUtils.FormatNumber(total.D10) : "";
                }

                if (tag == "TL2")
                {
                    if (value.Contains("T12.7"))
                        attRef.TextString = "T12.7 =" + MathUtils.FormatNumber(total.T127);
                    else
                        attRef.TextString = total.D10D18 != 0 ? MathUtils.FormatNumber(total.D10D18) : "";
                }

                if (tag == "TL3")
                    attRef.TextString = total.D18 != 0 ? MathUtils.FormatNumber(total.D18) : "";

                if (tag == "TL5")
                    attRef.TextString = "TOTAL =" + MathUtils.FormatNumber(total.D10 + total.D10D18 + total.D18);

                if (tag == "TL6")
                    attRef.TextString = "TOTAL =" + MathUtils.FormatNumber(total.T5T7 + total.T127);
            }
        }
        private Dictionary<string, AttributeReference> GetAttributeReferences(BlockReference blockRef, Transaction tr)
        {
            Dictionary<string, AttributeReference> result =
                new Dictionary<string, AttributeReference>(StringComparer.OrdinalIgnoreCase);

            foreach (ObjectId attId in blockRef.AttributeCollection)
            {
                AttributeReference attRef =
                    tr.GetObject(attId, OpenMode.ForWrite) as AttributeReference;

                if (attRef == null)
                    continue;

                result[attRef.Tag] = attRef;
            }

            return result;
        }

        private double GetSteelShapeArea(string blockName, string qc)
        {
            switch (blockName)
            {
                case "TKH_B1":
                    return DTThepHinhLDeu(qc);

                case "TKH_B2":
                    return DTThepHinhIDeu(qc);

                case "TKH_B3":
                    return DTThepHinhLKhongDeu(qc);

                case "TKH_B5":
                    return DTThepHinhCDeu(qc);

                case "TKH_B6":
                    return DTThepHinhHop(qc);

                case "TKH_B7":
                    return DTThepHinhXaGoC(qc);

                case "TKH_B8":
                    return DTThepHinhXaGoZ(qc);

                case "TKH_B14":
                    return DTThepOng(qc);

                case "TKH_B9":
                case "TKH_B11":
                    return KLBanMaChuNhat(qc);

                case "TKH_B10":
                    return KLBanMaTamGiac(qc);

                case "TKH_B12":
                    return KLBanMaTron(qc);

                case "TKH_B13":
                    return KLBanMaHinhThang(qc);

                default:
                    return 0;
            }
        }

        // Hàm Tính Diện Tích Thép L cánh đều, đơn vị m2
        public static double DTThepHinhLDeu(string QCThep)
        {
            string chuoiThep = QCThep.Remove(0, 1).Trim().ToUpper();
            string[] arrListStr = chuoiThep.Split('X');
            double L = Convert.ToDouble(arrListStr[0].Trim());
            double T = Convert.ToDouble(arrListStr[1].Trim());
            double DTThep = L * T + (L - T) * T;
            return DTThep / 1000000;
        }
        // Hàm Tính Diện Tích Thép L cánh không đều, đơn vị m2
        public static double DTThepHinhLKhongDeu(string QCThep)
        {
            string chuoiThep = QCThep.Remove(0, 1).Trim().ToUpper();
            string[] arrListStr = chuoiThep.Split('X');
            double L1 = Convert.ToDouble(arrListStr[0].Trim());
            double L2 = Convert.ToDouble(arrListStr[1].Trim());
            double T = Convert.ToDouble(arrListStr[2].Trim());
            double DTThep = L1 * T + (L2 - T) * T;
            return DTThep / 1000000;
        }
        // Hàm Tính Diện Tích Thép I cánh đều, đơn vị m2
        public static double DTThepHinhIDeu(string QCThep)
        {
            string chuoiThep = QCThep.Remove(0, 1).Trim().ToUpper();
            string[] arrListStr = chuoiThep.Split('X');
            double H = Convert.ToDouble(arrListStr[0].Trim());
            double B = Convert.ToDouble(arrListStr[1].Trim());
            double TB = Convert.ToDouble(arrListStr[2].Trim());
            double TC = Convert.ToDouble(arrListStr[3].Trim());
            double DTThep = 2 * B * TC + (H - 2 * TC) * TB;
            return DTThep / 1000000;
        }
        // Hàm Tính Diện Tích Thép C cánh  đều, đơn vị m2
        public static double DTThepHinhCDeu(string QCThep)
        {
            string chuoiThep = QCThep.Remove(0, 1).Trim().ToUpper();
            string[] arrListStr = chuoiThep.Split('X');
            double H = Convert.ToDouble(arrListStr[0].Trim());
            double B = Convert.ToDouble(arrListStr[1].Trim());
            double T = Convert.ToDouble(arrListStr[2].Trim());
            double DTThep = 2 * B * T + (H - 2 * T) * T;
            return DTThep / 1000000;
        }
        // Hàm Tính Diện Tích Thép Hộp, đơn vị m2
        public static double DTThepHinhHop(string QCThep)
        {
            string chuoiThep = QCThep.Remove(0, 1).Trim().ToUpper();
            string[] arrListStr = chuoiThep.Split('X');
            double H = Convert.ToDouble(arrListStr[0].Trim());
            double B = Convert.ToDouble(arrListStr[1].Trim());
            double T = Convert.ToDouble(arrListStr[2].Trim());
            double DTThep = B * H - (H - 2 * T) * (B - 2 * T);
            return DTThep / 1000000;
        }
        // Hàm Tính Diện Tích Thép Ống, đơn vị m2
        public static double DTThepOng(string QCThep)
        {
            string chuoiThep = QCThep.Remove(0, 1).Trim().ToUpper();
            string[] arrListStr = chuoiThep.Split('X');
            double D = Convert.ToDouble(arrListStr[0].Trim());
            double T = Convert.ToDouble(arrListStr[1].Trim());
            double DTThep = Math.PI * (D / 2) * (D / 2) - Math.PI * ((D - 2 * T) / 2) * ((D - 2 * T) / 2);
            return DTThep / 1000000;
        }
        // Hàm Tính Diện Tích Thép Xà Gồ C, đơn vị m2
        public static double DTThepHinhXaGoC(string QCThep)
        {
            string chuoiThep = QCThep.Remove(0, 1).Trim().ToUpper();
            string[] arrListStr = chuoiThep.Split('X');
            double H = Convert.ToDouble(arrListStr[0].Trim());
            double B = Convert.ToDouble(arrListStr[1].Trim());
            double BeMoc = Convert.ToDouble(arrListStr[2].Trim());
            double T = Convert.ToDouble(arrListStr[3].Trim());
            double DTThep = 2 * B * T + 2 * (BeMoc - T) * T + (H - 2 * T) * T;
            return DTThep / 1000000;
        }
        // Hàm Tính Diện Tích Thép Xà Gồ Z, đơn vị m2
        public static double DTThepHinhXaGoZ(string QCThep)
        {
            string chuoiThep = QCThep.Remove(0, 1).Trim().ToUpper();
            string[] arrListStr = chuoiThep.Split('X');
            double H = Convert.ToDouble(arrListStr[0].Trim());
            double B1 = Convert.ToDouble(arrListStr[1].Trim());
            double B2 = Convert.ToDouble(arrListStr[2].Trim());
            double BeMoc = Convert.ToDouble(arrListStr[3].Trim());
            double T = Convert.ToDouble(arrListStr[4].Trim());
            double DTThep = B1 * T + B2 * T + 2 * (BeMoc - T) * T + (H - 2 * T) * T;
            return DTThep / 1000000;
        }
        // Hàm Tính Khối Lượng Bản Mã Chữ Nhật, Đơn vị Kg
        public static double KLBanMaChuNhat(string QCThep)
        {
            string chuoiThep = QCThep.Remove(0, 2).Trim().ToUpper();
            string[] arrListStr = chuoiThep.Split('X');
            double B = Convert.ToDouble(arrListStr[0].Trim());
            double H = Convert.ToDouble(arrListStr[1].Trim());
            double T = Convert.ToDouble(arrListStr[2].Trim());
            double KL = B * H * T * 7850 / 1000000000;
            return KL;
        }
        // Hàm Tính Khối Lượng Bản Mã Tam Giac, Đơn vị Kg
        public static double KLBanMaTamGiac(string QCThep)
        {
            string chuoiThep = QCThep.Remove(0, 2).Trim().ToUpper();
            string[] arrListStr = chuoiThep.Split('X');
            double B = Convert.ToDouble(arrListStr[0].Trim());
            double H = Convert.ToDouble(arrListStr[1].Trim());
            double T = Convert.ToDouble(arrListStr[2].Trim());
            double KL = 0.5 * B * H * T * 7850 / 1000000000;
            return KL;
        }
        // Hàm Tính Khối Lượng Bản Mã Tron, Đơn vị Kg
        public static double KLBanMaTron(string QCThep)
        {
            string chuoiThep = QCThep.Remove(0, 2).Trim().ToUpper();
            string[] arrListStr = chuoiThep.Split('X');
            double D = Convert.ToDouble(arrListStr[0].Trim());
            double T = Convert.ToDouble(arrListStr[1].Trim());
            double KL = Math.PI * (D / 2) * (D / 2) * T * 7850 / 1000000000;
            return KL;
        }
        // Hàm Tính Khối Lượng Bản Mã Hinh Thang, Đơn vị Kg
        public static double KLBanMaHinhThang(string QCThep)
        {
            string chuoiThep = QCThep.Remove(0, 2).Trim().ToUpper();
            string[] arrListStr = chuoiThep.Split('X');
            double B1 = Convert.ToDouble(arrListStr[0].Trim());
            double B2 = Convert.ToDouble(arrListStr[1].Trim());
            double H = Convert.ToDouble(arrListStr[2].Trim());
            double T = Convert.ToDouble(arrListStr[3].Trim());
            double KL = (B2 * H + (B1 - B2) * H * 0.5) * T * 7850 / 1000000000;
            return KL;
        }
        private bool IsT5T7(Dictionary<string, AttributeReference> atts)
        {
            string value = GetAttrText(atts, "L2").Trim().ToUpper();

            return value == "T5" || value == "T7";
        }
        private bool IsT127OrT15(Dictionary<string, AttributeReference> atts)
        {
            string value = GetAttrText(atts, "L2").Trim().ToUpper();

            return value == "T12.7" ||
                   value == "T13" ||
                   value == "T15.24" ||
                   value == "T15";
        }

        private string GetAttrText(Dictionary<string, AttributeReference> atts, string tag)
        {
            if (!atts.TryGetValue(tag, out AttributeReference attRef))
                return "";

            return attRef.TextString ?? "";
        }

        private double GetAttrDouble(Dictionary<string, AttributeReference> atts, string tag)
        {
            return MathUtils.ToDouble(GetAttrText(atts, tag));
        }
        private void SetAttr(Dictionary<string, AttributeReference> atts, string tag, string value)
        {
            if (!atts.TryGetValue(tag, out AttributeReference attRef))
                return;

            attRef.TextString = value ?? "";
        }

        private double ChieuDaiNoiThep(double d, double length)
        {
            if (length <= 11700)
                return 0;

            if (d == 5 || d == 7 || d == 12.7 || d == 13)
                return 0;

            int soLanNoi = Convert.ToInt32(length) / 11700;

            if (d < 10)
                return MathUtils.LamTronBoiCua5(soLanNoi * XLTKCTSetting.LapD10 * d);

            if (d <= 16)
                return MathUtils.LamTronBoiCua5(soLanNoi * XLTKCTSetting.LapD10D16 * d);

            return MathUtils.LamTronBoiCua5(soLanNoi * XLTKCTSetting.LapD16 * d);
        }


        private double GetHookLength(double d)
        {
            double factor = XLTKCTSetting.IsDongDat ? XLTKCTSetting.HookEarthquake : XLTKCTSetting.HookNormal;

            return MathUtils.LamTronBoiCua5(factor * d);
        }


        private double TongKLThep(double cd, double d)
        {
            return cd * d * d * 0.006165;
        }


    }
}
