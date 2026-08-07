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
using System.Windows.Automation.Provider;
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
    public class VeCocAction
    {
        private VeCocVM _VeCocVM;
        private VeCocWindow _VeCocView;

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

        public VeCocAction()
        {
            _VeCocVM = new VeCocVM()
            {
                VeCocCmd = new RelayCommand(VeCocInvoke),
                CancelCmd = new RelayCommand(CancelInvoke)
            };
            _VeCocView = new VeCocWindow() { DataContext = _VeCocVM };
        }
        public void Execute()
        {
            try
            {
                UpdateCurrentDocument();
                string templatePath = BlockTemplateLoader.GetTemplatePath(StringDefinition.BlockTemplates);
                BlockTemplateLoader.LoadBlocksFromFile(_db, templatePath, new() { StringDefinition.blockNamecocmm, StringDefinition.blockNamecocm });
                _VeCocView.ShowDialog();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(VeCocAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }

        private void VeCocInvoke()
        {
            try
            {
                List<VeCocModel> veCocs = _VeCocVM.CauKienItems.ToList();
                if (!HasValidData(veCocs))
                {
                    MessageBox.Show("Vui lòng copy dữ liệu vào bảng.", StringDefinition.TITLE_MESSAGE);
                }
                else
                {
                    // Sắp xếp tọa độ cọc một cách tự nhiên (C1, C2, C3... not C1, C10, C2)
                    veCocs = veCocs
                        .OrderBy(x => x.SoHieu, Comparer<string>.Create(CompareSoHieuNatural))
                        .ToList();

                    List<ObjectId> createdIds = new List<ObjectId>();
                    if (_VeCocVM.IsMeter)
                    {
                        createdIds  = DrawPilesByCoordinates(veCocs, StringDefinition.blockNamecocm);
                    }
                    else
                    {
                        createdIds  = DrawPilesByCoordinates(veCocs, StringDefinition.blockNamecocmm);
                    }
                    if(createdIds.Count>0)
                    {
                        TextHelper.SelectAndZoom(_db,_ed,createdIds.ToArray());
                    }    
                    _VeCocView.Close();
                }
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(VeCocAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }

        private void CancelInvoke()
        {
            try
            {
                _VeCocView.Close();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(VeCocAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }
        private bool HasValidData(List<VeCocModel> veCocs)
        {
            if (veCocs == null || veCocs.Count == 0)
                return false;

            if (veCocs.Count == 1)
            {
                VeCocModel item = veCocs[0];

                if ( string.IsNullOrWhiteSpace(item.ToaDoX) &&string.IsNullOrWhiteSpace(item.ToaDoY))
                {
                    return false;
                }
            }

            return true;
        }
        private List<ObjectId> DrawPilesByCoordinates(List<VeCocModel> veCocs, string blockName)
        {
            List<ObjectId> createdIds = new List<ObjectId>();

            using (Transaction tr = _db.TransactionManager.StartTransaction())
            {
                BlockTable bt = tr.GetObject(_db.BlockTableId, OpenMode.ForRead) as BlockTable;

                if (bt == null)
                    return createdIds;

                if (!bt.Has(blockName))
                {
                    MessageBox.Show($"Không tìm thấy block {blockName}.", StringDefinition.TITLE_MESSAGE);
                    return createdIds;
                }

                BlockTableRecord blockDef =
                    tr.GetObject(bt[blockName], OpenMode.ForRead) as BlockTableRecord;

                BlockTableRecord modelSpace =
                    tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;

                if (blockDef == null || modelSpace == null)
                    return createdIds;

                double textHeight = _VeCocVM.IsMeter ? 0.3 : 300;

                foreach (VeCocModel item in veCocs)
                {
                    if (!double.TryParse(item.ToaDoX, out double xInput))
                        continue;

                    if (!double.TryParse(item.ToaDoY, out double yInput))
                        continue;

                    Point3d insertPoint = new Point3d(yInput, xInput, 0);

                    using (BlockReference blockRef = new BlockReference(insertPoint, blockDef.ObjectId))
                    {
                        blockRef.SetDatabaseDefaults(_db);

                        modelSpace.AppendEntity(blockRef);
                        tr.AddNewlyCreatedDBObject(blockRef, true);

                        // Lưu ObjectId block cọc vừa tạo
                        createdIds.Add(blockRef.ObjectId);
                    }

                    DBText text = new DBText
                    {
                        Position = insertPoint,
                        TextString = (item.SoHieu ?? "").ToUpper(),
                        Height = textHeight,
                        Rotation = 0
                    };

                    text.SetDatabaseDefaults(_db);

                    modelSpace.AppendEntity(text);
                    tr.AddNewlyCreatedDBObject(text, true);

                    // Lưu ObjectId text số hiệu vừa tạo
                    createdIds.Add(text.ObjectId);
                }

                tr.Commit();
            }

            return createdIds;
        }
    }
}
