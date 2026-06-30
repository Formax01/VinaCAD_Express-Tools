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
using Teigha.DatabaseServices;
using Tools.Model;
using Tools.Resources.Definitions;
using Tools.View.UI;
using Tools.ViewModel;
using Tools.VinaCad.Helper.Helper;
using Tools.VinaCad.Modeling;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCad.Action.Actions
{
    public class RenameTextAction
    {
        private RenameTextVM _renameTextVM;
        private RenameTextWindow _RenameTextView;
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

        public RenameTextAction()
        {
            _renameTextVM = new RenameTextVM()
            {
                OkCmd = new RelayCommand(OkInvoke),
                CancelCmd = new RelayCommand(CancelInvoke)
            };
            _RenameTextView = new RenameTextWindow() { DataContext = _renameTextVM };
        }

        public void Execute()
        {
            try
            {
                UpdateCurrentDocument();
                TextHelper.ClearSelection(_ed);

                List<ObjectId> textIds = BlockHelper.SelectTextIds(_db,_ed);

                if (textIds == null)
                {
                    return;
                }
                TextHelper.ClearSelection(_ed);
                _renameTextVM.CauKienItems = GetRenameTextItems(_db, textIds);
                _RenameTextView.Show();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(RenameTextAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }

        private void OkInvoke()
        {
            try
            {
                List<RenameTextModel> renameTextModels = _renameTextVM.CauKienItems.ToList();
                if (renameTextModels.Count == 0)
                {
                    MessageBox.Show("Chưa có text nào để đổi.", StringDefinition.TITLE_MESSAGE);
                    return;
                }
                bool allNewTextEmpty = renameTextModels.All(x =>x == null || string.IsNullOrWhiteSpace(x.NewText));
                if (allNewTextEmpty)
                {
                    MessageBox.Show("Vui lòng copy giá trị vào cột New Text.", StringDefinition.TITLE_MESSAGE);
                    return;
                }
                RenameTexts(renameTextModels, _renameTextVM.IsOverride);

                _RenameTextView.Close();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(RenameTextAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }

        private void CancelInvoke()
        {
            try
            {
                _RenameTextView.Close();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(RenameTextAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }

        public static ObservableCollection<RenameTextModel> GetRenameTextItems(Database db,List<ObjectId> textIds)
        {
            ObservableCollection<RenameTextModel> items =
                new ObservableCollection<RenameTextModel>();

            if (db == null || textIds == null || textIds.Count == 0)
                return items;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId textId in textIds)
                {
                    Entity ent = tr.GetObject(textId, OpenMode.ForRead) as Entity;

                    if (ent == null)
                        continue;

                    string textValue = string.Empty;

                    if (ent is DBText dbText)
                    {
                        textValue = dbText.TextString;
                    }
                    else if (ent is MText mText)
                    {
                        textValue = mText.Contents;
                    }
                    else
                    {
                        continue;
                    }

                    items.Add(new RenameTextModel
                    {
                        TextId = textId,
                        Text = textValue,
                        NewText = string.Empty
                    });
                }

                tr.Commit();
            }

            return items;
        }
        private void RenameTexts(List<RenameTextModel> renameTextModels, bool checkOveride)
        {
            if (renameTextModels == null || renameTextModels.Count == 0)
                return;

            using (Transaction tr = _db.TransactionManager.StartTransaction())
            {
                foreach (RenameTextModel item in renameTextModels)
                {
                    if (item == null)
                        continue;

                    if (item.TextId == ObjectId.Null)
                        continue;

                    if (string.IsNullOrWhiteSpace(item.NewText))
                        continue;

                    Entity ent = tr.GetObject(item.TextId, OpenMode.ForWrite) as Entity;

                    if (ent == null)
                        continue;

                    if (ent is DBText dbText)
                    {
                        if (checkOveride)
                        {
                            dbText.TextString = item.NewText;
                        }
                        else
                        {
                            dbText.TextString = $"{dbText.TextString} {item.NewText}";
                        }
                    }
                    else if (ent is MText mText)
                    {
                        if (checkOveride)
                        {
                            mText.Contents = item.NewText;
                        }
                        else
                        {
                            mText.Contents = $"{mText.Contents} {item.NewText}";
                        }
                    }
                }

                tr.Commit();
            }

            _ed.SetImpliedSelection(Array.Empty<ObjectId>());
            _ed.UpdateScreen();
        }
    }
}
