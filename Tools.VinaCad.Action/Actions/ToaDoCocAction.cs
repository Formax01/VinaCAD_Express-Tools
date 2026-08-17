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
using MessageBox = System.Windows.MessageBox;

namespace Tools.VinaCad.Action.Actions
{
    public class ToaDoCocAction
    {
        private ToaDoCocVM _ToaDoCocVM;
        private ToaDoCocWindow _ToaDoCocView;

        private BangToaDoCocVM _BangToaDoCocVM;
        private BangToaDoCocWindow _BangToaDoCocView;
        private Document _doc;
        private Database _db;
        private Editor _ed;

        // Thực hiện so sánh sắp xếp theo kiểu tự nhiên bằng cách sử dụng CompareTo và xử lý các số trong chuỗi
        public static int CompareSoHieuNatural(string a, string b)
        {
            a = a?.Trim() ?? "";
            b = b?.Trim() ?? "";

            if (a.Length == 0 && b.Length == 0)
                return 0;

            if (a.Length == 0)
                return 1;

            if (b.Length == 0)
                return -1;

            int ia = 0;
            int ib = 0;

            while (ia < a.Length && ib < b.Length)
            {
                char ca = a[ia];
                char cb = b[ib];

                bool isDigitA = char.IsDigit(ca);
                bool isDigitB = char.IsDigit(cb);

                if (isDigitA && isDigitB)
                {
                    string numA = ReadNumber(a, ref ia);
                    string numB = ReadNumber(b, ref ib);

                    long valueA;
                    long valueB;

                    bool okA = long.TryParse(numA, out valueA);
                    bool okB = long.TryParse(numB, out valueB);

                    if (okA && okB)
                    {
                        int cmpNum = valueA.CompareTo(valueB);

                        if (cmpNum != 0)
                            return cmpNum;
                    }
                    else
                    {
                        int cmpTextNum = string.Compare(numA, numB, StringComparison.OrdinalIgnoreCase);

                        if (cmpTextNum != 0)
                            return cmpTextNum;
                    }
                }
                else
                {
                    int cmpChar = char.ToUpperInvariant(ca).CompareTo(char.ToUpperInvariant(cb));

                    if (cmpChar != 0)
                        return cmpChar;

                    ia++;
                    ib++;
                }
            }

            return a.Length.CompareTo(b.Length);
        }

        private static string ReadNumber(string text, ref int index)
        {
            int start = index;

            while (index < text.Length && char.IsDigit(text[index]))
            {
                index++;
            }

            return text.Substring(start, index - start);
        }

        private void UpdateCurrentDocument()
        {
            _doc = Prima.VinaCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;

            if (_doc == null)
                return;

            _db = _doc.Database;
            _ed = _doc.Editor;
        }
        public ToaDoCocAction()
        {
            _ToaDoCocVM = new ToaDoCocVM()
            {
                SelectPileCmd = new RelayCommand(SelectPileInvoke),
                CancelCmd = new RelayCommand(CancelInvoke)
            };
            _ToaDoCocView = new ToaDoCocWindow() { DataContext = _ToaDoCocVM };

            _BangToaDoCocVM = new BangToaDoCocVM()
            {
                DrawTableCmd = new RelayCommand(DrawTableInvoke),
                CancelCmd = new RelayCommand(CancelInvoke)
            };
            _BangToaDoCocView = new BangToaDoCocWindow() { DataContext = _BangToaDoCocVM };
        }
        public void Execute()
        {
            try
            {
                UpdateCurrentDocument();
                _ToaDoCocView.ShowDialog();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(ToaDoCocAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }
        private void DrawTableInvoke()
        {
            try
            {
                string templatePath = BlockTemplateLoader.GetTemplatePath(StringDefinition.BlockTemplates);
                BlockTemplateLoader.LoadBlocksFromFile(_db, templatePath, new() { StringDefinition.blockNameTDC_TD, StringDefinition.blockNameTDC });

                using (Transaction tr = _db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = tr.GetObject(_db.BlockTableId, OpenMode.ForRead) as BlockTable;

                    if (!BlockHelper.CheckTableBlocksExist(bt, new List<string> { StringDefinition.blockNameTDC_TD, StringDefinition.blockNameTDC }))
                    {
                        _ToaDoCocView.Close();
                        return;
                    }

                    BlockTableRecord blockTD = tr.GetObject(bt[StringDefinition.blockNameTDC_TD], OpenMode.ForRead) as BlockTableRecord;

                    BlockTableRecord blockDef = tr.GetObject(bt[StringDefinition.blockNameTDC], OpenMode.ForRead) as BlockTableRecord;

                    BlockTableRecord modelSpace = tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;

                    _BangToaDoCocView.Hide();

                    PromptPointResult pointResult = _ed.GetPoint("\nChọn điểm chèn bảng thống kê: ");

                   

                    if (pointResult.Status != PromptStatus.OK)
                        return;
                    Point3d? basePoint = pointResult.Value;

                    List<ToaDoCocModel> items = _BangToaDoCocVM.CauKienItems.ToList();

                    DrawToaDoCocTable(tr, modelSpace, blockTD, blockDef, basePoint.Value, items, double.Parse(_ToaDoCocVM.Scale.Split('/')[1]));

                    _BangToaDoCocView.Close();

                    tr.Commit();
                }
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(ToaDoCocAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }
       
        private void SelectPileInvoke()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_ToaDoCocVM.CheckDistance.ToString()))
                {
                    _ToaDoCocView.Close();
                    List<ObjectId> blockIds;
                    List<ObjectId> textIds;
                    BlockHelper.PickBlocksByNamesAndTexts(_db,_ed,new List<string> { StringDefinition.blockNamecocm, StringDefinition.blockNamecocmm }, out blockIds, out textIds);
                    _ed.SetImpliedSelection(Array.Empty<ObjectId>());
                    _ed.UpdateScreen();
                    List<BlockTextPair> pairs = GetBlockTextPairs(blockIds, textIds, _ToaDoCocVM.CheckDistance);
                    if (pairs.Count() !=0 )
                    {
                        _BangToaDoCocVM.CauKienItems = GetToaDoCocItemsFromPairs(pairs);
                        _BangToaDoCocView.ShowDialog();
                    }    
                    
                }
                else
                {
                    
                    MessageBox.Show("Bạn phải nhập đầy đủ tỉ lệ và khoảng cách lệch cho phép", StringDefinition.TITLE_MESSAGE);
                }
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(ToaDoCocAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }

        private void CancelInvoke()
        {
            try
            {
                _ToaDoCocView.Close();
                _BangToaDoCocView.Close();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(ToaDoCocAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }
        private List<BlockTextPair> GetBlockTextPairs(List<ObjectId> blockIds, List<ObjectId> textIds, double checkDistance)
        {
            List<BlockTextPair> pairs = new List<BlockTextPair>();

            using (Transaction tr = _db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId blockId in blockIds)
                {
                    BlockReference blockRef = tr.GetObject(blockId, OpenMode.ForRead) as BlockReference;

                    if (blockRef == null)
                        continue;

                    Point3d blockPoint = blockRef.Position;
                    bool hasMatchedText = false;

                    if (textIds != null && textIds.Any())
                    {
                        foreach (ObjectId textId in textIds)
                        {
                            Entity textEnt = tr.GetObject(textId, OpenMode.ForRead) as Entity;

                            Point3d textPoint;

                            if (textEnt is DBText dbText)
                                textPoint = dbText.Position;
                            else if (textEnt is MText mText)
                                textPoint = mText.Location;
                            else
                                continue;

                            double distance = blockPoint.DistanceTo(textPoint);

                            if (distance <= checkDistance)
                            {
                                hasMatchedText = true;

                                pairs.Add(new BlockTextPair
                                {
                                    BlockId = blockId,
                                    TextId = textId,
                                    Distance = distance
                                });
                            }
                        }
                    }

                    // Không tìm thấy text nào phù hợp
                    if (!hasMatchedText)
                    {
                        pairs.Add(new BlockTextPair
                        {
                            BlockId = blockId,
                            TextId = ObjectId.Null,
                            Distance = 0
                        });
                    }
                }

                tr.Commit();
            }

            return pairs;
        }
        private ObservableCollection<ToaDoCocModel> GetToaDoCocItemsFromPairs(List<BlockTextPair> pairs)
        {
            List<ToaDoCocModel> result = new List<ToaDoCocModel>();

            using (Transaction tr = _db.TransactionManager.StartTransaction())
            {
                foreach (BlockTextPair pair in pairs)
                {
                    BlockReference blockRef = pair.BlockId.IsNull? null : tr.GetObject(pair.BlockId, OpenMode.ForRead) as BlockReference;

                    Entity textEnt = pair.TextId.IsNull ? null : tr.GetObject(pair.TextId, OpenMode.ForRead) as Entity;

                    if (blockRef == null)
                        continue;

                    string textContent = string.Empty;

                    if (textEnt is DBText dbText)
                    {
                        textContent = dbText.TextString;
                    }
                    else if (textEnt is MText mText)
                    {
                        textContent = mText.Contents;
                    }

                    Point3d blockPoint = blockRef.Position;

                    result.Add(new ToaDoCocModel
                    {
                        SoHieu = textContent?.Trim() ?? "",
                        KichThuoc = "",
                        ToaDoX = Math.Round(blockPoint.Y, 4).ToString("F4"),
                        ToaDoY = Math.Round(blockPoint.X, 4).ToString("F4"),
                        GhiChu = ""
                    });
                }

                tr.Commit();
            }
            result = result
                    .OrderBy(x => x.SoHieu, Comparer<string>.Create(ToaDoCocModel.CompareSoHieuNatural))
                    .ToList();
            return new ObservableCollection<ToaDoCocModel>(result);
        }

        private void DrawToaDoCocTable(Transaction tr, BlockTableRecord modelSpace, BlockTableRecord blockTD, BlockTableRecord blockDef, Point3d basePoint, List<ToaDoCocModel> items, double scale )
        {
            InsertToaDoCocBlock(tr, modelSpace, blockTD, basePoint, scale);

            for (int i = 0; i < items.Count; i++)
            {
                Point3d rowPoint = new Point3d(
                    basePoint.X,
                    basePoint.Y - ((i * 10 + 20) * scale),
                    basePoint.Z
                );

                BlockReference rowBlock = InsertToaDoCocBlock(tr, modelSpace, blockDef, rowPoint, scale );

                AddToaDoCocAttributes(tr, blockDef, rowBlock, items[i]);
            }
        }
        private BlockReference InsertToaDoCocBlock(Transaction tr, BlockTableRecord modelSpace, BlockTableRecord blockDef, Point3d insertPoint, double scale)
        {
            BlockReference blockRef = new BlockReference(insertPoint, blockDef.ObjectId)
            {
                ScaleFactors = new Scale3d(scale)
            };

            modelSpace.AppendEntity(blockRef);
            tr.AddNewlyCreatedDBObject(blockRef, true);

            return blockRef;
        }
        private void AddToaDoCocAttributes( Transaction tr, BlockTableRecord blockDef, BlockReference blockRef, ToaDoCocModel item)
        {
            foreach (ObjectId id in blockDef)
            {
                DBObject obj = tr.GetObject(id, OpenMode.ForRead);

                if (obj is not AttributeDefinition attDef)
                    continue;

                if (attDef.Constant)
                    continue;

                using (AttributeReference attRef = new AttributeReference())
                {
                    attRef.SetAttributeFromBlock(attDef,blockRef.BlockTransform);

                    string tag = attDef.Tag.ToUpper();

                    if (tag == "STT")
                        attRef.TextString = item.SoHieu?.ToLower() ?? "";

                    else if (tag == "DUONGKINH")
                        attRef.TextString = item.KichThuoc ?? "";

                    else if (tag == "TOADOY")
                        attRef.TextString = item.ToaDoY ?? "";

                    else if (tag == "TOADOX")
                        attRef.TextString = item.ToaDoX ?? "";

                    else if (tag == "GHICHU")
                        attRef.TextString = item.GhiChu ?? "";

                    blockRef.AttributeCollection.AppendAttribute(attRef);
                    tr.AddNewlyCreatedDBObject(attRef, true);
                }
            }
        }
    }
}
