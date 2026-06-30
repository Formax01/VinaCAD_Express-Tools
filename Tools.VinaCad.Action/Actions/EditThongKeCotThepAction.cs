using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using PrLogTrackingSystem;
using PrMVVMCore;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Teigha.DatabaseServices;
using Tools.Model;
using Tools.Resources.Definitions;
using Tools.View.UI;
using Tools.ViewModel;
using Tools.VinaCad.Helper.Helper;
using Tools.VinaCad.Helper.Utils;
using Tools.VinaCad.Modeling;
using MessageBox = System.Windows.MessageBox;

namespace Tools.VinaCad.Action.Actions
{
    public class EditThongKeCotThepAction
    {
        private TKCTVM _TKCTVM;
        private TKCTWindow _TKCTView;
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
        public EditThongKeCotThepAction()
        {
            _TKCTVM = new TKCTVM()
            {
                UpdateCmd = new RelayCommand(UpdateInvoke),
                CancelCmd = new RelayCommand(CancelInvoke)
            };
            _TKCTView = new TKCTWindow() { DataContext = _TKCTVM };
        }
        public void Execute()
        {
            try
            {
                UpdateCurrentDocument();
                TextHelper.ClearSelection(_ed);
                if (XLTKCTSetting.LapD10 == 0 || XLTKCTSetting.LapD10D16 == 0 || XLTKCTSetting.LapD16 == 0)
                {
                    MessageBoxResult result = MessageBox.Show( "Bạn cần xác lập đầy đủ trước khi thống kê thép!", StringDefinition.TITLE_MESSAGE, MessageBoxButton.OK, MessageBoxImage.Warning);

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
                                        StringDefinition.blockNameTKTTD,
                                        StringDefinition.blockNameTKTKLN
                                    };
                    List<ObjectId> blockIds = BlockHelper.PickBlocksByBlockNameContains(_db,_ed,StringDefinition.blockNameTKT)
                                                 .Where(id => !excludeNames.Contains(BlockHelper.GetBlockName(_db,id))).ToList();
                    if (blockIds.Count > 0)
                    {
                        _TKCTVM.CotThepItems = GetTKCTItemsFromBlocks(blockIds);
                        _TKCTView.Show();
                        TextHelper.ClearSelection(_ed);
                    }
                }
                

            }
            catch (Exception ex)
            {
                Logger.Info(nameof(EditThongKeCotThepAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }
        private void CancelInvoke()
        {
            try
            {
                _TKCTView.Close();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(EditThongKeCotThepAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }
        private void UpdateInvoke()
        {
            try
            {
                List<TKCTModel> items = _TKCTVM.CotThepItems.ToList();

                using (Transaction tr = _db.TransactionManager.StartTransaction())
                {
                    int count = Math.Min(blockIdSeleted.Count, items.Count);

                    for (int i = 0; i < count; i++)
                    {
                        UpdateOneTKCTBlock(tr, blockIdSeleted[i], items[i]);
                    }

                    tr.Commit();
                }

                _TKCTView.Close();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(EditThongKeCotThepAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }

        private void UpdateOneTKCTBlock(Transaction tr, ObjectId blockId, TKCTModel item)
        {
            BlockReference blockRef =
                tr.GetObject(blockId, OpenMode.ForWrite) as BlockReference;

            if (blockRef == null)
                return;

            Dictionary<string, AttributeReference> atts =
                GetAttributeReferences(blockRef, tr);

            string blockName = BlockHelper.GetBlockName(_db, blockId);

            UpdateBaseAttributes(atts, item);

            double l1 = GetAttrDouble(atts, "L1");
            double l2 = GetAttrDouble(atts, "L2");
            double l3 = GetAttrDouble(atts, "L3");
            
            double d = MathUtils.ToDouble(item.D);
            double sck = MathUtils.ToDouble(item.SCK);
            double sl = MathUtils.ToDouble(item.SL);
            double tongSoLuong = sl * sck;

            double dai = GetRebarLength(tr, blockRef, blockName, l1, l2, l3, d, atts);
            double cd = dai * tongSoLuong / 1000.0;

            SetAttr(atts, "SLTB", MathUtils.FormatNumber(tongSoLuong));
            SetAttr(atts, "DAI", MathUtils.FormatNumber(dai));
            SetAttr(atts, "DT", MathUtils.FormatNumber(cd));;
            SetAttr(atts, "CD", MathUtils.FormatNumber(cd));;
           

            SetWeightAttributes(atts,blockName, d, cd);
        }
        private Dictionary<string, AttributeReference> GetAttributeReferences( BlockReference blockRef,Transaction tr)
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
        private void UpdateBaseAttributes( Dictionary<string, AttributeReference> atts, TKCTModel item)
        {
            SetAttr(atts, "SH", item.SoHieu);
            SetAttr(atts, "L1", item.L1);
            SetAttr(atts, "L2", item.L2);
            SetAttr(atts, "L3", item.L3);
            SetAttr(atts, "DK", item.D);
            SetAttr(atts, "SL1", item.SL);
            SetAttr(atts, "L4", item.L4);
            SetAttr(atts, "L5", item.L5);
        }
        private double GetRebarLength(Transaction tr,BlockReference blockRef, string blockName, double l1, double l2, double l3, double d, Dictionary<string, AttributeReference> atts)
        {
            double l4 = GetAttrDouble(atts, "L4");
            double l5 = GetAttrDouble(atts, "L5");

            switch (blockName)
            {
                case "TKT_B0":
                    return GetAttrDouble(atts, "DAI");

                case "TKT_B1":
                    return RoundLength(l1, d);

                case "TKT_B2":
                case "TKT_B23":
                    return RoundLength(l1 + l2 + l3, d);

                case "TKT_B3":
                    return RoundLength(l1 + l2, d);

                case "TKT_B4":
                    return GetLengthB4(tr, blockRef, l1, d, atts);

                case "TKT_B5":
                    return GetLengthB5(l1, l2, d, atts);

                case "TKT_B6":
                    return RoundLength(2 * Math.PI * (l1 / 2.0), d);

                case "TKT_B7":
                    return RoundLength(l1 + 2 * l2 + l3 + l4 + 2 * l5, d);

                case "TKT_B8":
                    return GetLengthB8(l1, l2, l3, d);

                case "TKT_B24":
                    return RoundLength(l1 + 2 * l2 + 2 * l3, d);

                case "TKT_B25":
                case "TKT_B30":
                    return RoundLength(l1 + l2 + l3 + l4, d);

                case "TKT_B26":
                    return RoundLength(l1 + l2 + l3 + l4 + l5, d);

                case "TKT_B99":
                    return GetLengthB99(l1, l2, l4, d, atts);

                case "TKT_B102":
                    return GetLengthB102(l1, l2, d, atts);

                default:
                    return RoundLength(l1 + l2 + l3, d);
            }
        }

        
        private string GetSourceImage(string blockName)
        {
            string imageFileName;

            switch (blockName)
            {
                case "TKT_B0":
                case "TKT_B1":
                    imageFileName = "dang01.png";
                    break;

                case "TKT_B2":
                    imageFileName = "dang04.png";
                    break;

                case "TKT_B23":
                    imageFileName = "dang09.png";
                    break;

                case "TKT_B3":
                    imageFileName = "dang03.png";
                    break;

                case "TKT_B4":
                    imageFileName = "dang02.png";
                    break;

                case "TKT_B5":
                    imageFileName = "dang05.png";
                    break;

                case "TKT_B6":
                    imageFileName = "dang07.png";
                    break;

                case "TKT_B7":
                    imageFileName = "dang06.png";
                    break;

                case "TKT_B8":
                    imageFileName = "dang08.png";
                    break;

                case "TKT_B24":
                    imageFileName = "dang10.png";
                    break;

                case "TKT_B25":
                    imageFileName = "dang11.png";
                    break;

                case "TKT_B26":
                    imageFileName = "dang13.png";
                    break;

                case "TKT_B30":
                    imageFileName = "dang12.png";
                    break;

                case "TKT_B99":
                    imageFileName = "dang14.png";
                    break;

                case "TKT_B102":
                    imageFileName = "dang15.png";
                    break;

                default:
                    imageFileName = "dang01.png";
                    break;
            }

            return ImageHelper.GetImagePath(imageFileName);
        }
        private double GetLengthB4(Transaction tr,BlockReference blockRef, double l1,double d,Dictionary<string, AttributeReference> atts)
        {
            double hook = GetHookLength(d);

            SetAllAttr(blockRef, tr, "LD", MathUtils.FormatNumber(hook));

            return RoundLength(l1 + 2 * hook, d);
        }
        private double GetLengthB5(double l1, double l2, double d, Dictionary<string, AttributeReference> atts)
        {
            double hook = GetHookLength(d);
            SetAttr(atts, "L3", MathUtils.FormatNumber(hook));

            return RoundLength(2 * l1 + 2 * l2 + 2 * hook, d);
        }
        private double GetLengthB102(double l1, double l2, double d, Dictionary<string, AttributeReference> atts)
        {
            double hook = GetHookLength(d);
            SetAttr(atts, "L3", MathUtils.FormatNumber(hook));

            return RoundLength(2 * l1 + l2 + 2 * hook, d);
        }
        private double GetLengthB8(double l1, double l2, double l3, double d)
        {
            double length = Math.PI * l1 + 2 * l2 + 2 * l3;
            double lengthWithLap = length + ChieuDaiNoiThep(d, length);

            return MathUtils.LamTronBoiCua5(lengthWithLap);
        }
        private double GetLengthB99(double buoc, double chieuCao, double soBuoc, double d, Dictionary<string, AttributeReference> atts)
        {
            double duongCheoRaw = Math.Sqrt(chieuCao * chieuCao + (buoc / 2.0) * (buoc / 2.0));

            double duongCheoShow = Math.Round(duongCheoRaw, 0);

            double beMoc = GetHookLength(d);

            SetAttr(atts, "L3", MathUtils.FormatNumber(duongCheoShow));
            SetAttr(atts, "L5", MathUtils.FormatNumber(beMoc));

            double length = 2 * soBuoc * duongCheoRaw + 2 * beMoc;

            return RoundLength(length, d);
        }
        private double RoundLength(double length, double d)
        {
            double lengthWithLap = length + ChieuDaiNoiThep(d, length);
            return Math.Round(lengthWithLap, 0);
        }
        private void SetWeightAttributes(Dictionary<string, AttributeReference> atts,string blockName, double d, double cd)
        {
            double weight = TongKLThep(cd, d);

            if (blockName == "TKT_B1")
            {
                if (IsSame(d, 12.7) || IsSame(d, 13))
                {
                    weight = cd * 0.785;
                }
                else if (IsSame(d, 15.24) || IsSame(d, 15))
                {
                    weight = cd * 1.102;
                }
            }

            SetAttr(atts, "TL1", d <= 10 ? MathUtils.FormatNumber(weight) : "");
            SetAttr(atts, "TL2", d > 10 && d <= 18 ? MathUtils.FormatNumber(weight) : "");
            SetAttr(atts, "TL3", d > 18 ? MathUtils.FormatNumber(weight) : "");
        }
        private bool IsSame(double a, double b)
        {
            return Math.Abs(a - b) < 1e-6;
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
        private double GetAttrDouble(Dictionary<string, AttributeReference> atts, string tag)
        {
            if (!atts.TryGetValue(tag, out AttributeReference attRef))
                return 0;

            return MathUtils.ToDouble(attRef.TextString);
        }
        private void SetAttr(Dictionary<string, AttributeReference> atts, string tag,  string value)
        {
            if (atts.TryGetValue(tag, out AttributeReference attRef))
            {
                attRef.TextString = value ?? "";
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
                {
                    attRef.TextString = value ?? "";
                }
            }
        }
        private Dictionary<string, List<AttributeReference>> GetAttributes(BlockReference br)
        {
            var result = new Dictionary<string, List<AttributeReference>>();

            foreach (ObjectId attId in br.AttributeCollection)
            {
                var att = attId.GetObject(OpenMode.ForWrite) as AttributeReference;
                if (att == null) continue;

                string tag = att.Tag.ToUpper();

                if (!result.ContainsKey(tag))
                    result[tag] = new List<AttributeReference>();

                result[tag].Add(att);
            }

            return result;
        }
        private double GetHookLength(double d)
        {
            double factor = XLTKCTSetting.IsDongDat ? XLTKCTSetting.HookEarthquake : XLTKCTSetting.HookNormal;

            return MathUtils.LamTronBoiCua5(factor * d);
        }

        private double TongKLThep(double totalLength, double d)
        {
            return totalLength * d * d * 0.006165;
        }
       
        private ObservableCollection<TKCTModel> GetTKCTItemsFromBlocks(List<ObjectId> blockIds)
        {
            List<TKCTModel> result = new List<TKCTModel>();

            using (Transaction tr = _db.TransactionManager.StartTransaction())
            {
                List<ObjectId> sortedBlockIds = SortBlockIdsLikeOld(blockIds, tr);
                blockIdSeleted = sortedBlockIds;
                blockIdSeleted = sortedBlockIds;
                foreach (ObjectId blockId in sortedBlockIds)
                {
                    BlockReference blockRef = tr.GetObject(blockId, OpenMode.ForRead) as BlockReference;
                    if (blockRef == null)
                        continue;

                    Dictionary<string, string> attrs = BlockHelper.GetBlockAttributes(blockRef, tr);
                    string blockName = BlockHelper.GetBlockName(_db, blockId);
                    double sltb = MathUtils.ToDouble(GetAttr(attrs, "SLTB"));
                    double sl1 = MathUtils.ToDouble(GetAttr(attrs, "SL1"));

                    string sck = "";

                    if (Math.Abs(sl1) > 1e-9)
                    {
                        sck = MathUtils.FormatNumber(sltb / sl1);
                    }

                    result.Add(new TKCTModel
                    {
                        Image = GetSourceImage(blockName),
                        SoHieu = GetAttr(attrs, "SH"),
                        L1 = GetAttr(attrs, "L1"),
                        L2 = GetAttr(attrs, "L2"),
                        L3 = GetAttr(attrs, "L3"),
                        D = GetAttr(attrs, "DK"),
                        SL = GetAttr(attrs, "SL1"),
                        SCK = sck,
                        TCD = !string.IsNullOrEmpty(GetAttr(attrs, "CD"))
                                    ? GetAttr(attrs, "CD")
                                    : GetAttr(attrs, "DT"),
                        L4 = GetAttr(attrs, "L4"),
                        L5 = GetAttr(attrs, "L5"),
                    });
                }

                tr.Commit();
            }

            return new ObservableCollection<TKCTModel>(result);
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

                HashSet<ObjectId> removedIds = new HashSet<ObjectId>(
                    oneColumn.Select(x => x.Id)
                );

                remaining = remaining
                    .Where(x => !removedIds.Contains(x.Id))
                    .ToList();
            }

            return result;
        }
        private string GetAttr(Dictionary<string, string> attrs, string tag)
        {
            return attrs.TryGetValue(tag, out string value)
                ? value
                : string.Empty;
        }
    }
}
