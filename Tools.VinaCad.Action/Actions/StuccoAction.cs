using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using PrLogTrackingSystem;
using System;
using System.Collections.Generic;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.Resources.Language;
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
                    throw new InvalidOperationException(Lang.Common_Error_KhongCoBanVeDangHoatDong);

                _editor = document.Editor;
                _database = document.Database;

                StuccoSetting settings = _sessionSettings.Clone();
                EnsureTargetLayer(settings);
                DrawStucco(settings);
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(StuccoAction), ex);
                MessageBox.Show(Lang.FN_Msg_LoiFn.Replace("{ex.Message}", ex.Message), Lang.Common_Text_Loi);
            }
        }

        private void DrawStucco(StuccoSetting settings)
        {
            while (true)
            {
                PromptPointOptions pointOptions = new PromptPointOptions("\n" + Lang.FN_Prompt_FnChonDiemTrongPhongHoacOQuetChonTuongEMat)
                {
                    AllowNone = true,
                    AllowArbitraryInput = false,
                    AppendKeywordsToMessage = false
                };
                pointOptions.Keywords.Add("Open", "O", Lang.FN_Prompt_QuetChonTuong);
                pointOptions.Keywords.Add("Exterior", "E", Lang.FN_Prompt_MatNgoai);
                pointOptions.Keywords.Add("Thick", "D", Lang.FN_Prompt_DoDay);
                pointOptions.Keywords.Add("Settings", "S", Lang.FN_Prompt_CaiDat);

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

                            _editor.UpdateScreen();
                    }
                    catch (Exception ex)
                    {
                        Logger.Info(nameof(DrawStucco), ex);
                        _editor.WriteMessage("\nFN: " + ex.Message);
                    }

                    continue;
                }
                if (pointResult.Status != PromptStatus.Keyword)
                    continue;

                string selectedMode = pointResult.StringResult;
                if (IsMode(selectedMode, "Settings", "S"))
                {
                    if (!TryEditSettings(settings, out StuccoSetting updatedSettings))
                        return;

                    settings = updatedSettings;
                    EnsureTargetLayer(settings);

                    continue;
                }

                if (IsMode(selectedMode, "Thick", "D"))
                {
                    try
                    {
                        if (ChangeStuccoThicknessBySelection(settings) > 0)
                            _editor.UpdateScreen();
                    }
                    catch (Exception ex)
                    {
                        Logger.Info(nameof(DrawStucco), ex);
                        _editor.WriteMessage("\nFN: " + ex.Message);
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
                        _editor.UpdateScreen();
                    }
                }
                catch (Exception ex)
                {
                    Logger.Info(nameof(DrawStucco), ex);
                    _editor.WriteMessage("\nFN: " + ex.Message);
                }

                break;
            }

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
                MessageForAdding = "\n" + Lang.FN_Prompt_ChonCacCanhLienTiepCuaMatNgoaiNhanEnterDeXac
            };
            PromptSelectionResult selectionResult = _editor.GetSelection(selectionOptions, new SelectionFilter(filterValues));
            if (selectionResult.Status != PromptStatus.OK || selectionResult.Value == null || selectionResult.Value.Count == 0)
                return 0;

            PromptPointOptions sideOptions = new PromptPointOptions("\n" + Lang.FN_Prompt_ChonDiemPhiaNgoaiDeXacDinhHuongVua);
            PromptPointResult sideResult = _editor.GetPoint(sideOptions);
            if (sideResult.Status != PromptStatus.OK)
                return 0;

            return StuccoHelper.CreateStuccoForExteriorFaces(
                _database,
                selectionResult.Value.GetObjectIds(),
                ToWorldPoint(sideResult.Value),
                settings.Thickness,
                settings.LayerName);
        }

        private int ChangeStuccoThicknessBySelection(StuccoSetting settings)
        {
            TypedValue[] filterValues =
            {
                new TypedValue((int)DxfCode.Start, "LINE"),
                new TypedValue((int)DxfCode.LayerName, settings.LayerName)
            };
            PromptSelectionOptions selectionOptions = new PromptSelectionOptions
            {
                MessageForAdding = "\n" + Lang.FN_Prompt_ChonCanhVuaCanDoiDoDay
            };
            PromptSelectionResult selectionResult = _editor.GetSelection(selectionOptions, new SelectionFilter(filterValues));
            if (selectionResult.Status != PromptStatus.OK || selectionResult.Value == null || selectionResult.Value.Count == 0)
                return 0;

            PromptDoubleOptions thicknessOptions = new PromptDoubleOptions("\n" + Lang.FN_Prompt_NhapDoDayMoi.Replace("{settings.Thickness}", settings.Thickness.ToString()))
            {
                AllowNegative = false,
                AllowZero = false,
                AllowNone = true,
                UseDefaultValue = false
            };
            PromptDoubleResult thicknessResult = _editor.GetDouble(thicknessOptions);

            double newThickness;
            if (thicknessResult.Status == PromptStatus.None)
            {
                newThickness = settings.Thickness;
            }
            else if (thicknessResult.Status == PromptStatus.OK)
            {
                newThickness = thicknessResult.Value;
            }
            else
            {
                return 0;
            }

            return StuccoHelper.ChangeStuccoThickness(
                _database,
                selectionResult.Value.GetObjectIds(),
                settings.Thickness,
                newThickness,
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
                MessageForAdding = "\n" + Lang.FN_Prompt_QuetChonToanBoHaiMatCuaCacTuongHoNhanEnter
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
            try
            {
                return StuccoHelper.CreateStuccoForRoomFaces(
                    _database,
                    ToWorldPoint(interiorPoint),
                    settings.Thickness,
                    settings.LayerName);
            }
            catch (InvalidOperationException ex)
            {
                Logger.Info(nameof(DrawInteriorBoundary), ex);
                _editor.WriteMessage("\n" + Lang.FN_Output_ThuLaiBangBoundary.Replace("{ex.Message}", ex.Message));
            }

            return DrawInteriorBoundaryByBoundaryCommand(interiorPoint, settings);
        }

        private int DrawInteriorBoundaryByBoundaryCommand(Point3d interiorPoint, StuccoSetting settings)
        {
            ObjectId[] temporaryBoundaryIds = Array.Empty<ObjectId>();
            try
            {
                _editor.Regen();
                temporaryBoundaryIds = CreateBoundaryWithCoreCommand(interiorPoint, settings.Thickness);
                if (temporaryBoundaryIds.Length == 0)
                    throw new InvalidOperationException(Lang.FN_Error_KhongTimThayBienKinQuanhDiemDaChonHayKiemTra);

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
            double baseOffset = Math.Max(0.0001, Math.Abs(thickness) * 0.01);
            List<Point3d> seedPoints = new List<Point3d> { interiorPointInCurrentUcs };
            foreach (double scale in new[] { 1.0, 10.0, 100.0 })
            {
                double offset = baseOffset * scale;
                seedPoints.Add(new Point3d(interiorPointInCurrentUcs.X + offset, interiorPointInCurrentUcs.Y, interiorPointInCurrentUcs.Z));
                seedPoints.Add(new Point3d(interiorPointInCurrentUcs.X - offset, interiorPointInCurrentUcs.Y, interiorPointInCurrentUcs.Z));
                seedPoints.Add(new Point3d(interiorPointInCurrentUcs.X, interiorPointInCurrentUcs.Y + offset, interiorPointInCurrentUcs.Z));
                seedPoints.Add(new Point3d(interiorPointInCurrentUcs.X, interiorPointInCurrentUcs.Y - offset, interiorPointInCurrentUcs.Z));
            }

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
            BlockTableRecord currentSpace = (BlockTableRecord)transaction.GetObject(_database.CurrentSpaceId, OpenMode.ForRead);
            foreach (ObjectId objectId in currentSpace)
            {
                if (!idsBefore.Contains(objectId) && transaction.GetObject(objectId, OpenMode.ForRead) is Curve)
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
            BlockTableRecord currentSpace = (BlockTableRecord)transaction.GetObject(_database.CurrentSpaceId, OpenMode.ForRead);
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
            try { return Application.GetSystemVariable(name); }
            catch { return fallbackValue; }
        }

        private static void TrySetSystemVariable(string name, object value)
        {
            try
            {
                object currentValue = Application.GetSystemVariable(name);
                Application.SetSystemVariable(name, currentValue != null && currentValue.GetType() != value.GetType()
                    ? Convert.ChangeType(value, currentValue.GetType())
                    : value);
            }
            catch { }
        }
    }
}