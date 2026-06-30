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
using Tools.AutoCad.Action.Actions;
using Tools.Model;
using Tools.Resources.Definitions;
using Tools.View.UI;
using Tools.ViewModel;
using Tools.VinaCad.Helper.Helper;
using Tools.VinaCad.Modeling;
using MessageBox = System.Windows.MessageBox;
namespace Tools.VinaCad.Action.Actions
{
    public class DrawingCategoriesAction
    {
        private DrawingCategoriesVM _drawingCategoriesVM;
        private DrawingCategoriesWindow _drawingCategoriesView;
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

        public DrawingCategoriesAction()
        {
            _drawingCategoriesVM = new DrawingCategoriesVM()
            {
                DrawTableCmd = new RelayCommand(DrawTableInvoke),
                CancelCmd = new RelayCommand(CancelInvoke)
            };
            _drawingCategoriesView = new DrawingCategoriesWindow() { DataContext = _drawingCategoriesVM };
        }

        public void Execute()
        {
            try
            {
                UpdateCurrentDocument();
                string templatePath = BlockTemplateLoader.GetTemplatePath(StringDefinition.BlockTemplates);
                BlockTemplateLoader.LoadBlocksFromFile(_db, templatePath, new() { StringDefinition.blockNameTKDM_TD, StringDefinition.blockNameTKDM });

                if (NumberingDrawingsSetting.BlockName != null)
                {

                    if (NumberingDrawingsSetting.IsLeftToRight || NumberingDrawingsSetting.IsTopToBottom)
                    {
                        if (NumberingDrawingsSetting.IsTopToBottom)
                        {
                            List<ObjectId> blockIds = BlockHelper.PickBlocksByBlockName(_db,_ed,NumberingDrawingsSetting.BlockName);
                            _ed.SetImpliedSelection(Array.Empty<ObjectId>());
                            _ed.UpdateScreen();
                            if (blockIds.Count() > 0)
                            {
                                blockIds = BlockHelper.SortBlocksTopToBottomLeftToRight(_db, blockIds, NumberingDrawingsSetting.Tolerance);
                                List<DrawingCategoriesModel> drawingCategories = BlockHelper.GetDrawingCategories(_db,blockIds, NumberingDrawingsSetting.DrawingTitle1Tag, NumberingDrawingsSetting.DrawingTitle2Tag, NumberingDrawingsSetting.DrawingTitle3Tag, NumberingDrawingsSetting.DrawingNoTag.ToString());
                               
                                _drawingCategoriesVM.DrawingItems = new ObservableCollection<DrawingCategoriesModel>(drawingCategories);
                                _drawingCategoriesView.ShowDialog();
                            }
                            
                        }
                        if(NumberingDrawingsSetting.IsLeftToRight)
                        {
                            List<ObjectId> blockIds = BlockHelper.PickBlocksByBlockName(_db,_ed,NumberingDrawingsSetting.BlockName);
                            _ed.SetImpliedSelection(Array.Empty<ObjectId>());
                            _ed.UpdateScreen();
                            if (blockIds.Count()>0 )
                            {
                                blockIds = BlockHelper.SortBlocksLeftToRightTopToBottom(_db, blockIds, NumberingDrawingsSetting.Tolerance);
                                List<DrawingCategoriesModel> drawingCategories = BlockHelper.GetDrawingCategories(_db,blockIds, NumberingDrawingsSetting.DrawingTitle1Tag, NumberingDrawingsSetting.DrawingTitle2Tag, NumberingDrawingsSetting.DrawingTitle3Tag, NumberingDrawingsSetting.DrawingNoTag.ToString());

                                _drawingCategoriesVM.DrawingItems = new ObservableCollection<DrawingCategoriesModel>(drawingCategories);
                                _drawingCategoriesView.ShowDialog();
                            }

                           
                        }    
                    }
                }
                else
                {
                    MessageBoxResult result = MessageBox.Show("Bạn chưa xác lập danh mục.", StringDefinition.TITLE_MESSAGE, MessageBoxButton.OK, MessageBoxImage.Warning);

                    if (result == MessageBoxResult.OK)
                    {
                        _doc.SendStringToExecute("XLDM ", true, false, false);
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
        private void DrawTableInvoke()
        {
            try
            {
                int scale = 100;
                if(NumberingDrawingsSetting.Scale!=null)
                {
                    scale = NumberingDrawingsSetting.Scale;
                }    

                using (Transaction tr = _db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = tr.GetObject(_db.BlockTableId, OpenMode.ForRead) as BlockTable;

                    if (!bt.Has(StringDefinition.blockNameTKDM_TD) || !bt.Has(StringDefinition.blockNameTKDM))
                    {
                        MessageBox.Show("Bạn cần chèn block thống kê mẫu vào bản vẽ.",StringDefinition.TITLE_MESSAGE);
                        return;
                    }

                    BlockTableRecord blockTD = tr.GetObject(bt[StringDefinition.blockNameTKDM_TD], OpenMode.ForRead) as BlockTableRecord;

                    BlockTableRecord blockDef = tr.GetObject(bt[StringDefinition.blockNameTKDM], OpenMode.ForRead) as BlockTableRecord;

                    BlockTableRecord modelSpace = tr.GetObject(bt[BlockTableRecord.ModelSpace],OpenMode.ForWrite) as BlockTableRecord;

                    DrawTable( tr,  modelSpace, blockTD, blockDef, scale);
                    _drawingCategoriesView.Close();
                    _ed.SetImpliedSelection(Array.Empty<ObjectId>());
                    _ed.UpdateScreen();
                    tr.Commit();
                }
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(DrawingCategoriesAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }
        private void DrawTable(Transaction tr, BlockTableRecord modelSpace, BlockTableRecord blockTD, BlockTableRecord blockDef, int scale)
        {
            
            _drawingCategoriesView.Hide();
            PromptPointResult pointResult = _ed.GetPoint("\nChọn điểm chèn bảng thống kê: ");
            _drawingCategoriesView.Show();
            _drawingCategoriesView.Activate();

            Point3d basePoint = pointResult.Value;

            using (BlockReference titleBlock = new BlockReference(basePoint, blockTD.ObjectId))
            {
                modelSpace.AppendEntity(titleBlock);

                tr.AddNewlyCreatedDBObject(titleBlock, true);

                titleBlock.ScaleFactors =new Scale3d(scale, scale, scale);
            }

            List<DrawingCategoriesModel> items = _drawingCategoriesVM.DrawingItems.ToList();

            for (int i = 0; i < items.Count; i++)
            {
                DrawingCategoriesModel item = items[i];

                Point3d insertPoint = new Point3d( basePoint.X, basePoint.Y - ((i + 2) * 10 * scale), basePoint.Z);

                using (BlockReference rowBlock = new BlockReference(insertPoint, blockDef.ObjectId))
                {
                    modelSpace.AppendEntity(rowBlock);

                    tr.AddNewlyCreatedDBObject(rowBlock, true);

                    rowBlock.ScaleFactors = new Scale3d(scale, scale, scale);

                    AddTableRowAttributes(tr, blockDef, rowBlock, item);
                }
            }
        }
        private void AddTableRowAttributes(Transaction tr, BlockTableRecord blockDef, BlockReference blockRef, DrawingCategoriesModel item)
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
                    attRef.SetAttributeFromBlock(
                        attDef,
                        blockRef.BlockTransform);

                    if (attDef.Tag.Equals("STT", StringComparison.OrdinalIgnoreCase))
                    {
                        attRef.TextString = item.STT.ToString();
                    }
                    else if (attDef.Tag.Equals("TENBANVE", StringComparison.OrdinalIgnoreCase))
                    {
                        attRef.TextString = item.DrawingName ?? string.Empty;
                    }
                    else if (attDef.Tag.Equals("KIHIEU", StringComparison.OrdinalIgnoreCase))
                    {
                        attRef.TextString = item.DrawingNo ?? string.Empty;
                    }

                    blockRef.AttributeCollection.AppendAttribute(attRef);
                    tr.AddNewlyCreatedDBObject(attRef, true);
                }
            }
        }
        private void CancelInvoke()
        {
            try
            {
                _drawingCategoriesView.Close();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(DrawingCategoriesAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }
    }
}
