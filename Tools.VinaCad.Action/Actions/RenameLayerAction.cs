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
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCad.Action.Actions
{
    public class RenameLayerAction
    {
        private RenameLayerVM _renameLayerVM;
        private RenameLayerWindow _renameLayerView;
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


        public RenameLayerAction()
        {
            _renameLayerVM = new RenameLayerVM()
            {
                OkCmd = new RelayCommand(OkInvoke),
                CancelCmd = new RelayCommand(CancelInvoke)
            };
            _renameLayerView = new RenameLayerWindow() { DataContext = _renameLayerVM };
        }

        public void Execute()
        {
            try
            {
                UpdateCurrentDocument();
                TextHelper.ClearSelection(_ed);

                _renameLayerVM.CauKienItems = GetRenameLayerItems(_db);
                _renameLayerView.Show();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(RenameLayerAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }
        private void OkInvoke()
        {
            try
            {
                List<RenameLayerModel> renameLayerModels = _renameLayerVM.CauKienItems?.ToList();
                if (renameLayerModels.Count == 0)
                {
                    MessageBox.Show("Chưa có layer nào để đổi.", StringDefinition.TITLE_MESSAGE);
                    return;
                }
                bool allRenameLayerEmpty = renameLayerModels.All(x => x == null || string.IsNullOrWhiteSpace(x.RenameLayer));

                if (allRenameLayerEmpty)
                {
                    MessageBox.Show("Vui lòng copy giá trị vào cột Rename Layer.", StringDefinition.TITLE_MESSAGE);
                    return;
                }

                RenameLayers(renameLayerModels);

                _renameLayerView.Close();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(RenameLayerAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }

        private void CancelInvoke()
        {
            try
            {
                _renameLayerView.Close();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(RenameLayerAction), ex);
                throw new Exception(ex.Message, ex);
            }
        }
        private void RenameLayers(List<RenameLayerModel> renameLayerModels)
        {
            if (renameLayerModels == null || renameLayerModels.Count == 0)
                return;

            using (Transaction tr = _db.TransactionManager.StartTransaction())
            {
                LayerTable layerTable =tr.GetObject(_db.LayerTableId, OpenMode.ForRead) as LayerTable;

                if (layerTable == null)
                    return;

                foreach (RenameLayerModel item in renameLayerModels)
                {
                    if (item == null)
                        continue;

                    if (item.LayerId == ObjectId.Null)
                        continue;

                    if (string.IsNullOrWhiteSpace(item.RenameLayer))
                        continue;

                    string newLayerName = item.RenameLayer.Trim();

                    LayerTableRecord layer = tr.GetObject(item.LayerId, OpenMode.ForWrite) as LayerTableRecord;

                    if (layer == null)
                        continue;

                    if (string.Equals(layer.Name, newLayerName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (string.Equals(layer.Name, "0", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (layerTable.Has(newLayerName))
                    {
                        MessageBox.Show($"Layer \"{newLayerName}\" đã tồn tại.", StringDefinition.TITLE_MESSAGE);
                        continue;
                    }

                    layer.Name = newLayerName;
                }

                tr.Commit();
            }

            _ed.SetImpliedSelection(Array.Empty<ObjectId>());
            _ed.UpdateScreen();
            _ed.Regen();
        }

        public static ObservableCollection<RenameLayerModel> GetRenameLayerItems(Database db)
        {
            ObservableCollection<RenameLayerModel> items = new ObservableCollection<RenameLayerModel>();

            if (db == null)
                return items;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                LayerTable layerTable = tr.GetObject(db.LayerTableId, OpenMode.ForRead) as LayerTable;

                if (layerTable == null)
                    return items;

                foreach (ObjectId layerId in layerTable)
                {
                    LayerTableRecord layer = tr.GetObject(layerId, OpenMode.ForRead) as LayerTableRecord;

                    if (layer == null)
                        continue;

                    items.Add(new RenameLayerModel
                    {
                        LayerId = layerId,
                        NameLayer = layer.Name,
                        RenameLayer = string.Empty
                    });
                }

                tr.Commit();
            }

            return items;
        }
    }
}
