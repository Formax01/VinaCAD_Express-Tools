using Prima.VinaCAD.EditorInput;
using System;
using System.Collections.Generic;
using System.Linq;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.VinaCad.Modeling;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCad.Helper.Helper
{
    public class DrawWallHelper
    {
        private const double Tolerance = 0.001;

        private const string WallSideAppName = "VINACAD_WALL_SIDE";
        private const string WallSegmentAppName = "VINACAD_WALL_SEGMENT";
        private const string SideA = "A";
        private const string SideB = "B";

        private const string CapAppName = "VINACAD_WALL_CAP";
        private const string CapMarker = "1";

        private static void EnsureRegApp(Transaction tr, Database db, string appName)
        {
            RegAppTable regTable = tr.GetObject(db.RegAppTableId, OpenMode.ForRead) as RegAppTable;
            if (regTable != null && !regTable.Has(appName))
            {
                regTable.UpgradeOpen();
                RegAppTableRecord app = new RegAppTableRecord { Name = appName };
                regTable.Add(app);
                tr.AddNewlyCreatedDBObject(app, true);
            }
        }

        private static void TagWallSide(Transaction tr, Database db, Line line, string side)
        {
            EnsureRegApp(tr, db, WallSideAppName);
            ResultBuffer rb = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, WallSideAppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, side));
            line.XData = rb;
        }

        public static string GetWallSideMarker(Line line)
        {
            try
            {
                ResultBuffer rb = line.GetXDataForApplication(WallSideAppName);
                if (rb == null) return null;
                foreach (TypedValue tv in rb)
                {
                    if (tv.TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                        return tv.Value as string;
                }
            }
            catch { /* line chưa từng có XData của app này */ }
            return null;
        }

        public static string GetWallSegmentId(Line line)
        {
            try
            {
                ResultBuffer rb = line.GetXDataForApplication(WallSegmentAppName);
                if (rb == null) return null;

                foreach (TypedValue tv in rb)
                {
                    if (tv.TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                        return tv.Value as string;
                }
            }
            catch { /* bản vẽ cũ chưa có XData đoạn tường */ }
            return null;
        }

        private static void TagWallSegment(
            Transaction tr,
            Database db,
            Line line,
            string side,
            string segmentId,
            Point3d startPoint,
            Point3d endPoint,
            Point3d line1Start,
            Point3d line1End,
            Point3d line2Start,
            Point3d line2End)
        {
            EnsureRegApp(tr, db, WallSideAppName);
            EnsureRegApp(tr, db, WallSegmentAppName);

            // Ghi hai nhóm XData trong cùng một lần để không làm mất tag của nhóm kia.
            // Sáu Point3d trước đây nằm trong WallSegment nay được lưu trực tiếp trên entity.
            line.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, WallSideAppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, side),
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, WallSegmentAppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, segmentId),
                new TypedValue((int)DxfCode.ExtendedDataReal, startPoint.X),
                new TypedValue((int)DxfCode.ExtendedDataReal, startPoint.Y),
                new TypedValue((int)DxfCode.ExtendedDataReal, startPoint.Z),
                new TypedValue((int)DxfCode.ExtendedDataReal, endPoint.X),
                new TypedValue((int)DxfCode.ExtendedDataReal, endPoint.Y),
                new TypedValue((int)DxfCode.ExtendedDataReal, endPoint.Z),
                new TypedValue((int)DxfCode.ExtendedDataReal, line1Start.X),
                new TypedValue((int)DxfCode.ExtendedDataReal, line1Start.Y),
                new TypedValue((int)DxfCode.ExtendedDataReal, line1Start.Z),
                new TypedValue((int)DxfCode.ExtendedDataReal, line1End.X),
                new TypedValue((int)DxfCode.ExtendedDataReal, line1End.Y),
                new TypedValue((int)DxfCode.ExtendedDataReal, line1End.Z),
                new TypedValue((int)DxfCode.ExtendedDataReal, line2Start.X),
                new TypedValue((int)DxfCode.ExtendedDataReal, line2Start.Y),
                new TypedValue((int)DxfCode.ExtendedDataReal, line2Start.Z),
                new TypedValue((int)DxfCode.ExtendedDataReal, line2End.X),
                new TypedValue((int)DxfCode.ExtendedDataReal, line2End.Y),
                new TypedValue((int)DxfCode.ExtendedDataReal, line2End.Z));
        }

        public static void CopyWallMetadata(Transaction tr, Database db, Line source, Line target)
        {
            ResultBuffer sourceXData = source.XData;
            if (sourceXData == null) return;

            EnsureRegApp(tr, db, WallSideAppName);
            EnsureRegApp(tr, db, WallSegmentAppName);
            target.XData = new ResultBuffer(sourceXData.AsArray());
        }

        private static void TagAsCap(Transaction tr, Database db, Line line)
        {
            EnsureRegApp(tr, db, CapAppName);
            ResultBuffer rb = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, CapAppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, CapMarker));
            line.XData = rb;
        }

        public static bool IsWallCap(Line line)
        {
            try
            {
                ResultBuffer rb = line.GetXDataForApplication(CapAppName);
                return rb != null;
            }
            catch { return false; }
        }

        private static Point3d MidPoint(Point3d first, Point3d second)
        {
            return new Point3d(
                (first.X + second.X) / 2.0,
                (first.Y + second.Y) / 2.0,
                (first.Z + second.Z) / 2.0);
        }

        private sealed class WallSegmentData
        {
            public string SegmentId = string.Empty;
            public Point3d CenterStart;
            public Point3d CenterEnd;
            public Point3d SideAStart;
            public Point3d SideAEnd;
            public Point3d SideBStart;
            public Point3d SideBEnd;

            public double Width => SideAStart.DistanceTo(SideBStart);

            public Point3d[] Outline => new[]
            {
                SideAStart,
                SideAEnd,
                SideBEnd,
                SideBStart
            };

            public void GetEndpointFace(bool atStart, out Point3d sideA, out Point3d sideB)
            {
                sideA = atStart ? SideAStart : SideAEnd;
                sideB = atStart ? SideBStart : SideBEnd;
            }

            public void GetBoundary(string side, out Point3d start, out Point3d end)
            {
                bool isSideA = side == SideA;
                start = isSideA ? SideAStart : SideBStart;
                end = isSideA ? SideAEnd : SideBEnd;
            }
        }

        private static WallSegmentData CreateWallSegmentData(
            string segmentId,
            Point3d line1Start,
            Point3d line1End,
            Point3d line2Start,
            Point3d line2End)
        {
            return new WallSegmentData
            {
                SegmentId = segmentId,
                CenterStart = MidPoint(line1Start, line2Start),
                CenterEnd = MidPoint(line1End, line2End),
                SideAStart = line1Start,
                SideAEnd = line1End,
                SideBStart = line2Start,
                SideBEnd = line2End
            };
        }

        private static bool TryGetWallSegmentData(Line line, out WallSegmentData data)
        {
            data = null!;

            try
            {
                ResultBuffer rb = line.GetXDataForApplication(WallSegmentAppName);
                if (rb == null) return false;

                string? segmentId = null;
                List<double> values = new List<double>();

                foreach (TypedValue tv in rb)
                {
                    if (tv.TypeCode == (int)DxfCode.ExtendedDataAsciiString && segmentId == null)
                        segmentId = tv.Value as string;
                    else if (tv.TypeCode == (int)DxfCode.ExtendedDataReal && tv.Value != null)
                        values.Add(Convert.ToDouble(tv.Value));
                }

                if (string.IsNullOrEmpty(segmentId) || values.Count < 18) return false;

                data = new WallSegmentData
                {
                    SegmentId = segmentId,
                    CenterStart = new Point3d(values[0], values[1], values[2]),
                    CenterEnd = new Point3d(values[3], values[4], values[5]),
                    SideAStart = new Point3d(values[6], values[7], values[8]),
                    SideAEnd = new Point3d(values[9], values[10], values[11]),
                    SideBStart = new Point3d(values[12], values[13], values[14]),
                    SideBEnd = new Point3d(values[15], values[16], values[17])
                };

                return true;
            }
            catch
            {
                return false;
            }
        }

        #region 1. TÍNH TOÁN & TẠO LINE
        public static void CalculateWallLines(
            Point3d startPoint, Point3d endPoint, double thickness, WallAlignment alignment,
            out Point3d line1Start, out Point3d line1End, out Point3d line2Start, out Point3d line2End)
        {
            Vector3d direction = endPoint - startPoint;
            direction = direction.GetNormal();
            Vector3d perpendicular = new Vector3d(-direction.Y, direction.X, 0);

            switch (alignment)
            {
                case WallAlignment.Center:
                    Vector3d offsetHalf = perpendicular * (thickness / 2.0);
                    line1Start = startPoint + offsetHalf; line1End = endPoint + offsetHalf;
                    line2Start = startPoint - offsetHalf; line2End = endPoint - offsetHalf;
                    break;
                case WallAlignment.Left:
                    Vector3d offsetRight = perpendicular * thickness;
                    line1Start = startPoint; line1End = endPoint;
                    line2Start = startPoint + offsetRight; line2End = endPoint + offsetRight;
                    break;
                case WallAlignment.Right:
                    Vector3d offsetLeft = perpendicular * (-thickness);
                    line1Start = startPoint; line1End = endPoint;
                    line2Start = startPoint + offsetLeft; line2End = endPoint + offsetLeft;
                    break;
                default:
                    line1Start = startPoint; line1End = endPoint;
                    line2Start = startPoint; line2End = endPoint;
                    break;
            }
        }

        public static List<ObjectId> CreateWallLines(
            Database db, Point3d line1Start, Point3d line1End, Point3d line2Start, Point3d line2End, string layerName)
        {
            List<ObjectId> lineIds = new List<ObjectId>();
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    BlockTable blockTable = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                    BlockTableRecord modelSpace = tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;
                    ObjectId layerId = GetOrCreateLayer(db, tr, layerName);

                    string segmentId = Guid.NewGuid().ToString("N");
                    Point3d startPoint = MidPoint(line1Start, line2Start);
                    Point3d endPoint = MidPoint(line1End, line2End);

                    Line line1 = new Line(line1Start, line1End) { LayerId = layerId };
                    modelSpace.AppendEntity(line1); tr.AddNewlyCreatedDBObject(line1, true); lineIds.Add(line1.ObjectId);
                    TagWallSegment(tr, db, line1, SideA, segmentId, startPoint, endPoint,
                        line1Start, line1End, line2Start, line2End);

                    Line line2 = new Line(line2Start, line2End) { LayerId = layerId };
                    modelSpace.AppendEntity(line2); tr.AddNewlyCreatedDBObject(line2, true); lineIds.Add(line2.ObjectId);
                    TagWallSegment(tr, db, line2, SideB, segmentId, startPoint, endPoint,
                        line1Start, line1End, line2Start, line2End);

                    tr.Commit();
                }
                catch (Exception ex) { tr.Abort(); throw new Exception($"Error creating wall lines: {ex.Message}", ex); }
            }
            return lineIds;
        }

        public static void EraseEntities(Database db, IEnumerable<ObjectId> entityIds)
        {
            if (entityIds == null) return;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    foreach (ObjectId id in entityIds.Distinct())
                    {
                        DBObject obj = tr.GetObject(id, OpenMode.ForWrite);
                        if (obj != null && !obj.IsErased) obj.Erase();
                    }

                    tr.Commit();
                }
                catch
                {
                    tr.Abort();
                    throw;
                }
            }
        }

        private static ObjectId GetOrCreateLayer(Database db, Transaction tr, string layerName)
        {
            LayerTable layerTable = tr.GetObject(db.LayerTableId, OpenMode.ForRead) as LayerTable;
            if (layerTable.Has(layerName)) return layerTable[layerName];

            LayerTableRecord layerRecord = new LayerTableRecord { Name = layerName, Color = Teigha.Colors.Color.FromColorIndex(Teigha.Colors.ColorMethod.ByAci, 7) };
            LayerTable layerTableWrite = tr.GetObject(db.LayerTableId, OpenMode.ForWrite) as LayerTable;
            ObjectId layerId = layerTableWrite.Add(layerRecord);
            tr.AddNewlyCreatedDBObject(layerRecord, true);
            return layerId;
        }

        public static string EnsureWallLayer(Database db, string layerName, out bool wasCreated)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                LayerTable layerTable = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                wasCreated = !layerTable.Has(layerName);

                ObjectId layerId = GetOrCreateLayer(db, tr, layerName);
                LayerTableRecord layer = (LayerTableRecord)tr.GetObject(layerId, OpenMode.ForWrite);

                // Layer tường phải ở trạng thái có thể vẽ và đặt làm layer hiện hành.
                layer.IsOff = false;
                layer.IsFrozen = false;
                layer.IsLocked = false;

                // Đặt làm layer hiện hành và đồng thời trả về đúng tên đang có trong DWG
                // (LayerTable không phân biệt hoa/thường, tránh tạo trùng WALL và Wall).
                db.Clayer = layerId;
                string actualLayerName = layer.Name;
                tr.Commit();
                return actualLayerName;
            }
        }
        #endregion

        #region 2. TOPOLOGY & LOCAL CSG
        public static List<IntersectionInfo> FindWallIntersections(
            Database db, Point3d line1Start, Point3d line1End, Point3d line2Start, Point3d line2End, string wallLayerName)
        {
            List<IntersectionInfo> intersections = new List<IntersectionInfo>();
            WallSegmentData newWall = CreateWallSegmentData(
                "__NEW_WALL__", line1Start, line1End, line2Start, line2End);

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    BlockTable blockTable = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                    BlockTableRecord modelSpace = tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead) as BlockTableRecord;
                    LayerTable layerTable = tr.GetObject(db.LayerTableId, OpenMode.ForRead) as LayerTable;

                    if (!layerTable.Has(wallLayerName)) { tr.Commit(); return intersections; }
                    ObjectId targetLayerId = layerTable[wallLayerName];

                    List<WallLineCandidate> tagged = new List<WallLineCandidate>();
                    List<WallLineCandidate> legacy = new List<WallLineCandidate>();

                    foreach (ObjectId objId in modelSpace)
                    {
                        DBObject obj = tr.GetObject(objId, OpenMode.ForRead);
                        if (!(obj is Line line) || line.LayerId != targetLayerId || IsWallCap(line)) continue;
                        if (line.StartPoint.DistanceTo(line.EndPoint) <= Tolerance) continue;

                        WallLineCandidate candidate = new WallLineCandidate { LineId = objId, Line = line };
                        if (TryGetWallSegmentData(line, out WallSegmentData data))
                        {
                            candidate.Wall = data;
                            tagged.Add(candidate);
                        }
                        else
                        {
                            legacy.Add(candidate);
                        }
                    }

                    foreach (IGrouping<string, WallLineCandidate> group in tagged.GroupBy(x => x.Wall.SegmentId))
                    {
                        WallSegmentData existingWall = group.First().Wall;
                        if (!WallFootprintsOverlap(newWall, existingWall)) continue;

                        bool isEndJunction = TryFindEndpointConnection(
                            newWall, existingWall,
                            out _, out bool existingAtStart);

                        List<WallLineCandidate> local = group
                            .Where(x => SegmentTouchesWallFootprint(x.Line.StartPoint, x.Line.EndPoint, newWall) ||
                                        (isEndJunction && LineBelongsToEndpoint(x.Line, existingWall, existingAtStart)))
                            .ToList();

                        // Trường hợp đoạn mới nằm hoàn toàn trong footprint tường cũ: vẫn cần
                        // chạy CSG để xóa hai biên mới, dù không có cạnh nào cắt trực tiếp.
                        if (local.Count == 0)
                        {
                            WallLineCandidate nearest = group
                                .OrderBy(x => DistancePointToSegment(newWall.CenterStart, x.Line.StartPoint, x.Line.EndPoint))
                                .First();
                            local.Add(nearest);
                        }

                        intersections.AddRange(local.Select(x => new IntersectionInfo { ExistingLineId = x.LineId }));
                    }

                    // Bản vẽ cũ chưa có XData chỉ được xử lý khi line thực sự cắt footprint.
                    // Không suy đoán cặp mặt bằng khoảng cách vì đó là nguồn gây ghép nhầm.
                    foreach (WallLineCandidate candidate in legacy)
                    {
                        if (SegmentTouchesWallFootprint(candidate.Line.StartPoint, candidate.Line.EndPoint, newWall))
                            intersections.Add(new IntersectionInfo { ExistingLineId = candidate.LineId });
                    }

                    tr.Commit();
                }
                catch (Exception ex) { tr.Abort(); throw new Exception($"Error finding intersections: {ex.Message}", ex); }
            }

            return intersections
                .GroupBy(x => x.ExistingLineId)
                .Select(x => x.First())
                .ToList();
        }

        private sealed class WallLineCandidate
        {
            public ObjectId LineId;
            public Line Line = null!;
            public WallSegmentData Wall = null!;
        }

        public static void CleanupIntersections(
            Database db, List<ObjectId> newWallLineIds, List<IntersectionInfo> intersections, string wallLayerName, double thickness, WallAlignment alignment)
        {
            if (intersections == null || intersections.Count == 0 || newWallLineIds.Count < 2) return;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    BlockTable blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(
                        blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    List<Line> newLines = newWallLineIds
                        .Select(id => tr.GetObject(id, OpenMode.ForWrite) as Line)
                        .OfType<Line>()
                        .Where(line => !line.IsErased)
                        .ToList();
                    List<Line> existingLines = intersections
                        .Select(i => tr.GetObject(i.ExistingLineId, OpenMode.ForWrite) as Line)
                        .OfType<Line>()
                        .Where(line => !line.IsErased && !IsWallCap(line))
                        .GroupBy(line => line.ObjectId)
                        .Select(g => g.First())
                        .ToList();

                    if (newLines.Count < 2 || !TryGetWallSegmentData(newLines[0], out WallSegmentData newWall))
                    {
                        tr.Commit();
                        return;
                    }

                    Dictionary<string, WallSegmentData> wallData = new Dictionary<string, WallSegmentData>
                    {
                        [newWall.SegmentId] = newWall
                    };

                    foreach (Line line in existingLines)
                    {
                        if (TryGetWallSegmentData(line, out WallSegmentData data))
                            wallData[data.SegmentId] = data;
                    }

                    // Chỉ junction endpoint-endpoint mới được miter. T và X giữ nguyên
                    // endpoint; phần giao được giải quyết bởi local CSG phía dưới.
                    foreach (WallSegmentData targetWall in wallData.Values.Where(x => x.SegmentId != newWall.SegmentId))
                    {
                        if (TryFindEndpointConnection(
                            newWall, targetWall,
                            out bool newAtStart, out bool targetAtStart))
                        {
                            MiterEndpointJunction(
                                newLines, existingLines,
                                newWall, targetWall,
                                newAtStart, targetAtStart);
                        }
                    }

                    // Xử lý line hiện hữu trước để khi hai đoạn đồng tuyến chồng nhau,
                    // phần trùng giữ metadata cũ ổn định thay vì đổi chủ sở hữu tùy lượt vẽ.
                    List<Line> allLines = existingLines
                        .Concat(newLines)
                        .GroupBy(l => l.ObjectId)
                        .Select(g => g.First())
                        .ToList();

                    Dictionary<ObjectId, string> ownerMap = new Dictionary<ObjectId, string>();
                    foreach (Line line in allLines)
                    {
                        ownerMap[line.ObjectId] = TryGetWallSegmentData(line, out WallSegmentData data)
                            ? data.SegmentId
                            : $"LEGACY_{line.ObjectId}";
                    }

                    Dictionary<ObjectId, List<Point3d>> lineCuts = allLines
                        .ToDictionary(line => line.ObjectId, line => new List<Point3d>());

                    for (int i = 0; i < allLines.Count; i++)
                    {
                        for (int j = i + 1; j < allLines.Count; j++)
                        {
                            Line first = allLines[i];
                            Line second = allLines[j];
                            if (ownerMap[first.ObjectId] == ownerMap[second.ObjectId]) continue;

                            if (TryGetFiniteIntersection(
                                first.StartPoint, first.EndPoint,
                                second.StartPoint, second.EndPoint,
                                out Point3d intersection))
                            {
                                lineCuts[first.ObjectId].Add(intersection);
                                lineCuts[second.ObjectId].Add(intersection);
                            }
                            else
                            {
                                AddSharedCollinearCuts(first, second, lineCuts);
                            }
                        }
                    }

                    List<(Point3d Start, Point3d End)> createdSegments =
                        new List<(Point3d Start, Point3d End)>();

                    foreach (Line line in allLines)
                    {
                        List<Point3d> cuts = lineCuts[line.ObjectId];
                        cuts.Add(line.StartPoint);
                        cuts.Add(line.EndPoint);

                        List<Point3d> uniqueCuts = cuts
                            .OrderBy(p => p.DistanceTo(line.StartPoint))
                            .Aggregate(new List<Point3d>(), (result, point) =>
                            {
                                if (result.Count == 0 || result.Last().DistanceTo(point) > Tolerance)
                                    result.Add(point);
                                return result;
                            });

                        for (int i = 0; i < uniqueCuts.Count - 1; i++)
                        {
                            Point3d first = uniqueCuts[i];
                            Point3d second = uniqueCuts[i + 1];
                            if (first.DistanceTo(second) <= Tolerance) continue;

                            Point3d midpoint = MidPoint(first, second);
                            string ownerId = ownerMap[line.ObjectId];

                            if (IsInsideAnotherWall(midpoint, ownerId, wallData.Values)) continue;
                            if (createdSegments.Any(segment => SameUndirectedSegment(
                                first, second, segment.Start, segment.End))) continue;

                            Line replacement = new Line(first, second) { LayerId = line.LayerId };
                            modelSpace.AppendEntity(replacement);
                            tr.AddNewlyCreatedDBObject(replacement, true);
                            CopyWallMetadata(tr, db, line, replacement);
                            createdSegments.Add((first, second));
                        }
                    }

                    foreach (Line line in allLines)
                    {
                        if (!line.IsErased) line.Erase();
                    }

                    tr.Commit();
                }
                catch (Exception ex) { tr.Abort(); throw new Exception($"Error cleaning up intersections: {ex.Message}", ex); }
            }
        }

        private static void MiterEndpointJunction(
            List<Line> newLines,
            List<Line> existingLines,
            WallSegmentData newWall,
            WallSegmentData targetWall,
            bool newAtStart,
            bool targetAtStart)
        {
            bool preservePhysicalSide = newAtStart != targetAtStart;
            string[] sides = { SideA, SideB };

            foreach (string newSide in sides)
            {
                bool newSideIsLeft = IsBoundaryOnLeft(newWall, newSide);
                bool targetMustBeLeft = preservePhysicalSide
                    ? newSideIsLeft
                    : !newSideIsLeft;
                string targetSide = sides.First(side =>
                    IsBoundaryOnLeft(targetWall, side) == targetMustBeLeft);

                newWall.GetBoundary(newSide, out Point3d newBoundaryStart, out Point3d newBoundaryEnd);
                targetWall.GetBoundary(targetSide, out Point3d targetBoundaryStart, out Point3d targetBoundaryEnd);

                if (!GetTrueIntersection(
                    newBoundaryStart, newBoundaryEnd,
                    targetBoundaryStart, targetBoundaryEnd,
                    out Point3d intersection))
                {
                    continue;
                }

                newWall.GetEndpointFace(newAtStart, out Point3d newAEnd, out Point3d newBEnd);
                targetWall.GetEndpointFace(targetAtStart, out Point3d targetAEnd, out Point3d targetBEnd);
                Point3d expectedNewEnd = newSide == SideA ? newAEnd : newBEnd;
                Point3d expectedTargetEnd = targetSide == SideA ? targetAEnd : targetBEnd;

                double miterLimit = Math.Max(newWall.Width, targetWall.Width) * 10.0 + 1.0;
                if (intersection.DistanceTo(expectedNewEnd) > miterLimit ||
                    intersection.DistanceTo(expectedTargetEnd) > miterLimit)
                {
                    continue;
                }

                Line newLine = newLines.FirstOrDefault(line => GetWallSideMarker(line) == newSide);
                Line targetLine = existingLines
                    .Where(line => GetWallSegmentId(line) == targetWall.SegmentId &&
                                   GetWallSideMarker(line) == targetSide)
                    .OrderBy(line => Math.Min(
                        line.StartPoint.DistanceTo(expectedTargetEnd),
                        line.EndPoint.DistanceTo(expectedTargetEnd)))
                    .FirstOrDefault();

                if (newLine == null || targetLine == null) continue;

                if (newAtStart) newLine.StartPoint = intersection;
                else newLine.EndPoint = intersection;

                if (targetLine.StartPoint.DistanceTo(expectedTargetEnd) <=
                    targetLine.EndPoint.DistanceTo(expectedTargetEnd))
                    targetLine.StartPoint = intersection;
                else
                    targetLine.EndPoint = intersection;
            }
        }

        private static bool TryFindEndpointConnection(
            WallSegmentData first,
            WallSegmentData second,
            out bool firstAtStart,
            out bool secondAtStart)
        {
            firstAtStart = false;
            secondAtStart = false;
            double bestScore = double.MaxValue;
            bool found = false;

            for (int firstEnd = 0; firstEnd < 2; firstEnd++)
            {
                for (int secondEnd = 0; secondEnd < 2; secondEnd++)
                {
                    bool firstStart = firstEnd == 0;
                    bool secondStart = secondEnd == 0;
                    first.GetEndpointFace(firstStart, out Point3d firstA, out Point3d firstB);
                    second.GetEndpointFace(secondStart, out Point3d secondA, out Point3d secondB);

                    if (!SegmentsIntersectInclusive(firstA, firstB, secondA, secondB)) continue;

                    double score = MidPoint(firstA, firstB).DistanceTo(MidPoint(secondA, secondB));
                    if (score >= bestScore) continue;

                    bestScore = score;
                    firstAtStart = firstStart;
                    secondAtStart = secondStart;
                    found = true;
                }
            }

            return found;
        }

        private static bool LineBelongsToEndpoint(Line line, WallSegmentData wall, bool atStart)
        {
            wall.GetEndpointFace(atStart, out Point3d sideA, out Point3d sideB);
            double localRadius = Math.Max(wall.Width * 10.0 + 1.0, 1.0);

            return Math.Min(line.StartPoint.DistanceTo(sideA), line.EndPoint.DistanceTo(sideA)) <= localRadius ||
                   Math.Min(line.StartPoint.DistanceTo(sideB), line.EndPoint.DistanceTo(sideB)) <= localRadius;
        }

        private static bool IsBoundaryOnLeft(WallSegmentData wall, string side)
        {
            wall.GetBoundary(side, out Point3d boundaryStart, out Point3d boundaryEnd);
            Vector3d direction = wall.CenterEnd - wall.CenterStart;
            Point3d centerMidpoint = MidPoint(wall.CenterStart, wall.CenterEnd);
            Point3d boundaryMidpoint = MidPoint(boundaryStart, boundaryEnd);
            Vector3d offset = boundaryMidpoint - centerMidpoint;
            return direction.X * offset.Y - direction.Y * offset.X > 0.0;
        }

        private static void AddSharedCollinearCuts(
            Line first,
            Line second,
            Dictionary<ObjectId, List<Point3d>> lineCuts)
        {
            if (Math.Abs(Cross2d(first.StartPoint, first.EndPoint, second.StartPoint)) > Tolerance ||
                Math.Abs(Cross2d(first.StartPoint, first.EndPoint, second.EndPoint)) > Tolerance)
            {
                return;
            }

            Point3d[] endpoints =
            {
                first.StartPoint,
                first.EndPoint,
                second.StartPoint,
                second.EndPoint
            };

            foreach (Point3d endpoint in endpoints)
            {
                if (!IsPointOnSegment2d(endpoint, first.StartPoint, first.EndPoint) ||
                    !IsPointOnSegment2d(endpoint, second.StartPoint, second.EndPoint))
                {
                    continue;
                }

                lineCuts[first.ObjectId].Add(endpoint);
                lineCuts[second.ObjectId].Add(endpoint);
            }
        }

        private static bool SameUndirectedSegment(
            Point3d firstStart,
            Point3d firstEnd,
            Point3d secondStart,
            Point3d secondEnd)
        {
            return (firstStart.DistanceTo(secondStart) <= Tolerance &&
                    firstEnd.DistanceTo(secondEnd) <= Tolerance) ||
                   (firstStart.DistanceTo(secondEnd) <= Tolerance &&
                    firstEnd.DistanceTo(secondStart) <= Tolerance);
        }

        private static bool IsInsideAnotherWall(
            Point3d point,
            string ownerSegmentId,
            IEnumerable<WallSegmentData> walls)
        {
            foreach (WallSegmentData wall in walls)
            {
                if (wall.SegmentId == ownerSegmentId) continue;
                if (IsPointInPolygon(point, wall.Outline, includeBoundary: false)) return true;
            }

            return false;
        }
        #endregion

        #region 3. HÀM TOÁN HỌC HỖ TRỢ (MATH UTILS)
        private static bool WallFootprintsOverlap(WallSegmentData first, WallSegmentData second)
        {
            Point3d[] firstOutline = first.Outline;
            Point3d[] secondOutline = second.Outline;

            for (int i = 0; i < firstOutline.Length; i++)
            {
                Point3d firstStart = firstOutline[i];
                Point3d firstEnd = firstOutline[(i + 1) % firstOutline.Length];

                for (int j = 0; j < secondOutline.Length; j++)
                {
                    Point3d secondStart = secondOutline[j];
                    Point3d secondEnd = secondOutline[(j + 1) % secondOutline.Length];
                    if (SegmentsIntersectInclusive(firstStart, firstEnd, secondStart, secondEnd)) return true;
                }
            }

            return IsPointInPolygon(firstOutline[0], secondOutline, includeBoundary: true) ||
                   IsPointInPolygon(secondOutline[0], firstOutline, includeBoundary: true);
        }

        private static bool SegmentTouchesWallFootprint(Point3d start, Point3d end, WallSegmentData wall)
        {
            Point3d[] outline = wall.Outline;
            for (int i = 0; i < outline.Length; i++)
            {
                if (SegmentsIntersectInclusive(start, end, outline[i], outline[(i + 1) % outline.Length]))
                    return true;
            }

            return IsPointInPolygon(start, outline, includeBoundary: true) ||
                   IsPointInPolygon(end, outline, includeBoundary: true) ||
                   IsPointInPolygon(MidPoint(start, end), outline, includeBoundary: true);
        }

        private static bool IsPointInPolygon(Point3d point, Point3d[] polygon, bool includeBoundary)
        {
            bool inside = false;

            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                Point3d first = polygon[j];
                Point3d second = polygon[i];

                if (IsPointOnSegment2d(point, first, second)) return includeBoundary;

                bool crossesRay = (second.Y > point.Y) != (first.Y > point.Y);
                if (!crossesRay) continue;

                double xAtPointY =
                    (first.X - second.X) * (point.Y - second.Y) /
                    (first.Y - second.Y) + second.X;
                if (point.X < xAtPointY) inside = !inside;
            }

            return inside;
        }

        private static bool SegmentsIntersectInclusive(Point3d p1, Point3d p2, Point3d p3, Point3d p4)
        {
            double c1 = Cross2d(p1, p2, p3);
            double c2 = Cross2d(p1, p2, p4);
            double c3 = Cross2d(p3, p4, p1);
            double c4 = Cross2d(p3, p4, p2);

            bool properIntersection =
                ((c1 > Tolerance && c2 < -Tolerance) || (c1 < -Tolerance && c2 > Tolerance)) &&
                ((c3 > Tolerance && c4 < -Tolerance) || (c3 < -Tolerance && c4 > Tolerance));
            if (properIntersection) return true;

            if (Math.Abs(c1) <= Tolerance && IsPointOnSegment2d(p3, p1, p2)) return true;
            if (Math.Abs(c2) <= Tolerance && IsPointOnSegment2d(p4, p1, p2)) return true;
            if (Math.Abs(c3) <= Tolerance && IsPointOnSegment2d(p1, p3, p4)) return true;
            if (Math.Abs(c4) <= Tolerance && IsPointOnSegment2d(p2, p3, p4)) return true;

            return false;
        }

        private static bool IsPointOnSegment2d(Point3d point, Point3d start, Point3d end)
        {
            if (Math.Abs(Cross2d(start, end, point)) > Tolerance) return false;

            return point.X >= Math.Min(start.X, end.X) - Tolerance &&
                   point.X <= Math.Max(start.X, end.X) + Tolerance &&
                   point.Y >= Math.Min(start.Y, end.Y) - Tolerance &&
                   point.Y <= Math.Max(start.Y, end.Y) + Tolerance;
        }

        private static double Cross2d(Point3d start, Point3d end, Point3d point)
        {
            return (end.X - start.X) * (point.Y - start.Y) -
                   (end.Y - start.Y) * (point.X - start.X);
        }

        private static bool TryGetFiniteIntersection(
            Point3d p1, Point3d p2, Point3d p3, Point3d p4, out Point3d intersection)
        {
            return GetTrueIntersection(p1, p2, p3, p4, out intersection) &&
                   IsPointOnSegment(intersection, p1, p2, 0.01) &&
                   IsPointOnSegment(intersection, p3, p4, 0.01);
        }

        private static bool GetTrueIntersection(Point3d p1, Point3d p2, Point3d p3, Point3d p4, out Point3d intersection)
        {
            intersection = Point3d.Origin;
            double x1 = p1.X, y1 = p1.Y; double x2 = p2.X, y2 = p2.Y;
            double x3 = p3.X, y3 = p3.Y; double x4 = p4.X, y4 = p4.Y;

            double denom = (y4 - y3) * (x2 - x1) - (x4 - x3) * (y2 - y1);
            if (Math.Abs(denom) < Tolerance) return false;

            double ua = ((x4 - x3) * (y1 - y3) - (y4 - y3) * (x1 - x3)) / denom;
            intersection = new Point3d(x1 + ua * (x2 - x1), y1 + ua * (y2 - y1), p1.Z);
            return true;
        }

        private static bool IsPointOnSegment(Point3d pt, Point3d start, Point3d end, double tolerance)
        {
            return Math.Abs((pt.DistanceTo(start) + pt.DistanceTo(end)) - start.DistanceTo(end)) <= tolerance;
        }

        private static double DistancePointToSegment(Point3d point, Point3d start, Point3d end)
        {
            Vector3d segment = end - start;
            double lengthSquared = segment.DotProduct(segment);
            if (lengthSquared <= Tolerance * Tolerance) return point.DistanceTo(start);

            Vector3d fromStart = point - start;
            double ratio = fromStart.DotProduct(segment) / lengthSquared;
            ratio = Math.Max(0.0, Math.Min(1.0, ratio));
            Point3d projected = start + segment * ratio;
            return point.DistanceTo(projected);
        }

        public static Point2d ToPoint2d(Point3d pt) { return new Point2d(pt.X, pt.Y); }
        public static Point3d ToPoint3d(Point2d pt) { return new Point3d(pt.X, pt.Y, 0); }
        #endregion

        #region 4. BO ĐẦU TƯỜNG (END CAP)
        public static void CapFreeEnd(Database db, Point3d vertex, double thickness, string wallLayerName)
        {
            double pickTolerance = Math.Max(1.0, thickness * 0.01);

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    BlockTable blockTable = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                    BlockTableRecord modelSpace = tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;
                    LayerTable layerTable = tr.GetObject(db.LayerTableId, OpenMode.ForRead) as LayerTable;

                    if (!layerTable.Has(wallLayerName)) { tr.Commit(); return; }
                    ObjectId targetLayerId = layerTable[wallLayerName];

                    List<Line> wallLines = new List<Line>();
                    List<Line> caps = new List<Line>();
                    Dictionary<string, WallSegmentData> wallData = new Dictionary<string, WallSegmentData>();

                    foreach (ObjectId objId in modelSpace)
                    {
                        DBObject obj = tr.GetObject(objId, OpenMode.ForRead);
                        if (!(obj is Line line) || line.LayerId != targetLayerId) continue;
                        if (line.StartPoint.DistanceTo(line.EndPoint) <= Tolerance) continue;

                        if (IsWallCap(line))
                        {
                            caps.Add(line);
                            continue;
                        }

                        wallLines.Add(line);
                        if (TryGetWallSegmentData(line, out WallSegmentData data))
                            wallData[data.SegmentId] = data;
                    }

                    WallSegmentData selectedWall = null;
                    bool selectedAtStart = false;
                    double bestDistance = double.MaxValue;

                    foreach (WallSegmentData data in wallData.Values)
                    {
                        for (int endpoint = 0; endpoint < 2; endpoint++)
                        {
                            bool atStart = endpoint == 0;
                            data.GetEndpointFace(atStart, out Point3d sideA, out Point3d sideB);
                            double distance = DistancePointToSegment(vertex, sideA, sideB);
                            if (distance > pickTolerance || distance >= bestDistance) continue;

                            selectedWall = data;
                            selectedAtStart = atStart;
                            bestDistance = distance;
                        }
                    }

                    if (selectedWall == null ||
                        IsEndpointConnectedToAnotherWall(selectedWall, selectedAtStart, wallData.Values))
                    {
                        tr.Commit();
                        return;
                    }

                    selectedWall.GetEndpointFace(selectedAtStart, out Point3d expectedA, out Point3d expectedB);
                    Line sideALine = FindLineAtEndpoint(wallLines, selectedWall.SegmentId, SideA, expectedA);
                    Line sideBLine = FindLineAtEndpoint(wallLines, selectedWall.SegmentId, SideB, expectedB);

                    if (sideALine == null || sideBLine == null)
                    {
                        tr.Commit();
                        return;
                    }

                    Point3d actualA = sideALine.StartPoint.DistanceTo(expectedA) <= sideALine.EndPoint.DistanceTo(expectedA)
                        ? sideALine.StartPoint
                        : sideALine.EndPoint;
                    Point3d actualB = sideBLine.StartPoint.DistanceTo(expectedB) <= sideBLine.EndPoint.DistanceTo(expectedB)
                        ? sideBLine.StartPoint
                        : sideBLine.EndPoint;

                    bool alreadyCapped = caps.Any(cap =>
                        (cap.StartPoint.DistanceTo(actualA) <= Tolerance && cap.EndPoint.DistanceTo(actualB) <= Tolerance) ||
                        (cap.StartPoint.DistanceTo(actualB) <= Tolerance && cap.EndPoint.DistanceTo(actualA) <= Tolerance));

                    if (!alreadyCapped && actualA.DistanceTo(actualB) > Tolerance)
                    {
                        Line cap = new Line(actualA, actualB) { LayerId = targetLayerId };
                        modelSpace.AppendEntity(cap);
                        tr.AddNewlyCreatedDBObject(cap, true);
                        TagAsCap(tr, db, cap);
                    }

                    tr.Commit();
                }
                catch (Exception ex) { tr.Abort(); throw new Exception($"Error capping wall end: {ex.Message}", ex); }
            }
        }

        private static Line FindLineAtEndpoint(
            IEnumerable<Line> lines,
            string segmentId,
            string side,
            Point3d expectedEndpoint)
        {
            return lines
                .Where(line => GetWallSegmentId(line) == segmentId && GetWallSideMarker(line) == side)
                .OrderBy(line => Math.Min(
                    line.StartPoint.DistanceTo(expectedEndpoint),
                    line.EndPoint.DistanceTo(expectedEndpoint)))
                .FirstOrDefault();
        }

        private static bool IsEndpointConnectedToAnotherWall(
            WallSegmentData owner,
            bool atStart,
            IEnumerable<WallSegmentData> walls)
        {
            owner.GetEndpointFace(atStart, out Point3d sideA, out Point3d sideB);

            foreach (WallSegmentData wall in walls)
            {
                if (wall.SegmentId == owner.SegmentId) continue;
                if (SegmentTouchesWallFootprint(sideA, sideB, wall)) return true;
            }

            return false;
        }

        public static void RemoveCapAt(Database db, Point3d vertex, double thickness, string wallLayerName)
        {
            double pickTolerance = Math.Max(1.0, thickness * 0.01);

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    LayerTable layerTable = tr.GetObject(db.LayerTableId, OpenMode.ForRead) as LayerTable;
                    if (!layerTable.Has(wallLayerName)) { tr.Commit(); return; }
                    ObjectId targetLayerId = layerTable[wallLayerName];

                    BlockTable blockTable = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                    BlockTableRecord modelSpace = tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead) as BlockTableRecord;

                    List<ObjectId> toErase = new List<ObjectId>();
                    foreach (ObjectId objId in modelSpace)
                    {
                        DBObject obj = tr.GetObject(objId, OpenMode.ForRead);
                        if (obj is Line ln && ln.LayerId == targetLayerId && IsWallCap(ln))
                        {
                            if (DistancePointToSegment(vertex, ln.StartPoint, ln.EndPoint) <= pickTolerance)
                                toErase.Add(objId);
                        }
                    }

                    foreach (ObjectId id in toErase)
                    {
                        DBObject obj = tr.GetObject(id, OpenMode.ForWrite);
                        if (obj != null && !obj.IsErased) obj.Erase();
                    }

                    tr.Commit();
                }
                catch (Exception ex) { tr.Abort(); throw new Exception($"Error removing old cap: {ex.Message}", ex); }
            }
        }
        #endregion
    }

    public class IntersectionInfo
    {
        public ObjectId ExistingLineId { get; set; }
    }
}
