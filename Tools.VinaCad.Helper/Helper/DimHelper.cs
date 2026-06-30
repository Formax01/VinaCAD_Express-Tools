using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.Resources.Definitions;
using Tools.VinaCad.Modeling;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCad.Helper.Helper
{
    public class DimHelper
    {

        private const double Tolerance = 1.0;

        public static List<ObjectId> CheckDuplicateDimensions(Database db, Editor ed)
        {
            List<DimVinaCAD> dims = new List<DimVinaCAD>();
            HashSet<ObjectId> duplicateIds = new HashSet<ObjectId>();

            if (db == null || ed == null)
                return duplicateIds.ToList();

            TypedValue[] filterValues =
            {
                new TypedValue((int)DxfCode.Start, "DIMENSION")
            };

            SelectionFilter filter = new SelectionFilter(filterValues);

            PromptSelectionOptions opt = new PromptSelectionOptions();
            opt.MessageForAdding = "\nQuét chọn vùng chứa Dim: ";
            opt.RejectObjectsOnLockedLayers = true;

            PromptSelectionResult result = ed.GetSelection(opt, filter);

            if (result.Status != PromptStatus.OK)
                return duplicateIds.ToList();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject selObj in result.Value)
                {
                    if (selObj == null)
                        continue;

                    RotatedDimension dim =
                        tr.GetObject(selObj.ObjectId, OpenMode.ForRead) as RotatedDimension;

                    if (dim == null)
                        continue;

                    dims.Add(new DimVinaCAD
                    {
                        Diem1 = dim.XLine1Point,
                        Diem2 = dim.XLine2Point,
                        DiemDung = dim.DimLinePoint,
                        GocDim = dim.Rotation,
                        Id = dim.ObjectId
                    });
                }

                if (dims.Count > 0)
                {
                    CheckHorizontalDuplicateDims(dims, duplicateIds);
                    CheckVerticalDuplicateDims(dims, duplicateIds);
                }

                tr.Commit();
            }

            List<ObjectId> ids = duplicateIds.ToList();

            HighlightDuplicateDimensions(db, ed, ids);

            return ids;
        }

        private static void HighlightDuplicateDimensions(Database db, Editor ed, List<ObjectId> ids)
        {
            if (ed == null)
                return;

            if (ids != null && ids.Count > 0)
            {
                ed.SetImpliedSelection(ids.ToArray());
                ed.UpdateScreen();
                ed.WriteMessage("\nCó dim bị trùng!");
                ZoomToObjects(db, ed, ids.ToArray());
            }
            else
            {
                ed.SetImpliedSelection(Array.Empty<ObjectId>());
                ed.UpdateScreen();
                ed.WriteMessage("\nKhông tìm thấy dim nào bị trùng!");
            }
        }

        private static void CheckHorizontalDuplicateDims(List<DimVinaCAD> dims, HashSet<ObjectId> duplicateIds)
        {
            Dictionary<double, List<DimVinaCAD>> groups = dims
                                                        .Where(IsHorizontalDimTD)
                                                        .GroupBy(x => Math.Truncate(x.DiemDung.Y))
                                                        .ToDictionary(x => x.Key, x => x.ToList());
            foreach (var group in groups)
            {
                List<DimVinaCAD> sameLineDims = group.Value;

                if (sameLineDims.Count <= 1)
                    continue;

                for (int i = 0; i < sameLineDims.Count; i++)
                {
                    for (int j = i + 1; j < sameLineDims.Count; j++)
                    {
                        DimVinaCAD dim1 = sameLineDims[i];
                        DimVinaCAD dim2 = sameLineDims[j];

                        if (IsDuplicateHorizontalDim(dim1, dim2))
                        {
                            duplicateIds.Add(dim1.Id);
                            duplicateIds.Add(dim2.Id);
                        }
                    }
                }
            }
        }

        private static void CheckVerticalDuplicateDims(List<DimVinaCAD> dims, HashSet<ObjectId> duplicateIds)
        {
            Dictionary<double, List<DimVinaCAD>> groups = dims
                .Where(IsVerticalDimTD)
                .GroupBy(x => Math.Truncate(x.DiemDung.X))
                .ToDictionary(x => x.Key, x => x.ToList());

            foreach (var group in groups)
            {
                List<DimVinaCAD> sameLineDims = group.Value;

                if (sameLineDims.Count <= 1)
                    continue;

                for (int i = 0; i < sameLineDims.Count; i++)
                {
                    for (int j = i + 1; j < sameLineDims.Count; j++)
                    {
                        DimVinaCAD dim1 = sameLineDims[i];
                        DimVinaCAD dim2 = sameLineDims[j];

                        if (IsDuplicateVerticalDim(dim1, dim2))
                        {
                            duplicateIds.Add(dim1.Id);
                            duplicateIds.Add(dim2.Id);
                        }
                    }
                }
            }
        }

        private static bool IsDuplicateHorizontalDim(DimVinaCAD dim1, DimVinaCAD dim2)
        {
            if (Math.Abs(dim1.Diem1.X - dim2.Diem1.X) <= Tolerance &&
                (IsPointBetweenPointsX(dim1.Diem2, dim2.Diem1, dim2.Diem2) ||
                 IsPointBetweenPointsX(dim2.Diem2, dim1.Diem1, dim1.Diem2)))
            {
                return true;
            }

            if (Math.Abs(dim1.Diem1.X - dim2.Diem2.X) <= Tolerance &&
                (IsPointBetweenPointsX(dim1.Diem2, dim2.Diem1, dim2.Diem2) ||
                 IsPointBetweenPointsX(dim2.Diem1, dim1.Diem1, dim1.Diem2)))
            {
                return true;
            }

            if (Math.Abs(dim1.Diem2.X - dim2.Diem1.X) <= Tolerance &&
                (IsPointBetweenPointsX(dim1.Diem1, dim2.Diem1, dim2.Diem2) ||
                 IsPointBetweenPointsX(dim2.Diem2, dim1.Diem1, dim1.Diem2)))
            {
                return true;
            }

            if (Math.Abs(dim1.Diem2.X - dim2.Diem2.X) <= Tolerance &&
                (IsPointBetweenPointsX(dim1.Diem1, dim2.Diem1, dim2.Diem2) ||
                 IsPointBetweenPointsX(dim2.Diem1, dim1.Diem1, dim1.Diem2)))
            {
                return true;
            }

            return false;
        }
        private static void ZoomToObjects(Database db, Editor ed, ObjectId[] objectIds)
        {
            Extents3d? totalExtents = null;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in objectIds)
                {
                    Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;

                    if (ent == null)
                        continue;

                    try
                    {
                        Extents3d ext = ent.GeometricExtents;

                        if (totalExtents == null)
                            totalExtents = ext;
                        else
                        {
                            Extents3d temp = totalExtents.Value;
                            temp.AddExtents(ext);
                            totalExtents = temp;
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }

                tr.Commit();
            }

            if (totalExtents == null)
                return;

            Extents3d ex = totalExtents.Value;

            Point3d min = ex.MinPoint;
            Point3d max = ex.MaxPoint;

            Point3d center3d = new Point3d(
                (min.X + max.X) / 2.0,
                (min.Y + max.Y) / 2.0,
                (min.Z + max.Z) / 2.0);

            double width = Math.Max((max.X - min.X) * 1.5, 1000);
            double height = Math.Max((max.Y - min.Y) * 1.5, 1000);

            using (ViewTableRecord view = ed.GetCurrentView())
            {
                view.Target = center3d;
                view.CenterPoint = new Point2d(0, 0);
                view.Width = width;
                view.Height = height;

                ed.SetCurrentView(view);
            }

            ed.Regen();
        }
        private static bool IsDuplicateVerticalDim(DimVinaCAD dim1, DimVinaCAD dim2)
        {
            if (Math.Abs(dim1.Diem1.Y - dim2.Diem1.Y) <= Tolerance &&
                (IsPointBetweenPointsY(dim1.Diem2, dim2.Diem1, dim2.Diem2) ||
                 IsPointBetweenPointsY(dim2.Diem2, dim1.Diem1, dim1.Diem2)))
            {
                return true;
            }

            if (Math.Abs(dim1.Diem1.Y - dim2.Diem2.Y) <= Tolerance &&
                (IsPointBetweenPointsY(dim1.Diem2, dim2.Diem1, dim2.Diem2) ||
                 IsPointBetweenPointsY(dim2.Diem1, dim1.Diem1, dim1.Diem2)))
            {
                return true;
            }

            if (Math.Abs(dim1.Diem2.Y - dim2.Diem1.Y) <= Tolerance &&
                (IsPointBetweenPointsY(dim1.Diem1, dim2.Diem1, dim2.Diem2) ||
                 IsPointBetweenPointsY(dim2.Diem2, dim1.Diem1, dim1.Diem2)))
            {
                return true;
            }

            if (Math.Abs(dim1.Diem2.Y - dim2.Diem2.Y) <= Tolerance &&
                (IsPointBetweenPointsY(dim1.Diem1, dim2.Diem1, dim2.Diem2) ||
                 IsPointBetweenPointsY(dim2.Diem1, dim1.Diem1, dim1.Diem2)))
            {
                return true;
            }

            return false;
        }
        public static List<ObjectId> CheckCumulativeDimensions(Database db, Editor ed, double tolerance, double lineTolerance, double gapTolerance)
        {
            HashSet<ObjectId> wrongIds = new HashSet<ObjectId>();
            HashSet<ObjectId> overrideIds = new HashSet<ObjectId>();

            if (db == null || ed == null)
                return wrongIds.ToList();

            TypedValue[] filterValues =
                        {
                                new TypedValue((int)DxfCode.Start, "DIMENSION")
                        };
            SelectionFilter filter = new SelectionFilter(filterValues);
            PromptSelectionOptions opt = new PromptSelectionOptions();
            opt.MessageForAdding = "\nQuét chọn dim cộng dồn: ";
            opt.RejectObjectsOnLockedLayers = true;
            PromptSelectionResult result = ed.GetSelection(opt, filter);

            if (result.Status != PromptStatus.OK)
                return wrongIds.ToList();

            // Xuất list DimVinaCad
            List<DimVinaCAD> DimVinaCADs = GetDimVinaCADsFromSelection(db, result);

            // Check và tạo dim cộng dồn
            CheckHorizontalCumulativeDims(db, DimVinaCADs, wrongIds, overrideIds, tolerance, lineTolerance, gapTolerance);

            CheckVerticalCumulativeDims(db, DimVinaCADs, wrongIds, overrideIds, tolerance, lineTolerance, gapTolerance);

            CheckAlignedCumulativeDims(db, DimVinaCADs, wrongIds, overrideIds, tolerance, lineTolerance, gapTolerance);

            // Chọn các dim bị sai
            List<ObjectId> ids = wrongIds.Concat(overrideIds).Distinct().ToList();
            ed.SetImpliedSelection(Array.Empty<ObjectId>());
            ed.UpdateScreen();

            if (ids.Count > 0)
            {

                ed.SetImpliedSelection(ids.ToArray());
                ZoomToObjects(db, ed, ids.ToArray());
                ed.UpdateScreen();
                ed.WriteMessage("\nCó chuỗi dim cộng dồn bị sai!");
                MessageBox.Show("Có các dim bị chồng lấn, hở, sai giá trị thực! \nBạn vui lòng điều chỉnh trước khi dim cộng dồn.", StringDefinition.TITLE_MESSAGE);
            }
            else
            {
                ed.SetImpliedSelection(Array.Empty<ObjectId>());
                ed.UpdateScreen();
                ed.WriteMessage("\nKhông tìm thấy dim cộng dồn sai!");
            }

            return ids;
        }

        private static List<DimVinaCAD> GetDimVinaCADsFromSelection(Database db, PromptSelectionResult result)
        {
            List<DimVinaCAD> dimVinaCADs = new List<DimVinaCAD>();

            if (db == null || result == null || result.Status != PromptStatus.OK)
                return dimVinaCADs;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject selObj in result.Value)
                {
                    if (selObj == null)
                        continue;

                    Dimension dim = tr.GetObject(selObj.ObjectId, OpenMode.ForRead) as Dimension;
                    if (dim == null)
                        continue;

                    if (!(dim is RotatedDimension || dim is AlignedDimension))
                        continue;

                    Point3d xline1Point;
                    Point3d xline2Point;
                    Point3d dimLinePoint;
                    double rotation;

                    if (dim is RotatedDimension rotatedDim)
                    {
                        xline1Point = rotatedDim.XLine1Point;
                        xline2Point = rotatedDim.XLine2Point;
                        dimLinePoint = rotatedDim.DimLinePoint;
                        rotation = NormalizeAngle(rotatedDim.Rotation);
                    }
                    else if (dim is AlignedDimension alignedDim)
                    {
                        xline1Point = alignedDim.XLine1Point;
                        xline2Point = alignedDim.XLine2Point;
                        dimLinePoint = alignedDim.DimLinePoint;

                        Vector3d v = xline2Point - xline1Point;
                        rotation = NormalizeAngle(Math.Atan2(v.Y, v.X));
                    }
                    else
                    {
                        continue;
                    }

                    DimVinaCAD info = new DimVinaCAD
                    {
                        Id = dim.ObjectId,
                        Diem1 = xline1Point,
                        Diem2 = xline2Point,
                        DimLinePoint = dimLinePoint,
                        Rotation = rotation,
                        Value = Math.Round(GetDimensionValue(dim), 0, MidpointRounding.AwayFromZero),
                        Dimlfac = GetDimensionLinearScale(dim)
                    };

                    dimVinaCADs.Add(info);
                }

                tr.Commit();
            }

            return dimVinaCADs;
        }
        private static void CheckHorizontalCumulativeDims(Database db, List<DimVinaCAD> DimVinaCADs, HashSet<ObjectId> wrongIds, HashSet<ObjectId> overrideIds, double tolerance, double lineTolerance, double gapTolerance)
        {
            List<DimVinaCAD> horizontalDims = DimVinaCADs
                                            .Where(IsHorizontalDim)
                                            .ToList();

            List<List<DimVinaCAD>> groups = GroupHorizontalDimsByLineAndContinuity(horizontalDims, lineTolerance, gapTolerance);

            foreach (List<DimVinaCAD> group in groups)
            {
                bool groupHasWrong = false;
                List<DimVinaCAD> dims = XLDCDSetting.IsLeftToRightHorizontal
                        ? group.OrderBy(x => x.MinX).ToList()
                        : group.OrderByDescending(x => x.MaxX).ToList();

                if (dims.Count <= 1)
                    continue;

                // Check nối tiếp chân dim 
                for (int i = 1; i < dims.Count; i++)
                {
                    DimVinaCAD previousDim = dims[i - 1];
                    DimVinaCAD currentDim = dims[i];

                    double previousEnd = XLDCDSetting.IsLeftToRightHorizontal
                        ? previousDim.MaxX
                        : previousDim.MinX;

                    double currentStart = XLDCDSetting.IsLeftToRightHorizontal
                        ? currentDim.MinX
                        : currentDim.MaxX;

                    if (Math.Abs(currentStart - previousEnd) > gapTolerance)
                    {
                        wrongIds.Add(currentDim.Id);
                        groupHasWrong = true;
                    }
                }

                // Check text dim bị sửa / không khớp khoảng cách thật
                foreach (DimVinaCAD dim in dims)
                {
                    double realDimDistance = Math.Abs(dim.MaxX - dim.MinX);
                    double realDisplayDistance = realDimDistance * dim.Dimlfac;
                    double dimTextValue = Math.Abs(dim.Value);

                    if (Math.Abs(realDisplayDistance - dimTextValue) > tolerance)
                    {
                        overrideIds.Add(dim.Id);
                        groupHasWrong = true;
                    }
                }

                // Tạo dim cộng dồn
                if (!groupHasWrong)
                {
                    CreateHorizontalCheckDimensions(db, dims, StringDefinition.dimCongDonName);
                }
            }
        }

        private static void CheckAlignedCumulativeDims(Database db, List<DimVinaCAD> dimVinaCADs, HashSet<ObjectId> wrongIds, HashSet<ObjectId> overrideIds, double tolerance, double lineTolerance, double gapTolerance)
        {
            List<DimVinaCAD> alignedDims = dimVinaCADs.Where(x => !IsHorizontalDim(x) && !IsVerticalDim(x)).ToList();

            List<List<DimVinaCAD>> groups = GroupAlignedDimsByDirectionLineAndContinuity(alignedDims, lineTolerance, gapTolerance);

            foreach (List<DimVinaCAD> group in groups)
            {
                bool groupHasWrong = false;

                if (group.Count <= 1)
                    continue;

                Vector3d baseDir = GetFootDirection(group[0]);

                List<DimVinaCAD> dims = XLDCDSetting.IsTopToBottomAligned
                    ? group.OrderByDescending(x => Math.Max(x.Diem1.Y, x.Diem2.Y)).ToList()
                    : group.OrderBy(x => Math.Min(x.Diem1.Y, x.Diem2.Y)).ToList();

                // Check nối tiếp chân dim 
                for (int i = 1; i < dims.Count; i++)
                {
                    DimVinaCAD previousDim = dims[i - 1];
                    DimVinaCAD currentDim = dims[i];

                    double previousEnd = XLDCDSetting.IsTopToBottomAligned
                        ? GetBottomStation(previousDim, baseDir)
                        : GetTopStation(previousDim, baseDir);

                    double currentStart = XLDCDSetting.IsTopToBottomAligned
                        ? GetTopStation(currentDim, baseDir)
                        : GetBottomStation(currentDim, baseDir);

                    if (Math.Abs(currentStart - previousEnd) > gapTolerance)
                    {
                        wrongIds.Add(currentDim.Id);
                        groupHasWrong = true;
                    }
                }

                // Check text dim bị sửa / không khớp khoảng cách thật
                foreach (DimVinaCAD dim in dims)
                {
                    double realDimDistance =
                        Math.Abs(GetEndStation(dim, baseDir) - GetStartStation(dim, baseDir));

                    double realDisplayDistance = realDimDistance * dim.Dimlfac;
                    double dimTextValue = Math.Abs(dim.Value);

                    if (Math.Abs(realDisplayDistance - dimTextValue) > tolerance)
                    {
                        overrideIds.Add(dim.Id);
                        groupHasWrong = true;
                    }
                }

                // Tạo dim cộng dồn
                if (!groupHasWrong)
                {
                    CreateAlignedCheckDimensions(db, dims, StringDefinition.dimCongDonName);
                }
            }
        }

        private static void CheckVerticalCumulativeDims(Database db, List<DimVinaCAD> DimVinaCADs, HashSet<ObjectId> wrongIds, HashSet<ObjectId> overrideIds, double tolerance, double lineTolerance, double gapTolerance)
        {
            List<DimVinaCAD> verticalDims = DimVinaCADs.Where(IsVerticalDim).ToList();

            List<List<DimVinaCAD>> groups = GroupVerticalDimsByLineAndContinuity(verticalDims, lineTolerance, gapTolerance);

            foreach (List<DimVinaCAD> group in groups)
            {
                bool groupHasWrong = false;

                List<DimVinaCAD> dims = XLDCDSetting.IsTopToBottomVertical
                    ? group.OrderByDescending(x => x.MaxY).ToList()
                    : group.OrderBy(x => x.MinY).ToList();

                if (dims.Count <= 1)
                    continue;

                // Check nối tiếp chân dim 
                for (int i = 1; i < dims.Count; i++)
                {
                    DimVinaCAD previousDim = dims[i - 1];
                    DimVinaCAD currentDim = dims[i];

                    double previousEndY = XLDCDSetting.IsTopToBottomVertical
                        ? previousDim.MinY
                        : previousDim.MaxY;

                    double currentStartY = XLDCDSetting.IsTopToBottomVertical
                        ? currentDim.MaxY
                        : currentDim.MinY;

                    if (Math.Abs(currentStartY - previousEndY) > gapTolerance)
                    {
                        wrongIds.Add(currentDim.Id);
                        groupHasWrong = true;
                    }
                }

                // Check text dim bị sửa / không khớp khoảng cách thật
                foreach (DimVinaCAD dim in dims)
                {
                    double realDimDistance = Math.Abs(dim.MaxY - dim.MinY);
                    double realDisplayDistance = realDimDistance * dim.Dimlfac;
                    double dimTextValue = Math.Abs(dim.Value);

                    if (Math.Abs(realDisplayDistance - dimTextValue) > tolerance)
                    {
                        overrideIds.Add(dim.Id);
                        groupHasWrong = true;
                    }
                }

                // Tạo dim cộng dồn
                if (!groupHasWrong)
                {
                    CreateVerticalCheckDimensions(db, dims, StringDefinition.dimCongDonName);
                }
            }
        }

        private static List<List<DimVinaCAD>> GroupHorizontalDimsByLineAndContinuity(List<DimVinaCAD> dims, double lineTolerance, double gapTolerance)
        {
            List<List<DimVinaCAD>> lineGroups = new List<List<DimVinaCAD>>();

            foreach (DimVinaCAD dim in dims.OrderBy(x => x.DimLinePoint.Y))
            {
                List<DimVinaCAD> lineGroup = lineGroups.FirstOrDefault(g => Math.Abs(g.Average(x => x.DimLinePoint.Y) - dim.DimLinePoint.Y) <= lineTolerance);

                if (lineGroup == null)
                {
                    lineGroup = new List<DimVinaCAD>();
                    lineGroups.Add(lineGroup);
                }

                lineGroup.Add(dim);
            }

            List<List<DimVinaCAD>> resultGroups = new List<List<DimVinaCAD>>();

            foreach (List<DimVinaCAD> lineGroup in lineGroups)
            {
                List<DimVinaCAD> sortedDims = lineGroup.OrderBy(x => x.MinX).ToList();

                List<DimVinaCAD> currentGroup = new List<DimVinaCAD>();

                foreach (DimVinaCAD dim in sortedDims)
                {
                    if (currentGroup.Count == 0)
                    {
                        currentGroup.Add(dim);
                        continue;
                    }

                    DimVinaCAD lastDim = currentGroup.Last();

                    double gap = dim.MinX - lastDim.MaxX;

                    if (gap <= gapTolerance)
                    {
                        currentGroup.Add(dim);
                    }
                    else
                    {
                        resultGroups.Add(currentGroup);
                        currentGroup = new List<DimVinaCAD> { dim };
                    }
                }

                if (currentGroup.Count > 0)
                    resultGroups.Add(currentGroup);
            }

            return resultGroups;
        }
        private static List<List<DimVinaCAD>> GroupVerticalDimsByLineAndContinuity(List<DimVinaCAD> dims, double lineTolerance, double gapTolerance)
        {
            List<List<DimVinaCAD>> lineGroups = new List<List<DimVinaCAD>>();

            foreach (DimVinaCAD dim in dims.OrderBy(x => x.DimLinePoint.X))
            {
                List<DimVinaCAD> lineGroup = lineGroups.FirstOrDefault(g => Math.Abs(g.Average(x => x.DimLinePoint.X) - dim.DimLinePoint.X) <= lineTolerance);

                if (lineGroup == null)
                {
                    lineGroup = new List<DimVinaCAD>();
                    lineGroups.Add(lineGroup);
                }

                lineGroup.Add(dim);
            }

            List<List<DimVinaCAD>> resultGroups = new List<List<DimVinaCAD>>();

            foreach (List<DimVinaCAD> lineGroup in lineGroups)
            {
                List<DimVinaCAD> sortedDims = lineGroup.OrderBy(x => x.MinY).ToList();

                List<DimVinaCAD> currentGroup = new List<DimVinaCAD>();

                foreach (DimVinaCAD dim in sortedDims)
                {
                    if (currentGroup.Count == 0)
                    {
                        currentGroup.Add(dim);
                        continue;
                    }

                    DimVinaCAD lastDim = currentGroup.Last();

                    double gap = dim.MinY - lastDim.MaxY;

                    if (gap <= gapTolerance)
                    {
                        currentGroup.Add(dim);
                    }
                    else
                    {
                        resultGroups.Add(currentGroup);
                        currentGroup = new List<DimVinaCAD> { dim };
                    }
                }

                if (currentGroup.Count > 0)
                    resultGroups.Add(currentGroup);
            }

            return resultGroups;
        }

        private static List<List<DimVinaCAD>> GroupAlignedDimsByDirectionLineAndContinuity(List<DimVinaCAD> dims, double lineTolerance, double gapTolerance)
        {
            List<List<DimVinaCAD>> directionGroups = new List<List<DimVinaCAD>>();

            // 1. Gom các dim cùng phương theo vector Diem1 -> Diem2
            foreach (DimVinaCAD dim in dims)
            {
                List<DimVinaCAD> directionGroup = directionGroups.FirstOrDefault(g => IsSameFootDirection(g[0], dim));

                if (directionGroup == null)
                {
                    directionGroup = new List<DimVinaCAD>();
                    directionGroups.Add(directionGroup);
                }

                directionGroup.Add(dim);
            }

            List<List<DimVinaCAD>> resultGroups = new List<List<DimVinaCAD>>();

            foreach (List<DimVinaCAD> directionGroup in directionGroups)
            {
                if (directionGroup.Count == 0)
                    continue;

                Vector3d baseDir = GetFootDirection(directionGroup[0]);

                // Pháp tuyến với phương dim
                Vector3d normal = new Vector3d(-baseDir.Y, baseDir.X, 0);

                List<List<DimVinaCAD>> lineGroups = new List<List<DimVinaCAD>>();

                // Sắp xếp từ trên xuống theo Y
                foreach (DimVinaCAD dim in directionGroup.OrderByDescending(x => x.DimLinePoint.Y))
                {
                    List<DimVinaCAD> lineGroup = lineGroups.LastOrDefault();

                    if (lineGroup == null)
                    {
                        lineGroup = new List<DimVinaCAD> { dim };
                        lineGroups.Add(lineGroup);
                        continue;
                    }

                    DimVinaCAD lastDim = lineGroup.Last();

                    // Độ lệch line = khoảng cách pháp tuyến giữa 2 dim line
                    double lineGap = Math.Abs(
                        GetPerpendicularLineValue(dim, normal) -
                        GetPerpendicularLineValue(lastDim, normal));

                    if (lineGap <= lineTolerance)
                    {
                        lineGroup.Add(dim);
                    }
                    else
                    {
                        lineGroup = new List<DimVinaCAD> { dim };
                        lineGroups.Add(lineGroup);
                    }
                }

                // Trong từng line group, sắp theo phương dim rồi gom liên tục
                foreach (List<DimVinaCAD> lineGroup in lineGroups)
                {
                    List<DimVinaCAD> sortedDims = lineGroup.OrderBy(x => GetStartStation(x, baseDir)).ToList();

                    List<DimVinaCAD> currentGroup = new List<DimVinaCAD>();

                    foreach (DimVinaCAD dim in sortedDims)
                    {
                        if (currentGroup.Count == 0)
                        {
                            currentGroup.Add(dim);
                            continue;
                        }

                        DimVinaCAD lastDim = currentGroup.Last();

                        double gap = GetStartStation(dim, baseDir) - GetEndStation(lastDim, baseDir);

                        if (Math.Abs(gap) <= gapTolerance)
                        {
                            currentGroup.Add(dim);
                        }
                        else
                        {
                            resultGroups.Add(currentGroup);
                            currentGroup = new List<DimVinaCAD> { dim };
                        }
                    }

                    if (currentGroup.Count > 0)
                        resultGroups.Add(currentGroup);
                }
            }

            return resultGroups;
        }
        private static void CreateHorizontalCheckDimensions(Database db, List<DimVinaCAD> dims, string dimStyleName)
        {
            if (db == null)
                return;

            if (dims == null || dims.Count == 0)
                return;

            dims = XLDCDSetting.IsLeftToRightHorizontal
             ? dims.OrderBy(x => x.MinX).ToList()
             : dims.OrderByDescending(x => x.MaxX).ToList();

            Point3d basePoint = XLDCDSetting.IsLeftToRightHorizontal
                ? GetLeftPoint(dims[0])
                : GetRightPoint(dims[0]);

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable blockTable = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;

                if (blockTable == null)
                    return;

                BlockTableRecord modelSpace = tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;

                if (modelSpace == null)
                    return;

                ObjectId dimStyleId = GetDimStyleIdByName(db, tr, dimStyleName);

                Dimension firstSourceDim = tr.GetObject(dims[0].Id, OpenMode.ForRead) as Dimension;

                // Tạo dim 0 ngay tại chân trái của dim đầu tiên
                RotatedDimension zeroDim = new RotatedDimension(dims[0].Rotation, basePoint, basePoint, dims[0].DimLinePoint, "0", dimStyleId);
                zeroDim.ColorIndex = 2;

                if (firstSourceDim != null)
                {
                    CopyDimensionDisplayProperties(firstSourceDim, zeroDim);
                }

                modelSpace.AppendEntity(zeroDim);
                tr.AddNewlyCreatedDBObject(zeroDim, true);
                zeroDim.RecordGraphicsModified(true);
                Vector3d groupDir = XLDCDSetting.IsLeftToRightHorizontal ? Vector3d.XAxis : -Vector3d.XAxis;

                double cumulativeLength = 0;

                for (int i = 0; i < dims.Count; i++)
                {
                    DimVinaCAD currentDim = dims[i];

                    Dimension sourceDim = tr.GetObject(currentDim.Id, OpenMode.ForRead) as Dimension;

                    double realValue = currentDim.Value;

                    if (Math.Abs(currentDim.Dimlfac) > 1e-9)
                        realValue = currentDim.Value / currentDim.Dimlfac;

                    cumulativeLength += realValue;

                    Point3d xLine1Point = basePoint;
                    Point3d xLine2Point = basePoint + groupDir * cumulativeLength;

                    RotatedDimension newDim = new RotatedDimension(
                        currentDim.Rotation,
                        xLine1Point,
                        xLine2Point,
                        currentDim.DimLinePoint,
                        "<>",
                        dimStyleId);

                    newDim.ColorIndex = 2;

                    if (sourceDim != null)
                        CopyDimensionDisplayProperties(sourceDim, newDim);

                    newDim.Dimexe = 5.0;
                    zeroDim.Dimexe = 5.0;

                    modelSpace.AppendEntity(newDim);
                    tr.AddNewlyCreatedDBObject(newDim, true);
                    newDim.RecordGraphicsModified(true);
                }

                tr.Commit();
            }
        }

        private static void CreateVerticalCheckDimensions(Database db, List<DimVinaCAD> dims, string dimStyleName)
        {
            if (db == null)
                return;

            if (dims == null || dims.Count == 0)
                return;

            dims = XLDCDSetting.IsTopToBottomVertical
                ? dims.OrderByDescending(x => x.MaxY).ToList()
                : dims.OrderBy(x => x.MinY).ToList();

            Point3d basePoint = XLDCDSetting.IsTopToBottomVertical
                ? GetTopPoint(dims[0])
                : GetBottomPoint(dims[0]);


            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable blockTable = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;

                if (blockTable == null)
                    return;

                BlockTableRecord modelSpace = tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;
                if (modelSpace == null)
                    return;

                ObjectId dimStyleId = GetDimStyleIdByName(db, tr, dimStyleName);
                Dimension firstSourceDim = tr.GetObject(dims[0].Id, OpenMode.ForRead) as Dimension;

                // Tạo dim 0 ngay tại chân trên của dim đầu tiên
                RotatedDimension zeroDim = new RotatedDimension(dims[0].Rotation, basePoint, basePoint, dims[0].DimLinePoint, "0", dimStyleId);
                zeroDim.ColorIndex = 2;

                if (firstSourceDim != null)
                {
                    CopyDimensionDisplayProperties(firstSourceDim, zeroDim);
                }

                modelSpace.AppendEntity(zeroDim);
                tr.AddNewlyCreatedDBObject(zeroDim, true);
                zeroDim.RecordGraphicsModified(true);

                Vector3d groupDir = XLDCDSetting.IsTopToBottomVertical? -Vector3d.YAxis : Vector3d.YAxis;

                double cumulativeLength = 0;

                for (int i = 0; i < dims.Count; i++)
                {
                    DimVinaCAD currentDim = dims[i];
                    Dimension sourceDim = tr.GetObject(currentDim.Id, OpenMode.ForRead) as Dimension;

                    double realValue = currentDim.Value;

                    if (Math.Abs(currentDim.Dimlfac) > 1e-9)
                        realValue = currentDim.Value / currentDim.Dimlfac;

                    cumulativeLength += realValue;

                    Point3d xLine1Point = basePoint;
                    Point3d xLine2Point = basePoint + groupDir * cumulativeLength;

                    RotatedDimension newDim = new RotatedDimension(
                        currentDim.Rotation,
                        xLine1Point,
                        xLine2Point,
                        currentDim.DimLinePoint,
                        "<>",
                        dimStyleId);

                    newDim.ColorIndex = 2;

                    if (sourceDim != null)
                        CopyDimensionDisplayProperties(sourceDim, newDim);

                    newDim.Dimexe = 5.0;
                    zeroDim.Dimexe = 5.0;

                    modelSpace.AppendEntity(newDim);
                    tr.AddNewlyCreatedDBObject(newDim, true);
                    newDim.RecordGraphicsModified(true);
                }
                tr.Commit();
            }
        }
        private static void CreateAlignedCheckDimensions(Database db, List<DimVinaCAD> dims, string dimStyleName)
        {
            if (db == null || dims == null || dims.Count == 0)
                return;

            dims = XLDCDSetting.IsTopToBottomAligned
                ? dims.OrderByDescending(x => x.DimLinePoint.Y).ToList()
                : dims.OrderBy(x => x.DimLinePoint.Y).ToList();

            Point3d basePoint = XLDCDSetting.IsTopToBottomAligned
                ? GetTopPoint(dims[0])
                : GetBottomPoint(dims[0]);

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable blockTable = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                if (blockTable == null)
                    return;
                BlockTableRecord modelSpace = tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;
                if (modelSpace == null)
                    return;
                ObjectId dimStyleId = GetDimStyleIdByName(db, tr, dimStyleName);
                DimStyleTableRecord dimStyle = tr.GetObject(dimStyleId, OpenMode.ForWrite) as DimStyleTableRecord;

                if (dimStyle != null)
                {
                    dimStyle.Dimexe = 5.0;
                }
                Dimension firstSourceDim = tr.GetObject(dims[0].Id, OpenMode.ForRead) as Dimension;

                Vector3d zeroDir = GetAlignedRuleDirection(dims[0]);
                Point3d zeroEndPoint = basePoint + zeroDir * 1e-3;
                Point3d zeroDimLinePoint = GetNewAlignedDimLinePoint(basePoint, zeroEndPoint, dims[0]);

                AlignedDimension zeroDim = new AlignedDimension(basePoint, zeroEndPoint, zeroDimLinePoint, "0", dimStyleId);
                zeroDim.ColorIndex = 2;

                if (firstSourceDim != null)
                    CopyDimensionDisplayProperties(firstSourceDim, zeroDim);

                modelSpace.AppendEntity(zeroDim);
                tr.AddNewlyCreatedDBObject(zeroDim, true);

                DimVinaCAD baseDim = dims[0];

                Vector3d groupDir = GetAlignedRuleDirection(baseDim).GetNormal();

                double cumulativeLength = 0;

                foreach (DimVinaCAD currentDim in dims)
                {
                    Dimension sourceDim = tr.GetObject(currentDim.Id, OpenMode.ForRead) as Dimension;

                    double realValue = currentDim.Value;

                    if (Math.Abs(currentDim.Dimlfac) > 1e-9)
                        realValue = currentDim.Value / currentDim.Dimlfac;

                    cumulativeLength += realValue;

                    Point3d xLine1Point = basePoint;
                    Point3d xLine2Point = basePoint + groupDir * cumulativeLength;

                    if (xLine1Point.DistanceTo(xLine2Point) <= 1e-6)
                        continue;

                    Point3d dimLinePoint = GetNewAlignedDimLinePoint(
                        xLine1Point,
                        xLine2Point,
                        baseDim);

                    AlignedDimension newDim =
                        new AlignedDimension(
                            xLine1Point,
                            xLine2Point,
                            dimLinePoint,
                            "<>",
                            dimStyleId);

                    if (sourceDim != null)
                        CopyDimensionDisplayProperties(sourceDim, newDim);

                    newDim.ColorIndex = 2;

                    modelSpace.AppendEntity(newDim);
                    tr.AddNewlyCreatedDBObject(newDim, true);
                }
                Point3d totalEndPoint = basePoint + groupDir * cumulativeLength;

                if (basePoint.DistanceTo(totalEndPoint) > 1e-6)
                {
                    Point3d totalDimLinePoint = GetNewAlignedDimLinePoint(
                        basePoint,
                        totalEndPoint,
                        dims[0]);

                    AlignedDimension totalDim =
                        new AlignedDimension(
                            basePoint,
                            totalEndPoint,
                            totalDimLinePoint,
                            "<>",
                            dimStyleId);

                    Dimension sourceDim = tr.GetObject(dims.Last().Id, OpenMode.ForRead) as Dimension;

                    if (sourceDim != null)
                        CopyDimensionDisplayProperties(sourceDim, totalDim);

                    totalDim.ColorIndex = 2;

                    modelSpace.AppendEntity(totalDim);
                    tr.AddNewlyCreatedDBObject(totalDim, true);
                }
                tr.Commit();
                Application.DocumentManager.MdiActiveDocument.Editor.Regen();
            }
        }
        private static Vector3d GetAlignedRuleDirection(DimVinaCAD dim)
        {
            Point3d topPoint = GetTopPoint(dim);
            Point3d bottomPoint = GetBottomPoint(dim);

            Vector3d dir = XLDCDSetting.IsTopToBottomAligned
                ? bottomPoint - topPoint
                : topPoint - bottomPoint;

            if (dir.Length <= 1e-9)
                return Vector3d.XAxis;

            return dir.GetNormal();
        }
        private static double GetTopStation(DimVinaCAD dim, Vector3d baseDir)
        {
            Point3d p = dim.Diem1.Y >= dim.Diem2.Y ? dim.Diem1 : dim.Diem2;
            return p.X * baseDir.X + p.Y * baseDir.Y;
        }

        private static double GetBottomStation(DimVinaCAD dim, Vector3d baseDir)
        {
            Point3d p = dim.Diem1.Y < dim.Diem2.Y ? dim.Diem1 : dim.Diem2;
            return p.X * baseDir.X + p.Y * baseDir.Y;
        }
        private static Point3d GetNewAlignedDimLinePoint(Point3d p1, Point3d p2, DimVinaCAD sourceDim)
        {
            Vector3d dir = p2 - p1;

            if (dir.Length <= 1e-9)
                return sourceDim.DimLinePoint;

            dir = dir.GetNormal();

            Vector3d normal = new Vector3d(-dir.Y, dir.X, 0).GetNormal();

            double offset =
                (sourceDim.DimLinePoint.X - sourceDim.Diem1.X) * normal.X +
                (sourceDim.DimLinePoint.Y - sourceDim.Diem1.Y) * normal.Y;

            return p1 + normal * offset;
        }
        private static Vector3d GetFootDirection(DimVinaCAD dim)
        {
            Vector3d v = dim.Diem2 - dim.Diem1;

            if (v.Length <= 1e-9)
                return Vector3d.XAxis;

            v = v.GetNormal();

            if (v.X < 0 || (Math.Abs(v.X) <= 1e-9 && v.Y < 0))
                v = -v;

            return v;
        }

        private static bool IsSameFootDirection(DimVinaCAD dim1, DimVinaCAD dim2, double angleTolerance = 0.03)
        {
            Vector3d v1 = GetFootDirection(dim1);
            Vector3d v2 = GetFootDirection(dim2);

            double dot = Math.Abs(v1.DotProduct(v2));
            double cosTol = Math.Cos(angleTolerance);

            return dot >= cosTol;
        }

        private static double GetPerpendicularLineValue(DimVinaCAD dim, Vector3d normal)
        {
            return dim.DimLinePoint.X * normal.X + dim.DimLinePoint.Y * normal.Y;
        }

        private static double GetStartStation(DimVinaCAD dim, Vector3d direction)
        {
            double s1 = dim.Diem1.X * direction.X + dim.Diem1.Y * direction.Y;
            double s2 = dim.Diem2.X * direction.X + dim.Diem2.Y * direction.Y;

            return Math.Min(s1, s2);
        }

        private static double GetEndStation(DimVinaCAD dim, Vector3d direction)
        {
            double s1 = dim.Diem1.X * direction.X + dim.Diem1.Y * direction.Y;
            double s2 = dim.Diem2.X * direction.X + dim.Diem2.Y * direction.Y;

            return Math.Max(s1, s2);
        }
        private static double GetDimensionLinearScale(Dimension dim)
        {
            if (dim == null)
                return 1.0;

            if (Math.Abs(dim.Dimlfac) <= 1e-9)
                return 1.0;

            return dim.Dimlfac;
        }
        private static double ProjectPoint(Point3d point, double angle)
        {
            double ux = Math.Cos(angle);
            double uy = Math.Sin(angle);

            return point.X * ux + point.Y * uy;
        }

        private static double ProjectPointToNormal(Point3d point, double angle)
        {
            double nx = -Math.Sin(angle);
            double ny = Math.Cos(angle);

            return point.X * nx + point.Y * ny;
        }

        private static double GetDimStartStation(DimVinaCAD dim)
        {
            double s1 = ProjectPoint(dim.Diem1, dim.Rotation);
            double s2 = ProjectPoint(dim.Diem2, dim.Rotation);

            return Math.Min(s1, s2);
        }
        private static double GetDimEndStation(DimVinaCAD dim)
        {
            double s1 = ProjectPoint(dim.Diem1, dim.Rotation);
            double s2 = ProjectPoint(dim.Diem2, dim.Rotation);

            return Math.Max(s1, s2);
        }
        private static Point3d GetStartPoint(DimVinaCAD dim)
        {
            double s1 = ProjectPoint(dim.Diem1, dim.Rotation);
            double s2 = ProjectPoint(dim.Diem2, dim.Rotation);

            return s1 <= s2 ? dim.Diem1 : dim.Diem2;
        }

        private static Point3d GetEndPoint(DimVinaCAD dim)
        {
            double s1 = ProjectPoint(dim.Diem1, dim.Rotation);
            double s2 = ProjectPoint(dim.Diem2, dim.Rotation);

            return s1 >= s2 ? dim.Diem1 : dim.Diem2;
        }
        private static double GetDimLineKey(DimVinaCAD dim, double baseAngle)
        {
            double nx = -Math.Sin(baseAngle);
            double ny = Math.Cos(baseAngle);

            return dim.DimLinePoint.X * nx + dim.DimLinePoint.Y * ny;
        }
        private static bool IsSameDirection(DimVinaCAD dim1, DimVinaCAD dim2, double angleTolerance = 0.03)
        {
            double a1 = NormalizeAngle(dim1.Rotation);
            double a2 = NormalizeAngle(dim2.Rotation);

            double diff = Math.Abs(a1 - a2);

            diff = diff % (Math.PI * 2);
            if (diff > Math.PI)
                diff = Math.PI * 2 - diff;

            diff = Math.Min(diff, Math.Abs(Math.PI - diff));

            return diff <= angleTolerance;
        }

        private static ObjectId GetDimStyleIdByName(Database db, Transaction tr, string dimStyleName)
        {
            if (db == null || tr == null)
                return ObjectId.Null;

            if (string.IsNullOrWhiteSpace(dimStyleName))
                return db.Dimstyle;

            DimStyleTable dimStyleTable = tr.GetObject(db.DimStyleTableId, OpenMode.ForRead) as DimStyleTable;

            if (dimStyleTable == null)
                return db.Dimstyle;

            if (!dimStyleTable.Has(dimStyleName))
                return db.Dimstyle;

            return dimStyleTable[dimStyleName];
        }

        private static void CopyDimensionDisplayProperties(Dimension sourceDim, Dimension targetDim)
        {
            if (sourceDim == null || targetDim == null)
                return;


            targetDim.Dimscale = sourceDim.Dimscale * sourceDim.Dimtxt / targetDim.Dimtxt;

            targetDim.Dimlfac = sourceDim.Dimlfac;

            targetDim.TextStyleId = sourceDim.TextStyleId;

        }
        private static Point3d GetLeftPoint(DimVinaCAD dim)
        {
            if (dim.Diem1.X <= dim.Diem2.X)
                return dim.Diem1;

            return dim.Diem2;
        }
        private static Point3d GetRightPoint(DimVinaCAD dim)
        {
            if (dim.Diem1.X >= dim.Diem2.X)
                return dim.Diem1;

            return dim.Diem2;
        }

        private static Point3d GetTopPoint(DimVinaCAD dim)
        {
            return dim.Diem1.Y >= dim.Diem2.Y ? dim.Diem1 : dim.Diem2;
        }

        private static Point3d GetBottomPoint(DimVinaCAD dim)
        {
            return dim.Diem1.Y <= dim.Diem2.Y ? dim.Diem1 : dim.Diem2;
        }

        private static double GetDimensionValue(Dimension dim)
        {
            if (dim == null)
                return 0;

            string dimText = dim.DimensionText;

            double dimlfac = GetDimensionLinearScale(dim);

            if (string.IsNullOrWhiteSpace(dimText) || dimText.Trim() == "<>")
                return dim.Measurement;

            double? parsedValue = TryParseDimensionDisplayText(dimText);

            if (parsedValue.HasValue)
                return parsedValue.Value;

            return dim.Measurement * dimlfac;
        }
        private static double? TryParseDimensionDisplayText(string dimText)
        {
            if (string.IsNullOrWhiteSpace(dimText))
                return null;

            string text = dimText.Trim();

            text = text.Replace("\\P", " ");
            text = text.Replace("\\~", " ");
            text = text.Replace("{", "");
            text = text.Replace("}", "");
            text = text.Replace(",", ".");

            text = System.Text.RegularExpressions.Regex.Replace(
                text,
                @"\\[A-Za-z][^;]*;",
                "");

            text = text.Replace("mm", "", StringComparison.OrdinalIgnoreCase);

            System.Text.RegularExpressions.Match match =
                System.Text.RegularExpressions.Regex.Match(
                    text,
                    @"[-+]?\d+(\.\d+)?");

            if (!match.Success)
                return null;

            if (double.TryParse(
                    match.Value,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double value))
            {
                return value;
            }

            return null;
        }
        private static bool IsHorizontalDimTD(DimVinaCAD dim)
        {
            double angle = NormalizeAngle(dim.GocDim);

            return IsSameAngle(angle, 0) ||
                   IsSameAngle(angle, Math.PI);
        }

        private static bool IsVerticalDimTD(DimVinaCAD dim)
        {
            double angle = NormalizeAngle(dim.GocDim);

            return IsSameAngle(angle, Math.PI / 2) ||
                   IsSameAngle(angle, 3 * Math.PI / 2);
        }
        private static bool IsHorizontalDim(DimVinaCAD dim)
        {
            double angle = NormalizeAngle(dim.Rotation);

            return IsSameAngle(angle, 0) ||
                   IsSameAngle(angle, Math.PI);
        }

        private static bool IsVerticalDim(DimVinaCAD dim)
        {
            double angle = NormalizeAngle(dim.Rotation);

            return IsSameAngle(angle, Math.PI / 2) ||
                   IsSameAngle(angle, 3 * Math.PI / 2);
        }

        private static bool IsSameAngle(double angle1, double angle2)
        {
            double tolerance = 1e-6;

            angle1 = NormalizeAngle(angle1);
            angle2 = NormalizeAngle(angle2);

            double diff = Math.Abs(angle1 - angle2);

            return diff < tolerance ||
                   Math.Abs(diff - 2 * Math.PI) < tolerance;
        }



        private static bool IsPointBetweenPointsX(Point3d point, Point3d point1, Point3d point2)
        {
            double minX = Math.Min(point1.X, point2.X);
            double maxX = Math.Max(point1.X, point2.X);

            return point.X >= minX && point.X <= maxX;
        }

        private static bool IsPointBetweenPointsY(Point3d point, Point3d point1, Point3d point2)
        {
            double minY = Math.Min(point1.Y, point2.Y);
            double maxY = Math.Max(point1.Y, point2.Y);

            return point.Y >= minY && point.Y <= maxY;
        }

        private static double NormalizeAngle(double angle)
        {
            double twoPi = Math.PI * 2;

            angle %= twoPi;

            if (angle < 0)
                angle += twoPi;

            return angle;
        }


    }
}


