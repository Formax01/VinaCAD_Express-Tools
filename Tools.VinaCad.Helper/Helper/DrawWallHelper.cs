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

        public static bool TryGetWallCenterLine(
            Line line,
            out Point3d startPoint,
            out Point3d endPoint)
        {
            startPoint = Point3d.Origin;
            endPoint = Point3d.Origin;

            try
            {
                ResultBuffer rb = line.GetXDataForApplication(WallSegmentAppName);
                if (rb == null) return false;

                List<double> coordinates = rb
                    .Cast<TypedValue>()
                    .Where(value => value.TypeCode == (int)DxfCode.ExtendedDataReal)
                    .Select(value => Convert.ToDouble(value.Value))
                    .ToList();
                if (coordinates.Count < 6) return false;

                startPoint = new Point3d(
                    coordinates[0], coordinates[1], coordinates[2]);
                endPoint = new Point3d(
                    coordinates[3], coordinates[4], coordinates[5]);
                return startPoint.DistanceTo(endPoint) > Tolerance;
            }
            catch
            {
                return false;
            }
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
            target.XData = source.XData;
        }

        public static bool UpdateWallPairMetadata(
            Transaction tr,
            Database db,
            Line first,
            Line second)
        {
            string segmentId = GetWallSegmentId(first);
            if (string.IsNullOrEmpty(segmentId) ||
                GetWallSegmentId(second) != segmentId)
                return false;

            string firstSide = GetWallSideMarker(first);
            string secondSide = GetWallSideMarker(second);
            if (string.IsNullOrEmpty(firstSide) ||
                string.IsNullOrEmpty(secondSide) ||
                firstSide == secondSide)
                return false;

            double direct = first.StartPoint.DistanceTo(second.StartPoint) +
                            first.EndPoint.DistanceTo(second.EndPoint);
            double crossed = first.StartPoint.DistanceTo(second.EndPoint) +
                             first.EndPoint.DistanceTo(second.StartPoint);
            Point3d secondStart = direct <= crossed
                ? second.StartPoint
                : second.EndPoint;
            Point3d secondEnd = direct <= crossed
                ? second.EndPoint
                : second.StartPoint;
            Point3d centerStart = MidPoint(first.StartPoint, secondStart);
            Point3d centerEnd = MidPoint(first.EndPoint, secondEnd);

            TagWallSegment(
                tr, db, first, firstSide, segmentId, centerStart, centerEnd,
                first.StartPoint, first.EndPoint, secondStart, secondEnd);
            TagWallSegment(
                tr, db, second, secondSide, segmentId, centerStart, centerEnd,
                first.StartPoint, first.EndPoint, secondStart, secondEnd);
            return true;
        }

        private static void UpdateLineXData(Line line, WallSegmentData data)
        {
            ResultBuffer rb = line.XData;
            if (rb == null) return;
            TypedValue[] values = rb.AsArray();
            int idx = Array.FindIndex(values, v => v.TypeCode == (int)DxfCode.ExtendedDataRegAppName && string.Equals(v.Value as string, WallSegmentAppName));
            if (idx >= 0 && values.Length >= idx + 20)
            {
                values[idx + 2] = new TypedValue((int)DxfCode.ExtendedDataReal, data.CenterStart.X);
                values[idx + 3] = new TypedValue((int)DxfCode.ExtendedDataReal, data.CenterStart.Y);
                values[idx + 4] = new TypedValue((int)DxfCode.ExtendedDataReal, data.CenterStart.Z);
                values[idx + 5] = new TypedValue((int)DxfCode.ExtendedDataReal, data.CenterEnd.X);
                values[idx + 6] = new TypedValue((int)DxfCode.ExtendedDataReal, data.CenterEnd.Y);
                values[idx + 7] = new TypedValue((int)DxfCode.ExtendedDataReal, data.CenterEnd.Z);
                values[idx + 8] = new TypedValue((int)DxfCode.ExtendedDataReal, data.SideAStart.X);
                values[idx + 9] = new TypedValue((int)DxfCode.ExtendedDataReal, data.SideAStart.Y);
                values[idx + 10] = new TypedValue((int)DxfCode.ExtendedDataReal, data.SideAStart.Z);
                values[idx + 11] = new TypedValue((int)DxfCode.ExtendedDataReal, data.SideAEnd.X);
                values[idx + 12] = new TypedValue((int)DxfCode.ExtendedDataReal, data.SideAEnd.Y);
                values[idx + 13] = new TypedValue((int)DxfCode.ExtendedDataReal, data.SideAEnd.Z);
                values[idx + 14] = new TypedValue((int)DxfCode.ExtendedDataReal, data.SideBStart.X);
                values[idx + 15] = new TypedValue((int)DxfCode.ExtendedDataReal, data.SideBStart.Y);
                values[idx + 16] = new TypedValue((int)DxfCode.ExtendedDataReal, data.SideBStart.Z);
                values[idx + 17] = new TypedValue((int)DxfCode.ExtendedDataReal, data.SideBEnd.X);
                values[idx + 18] = new TypedValue((int)DxfCode.ExtendedDataReal, data.SideBEnd.Y);
                values[idx + 19] = new TypedValue((int)DxfCode.ExtendedDataReal, data.SideBEnd.Z);
                line.XData = new ResultBuffer(values);
            }
        }

        public static void TagAsCap(Transaction tr, Database db, Line line)
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
            string segmentId, Point3d line1Start, Point3d line1End, Point3d line2Start, Point3d line2End)
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
            catch { return false; }
        }

        #region 1. TÍNH TOÁN & TẠO LINE
        public static void CalculateWallLines(
            Point3d startPoint, Point3d endPoint, double thickness, WallAlignment alignment,
            out Point3d line1Start, out Point3d line1End, out Point3d line2Start, out Point3d line2End)
        {
            Vector3d direction = endPoint - startPoint;

            if (direction.Length < 1e-10)
            {
                line1Start = line1End = line2Start = line2End = startPoint;
                return;
            }

            direction = direction.GetNormal();
            Vector3d perpendicular = new Vector3d(-direction.Y, direction.X, 0).GetNormal();

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

        public static List<ObjectId> CreateWallLines(
            Transaction tr,
            Database db,
            BlockTableRecord owner,
            ObjectId layerId,
            Point3d line1Start,
            Point3d line1End,
            Point3d line2Start,
            Point3d line2End)
        {
            List<ObjectId> lineIds = new List<ObjectId>();
            string segmentId = Guid.NewGuid().ToString("N");
            Point3d startPoint = MidPoint(line1Start, line2Start);
            Point3d endPoint = MidPoint(line1End, line2End);

            Line line1 = new Line(line1Start, line1End) { LayerId = layerId };
            owner.AppendEntity(line1);
            tr.AddNewlyCreatedDBObject(line1, true);
            lineIds.Add(line1.ObjectId);
            TagWallSegment(
                tr, db, line1, SideA, segmentId, startPoint, endPoint,
                line1Start, line1End, line2Start, line2End);

            Line line2 = new Line(line2Start, line2End) { LayerId = layerId };
            owner.AppendEntity(line2);
            tr.AddNewlyCreatedDBObject(line2, true);
            lineIds.Add(line2.ObjectId);
            TagWallSegment(
                tr, db, line2, SideB, segmentId, startPoint, endPoint,
                line1Start, line1End, line2Start, line2End);

            return lineIds;
        }

        public static ObjectId CreateWallCap(
            Transaction tr, Database db, BlockTableRecord owner, ObjectId layerId, Point3d startPoint, Point3d endPoint)
        {
            Line cap = new Line(startPoint, endPoint) { LayerId = layerId };
            owner.AppendEntity(cap);
            tr.AddNewlyCreatedDBObject(cap, true);
            TagAsCap(tr, db, cap);
            return cap.ObjectId;
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

                layer.IsOff = false;
                layer.IsFrozen = false;
                layer.IsLocked = false;

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
            WallSegmentData newWall = CreateWallSegmentData("__NEW_WALL__", line1Start, line1End, line2Start, line2End);

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
                    }

                    foreach (IGrouping<string, WallLineCandidate> group in tagged.GroupBy(x => x.Wall.SegmentId))
                    {
                        List<WallSegmentData> overlappingPieces = group
                            .Select(candidate => candidate.Wall)
                            .Where(wall => WallFootprintsOverlap(newWall, wall))
                            .ToList();
                        if (overlappingPieces.Count == 0) continue;

                        intersections.AddRange(group.Select(x => new IntersectionInfo { ExistingLineId = x.LineId }));
                    }

                    tr.Commit();
                }
                catch (Exception ex) { tr.Abort(); throw new Exception($"Error finding intersections: {ex.Message}", ex); }
            }

            return intersections.GroupBy(x => x.ExistingLineId).Select(x => x.First()).ToList();
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
                    BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    List<Line> newLines = newWallLineIds.Select(id => tr.GetObject(id, OpenMode.ForWrite) as Line).OfType<Line>().Where(l => !l.IsErased).ToList();
                    List<Line> existingLines = intersections.Select(i => tr.GetObject(i.ExistingLineId, OpenMode.ForWrite) as Line).OfType<Line>().Where(l => !l.IsErased && !IsWallCap(l)).GroupBy(l => l.ObjectId).Select(g => g.First()).ToList();

                    if (newLines.Count < 2 || !TryGetWallSegmentData(newLines[0], out WallSegmentData newWall))
                    {
                        tr.Commit(); return;
                    }

                    List<WallSegmentData> wallData = new List<WallSegmentData> { newWall };
                    foreach (Line line in existingLines)
                    {
                        if (TryGetWallSegmentData(line, out WallSegmentData data))
                        {
                            if (!wallData.Any(w => w.SegmentId == data.SegmentId)) wallData.Add(data);
                        }
                    }

                    List<Line> allLines = existingLines.Concat(newLines).GroupBy(l => l.ObjectId).Select(g => g.First()).ToList();
                    Dictionary<ObjectId, string> ownerMap = new Dictionary<ObjectId, string>();
                    foreach (Line line in allLines)
                    {
                        ownerMap[line.ObjectId] = TryGetWallSegmentData(line, out WallSegmentData data) ? data.SegmentId : $"LEGACY_{line.ObjectId}";
                    }

                    // Tiền xử lý (Pre-process) nối tường
                    for (int i = 0; i < wallData.Count; i++)
                    {
                        for (int j = i + 1; j < wallData.Count; j++)
                        {
                            WallSegmentData wallA = wallData[i];
                            WallSegmentData wallB = wallData[j];
                            if (!WallFootprintsOverlap(wallA, wallB)) continue;

                            ResolveJoint(wallA, wallB, allLines);
                        }
                    }

                    // Cắt mảng CSG
                    Dictionary<ObjectId, List<Point3d>> lineCuts = allLines.ToDictionary(line => line.ObjectId, line => new List<Point3d>());

                    for (int i = 0; i < allLines.Count; i++)
                    {
                        for (int j = i + 1; j < allLines.Count; j++)
                        {
                            Line first = allLines[i];
                            Line second = allLines[j];
                            if (ownerMap[first.ObjectId] == ownerMap[second.ObjectId]) continue;

                            if (TryGetFiniteIntersection(first.StartPoint, first.EndPoint, second.StartPoint, second.EndPoint, out Point3d intersection))
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

                    List<(Point3d Start, Point3d End)> createdSegments = new List<(Point3d Start, Point3d End)>();

                    foreach (Line line in allLines)
                    {
                        List<Point3d> cuts = lineCuts[line.ObjectId];
                        cuts.Add(line.StartPoint);
                        cuts.Add(line.EndPoint);

                        List<Point3d> uniqueCuts = cuts
                            .OrderBy(p => p.DistanceTo(line.StartPoint))
                            .Aggregate(new List<Point3d>(), (result, point) =>
                            {
                                if (result.Count == 0 || result.Last().DistanceTo(point) > Tolerance) result.Add(point);
                                return result;
                            });

                        for (int i = 0; i < uniqueCuts.Count - 1; i++)
                        {
                            Point3d first = uniqueCuts[i];
                            Point3d second = uniqueCuts[i + 1];
                            if (first.DistanceTo(second) <= Tolerance) continue;

                            Point3d midpoint = MidPoint(first, second);
                            string ownerId = ownerMap[line.ObjectId];

                            if (IsInsideAnotherWall(midpoint, ownerId, wallData)) continue;
                            if (createdSegments.Any(segment => SameUndirectedSegment(first, second, segment.Start, segment.End))) continue;

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

        private static void ResolveJoint(WallSegmentData wallA, WallSegmentData wallB, List<Line> allLines)
        {
            if (!GetTrueIntersection(wallA.CenterStart, wallA.CenterEnd, wallB.CenterStart, wallB.CenterEnd, out Point3d centerInt)) return;

            double lenA = wallA.CenterStart.DistanceTo(wallA.CenterEnd);
            double lenB = wallB.CenterStart.DistanceTo(wallB.CenterEnd);
            if (lenA < Tolerance || lenB < Tolerance) return;

            double tA = GetParameterOnSegment(centerInt, wallA.CenterStart, wallA.CenterEnd);
            double tB = GetParameterOnSegment(centerInt, wallB.CenterStart, wallB.CenterEnd);

            // FIX: Đánh giá L-joint hay T-joint dựa trên khoảng cách THỰC TẾ thay vì dùng hệ số nhân quá lớn.
            // Điều này giải quyết hoàn toàn lỗi bắt điểm sát 2 đầu của tường bị nhận nhầm thành góc L
            double endTolA = wallA.Width * 0.5 + Tolerance;
            double endTolB = wallB.Width * 0.5 + Tolerance;

            double distA = Math.Min(centerInt.DistanceTo(wallA.CenterStart), centerInt.DistanceTo(wallA.CenterEnd));
            double distB = Math.Min(centerInt.DistanceTo(wallB.CenterStart), centerInt.DistanceTo(wallB.CenterEnd));

            bool aIsEnd = distA <= endTolA || tA <= 0 || tA >= 1;
            bool bIsEnd = distB <= endTolB || tB <= 0 || tB >= 1;

            if (aIsEnd && bIsEnd)
            {
                bool aAtStart = Math.Abs(tA * lenA) < Math.Abs((1 - tA) * lenA);
                bool bAtStart = Math.Abs(tB * lenB) < Math.Abs((1 - tB) * lenB);
                MiterLJoint(wallA, wallB, aAtStart, bAtStart, allLines);
            }
            else if (aIsEnd && !bIsEnd)
            {
                bool aAtStart = Math.Abs(tA * lenA) < Math.Abs((1 - tA) * lenA);
                TrimTJointStem(wallA, wallB, aAtStart, allLines);
            }
            else if (!aIsEnd && bIsEnd)
            {
                bool bAtStart = Math.Abs(tB * lenB) < Math.Abs((1 - tB) * lenB);
                TrimTJointStem(wallB, wallA, bAtStart, allLines);
            }
        }

        private static void MiterLJoint(WallSegmentData wallA, WallSegmentData wallB, bool aAtStart, bool bAtStart, List<Line> allLines)
        {
            bool preservePhysicalSide = aAtStart != bAtStart;
            string[] sides = { SideA, SideB };

            Line lineAA = GetEndmostLine(wallA, SideA, aAtStart, allLines);
            Line lineAB = GetEndmostLine(wallA, SideB, aAtStart, allLines);
            Line lineBA = GetEndmostLine(wallB, SideA, bAtStart, allLines);
            Line lineBB = GetEndmostLine(wallB, SideB, bAtStart, allLines);

            if (lineAA == null || lineAB == null || lineBA == null || lineBB == null) return;

            foreach (string sideA in sides)
            {
                bool aIsLeft = IsBoundaryOnLeft(wallA, sideA);
                bool bMustBeLeft = preservePhysicalSide ? aIsLeft : !aIsLeft;
                string sideB = sides.First(s => IsBoundaryOnLeft(wallB, s) == bMustBeLeft);

                wallA.GetBoundary(sideA, out Point3d aStart, out Point3d aEnd);
                wallB.GetBoundary(sideB, out Point3d bStart, out Point3d bEnd);

                if (GetTrueIntersection(aStart, aEnd, bStart, bEnd, out Point3d intersection))
                {
                    Line la = sideA == SideA ? lineAA : lineAB;
                    Line lb = sideB == SideA ? lineBA : lineBB;

                    if (aAtStart) la.StartPoint = intersection; else la.EndPoint = intersection;
                    if (bAtStart) lb.StartPoint = intersection; else lb.EndPoint = intersection;

                    if (sideA == SideA) { if (aAtStart) wallA.SideAStart = intersection; else wallA.SideAEnd = intersection; }
                    else { if (aAtStart) wallA.SideBStart = intersection; else wallA.SideBEnd = intersection; }

                    if (sideB == SideA) { if (bAtStart) wallB.SideAStart = intersection; else wallB.SideAEnd = intersection; }
                    else { if (bAtStart) wallB.SideBStart = intersection; else wallB.SideBEnd = intersection; }
                }
            }

            if (GetTrueIntersection(wallA.CenterStart, wallA.CenterEnd, wallB.CenterStart, wallB.CenterEnd, out Point3d centerInt))
            {
                if (aAtStart) wallA.CenterStart = centerInt; else wallA.CenterEnd = centerInt;
                if (bAtStart) wallB.CenterStart = centerInt; else wallB.CenterEnd = centerInt;
            }

            UpdateWallDataAndLines(wallA, allLines);
            UpdateWallDataAndLines(wallB, allLines);
        }

        private static void TrimTJointStem(WallSegmentData stem, WallSegmentData crossbar, bool stemAtStart, List<Line> allLines)
        {
            Line stemA = GetEndmostLine(stem, SideA, stemAtStart, allLines);
            Line stemB = GetEndmostLine(stem, SideB, stemAtStart, allLines);
            if (stemA == null || stemB == null) return;

            string[] sides = { SideA, SideB };
            foreach (string sSide in sides)
            {
                stem.GetBoundary(sSide, out Point3d sStart, out Point3d sEnd);
                Point3d stemSource = stemAtStart ? sEnd : sStart;
                Point3d stemEndToTrim = stemAtStart ? sStart : sEnd;

                Point3d bestInt = Point3d.Origin;
                double minDistFromSource = double.MaxValue;
                string bestCrossSide = null;

                foreach (string cSide in sides)
                {
                    crossbar.GetBoundary(cSide, out Point3d cStart, out Point3d cEnd);
                    if (GetTrueIntersection(sStart, sEnd, cStart, cEnd, out Point3d intersection))
                    {
                        double distFromSource = stemSource.DistanceTo(intersection);
                        if (distFromSource < minDistFromSource)
                        {
                            minDistFromSource = distFromSource;
                            bestInt = intersection;
                            bestCrossSide = cSide;
                        }
                    }
                }

                if (bestCrossSide != null && stemEndToTrim.DistanceTo(bestInt) < Math.Max(stem.Width, crossbar.Width) * 10.0)
                {
                    Line ls = sSide == SideA ? stemA : stemB;
                    if (stemAtStart) ls.StartPoint = bestInt; else ls.EndPoint = bestInt;

                    if (sSide == SideA) { if (stemAtStart) stem.SideAStart = bestInt; else stem.SideAEnd = bestInt; }
                    else { if (stemAtStart) stem.SideBStart = bestInt; else stem.SideBEnd = bestInt; }
                }
            }

            if (GetTrueIntersection(stem.CenterStart, stem.CenterEnd, crossbar.CenterStart, crossbar.CenterEnd, out Point3d centerInt))
            {
                if (stemAtStart) stem.CenterStart = centerInt; else stem.CenterEnd = centerInt;
            }

            UpdateWallDataAndLines(stem, allLines);
        }

        private static Line GetEndmostLine(WallSegmentData wall, string side, bool atStart, List<Line> allLines)
        {
            var lines = allLines.Where(l => GetWallSegmentId(l) == wall.SegmentId && GetWallSideMarker(l) == side).ToList();
            if (lines.Count == 0) return null;

            Point3d targetPt = atStart ? (side == SideA ? wall.SideAStart : wall.SideBStart) : (side == SideA ? wall.SideAEnd : wall.SideBEnd);
            return lines.OrderBy(l => Math.Min(l.StartPoint.DistanceTo(targetPt), l.EndPoint.DistanceTo(targetPt))).FirstOrDefault();
        }

        private static void UpdateWallDataAndLines(WallSegmentData wall, List<Line> allLines)
        {
            foreach (Line l in allLines.Where(x => GetWallSegmentId(x) == wall.SegmentId))
                UpdateLineXData(l, wall);
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
            Line first, Line second, Dictionary<ObjectId, List<Point3d>> lineCuts)
        {
            if (Math.Abs(SignedDistance2d(first.StartPoint, first.EndPoint, second.StartPoint)) > Tolerance ||
                Math.Abs(SignedDistance2d(first.StartPoint, first.EndPoint, second.EndPoint)) > Tolerance)
            {
                return;
            }

            Point3d[] endpoints = { first.StartPoint, first.EndPoint, second.StartPoint, second.EndPoint };

            foreach (Point3d endpoint in endpoints)
            {
                if (!IsPointOnSegment2d(endpoint, first.StartPoint, first.EndPoint) ||
                    !IsPointOnSegment2d(endpoint, second.StartPoint, second.EndPoint)) continue;

                lineCuts[first.ObjectId].Add(endpoint);
                lineCuts[second.ObjectId].Add(endpoint);
            }
        }

        private static bool SameUndirectedSegment(Point3d firstStart, Point3d firstEnd, Point3d secondStart, Point3d secondEnd)
        {
            return (firstStart.DistanceTo(secondStart) <= Tolerance && firstEnd.DistanceTo(secondEnd) <= Tolerance) ||
                   (firstStart.DistanceTo(secondEnd) <= Tolerance && firstEnd.DistanceTo(secondStart) <= Tolerance);
        }

        private static bool IsInsideAnotherWall(Point3d point, string ownerSegmentId, IEnumerable<WallSegmentData> walls)
        {
            foreach (WallSegmentData wall in walls)
            {
                if (wall.SegmentId == ownerSegmentId) continue;
                if (IsPointInPolygon(point, wall.Outline, includeBoundary: true)) return true;
            }
            return false;
        }
        #endregion

        #region 3. HÀM TOÁN HỌC HỖ TRỢ (MATH UTILS)
        // HÀM TỰ ĐỘNG HÍT ĐIỂM (AUTO-SNAP) CHUẨN XÁC VÀO TÂM HOẶC GÓC TƯỜNG
        public static Point3d SnapToWallCenter(Database db, Point3d pt, double thickness, string wallLayerName)
        {
            double searchRadius = thickness * 1.5;
            double snapTol = thickness * 0.5; // Khoảng cách nhỏ để bắt dính vào mút (ưu tiên nối góc)

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    BlockTable blockTable = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                    BlockTableRecord modelSpace = tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead) as BlockTableRecord;
                    LayerTable layerTable = tr.GetObject(db.LayerTableId, OpenMode.ForRead) as LayerTable;

                    if (!layerTable.Has(wallLayerName)) return pt;
                    ObjectId targetLayerId = layerTable[wallLayerName];

                    List<WallSegmentData> walls = new List<WallSegmentData>();

                    foreach (ObjectId objId in modelSpace)
                    {
                        DBObject obj = tr.GetObject(objId, OpenMode.ForRead);
                        if (obj is Line line && line.LayerId == targetLayerId && !IsWallCap(line))
                        {
                            if (TryGetWallSegmentData(line, out WallSegmentData data))
                            {
                                if (!walls.Any(w => w.SegmentId == data.SegmentId))
                                    walls.Add(data);
                            }
                        }
                    }

                    Point3d bestSnap = pt;
                    double bestDist = searchRadius;
                    bool snapped = false;

                    foreach (var wall in walls)
                    {
                        Point3d proj = GetProjectedPointOnSegment(pt, wall.CenterStart, wall.CenterEnd);
                        double distToProj = pt.DistanceTo(proj);

                        if (distToProj <= searchRadius)
                        {
                            // 1. Kiểm tra hình chiếu có gần sát 2 mút để nối miter (L-joint) không?
                            if (proj.DistanceTo(wall.CenterStart) <= snapTol)
                            {
                                double d = pt.DistanceTo(wall.CenterStart);
                                if (d < bestDist)
                                {
                                    bestDist = d;
                                    bestSnap = wall.CenterStart;
                                    snapped = true;
                                }
                            }
                            else if (proj.DistanceTo(wall.CenterEnd) <= snapTol)
                            {
                                double d = pt.DistanceTo(wall.CenterEnd);
                                if (d < bestDist)
                                {
                                    bestDist = d;
                                    bestSnap = wall.CenterEnd;
                                    snapped = true;
                                }
                            }
                            else
                            {
                                // 2. Nếu không nằm ở mút, hít thẳng vào đường tâm ở giữa để tạo chữ T
                                if (distToProj < bestDist)
                                {
                                    bestDist = distToProj;
                                    bestSnap = proj;
                                    snapped = true;
                                }
                            }
                        }
                    }
                    tr.Commit();
                    return snapped ? bestSnap : pt;
                }
                catch
                {
                    return pt;
                }
            }
        }

        public static Point3d GetProjectedPointOnSegment(Point3d pt, Point3d start, Point3d end)
        {
            Vector3d vec = end - start;
            double lenSq = vec.DotProduct(vec);
            if (lenSq < 1e-10) return start;

            double t = (pt - start).DotProduct(vec) / lenSq;
            t = Math.Max(0.0, Math.Min(1.0, t));
            return start + vec * t;
        }

        private static double GetParameterOnSegment(Point3d pt, Point3d start, Point3d end)
        {
            Vector3d vec = end - start;
            double lenSq = vec.DotProduct(vec);
            if (lenSq < 1e-9) return 0;
            return (pt - start).DotProduct(vec) / lenSq;
        }

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

                double xAtPointY = (first.X - second.X) * (point.Y - second.Y) / (first.Y - second.Y) + second.X;
                if (point.X < xAtPointY) inside = !inside;
            }
            return inside;
        }

        private static bool SegmentsIntersectInclusive(Point3d p1, Point3d p2, Point3d p3, Point3d p4)
        {
            double c1 = SignedDistance2d(p1, p2, p3);
            double c2 = SignedDistance2d(p1, p2, p4);
            double c3 = SignedDistance2d(p3, p4, p1);
            double c4 = SignedDistance2d(p3, p4, p2);

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
            if (Math.Abs(SignedDistance2d(start, end, point)) > Tolerance) return false;

            return point.X >= Math.Min(start.X, end.X) - Tolerance &&
                   point.X <= Math.Max(start.X, end.X) + Tolerance &&
                   point.Y >= Math.Min(start.Y, end.Y) - Tolerance &&
                   point.Y <= Math.Max(start.Y, end.Y) + Tolerance;
        }

        private static double SignedDistance2d(Point3d start, Point3d end, Point3d point)
        {
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);

            if (length < 1e-9) return Math.Sqrt(Math.Pow(point.X - start.X, 2) + Math.Pow(point.Y - start.Y, 2));

            return (dx * (point.Y - start.Y) - dy * (point.X - start.X)) / length;
        }

        private static bool TryGetFiniteIntersection(Point3d p1, Point3d p2, Point3d p3, Point3d p4, out Point3d intersection)
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
            if (Math.Abs(denom) < 1e-9) return false;

            double ua = ((x4 - x3) * (y1 - y3) - (y4 - y3) * (x1 - x3)) / denom;
            intersection = new Point3d(x1 + ua * (x2 - x1), y1 + ua * (y2 - y1), p1.Z);
            return true;
        }

        private static bool IsPointOnSegment(Point3d pt, Point3d start, Point3d end, double tolerance)
        {
            double lineLenSq = (end.X - start.X) * (end.X - start.X) + (end.Y - start.Y) * (end.Y - start.Y);
            if (lineLenSq < 1e-9) return pt.DistanceTo(start) <= tolerance;

            double lineLen = Math.Sqrt(lineLenSq);
            double cross = Math.Abs((end.X - start.X) * (start.Y - pt.Y) - (start.X - pt.X) * (end.Y - start.Y));
            double perpDist = cross / lineLen;

            if (perpDist > tolerance) return false;

            double dot = (pt.X - start.X) * (end.X - start.X) + (pt.Y - start.Y) * (end.Y - start.Y);
            if (dot < -tolerance || dot > lineLenSq + tolerance) return false;

            return true;
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
                    List<WallSegmentData> wallData = new List<WallSegmentData>();

                    foreach (ObjectId objId in modelSpace)
                    {
                        DBObject obj = tr.GetObject(objId, OpenMode.ForRead);
                        if (!(obj is Line line) || line.LayerId != targetLayerId) continue;
                        if (line.StartPoint.DistanceTo(line.EndPoint) <= Tolerance) continue;

                        if (IsWallCap(line))
                        {
                            caps.Add(line); continue;
                        }

                        wallLines.Add(line);
                        if (TryGetWallSegmentData(line, out WallSegmentData data))
                            wallData.Add(data);
                    }

                    WallSegmentData selectedWall = null;
                    bool selectedAtStart = false;
                    double bestDistance = double.MaxValue;

                    foreach (WallSegmentData data in wallData)
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

                    if (selectedWall == null || IsEndpointConnectedToAnotherWall(selectedWall, selectedAtStart, wallData))
                    {
                        tr.Commit(); return;
                    }

                    selectedWall.GetEndpointFace(selectedAtStart, out Point3d expectedA, out Point3d expectedB);
                    Line sideALine = FindLineAtEndpoint(wallLines, selectedWall.SegmentId, SideA, expectedA);
                    Line sideBLine = FindLineAtEndpoint(wallLines, selectedWall.SegmentId, SideB, expectedB);

                    if (sideALine == null || sideBLine == null)
                    {
                        tr.Commit(); return;
                    }

                    Point3d actualA = sideALine.StartPoint.DistanceTo(expectedA) <= sideALine.EndPoint.DistanceTo(expectedA)
                        ? sideALine.StartPoint : sideALine.EndPoint;
                    Point3d actualB = sideBLine.StartPoint.DistanceTo(expectedB) <= sideBLine.EndPoint.DistanceTo(expectedB)
                        ? sideBLine.StartPoint : sideBLine.EndPoint;

                    double expectedWidth = expectedA.DistanceTo(expectedB);
                    double endpointTolerance = Math.Max(pickTolerance * 2.0, expectedWidth * 2.0 + Tolerance);
                    double actualWidth = actualA.DistanceTo(actualB);
                    double widthTolerance = Math.Max(Tolerance * 10.0, expectedWidth * 0.25);
                    if (actualA.DistanceTo(expectedA) > endpointTolerance ||
                        actualB.DistanceTo(expectedB) > endpointTolerance ||
                        Math.Abs(actualWidth - expectedWidth) > widthTolerance)
                    {
                        tr.Commit(); return;
                    }

                    bool alreadyCapped = caps.Any(cap =>
                        (cap.StartPoint.DistanceTo(actualA) <= Tolerance && cap.EndPoint.DistanceTo(actualB) <= Tolerance) ||
                        (cap.StartPoint.DistanceTo(actualB) <= Tolerance && cap.EndPoint.DistanceTo(actualA) <= Tolerance));

                    if (!alreadyCapped && actualWidth > Tolerance)
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

        private static Line FindLineAtEndpoint(IEnumerable<Line> lines, string segmentId, string side, Point3d expectedEndpoint)
        {
            return lines
                .Where(line => GetWallSegmentId(line) == segmentId && GetWallSideMarker(line) == side)
                .OrderBy(line => Math.Min(line.StartPoint.DistanceTo(expectedEndpoint), line.EndPoint.DistanceTo(expectedEndpoint)))
                .FirstOrDefault();
        }

        private static bool IsEndpointConnectedToAnotherWall(WallSegmentData owner, bool atStart, IEnumerable<WallSegmentData> walls)
        {
            owner.GetEndpointFace(atStart, out Point3d sideA, out Point3d sideB);
            foreach (WallSegmentData wall in walls)
            {
                if (wall.SegmentId == owner.SegmentId) continue;
                if (IsPointInPolygon(MidPoint(sideA, sideB), wall.Outline, includeBoundary: true)) return true;
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

                    EraseEntities(tr, toErase);
                    tr.Commit();
                }
                catch (Exception ex) { tr.Abort(); throw new Exception($"Error removing old cap: {ex.Message}", ex); }
            }
        }

        public static void EraseEntities(Database db, IEnumerable<ObjectId> entityIds)
        {
            using Transaction tr = db.TransactionManager.StartTransaction();
            try
            {
                EraseEntities(tr, entityIds);
                tr.Commit();
            }
            catch { tr.Abort(); throw; }
        }

        private static void EraseEntities(Transaction tr, IEnumerable<ObjectId> entityIds)
        {
            foreach (ObjectId id in entityIds.Where(id => !id.IsNull).Distinct())
            {
                DBObject obj = tr.GetObject(id, OpenMode.ForWrite, true);
                if (!obj.IsErased) obj.Erase(true);
            }
        }
        #endregion
    }

    public class IntersectionInfo
    {
        public ObjectId ExistingLineId { get; set; }
    }
}