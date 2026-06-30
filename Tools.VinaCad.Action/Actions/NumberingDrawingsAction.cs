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
using Tools.AutoCad.Action.Actions;
using Tools.Resources.Definitions;
using Tools.View.UI;
using Tools.ViewModel;
using Tools.VinaCad.Helper.Helper;
using Tools.VinaCad.Modeling;
using Application = Prima.VinaCAD.ApplicationServices.Application;
using MessageBox = System.Windows.MessageBox;

namespace Tools.VinaCad.Action.Actions
{
    public class NumberingDrawingsAction
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
        public NumberingDrawingsAction()
        {
           
        }
        public void Execute()
        {
            try
            {
                UpdateCurrentDocument();
                if (NumberingDrawingsSetting.BlockName != null)
                {
                    if (NumberingDrawingsSetting.IsLeftToRight || NumberingDrawingsSetting.IsTopToBottom)
                    {
                        if (NumberingDrawingsSetting.Prefix != null && NumberingDrawingsSetting.Suffix != null)
                        {
                            if(int.TryParse(NumberingDrawingsSetting.Suffix, out _))
                            {
                                if (NumberingDrawingsSetting.IsTopToBottom)
                                {
                                    List<ObjectId> blockIds = BlockHelper.PickBlocksByBlockName(_db,_ed,NumberingDrawingsSetting.BlockName);
                                    blockIds = BlockHelper.SortBlocksTopToBottomLeftToRight(_db,blockIds, NumberingDrawingsSetting.Tolerance);
                                    BlockHelper.SetDrawingNoAttributes(_db,blockIds, NumberingDrawingsSetting.DrawingNoTag.ToString(), NumberingDrawingsSetting.Prefix, NumberingDrawingsSetting.Suffix);
                                }
                                if(NumberingDrawingsSetting.IsLeftToRight)
                                {
                                    List<ObjectId> blockIds = BlockHelper.PickBlocksByBlockName(_db,_ed,NumberingDrawingsSetting.BlockName);
                                    blockIds = BlockHelper.SortBlocksLeftToRightTopToBottom(_db, blockIds, NumberingDrawingsSetting.Tolerance);
                                    BlockHelper.SetDrawingNoAttributes(_db,blockIds, NumberingDrawingsSetting.DrawingNoTag.ToString(), NumberingDrawingsSetting.Prefix, NumberingDrawingsSetting.Suffix);
                                }
                              
                            }
                            else
                            {
                                MessageBox.Show("Hậu tố phải là số nguyên.",StringDefinition.TITLE_MESSAGE);
                                return;
                            }   
                        }
                        else
                        {
                            MessageBox.Show("Bạn vui lòng nhập đầy đủ tiền tố và hậu tố.", StringDefinition.TITLE_MESSAGE);
                            return;
                        }
                    }
                    else
                    {
                        MessageBox.Show("Bạn chưa chọn kiểu đánh tên.", StringDefinition.TITLE_MESSAGE);
                        return;
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
                Logger.Info(nameof(SampleAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }

    }
}
