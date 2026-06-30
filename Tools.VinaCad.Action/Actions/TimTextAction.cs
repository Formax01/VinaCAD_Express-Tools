using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools.Resources.Definitions;
using Tools.View.UI;
using Tools.ViewModel;
using System;
using System.Windows;
using Application = Prima.VinaCAD.ApplicationServices.Application;
using PrMVVMCore;
using PrLogTrackingSystem;
using Tools.AutoCad.Action.Actions;
using Prima.VinaCAD.ApplicationServices;
using Teigha.DatabaseServices;
using Prima.VinaCAD.EditorInput;
using Tools.AutoCad.Modeling;
using System.Runtime.InteropServices;
using Teigha.Geometry;
using System.Globalization;
using Tools.VinaCad.Modeling;
using Tools.VinaCad.Helper.Helper;
using MessageBox = System.Windows.MessageBox;

namespace Tools.VinaCad.Action.Actions
{
    public class TimTextAction
    {
        private TimTextVM _findtextVM;
        private TimTextWindow _findtextView;
       
        private bool _needScan;
        private string _scanText;

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
        public TimTextAction()
        {
            _findtextVM = new TimTextVM()
            {
                SelectAllCmd = new RelayCommand(SelectAllInvoke),
                ScanSelectCmd = new RelayCommand(ScanSelectInvoke)
            };
            _findtextView = new TimTextWindow() { DataContext = _findtextVM };
        }

        public void Execute()
        {
            try
            {
                UpdateCurrentDocument();
                if (!string.IsNullOrEmpty(TextContent.Content))
                {

                    _findtextVM.TextFind = TextContent.Content;
                    _findtextVM.IsContainsMatch = TextContent.IsContainsMatch;
                    _findtextVM.IsExactMatch = TextContent.IsExactMatch;
                }
                _findtextView.ShowDialog();

            }
            catch (Exception ex)
            {
                Logger.Info(nameof(TimTextAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }

        private void SelectAllInvoke()
        {
            try
            {
                
                string findText = GetAndSaveFindText();

                if (!IsValidFindText(findText))
                    return;

                TextHelper.ClearSelection(_ed);

                List<ObjectId> textIds = GetTextIdsContains(_db, _ed, findText);

                if (!HasResult(textIds, findText))
                    return;

                _findtextView.Close();

                ObjectId[] objectIds = GetValidObjectIds(textIds, _db);

               TextHelper.SelectAndZoom(_db, _ed, objectIds);
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(TimTextAction), ex);
                throw;
            }
        }
        private void ScanSelectInvoke()
        {
            try
            {
               
                string findText = GetAndSaveFindText();

                if (!IsValidFindText(findText))
                    return;

                _findtextView.Hide();

                TextHelper.ClearSelection(_ed);

                List<ObjectId> textIds = ScanTextIdsContains(_db, _ed, findText);

                if (textIds == null || textIds.Count == 0)
                {
                    _findtextView.Show();
                    MessageBox.Show($"Không tìm thấy text chứa '{findText}'.", StringDefinition.TITLE_MESSAGE);
                    return;
                }

                ObjectId[] objectIds = GetValidObjectIds(textIds, _db);

                TextHelper.SelectAndZoom(_db, _ed, objectIds);

                _findtextView.Close();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(TimTextAction), ex);
                throw;
            }
        }

        private string GetAndSaveFindText()
        {
            string findText = _findtextVM.TextFind;

            TextContent.Content = _findtextVM.TextFind;
            TextContent.IsContainsMatch = _findtextVM.IsContainsMatch;
            TextContent.IsExactMatch = _findtextVM.IsExactMatch;

            return findText;
        }
        private bool IsValidFindText(string findText)
        {
            if (!string.IsNullOrWhiteSpace(findText))
                return true;

            MessageBox.Show("Vui lòng nhập Text cần tìm.", StringDefinition.TITLE_MESSAGE);
            return false;
        }

        private bool HasResult(List<ObjectId> textIds, string findText)
        {
            if (textIds != null && textIds.Count > 0)
                return true;

            MessageBox.Show($"Không tìm thấy text chứa '{findText}'.", StringDefinition.TITLE_MESSAGE);
            return false;
        }

        private ObjectId[] GetValidObjectIds(List<ObjectId> ids, Database db)
        {
            if (ids == null || db == null)
                return Array.Empty<ObjectId>();

            return ids
                .Where(x => !x.IsNull && x.IsValid && x.Database == db)
                .Distinct()
                .ToArray();
        }
        
        
        
        private List<ObjectId> ScanTextIdsContains(Database db, Editor ed, string findText)
        {
            List<ObjectId> textIds = new List<ObjectId>();

            TypedValue[] filterValues =
            {
        new TypedValue((int)DxfCode.Start, "TEXT,MTEXT")
    };

            SelectionFilter filter = new SelectionFilter(filterValues);

            PromptSelectionOptions opt = new PromptSelectionOptions();
            opt.MessageForAdding = "\nQuét chọn vùng chứa text: ";
            opt.RejectObjectsOnLockedLayers = true;

            PromptSelectionResult result = ed.GetSelection(opt, filter);

            if (result.Status != PromptStatus.OK)
                return textIds;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject selObj in result.Value)
                {
                    if (selObj == null)
                        continue;

                    Entity ent = tr.GetObject(selObj.ObjectId, OpenMode.ForRead) as Entity;

                    if (ent == null)
                        continue;

                    string textValue = null;

                    if (ent is DBText dbText)
                    {
                        textValue = dbText.TextString;
                    }
                    else if (ent is MText mText)
                    {
                        textValue = mText.Contents;
                    }

                    if (string.IsNullOrWhiteSpace(textValue))
                        continue;

                    bool isMatch = false;

                    if (_findtextVM.IsExactMatch)
                    {
                        isMatch = string.Equals(
                            textValue.Trim(),
                            findText.Trim(),
                            StringComparison.OrdinalIgnoreCase);
                    }
                    else if (_findtextVM.IsContainsMatch)
                    {
                        isMatch = textValue.IndexOf(findText, StringComparison.OrdinalIgnoreCase) >= 0;
                    }

                    if (isMatch)
                    {
                        textIds.Add(selObj.ObjectId);
                    }
                }

                tr.Commit();
            }

            ed.SetImpliedSelection(Array.Empty<ObjectId>());
            ed.UpdateScreen();

            return textIds;
        }

        

        private List<ObjectId> GetTextIdsContains(Database db, Editor ed, string findText)
        {
            List<ObjectId> textIds = new List<ObjectId>();

            TypedValue[] values =
            {
            new TypedValue((int)DxfCode.Start, "TEXT,MTEXT")
                };

            SelectionFilter filter = new SelectionFilter(values);

            PromptSelectionResult result = ed.SelectAll(filter);

            if (result.Status != PromptStatus.OK)
                return textIds;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject selectedObj in result.Value)
                {
                    if (selectedObj == null)
                        continue;

                    Entity ent = tr.GetObject(selectedObj.ObjectId, OpenMode.ForRead) as Entity;

                    if (ent == null)
                        continue;

                    string textValue = null;

                    if (ent is DBText dbText)
                    {
                        textValue = dbText.TextString;
                    }
                    else if (ent is MText mText)
                    {
                        textValue = mText.Contents;
                    }

                    if (string.IsNullOrWhiteSpace(textValue))
                        continue;

                    bool isMatch = false;

                    if (_findtextVM.IsExactMatch)
                    {
                        isMatch = string.Equals(
                            textValue.Trim(),
                            findText.Trim(),
                            StringComparison.OrdinalIgnoreCase);
                    }
                    else if (_findtextVM.IsContainsMatch)
                    {
                        isMatch = textValue.IndexOf(findText, StringComparison.OrdinalIgnoreCase) >= 0;
                    }

                    if (isMatch)
                    {
                        textIds.Add(selectedObj.ObjectId);
                    }
                }

                tr.Commit();
            }

            ed.SetImpliedSelection(Array.Empty<ObjectId>());
            ed.UpdateScreen();

            return textIds;
        }


    }
}
