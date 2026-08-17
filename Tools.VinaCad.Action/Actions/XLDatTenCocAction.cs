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

                // Load text styles from the drawing and get available style names
                var styles = LoadTextStylesFromDrawing();

                // Default text style: prefer saved setting; otherwise choose the first text style in drawing; fallback to literal "C"
                if (!string.IsNullOrWhiteSpace(XLDatTenCocSetting.TextStyleName) &&
                    styles.Any(s => string.Equals(s, XLDatTenCocSetting.TextStyleName, StringComparison.OrdinalIgnoreCase)))
                {
                    _vm.SelectedTextStyle = XLDatTenCocSetting.TextStyleName;
                }
                else if (styles.Count > 0)
                {
                    // choose the first text style from drawing as default
                    _vm.SelectedTextStyle = styles.First();
                }
                else
                {
                    // no styles discovered -> use literal "C" as default shown to user
                    _vm.SelectedTextStyle = "C";
                }

                // If there is no saved prefix, try to load default prefix from drawing title block (DRAWINGNO);
                // if not found, fall back to "C"
                if (string.IsNullOrWhiteSpace(XLDatTenCocSetting.Prefix))
                {
                    string prefixFromDrawing = LoadDefaultPrefixFromDrawing();
                    if (!string.IsNullOrWhiteSpace(prefixFromDrawing))
                        _vm.Prefix = prefixFromDrawing;
                    else
                        _vm.Prefix = "C";
                }
                else
                {
                    // saved prefix exists
                    _vm.Prefix = XLDatTenCocSetting.Prefix;
                }

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

        /// <summary>
        /// Loads text style names from the drawing and populates the VM. Returns the list of style names.
        /// </summary>
        private List<string> LoadTextStylesFromDrawing()
        {
            var styleNames = new List<string>();
            try
            {
                using (Transaction tr = _db.TransactionManager.StartTransaction())
                {
                    TextStyleTable tst = tr.GetObject(_db.TextStyleTableId, OpenMode.ForRead) as TextStyleTable;
                    if (tst != null)
                    {
                        foreach (ObjectId tsId in tst)
                        {
                            TextStyleTableRecord tsr = tr.GetObject(tsId, OpenMode.ForRead) as TextStyleTableRecord;
                            if (tsr != null)
                                styleNames.Add(tsr.Name);
                        }
                    }
                    tr.Commit();
                }
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(LoadTextStylesFromDrawing), ex);
            }

            _vm.LoadTextStylesFromDrawing(styleNames);
            return styleNames;
        }

        /// <summary>
        /// Attempt to read a title-block attribute (DRAWINGNO) from modelspace block references and return its value.
        /// </summary>
        private string LoadDefaultPrefixFromDrawing()
        {
            try
            {
                using (Transaction tr = _db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = tr.GetObject(_db.BlockTableId, OpenMode.ForRead) as BlockTable;
                    if (bt == null) return string.Empty;

                    BlockTableRecord ms = tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead) as BlockTableRecord;
                    if (ms == null) return string.Empty;

                    foreach (ObjectId entId in ms)
                    {
                        DBObject obj = tr.GetObject(entId, OpenMode.ForRead);
                        if (obj is BlockReference br)
                        {
                            // iterate attribute references attached to the block reference
                            foreach (ObjectId attId in br.AttributeCollection)
                            {
                                DBObject attObj = tr.GetObject(attId, OpenMode.ForRead);
                                if (attObj is AttributeReference attrRef)
                                {
                                    if (string.Equals(attrRef.Tag, BlockAttributeName.SoHieuBanVe, StringComparison.OrdinalIgnoreCase))
                                    {
                                        string val = (attrRef.TextString ?? string.Empty).Trim();
                                        if (!string.IsNullOrWhiteSpace(val))
                                            return val;
                                    }
                                }
                            }
                        }
                    }
                    tr.Commit();
                }
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(LoadDefaultPrefixFromDrawing), ex);
            }
            return string.Empty;
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