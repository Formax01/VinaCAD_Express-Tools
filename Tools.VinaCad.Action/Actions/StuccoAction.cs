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
            bool hasInteriorPoint = false;
            WriteSettings(settings);

            while (true)
            {
                string defaultAction = hasInteriorPoint ? "Kết thúc" : "Chọn tường mặt ngoài";
                PromptPointOptions pointOptions = new PromptPointOptions($"\nChọn điểm trong phòng hoặc [S] <{defaultAction}>: ")
                {
                    AllowNone = true,
                    AppendKeywordsToMessage = false
                };
                pointOptions.Keywords.Add("S", "Settings", "Settings");

                PromptPointResult pointResult = _editor.GetPoint(pointOptions);

                if (pointResult.Status == PromptStatus.Keyword)
                {
                    if (TryEditSettings(settings, out StuccoSetting updatedSettings))
                    {
                        settings = updatedSettings;
                        EnsureTargetLayer(settings);
                        WriteSettings(settings);
                    }

                    continue;
                }

                if (pointResult.Status == PromptStatus.Cancel)
                    break;

                if (pointResult.Status == PromptStatus.None)
                {
                    if (hasInteriorPoint)
                        break;

                    try
                    {
                        int outsideCount = DrawExteriorSelection(settings);
                        if (outsideCount > 0)
                        {
                            createdCount += outsideCount;
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

                if (pointResult.Status != PromptStatus.OK)
                    continue;

                hasInteriorPoint = true;
                try
                {
                    createdCount += DrawInteriorBoundary(pointResult.Value, settings);
                    processedCount++;
                    _editor.UpdateScreen();
                }
                catch (Exception ex)
                {
                    Logger.Info(nameof(DrawStucco), ex);
                    failedCount++;
                }
            }

            _editor.WriteMessage($"\nFN: {processedCount} vùng, {createdCount} đối tượng vữa, {failedCount} lỗi.");
        }

        private int DrawExteriorSelection(StuccoSetting settings)
        {
            TypedValue[] filterValues =
            {
                new TypedValue((int)DxfCode.Start, "LINE,LWPOLYLINE,POLYLINE,ARC,CIRCLE,SPLINE,ELLIPSE")
            };
            PromptSelectionOptions selectionOptions = new PromptSelectionOptions
            {
                MessageForAdding = "\nChọn các đường bao mặt ngoài: "
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
            _editor.WriteMessage(
                $"\nFN: Layer={settings.LayerName}, ACI={settings.LayerColorIndex}, " +
                $"chiều dày={settings.Thickness:0.###}.");
        }

        private bool TryEditSettings(StuccoSetting initialSettings, out StuccoSetting acceptedSettings)
        {
            StuccoSetting workingSettings = initialSettings.Clone();

            while (true)
            {
                StuccoSettingsWindow window = new StuccoSettingsWindow(workingSettings);
                Application.ShowModalWindow(window);

                if (window.DialogResult != true)
                {
                    acceptedSettings = initialSettings;
                    return false;
                }

                if (window.AcceptedSettings != null)
                    workingSettings = window.AcceptedSettings.Clone();

                switch (window.RequestedAction)
                {
                    case StuccoSettingRequest.PickLayer:
                        PickLayerFromDrawing(workingSettings);
                        break;

                    case StuccoSettingRequest.MeasureThickness:
                        MeasureThickness(workingSettings);
                        break;

                    case StuccoSettingRequest.Accept:
                        acceptedSettings = workingSettings;
                        return true;

                    default:
                        acceptedSettings = initialSettings;
                        return false;
                }
            }
        }

        private void PickLayerFromDrawing(StuccoSetting settings)
        {
            PromptEntityOptions options = new PromptEntityOptions("\nFN - Chọn đối tượng để lấy layer và màu: ");
            PromptEntityResult result = _editor.GetEntity(options);
            if (result.Status != PromptStatus.OK)
                return;

            using Transaction transaction = _database.TransactionManager.StartTransaction();
            if (transaction.GetObject(result.ObjectId, OpenMode.ForRead) is not Entity entity)
                return;

            LayerTableRecord layer = (LayerTableRecord)transaction.GetObject(entity.LayerId, OpenMode.ForRead);
            settings.LayerName = layer.Name;

            short pickedColorIndex = layer.Color.ColorIndex;
            if (pickedColorIndex >= 1 && pickedColorIndex <= 255)
                settings.LayerColorIndex = pickedColorIndex;
        }

        private void MeasureThickness(StuccoSetting settings)
        {
            PromptDistanceOptions options = new PromptDistanceOptions("\nFN - Chọn hai điểm để đo chiều dày vữa: ");
            PromptDoubleResult result = _editor.GetDistance(options);
            if (result.Status != PromptStatus.OK || result.Value <= 0.0)
                return;

            settings.Thickness = result.Value;
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
