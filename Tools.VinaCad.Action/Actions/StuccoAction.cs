using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using PrLogTrackingSystem;
using System;
using System.Windows;
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
                PromptPointOptions pointOptions = new PromptPointOptions("\nChọn điểm trong phòng hoặc [S] Settings <Kết thúc>: ")
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
                        _sessionSettings = settings.Clone();
                        WriteSettings(settings);
                    }

                    continue;
                }

                if (pointResult.Status == PromptStatus.None ||
                    pointResult.Status == PromptStatus.Cancel)
                    break;

                if (pointResult.Status != PromptStatus.OK)
                    continue;

                try
                {
                    createdCount += DrawInteriorBoundary(pointResult.Value, settings);
                    processedCount++;
                    _editor.UpdateScreen();
                }
                catch (Exception ex)
                {
                    ReportBoundaryError(ex);
                    failedCount++;
                }
            }

            _editor.WriteMessage($"\nFN: {processedCount} vùng, {createdCount} đối tượng vữa, {failedCount} lỗi.");
        }

        private int DrawInteriorBoundary(Point3d interiorPoint, StuccoSetting settings)
        {
            ObjectId[] temporaryBoundaryIds = Array.Empty<ObjectId>();
            try
            {
                _editor.Regen();
                temporaryBoundaryIds = CreateBoundaryWithCoreCommand(
                    interiorPoint,
                    settings.Thickness);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Bước tạo biên kín bằng -BOUNDARY thất bại: {ex.Message}",
                    ex);
            }

            if (temporaryBoundaryIds.Length == 0)
                throw new InvalidOperationException("Không tìm thấy biên kín quanh điểm đã chọn. Hãy kiểm tra khe hở và bảo đảm toàn bộ biên phòng đang hiển thị trên màn hình.");

            try
            {
                Point3d sidePointWcs = ToWorldPoint(interiorPoint);
                try
                {
                    int count = StuccoHelper.CreateStuccoForBoundary(
                        _database,
                        temporaryBoundaryIds,
                        sidePointWcs,
                        settings.Thickness,
                        settings.LayerName);

                    if (count == 0)
                        throw new InvalidOperationException($"Biên tìm được ({GetEntityTypeSummary(temporaryBoundaryIds)}) không thể tạo đường vữa.");

                    return count;
                }
                catch (Exception ex)
                {
                    string boundaryTypes = GetEntityTypeSummary(temporaryBoundaryIds);
                    throw new InvalidOperationException(
                        $"Bước tạo và nối vữa ({boundaryTypes}) thất bại: {ex.Message}",
                        ex);
                }
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

        private bool TryEditSettings(
            StuccoSetting initialSettings,
            out StuccoSetting acceptedSettings)
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

            transaction.Commit();
        }

        private void MeasureThickness(StuccoSetting settings)
        {
            PromptDistanceOptions options = new PromptDistanceOptions("\nFN - Chọn hai điểm để đo chiều dày vữa: ");
            PromptDoubleResult result = _editor.GetDistance(options);
            if (result.Status != PromptStatus.OK || result.Value <= 0.0)
                return;

            settings.Thickness = result.Value;
        }

        private void ReportBoundaryError(Exception ex)
        {
            Logger.Info(nameof(DrawStucco), ex);
        }

        private ObjectId[] CreateBoundaryWithCoreCommand(
            Point3d interiorPointInCurrentUcs,
            double thickness)
        {
            System.Collections.Generic.HashSet<ObjectId> idsBefore = GetCurrentSpaceObjectIds();
            object previousHpBound = TryGetSystemVariable("HPBOUND", 1);
            object previousHpBoundRetain = TryGetSystemVariable("HPBOUNDRETAIN", 1);
            object previousCommandEcho = TryGetSystemVariable("CMDECHO", 1);
            object previousNoMutt = TryGetSystemVariable("NOMUTT", 0);
            object previousOsMode = TryGetSystemVariable("OSMODE", 0);
            object previousAutoSnap = TryGetSystemVariable("AUTOSNAP", 0);
            object previousOsnapCoord = TryGetSystemVariable("OSNAPCOORD", 0);

            double retryOffset = Math.Max(0.0001, Math.Abs(thickness) * 0.01);
            Point3d[] seedPoints =
            {
                interiorPointInCurrentUcs,
                new Point3d(interiorPointInCurrentUcs.X + retryOffset, interiorPointInCurrentUcs.Y + retryOffset, interiorPointInCurrentUcs.Z),
                new Point3d(interiorPointInCurrentUcs.X - retryOffset, interiorPointInCurrentUcs.Y - retryOffset, interiorPointInCurrentUcs.Z)
            };

            try
            {
                TrySetSystemVariable("HPBOUND", 1);
                TrySetSystemVariable("HPBOUNDRETAIN", 1);
                TrySetSystemVariable("CMDECHO", 0);
                TrySetSystemVariable("NOMUTT", 1);
                TrySetSystemVariable("OSMODE", 0);
                TrySetSystemVariable("AUTOSNAP", 0);
                TrySetSystemVariable("OSNAPCOORD", 1);

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
                TrySetSystemVariable("HPBOUND", previousHpBound);
                TrySetSystemVariable("HPBOUNDRETAIN", previousHpBoundRetain);
                TrySetSystemVariable("CMDECHO", previousCommandEcho);
                TrySetSystemVariable("NOMUTT", previousNoMutt);
                TrySetSystemVariable("OSMODE", previousOsMode);
                TrySetSystemVariable("AUTOSNAP", previousAutoSnap);
                TrySetSystemVariable("OSNAPCOORD", previousOsnapCoord);
            }

            return Array.Empty<ObjectId>();
        }

        private ObjectId[] GetNewCurrentSpaceCurveIds(
            System.Collections.Generic.HashSet<ObjectId> idsBefore)
        {
            System.Collections.Generic.List<ObjectId> newCurveIds =
                new System.Collections.Generic.List<ObjectId>();
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

            transaction.Commit();
            return newCurveIds.ToArray();
        }

        private Point3d ToWorldPoint(Point3d pointInCurrentUcs)
        {
            return pointInCurrentUcs.TransformBy(_editor.CurrentUserCoordinateSystem);
        }

        private System.Collections.Generic.HashSet<ObjectId> GetCurrentSpaceObjectIds()
        {
            System.Collections.Generic.HashSet<ObjectId> objectIds =
                new System.Collections.Generic.HashSet<ObjectId>();
            using Transaction transaction = _database.TransactionManager.StartTransaction();
            BlockTableRecord currentSpace = (BlockTableRecord)transaction.GetObject(
                _database.CurrentSpaceId,
                OpenMode.ForRead);

            foreach (ObjectId objectId in currentSpace)
                objectIds.Add(objectId);

            transaction.Commit();
            return objectIds;
        }

        private string GetEntityTypeSummary(ObjectId[] objectIds)
        {
            System.Collections.Generic.HashSet<string> typeNames =
                new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            using Transaction transaction = _database.TransactionManager.StartTransaction();

            foreach (ObjectId objectId in objectIds)
            {
                if (!objectId.IsNull && objectId.IsValid &&
                    transaction.GetObject(objectId, OpenMode.ForRead) is DBObject entity)
                {
                    typeNames.Add(entity.GetType().Name);
                }
            }

            return typeNames.Count == 0 ? "không rõ kiểu" : string.Join(", ", typeNames);
        }

        private void EraseTemporaryEntities(ObjectId[] objectIds)
        {
            if (objectIds == null || objectIds.Length == 0)
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
                if (currentValue != null && value != null &&
                    currentValue.GetType() != value.GetType())
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
