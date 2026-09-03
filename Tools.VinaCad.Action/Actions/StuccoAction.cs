using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using PrLogTrackingSystem;
using System;
using System.Collections.Generic;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.Resources.Definitions;
using Tools.VinaCad.Helper.Helper;
using Tools.VinaCad.Modeling;
using Tools.VinaCAD.UI;
using Application = Prima.VinaCAD.ApplicationServices.Application;
using MessageBox = System.Windows.MessageBox;

namespace Tools.VinaCad.Action.Actions
{
    public sealed class StuccoAction
    {
        private static StuccoSetting _sessionSettings = new StuccoSetting();

        private Editor _editor = null!;
        private Database _database = null!;

        public void Execute()
        {
            try
            {
                Document document = Application.DocumentManager.MdiActiveDocument;
                if (document == null)
                    throw new InvalidOperationException("Không có bản vẽ đang hoạt động.");

                _editor = document.Editor;
                _database = document.Database;

                StuccoSetting settings = _sessionSettings.Clone();
                EnsureTargetLayer(settings);
                DrawStucco(settings);
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(StuccoAction), ex);
                MessageBox.Show($"Lỗi FN: {ex.Message}", StringDefinition.TITLE_ERROR);
            }
        }

        private void DrawStucco(StuccoSetting settings)
        {
            int processedCount = 0;
            int createdCount = 0;
            int failedCount = 0;
            WriteSettings(settings);

            while (true)
            {
                PromptPointOptions pointOptions = new PromptPointOptions
                    ("\nFN - Chọn điểm trong phòng hoặc [O] Quét chọn tường | [E] Mặt ngoài | [S] Cài đặt <Kết thúc>: ")
                {
                    AllowNone = true,
  //                AllowArbitraryInput = false,
                    AppendKeywordsToMessage = false
                };
                pointOptions.Keywords.Add("O");
                pointOptions.Keywords.Add("E");
                pointOptions.Keywords.Add("S");

                PromptPointResult pointResult = _editor.GetPoint(pointOptions);
                if (pointResult.Status == PromptStatus.Cancel || pointResult.Status == PromptStatus.None)
                    break;
                if (pointResult.Status == PromptStatus.OK)
                {
                    try
                    {
                        int operationCount = DrawInteriorBoundary(pointResult.Value, settings);
                        if (operationCount <= 0)
                            continue;

                        createdCount += operationCount;
                        processedCount++;
                        _editor.UpdateScreen();
                    }
                    catch (Exception ex)
                    {
                        Logger.Info(nameof(DrawStucco), ex);
                        failedCount++;
                    }

                    continue;
                }
                if (pointResult.Status != PromptStatus.Keyword)
                    continue;

                string selectedMode = pointResult.StringResult;
                if (IsMode(selectedMode, "Settings", "S"))
                {
                    if (TryEditSettings(settings, out StuccoSetting updatedSettings))
                    {
                        settings = updatedSettings;
                        EnsureTargetLayer(settings);
                        WriteSettings(settings);
                    }

                    continue;
                }

                try
                {
                    int operationCount = IsMode(selectedMode, "Open", "O")
                        ? DrawOpenWallSelection(settings)
                        : DrawExteriorSelection(settings);
                    if (operationCount > 0)
                    {
                        createdCount += operationCount;
                        processedCount++;
                        _editor.UpdateScreen();
                    }
                }
                catch (Exception ex)
                {
                    Logger.Info(nameof(DrawStucco), ex);
                    failedCount++;
                }

                break;
            }

            _editor.WriteMessage($"\nFN: {processedCount} vùng, {createdCount} đối tượng vữa, {failedCount} lỗi.");
        }

        private static bool IsMode(string value, string globalName, string shortcut)
        {
            return string.Equals(value, globalName, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, shortcut, StringComparison.OrdinalIgnoreCase);
        }

        private int DrawExteriorSelection(StuccoSetting settings)
        {
            TypedValue[] filterValues =
            {
                new TypedValue((int)DxfCode.Start, "LINE,LWPOLYLINE,POLYLINE,ARC,CIRCLE,SPLINE,ELLIPSE")
            };
            PromptSelectionOptions selectionOptions = new PromptSelectionOptions
            {
                MessageForAdding = "\nChọn các cạnh liên tiếp của mặt ngoài, nhấn Enter để xác nhận: "
            };
            PromptSelectionResult selectionResult = _editor.GetSelection(selectionOptions, new SelectionFilter(filterValues));
            if (selectionResult.Status != PromptStatus.OK || selectionResult.Value == null || selectionResult.Value.Count == 0)
                return 0;

            PromptPointOptions sideOptions = new PromptPointOptions("\nChọn điểm phía ngoài để xác định hướng vữa: ");
            PromptPointResult sideResult = _editor.GetPoint(sideOptions);
            if (sideResult.Status != PromptStatus.OK)
                return 0;

            return StuccoHelper.CreateStuccoForSelection(
                _database,
                selectionResult.Value.GetObjectIds(),
                ToWorldPoint(sideResult.Value),
                settings.Thickness,
                settings.LayerName);
        }

        private int DrawOpenWallSelection(StuccoSetting settings)
        {
            TypedValue[] filterValues =
            {
                new TypedValue((int)DxfCode.Start, "LINE,LWPOLYLINE,POLYLINE")
            };
            PromptSelectionOptions selectionOptions = new PromptSelectionOptions
            {
                MessageForAdding = "\nQuét chọn toàn bộ hai mặt của các tường hở, nhấn Enter để xác nhận: "
            };
            PromptSelectionResult selectionResult = _editor.GetSelection(selectionOptions, new SelectionFilter(filterValues));
            if (selectionResult.Status != PromptStatus.OK || selectionResult.Value == null || selectionResult.Value.Count == 0)
                return 0;

            return StuccoHelper.CreateStuccoForOpenWalls(
                _database,
                selectionResult.Value.GetObjectIds(),
                settings.Thickness,
                settings.LayerName);
        }

        private int DrawInteriorBoundary(Point3d interiorPoint, StuccoSetting settings)
        {
            ObjectId[] temporaryBoundaryIds = Array.Empty<ObjectId>();
            try
            {
                _editor.Regen();
                temporaryBoundaryIds = CreateBoundaryWithCoreCommand(interiorPoint, settings.Thickness);
                if (temporaryBoundaryIds.Length == 0)
                    throw new InvalidOperationException("Không tìm thấy biên kín quanh điểm đã chọn. Hãy kiểm tra khe hở và bảo đảm toàn bộ biên phòng đang hiển thị trên màn hình.");

                return StuccoHelper.CreateStuccoForBoundary(
                    _database,
                    temporaryBoundaryIds,
                    ToWorldPoint(interiorPoint),
                    settings.Thickness,
                    settings.LayerName);
            }
            finally
            {
                EraseTemporaryEntities(temporaryBoundaryIds);
            }
        }

        private void EnsureTargetLayer(StuccoSetting settings)
        {
            if (string.IsNullOrWhiteSpace(settings.LayerName))
                settings.LayerName = StuccoSetting.DefaultLayerName;

            string layerName = StuccoHelper.EnsureLayer(
                _database,
                settings.LayerName,
                settings.LayerColorIndex);

            settings.LayerName = layerName;
            _sessionSettings = settings.Clone();
        }

        private void WriteSettings(StuccoSetting settings)
        {
            _editor.WriteMessage($"\nFN: Layer={settings.LayerName}, ACI={settings.LayerColorIndex}, " +$"chiều dày={settings.Thickness:0.###}.");
        }

        private bool TryEditSettings(StuccoSetting initialSettings, out StuccoSetting acceptedSettings)
        {
            StuccoSettingsWindow window = new StuccoSettingsWindow(initialSettings.Clone(), GetAvailableLayerColors());
            Application.ShowModalWindow(window);
            if (window.DialogResult == true && window.AcceptedSettings != null)
            {
                acceptedSettings = window.AcceptedSettings.Clone();
                return true;
            }

            acceptedSettings = initialSettings;
            return false;
        }

        private Dictionary<string, short> GetAvailableLayerColors()
        {
            Dictionary<string, short> layers = new Dictionary<string, short>(StringComparer.OrdinalIgnoreCase);
            using Transaction transaction = _database.TransactionManager.StartTransaction();
            LayerTable layerTable = (LayerTable)transaction.GetObject(_database.LayerTableId, OpenMode.ForRead);
            foreach (ObjectId layerId in layerTable)
            {
                LayerTableRecord layer = (LayerTableRecord)transaction.GetObject(layerId, OpenMode.ForRead);
                short colorIndex = layer.Color.ColorIndex;
                layers[layer.Name] = colorIndex >= 1 && colorIndex <= 255
                    ? colorIndex
                    : StuccoSetting.DefaultLayerColorIndex;
            }

            return layers;
        }

        private ObjectId[] CreateBoundaryWithCoreCommand(Point3d interiorPointInCurrentUcs, double thickness)
        {
            HashSet<ObjectId> idsBefore = GetCurrentSpaceObjectIds();
            (string Name, object Value)[] boundaryVariables =
            {
                ("HPBOUND", 1),
                ("HPBOUNDRETAIN", 1),
                ("HPISLANDDETECTIONMODE", 1),
                ("HPISLANDDETECTION", 0),
                ("CMDECHO", 0),
                ("NOMUTT", 1),
                ("OSMODE", 0),
                ("AUTOSNAP", 0),
                ("OSNAPCOORD", 1)
            };
            object[] previousValues = new object[boundaryVariables.Length];

            double retryOffset = Math.Max(0.0001, Math.Abs(thickness) * 0.01);
            Point3d[] seedPoints =
            {
                interiorPointInCurrentUcs,
                new Point3d(interiorPointInCurrentUcs.X + retryOffset, interiorPointInCurrentUcs.Y + retryOffset, interiorPointInCurrentUcs.Z),
                new Point3d(interiorPointInCurrentUcs.X - retryOffset, interiorPointInCurrentUcs.Y - retryOffset, interiorPointInCurrentUcs.Z)
            };

            try
            {
                for (int index = 0; index < boundaryVariables.Length; index++)
                {
                    previousValues[index] = TryGetSystemVariable(boundaryVariables[index].Name, boundaryVariables[index].Value);
                    TrySetSystemVariable(boundaryVariables[index].Name, boundaryVariables[index].Value);
                }

                foreach (Point3d seedPoint in seedPoints)
                {
                    _editor.Command("_-BOUNDARY", seedPoint, string.Empty);
                    ObjectId[] boundaryIds = GetNewCurrentSpaceCurveIds(idsBefore);
                    if (boundaryIds.Length > 0)
                        return boundaryIds;
                }
            }
            finally
            {
                for (int index = 0; index < boundaryVariables.Length; index++)
                    TrySetSystemVariable(boundaryVariables[index].Name, previousValues[index]);
            }

            return Array.Empty<ObjectId>();
        }

        private ObjectId[] GetNewCurrentSpaceCurveIds(HashSet<ObjectId> idsBefore)
        {
            List<ObjectId> newCurveIds = new List<ObjectId>();
            using Transaction transaction = _database.TransactionManager.StartTransaction();
            BlockTableRecord currentSpace = (BlockTableRecord)transaction.GetObject(
                _database.CurrentSpaceId,
                OpenMode.ForRead);

            foreach (ObjectId objectId in currentSpace)
            {
                if (idsBefore.Contains(objectId))
                    continue;

                if (transaction.GetObject(objectId, OpenMode.ForRead) is Curve)
                    newCurveIds.Add(objectId);
            }

            return newCurveIds.ToArray();
        }

        private Point3d ToWorldPoint(Point3d pointInCurrentUcs)
        {
            return pointInCurrentUcs.TransformBy(_editor.CurrentUserCoordinateSystem);
        }

        private HashSet<ObjectId> GetCurrentSpaceObjectIds()
        {
            HashSet<ObjectId> objectIds = new HashSet<ObjectId>();
            using Transaction transaction = _database.TransactionManager.StartTransaction();
            BlockTableRecord currentSpace = (BlockTableRecord)transaction.GetObject(
                _database.CurrentSpaceId,
                OpenMode.ForRead);

            foreach (ObjectId objectId in currentSpace)
                objectIds.Add(objectId);

            return objectIds;
        }

        private void EraseTemporaryEntities(ObjectId[] objectIds)
        {
            if (objectIds.Length == 0)
                return;

            try
            {
                using Transaction transaction = _database.TransactionManager.StartTransaction();
                foreach (ObjectId objectId in objectIds)
                {
                    if (objectId.IsNull || !objectId.IsValid)
                        continue;

                    if (transaction.GetObject(objectId, OpenMode.ForWrite) is DBObject entity &&
                        !entity.IsErased)
                    {
                        entity.Erase(true);
                    }
                }

                transaction.Commit();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(EraseTemporaryEntities), ex);
            }
        }

        private static object TryGetSystemVariable(string name, object fallbackValue)
        {
            try
            {
                return Application.GetSystemVariable(name);
            }
            catch
            {
                return fallbackValue;
            }
        }

        private static void TrySetSystemVariable(string name, object value)
        {
            try
            {
                object currentValue = Application.GetSystemVariable(name);
                object compatibleValue = value;
                if (currentValue != null && currentValue.GetType() != value.GetType())
                {
                    compatibleValue = Convert.ChangeType(value, currentValue.GetType());
                }

                Application.SetSystemVariable(name, compatibleValue);
            }
            catch
            {
            }
        }
    }
}
