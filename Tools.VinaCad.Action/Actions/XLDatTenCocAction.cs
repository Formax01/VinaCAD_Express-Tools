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
using Tools.VinaCAD.Action.Actions;
using Tools.Resources.Definitions;
using Tools.View.UI;
using Tools.ViewModel;
using Tools.VinaCad.Helper.Helper;
using Tools.VinaCad.Modeling;
using Application = Prima.VinaCAD.ApplicationServices.Application;
using MessageBox = System.Windows.MessageBox;

namespace Tools.VinaCad.Action.Actions
{
    public class XLDatTenCocAction
    {
        private Document _doc;
        private Database _db;
        private Editor _ed;

        private XLDatTenCocVM _vm;
        private XLDatTenCocWindow _view;

        public XLDatTenCocAction() { }

        public void Execute()
        {
            try
            {
                _doc = Application.DocumentManager.MdiActiveDocument;
                if (_doc == null)
                    return;

                _db = _doc.Database;
                _ed = _doc.Editor;

                // prepare VM and window
                _vm = new XLDatTenCocVM
                {
                    Prefix = XLDatTenCocSetting.Prefix,
                    StartNumber = XLDatTenCocSetting.StartNumber,
                    TextHeight = XLDatTenCocSetting.TextHeight,
                    Ordering = XLDatTenCocSetting.Ordering
                };

                // Load text styles from the drawing
                LoadTextStylesFromDrawing();

                // Set the selected text style from saved setting
                _vm.SelectedTextStyle = XLDatTenCocSetting.TextStyleName ?? string.Empty;

                // wire commands (close/save run on UI thread)
                _vm.SaveCmd = new RelayCommand(SaveInvoke);
                _vm.CancelCmd = new RelayCommand(CancelInvoke);

                _view = new XLDatTenCocWindow
                {
                    DataContext = _vm
                };

                // show dialog
                _view.ShowDialog();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(XLDatTenCocAction), ex);
                MessageBox.Show(ex.Message, StringDefinition.TITLE_ERROR);
            }
        }

        private void LoadTextStylesFromDrawing()
        {
            try
            {
                var styleNames = new List<string>();

                using (Transaction tr = _db.TransactionManager.StartTransaction())
                {
                    TextStyleTable tst = tr.GetObject(_db.TextStyleTableId, OpenMode.ForRead) as TextStyleTable;
                    if (tst != null)
                    {
                        foreach (ObjectId tsId in tst)
                        {
                            TextStyleTableRecord tsr = tr.GetObject(tsId, OpenMode.ForRead) as TextStyleTableRecord;
                            if (tsr != null)
                            {
                                styleNames.Add(tsr.Name);
                            }
                        }
                    }
                    tr.Commit();
                }

                _vm.LoadTextStylesFromDrawing(styleNames);
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(LoadTextStylesFromDrawing), ex);
                // If loading fails, continue with empty list
                _vm.LoadTextStylesFromDrawing(new List<string>());
            }
        }

        private void SaveInvoke()
        {
            try
            {
                // basic validation
                if (_vm.StartNumber <= 0)
                {
                    MessageBox.Show("Số bắt đầu phải lớn hơn 0.", StringDefinition.TITLE_MESSAGE);
                    return;
                }
                if (_vm.TextHeight <= 0)
                {
                    MessageBox.Show("Cỡ chữ phải lớn hơn 0.", StringDefinition.TITLE_MESSAGE);
                    return;
                }

                // persist settings for DTC command
                XLDatTenCocSetting.StartNumber = _vm.StartNumber;
                XLDatTenCocSetting.TextHeight = _vm.TextHeight;
                XLDatTenCocSetting.Ordering = _vm.Ordering;
                XLDatTenCocSetting.Prefix = _vm.Prefix ?? string.Empty;
                XLDatTenCocSetting.TextStyleName = _vm.SelectedTextStyle ?? string.Empty;

                _ed?.WriteMessage($"\nXLDTC: Saved settings (Prefix='{_vm.Prefix}', Start={_vm.StartNumber}, Height={_vm.TextHeight}, Ordering={_vm.Ordering}, TextStyle='{_vm.SelectedTextStyle}').\n");

                _view?.Close();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(SaveInvoke), ex);
                MessageBox.Show(ex.Message, StringDefinition.TITLE_ERROR);
            }
        }

        private void CancelInvoke()
        {
            try
            {
                _view?.Close();
                _ed?.WriteMessage("\nXLDTC: Cancelled.\n");
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(CancelInvoke), ex);
            }
        }
    }
}