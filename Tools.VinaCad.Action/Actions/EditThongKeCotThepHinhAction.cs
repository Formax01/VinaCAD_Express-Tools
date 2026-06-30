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
    public class EditThongKeCotThepHinhAction
    {
        private EditTKCTHVM _TKCTHVM;
        private EditTKCTHWindow _TKCTHView;
        private Document _doc;
        private Database _db;
        private Editor _ed;
        List<ObjectId> blockIdSeleted = new List<ObjectId>();

        private void UpdateCurrentDocument()
        {
            _doc = Prima.VinaCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;

            if (_doc == null)
                return;

            _db = _doc.Database;
            _ed = _doc.Editor;
        }

        public EditThongKeCotThepHinhAction()
        {
            _TKCTHVM = new EditTKCTHVM()
            {
                UpdateCmd = new RelayCommand(UpdateInvoke),
                CancelCmd = new RelayCommand(CancelInvoke)
            };
            _TKCTHView = new EditTKCTHWindow() { DataContext = _TKCTHVM };
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
                    var excludeNames = new[]
                                    {
                                        StringDefinition.blockNameTKTHTD,
                                        StringDefinition.blockNameTKTHKLN
                                    };
                    List<ObjectId> blockIds = BlockHelper.PickBlocksByBlockNameContains(_db, _ed, StringDefinition.blockNameTKTH)
                                                .Where(id => !excludeNames.Contains(BlockHelper.GetBlockName(_db, id))).ToList();
                    if (blockIds.Count > 0)
                    {
                        _TKCTHVM.ThepHinhItems = GetTKCTHItemsFromBlocks(blockIds);
                        _TKCTHView.Show();
                        TextHelper.ClearSelection(_ed);
                    }
                }    
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(EditThongKeCotThepHinhAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }

        private void UpdateInvoke()
        {
            try
            {

                List<TKCTHModel> items = _TKCTHVM.ThepHinhItems.ToList();

                using (Transaction tr = _db.TransactionManager.StartTransaction())
                {
                    int count = Math.Min(blockIdSeleted.Count, items.Count);

                    for (int i = 0; i < count; i++)
                    {
                        UpdateOneTKCTBlock(tr, blockIdSeleted[i], items[i]);
                    }

                    tr.Commit();
                }


                _TKCTHView.Close();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(EditThongKeCotThepAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }
        private void UpdateOneTKCTBlock(Transaction tr, ObjectId blockId, TKCTHModel item)
        {
            BlockReference blockRef = tr.GetObject(blockId, OpenMode.ForWrite) as BlockReference;

            if (blockRef == null)
                return;

            string blockName = BlockHelper.GetBlockName(_db, blockId);

            Dictionary<string, AttributeReference> atts = GetAttributeReferences(blockRef, tr);

            string qc = item.QCT ?? "";
            double chieuDai = MathUtils.ToDouble(item.CD);
            double sl = MathUtils.ToDouble(item.SL);
            double sck = MathUtils.ToDouble(item.SCK);
            double tongSoLuong = sl * sck;
            double tongChieuDai = tongSoLuong * chieuDai / 1000.0;

            bool isLinear = IsLinearTKHBlock(blockName);

            SetBaseTKHAttributes(atts, item, qc, chieuDai, tongSoLuong, isLinear);

            if (isLinear)
            {
                SetLengthTKHAttributes(atts, tongChieuDai);
            }

            SetWeightTKHAttributes(atts, blockName, qc, chieuDai, tongSoLuong);
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

                var removedIds = new HashSet<ObjectId>(
                    oneColumn.Select(x => x.Id)
                );

                remaining = remaining
                    .Where(x => !removedIds.Contains(x.Id))
                    .ToList();
            }

            return result;

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
        //private void SetBaseTKHAttributes(Dictionary<string, AttributeReference> atts, TKCTHModel item, string qc, double chieuDai, double tongSoLuong)
        //{
        //    SetAttr(atts, "SH", item.SoHieu);
        //    SetAttr(atts, "QC", qc);
        //    string daiValue = MathUtils.FormatNumber(chieuDai);

        //    if (daiValue!="0")
        //    {
        //        SetAttr(atts, "DAI", daiValue);
        //    }
        //    SetAttr(atts, "SL1", item.SL);
        //    SetAttr(atts, "SLTB", MathUtils.FormatNumber(tongSoLuong));
        //}
        private void SetBaseTKHAttributes(Dictionary<string, AttributeReference> atts, TKCTHModel item,string qc, double chieuDai, double tongSoLuong, bool isLinear)
        {
            SetAttr(atts, "SH", item.SoHieu);
            SetAttr(atts, "QC", qc);

            if (isLinear)
            {
                SetAttr(atts, "DAI", MathUtils.FormatNumber(chieuDai));
            }

            SetAttr(atts, "SL1", item.SL);
            SetAttr(atts, "SLTB", MathUtils.FormatNumber(tongSoLuong));
        }
        private void SetLengthTKHAttributes(Dictionary<string, AttributeReference> atts, double tongChieuDai)
        {
            string daiValue = MathUtils.FormatNumber(tongChieuDai);

            if (daiValue != "0")
            {
                SetAttr(atts, "CD", daiValue);
            }
            SetAttr(atts, "DT", MathUtils.FormatNumber(tongChieuDai));
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
        private Dictionary<string, AttributeReference> GetAttributeReferences(BlockReference blockRef, Transaction tr)
        {
            Dictionary<string, AttributeReference> result = new Dictionary<string, AttributeReference>(StringComparer.OrdinalIgnoreCase);

            foreach (ObjectId attId in blockRef.AttributeCollection)
            {
                AttributeReference attRef = tr.GetObject(attId, OpenMode.ForWrite) as AttributeReference;

                if (attRef == null)
                    continue;

                result[attRef.Tag] = attRef;
            }

            return result;
        }
        private void SetAttr(Dictionary<string, AttributeReference> atts, string tag, string value)
        {
            if (!atts.TryGetValue(tag, out AttributeReference attRef))
                return;

            attRef.TextString = value ?? "";
        }
      
        private void CancelInvoke()
        {
            try
            {
                _TKCTHView.Close();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(EditThongKeCotThepHinhAction), ex);
                throw new Exception(ex.Message, ex);
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

        private ObservableCollection<TKCTHModel> GetTKCTHItemsFromBlocks(List<ObjectId> blockIds)
        {
            List<TKCTHModel> result = new List<TKCTHModel>();

            using (Transaction tr = _db.TransactionManager.StartTransaction())
            {
                List<ObjectId> sortedBlockIds = SortBlockIdsLikeOld(blockIds, tr);
                blockIdSeleted = sortedBlockIds;
                foreach (ObjectId blockId in sortedBlockIds)
                {
                    BlockReference blockRef = tr.GetObject(blockId, OpenMode.ForRead) as BlockReference;
                    if (blockRef == null)
                        continue;

                    Dictionary<string, string> attrs = BlockHelper.GetBlockAttributes(blockRef, tr);
                    double sltb = MathUtils.ToDouble(GetAttr(attrs, "SLTB"));
                    double sl1 = MathUtils.ToDouble(GetAttr(attrs, "SL1"));

                    string sck = "";

                    if (Math.Abs(sl1) > 1e-9)
                    {
                        sck = MathUtils.FormatNumber(sltb / sl1);
                    }

                    result.Add(new TKCTHModel
                    {
                        SoHieu = GetAttr(attrs, "SH"),
                        QCT = GetAttr(attrs, "QC"),
                        CD = GetAttr(attrs, "DAI"),
                        SL = GetAttr(attrs, "SL1"),
                        SCK = sck

                    });
                }

                tr.Commit();
            }

            return new ObservableCollection<TKCTHModel>(result);
        }
        private string GetAttr(Dictionary<string, string> attrs, string tag)
        {
            return attrs.TryGetValue(tag, out string value)
                ? value
                : string.Empty;
        }
    }
}
