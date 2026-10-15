using System;
using System.Collections.Generic;
using System.Linq;
using Teigha.Colors;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.Model;

namespace Tools.VinaCad.Helper.Helper
{
    public static class StuccoHelper
    {
        private const double MinimumConnectionTolerance = 0.0001;
        private const double ParallelDotTolerance = 0.002;
        private const double MaximumWallWidthFactor = 20.0;
        private const double MaximumOpenWallWidthFactor = 50.0;
        private const double MinimumWallFaceOverlapRatio = 0.25;
        private const double CapPerpendicularDotTolerance = 0.2;
        private const double MiterLimitFactor = 10.0;
        private const int MaximumMissedRays = 14;
        private const int MaximumRoomSeeds = 48;

        private sealed class StuccoContext
        {
            public StuccoContext(Database database, Transaction transaction, ObjectId layerId, BlockTableRecord space)
            {
                Database = database;
                Transaction = transaction;
                LayerId = layerId;
                Space = space;
            }

            public Database Database { get; }
            public Transaction Transaction { get; }
            public ObjectId LayerId { get; }
            public BlockTableRecord Space { get; }
            public List<ObjectId> CreatedIds { get; } = new List<ObjectId>();
        }

        private sealed class WallFaceRef
        {
            public WallFaceRef(ObjectId id, Point3d start, Point3d end, bool tagged, bool isCap, ObjectId layerId)
            {
                Id = id;
                Start = start;
                End = end;
                Tagged = tagged;
                IsCap = isCap;
                LayerId = layerId;
            }

            public ObjectId LayerId { get; }

            public ObjectId Id { get; }
            public Point3d Start { get; }
            public Point3d End { get; }
            public bool Tagged { get; }
            public bool IsCap { get; }
        }

        private sealed class FaceSpan
        {
            public FaceSpan(WallFaceRef face, Point3d hit, Point3d viewPoint)
            {
                Face = face;
                ViewPoint = viewPoint;
                Ids.Add(face.Id);
                S0 = S1 = ProjectPoint(hit);
                Weight = 1;
            }

            public WallFaceRef Face { get; }
            public Point3d ViewPoint { get; }
            public HashSet<ObjectId> Ids { get; } = new HashSet<ObjectId>();
            public double S0 { get; private set; }
            public double S1 { get; private set; }
            public int Weight { get; private set; }

            public void Extend(WallFaceRef face, Point3d hit)
            {
                Ids.Add(face.Id);
                double parameter = ProjectPoint(hit);
                S0 = Math.Min(S0, parameter);
                S1 = Math.Max(S1, parameter);
                Weight++;
            }

            public void Merge(FaceSpan other)
            {
                foreach (ObjectId id in other.Ids)
                    Ids.Add(id);

                Vector3d vector = other.Face.End - other.Face.Start;
                foreach (double s in new[] { other.S0, other.S1 })
                {
                    double parameter = ProjectPoint(other.Face.Start + vector * s);
                    S0 = Math.Min(S0, parameter);
                    S1 = Math.Max(S1, parameter);
                }
                Weight += other.Weight;
            }

            public void ResetWeight() => Weight = 0;
            public Point3d PointAt(double parameter) => Face.Start + (Face.End - Face.Start) * parameter;
            public double ProjectPoint(Point3d point)
            {
                Vector3d vector = Face.End - Face.Start;
                double lengthSquared = vector.DotProduct(vector);
                return lengthSquared <= 0.0 ? 0.0 : (point - Face.Start).DotProduct(vector) / lengthSquared;
            }
        }

        private readonly struct RaySample
        {
            public RaySample(double angle, WallFaceRef face, Point3d hit, double distance)
            {
                Angle = angle;
                Face = face;
                Hit = hit;
                Distance = distance;
            }

            public double Angle { get; }
            public WallFaceRef Face { get; }
            public Point3d Hit { get; }
            public double Distance { get; }
        }
        private sealed class EndpointSnaps
        {
            private readonly Point3d?[] _points;
            private readonly double[] _distances;

            public EndpointSnaps(int lineCount)
            {
                _points = new Point3d?[lineCount * 2];
                _distances = new double[lineCount * 2];
                for (int index = 0; index < _distances.Length; index++)
                    _distances[index] = double.PositiveInfinity;
            }

            public void Offer(Line line, int lineIndex, Point3d intersection, double maximumJoinDistance)
            {
                double startDistance = line.StartPoint.DistanceTo(intersection);
                double endDistance = line.EndPoint.DistanceTo(intersection);
                double distance = Math.Min(startDistance, endDistance);
                double allowance = GetEndpointJoinAllowance(line.StartPoint.DistanceTo(line.EndPoint), maximumJoinDistance);
                if (distance > allowance)
                    return;

                int slot = lineIndex * 2 + (startDistance <= endDistance ? 0 : 1);
                if (distance >= _distances[slot])
                    return;

                _distances[slot] = distance;
                _points[slot] = intersection;
            }

            public void Apply(List<Line> lines)
            {
                for (int index = 0; index < lines.Count; index++)
                {
                    if (_points[index * 2] is Point3d start)
                        lines[index].StartPoint = start;
                    if (_points[index * 2 + 1] is Point3d end)
                        lines[index].EndPoint = end;
                }
            }
        }

        private sealed class BestFace
        {
            private double _distance = double.PositiveInfinity;
            private double _overlap;

            public Line? Face { get; private set; }

            public void Offer(Line candidate, double distance, double overlap)
            {
                if (distance > _distance + MinimumConnectionTolerance ||
                    (Math.Abs(distance - _distance) <= MinimumConnectionTolerance && overlap <= _overlap))
                {
                    return;
                }

                Face = candidate;
                _distance = distance;
                _overlap = overlap;
            }
        }

        public static string EnsureLayer(Database database, string requestedLayerName, short colorIndex)
        {
            if (database == null)
                throw new ArgumentNullException(nameof(database));
            if (string.IsNullOrWhiteSpace(requestedLayerName))
                throw new ArgumentException("Tên layer FN không hợp lệ.", nameof(requestedLayerName));
            if (colorIndex < 1 || colorIndex > 255)
                throw new ArgumentOutOfRangeException(nameof(colorIndex), "Màu ACI phải nằm trong khoảng 1..255.");

            string layerName = requestedLayerName.Trim();
            using Transaction transaction = database.TransactionManager.StartTransaction();
            LayerTable layerTable = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);

            LayerTableRecord layer;
            if (layerTable.Has(layerName))
            {
                layer = (LayerTableRecord)transaction.GetObject(layerTable[layerName], OpenMode.ForWrite);
            }
            else
            {
                layerTable.UpgradeOpen();
                layer = new LayerTableRecord { Name = layerName };
                layerTable.Add(layer);
                transaction.AddNewlyCreatedDBObject(layer, true);
            }

            layer.Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex);
            string actualLayerName = layer.Name;
            transaction.Commit();
            return actualLayerName;
        }

        public static int CreateStuccoForRoomFaces(Database database, Point3d interiorPoint, double thickness, string targetLayerName)
        {
            ValidateArguments(database, thickness, targetLayerName);

            List<FaceSpan> spans;
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                ObjectId layerId = GetTargetLayerId(transaction, database, targetLayerName);
                spans = FindRoomFaces(CollectWallFaceRefs(transaction, database, layerId), interiorPoint, thickness);
            }

            if (spans.Count == 0)
                throw new InvalidOperationException("Không tìm thấy mặt tường quanh điểm đã chọn.");

            ObjectId[] sourceIds = spans.SelectMany(span => span.Ids).Distinct().ToArray();
            return RunStucco(database, sourceIds, thickness, targetLayerName,
                "Chưa có mặt tường.",
                "Không thể tạo vữa mặt trong.",
                context =>
                {
                    int created = 0;
                    foreach (FaceSpan span in spans)
                    {
                        // Chỉ offset phần mặt tường THẤY ĐƯỢC từ điểm trong phòng (không kéo cả line dài sang phòng khác)
                        Vector3d vector = span.Face.End - span.Face.Start;
                        using Line segment = new Line(span.Face.Start + vector * span.S0, span.Face.Start + vector * span.S1);
                        Point3d guide = GetGuidePointOnSideOf(segment, span.ViewPoint, thickness);
                        created += AppendOffsetOnSide(segment, guide, thickness, context);
                    }

                    return created;
                },
                isolate: true);
        }

        public static int CreateStuccoForExteriorFaces(Database database, ObjectId[] sourceIds, Point3d sidePoint, double thickness, string targetLayerName)
        {
            return RunStucco(database, sourceIds, thickness, targetLayerName,
                "Chưa chọn mặt tường ngoài.",
                "Các đường đã chọn không thể tạo vữa mặt ngoài.",
                context =>
                {
                    List<Line> allFaces = new List<Line>();
                    List<Line> transientFaces = new List<Line>();
                    try
                    {
                        foreach (ObjectId id in context.Space)
                        {
                            Curve? curve = GetCurve(context.Transaction, id);
                            if (curve == null || curve.IsErased || curve.LayerId == context.LayerId)
                                continue;

                            if (curve is Line line)
                            {
                                if (!DrawWallHelper.IsWallCap(line) &&
                                    line.StartPoint.DistanceTo(line.EndPoint) > MinimumConnectionTolerance)
                                    allFaces.Add(line);
                            }
                            else if (curve is Polyline polyline)
                            {
                                CollectStraightPolylineSegments(polyline, transientFaces);
                            }
                        }

                        allFaces.AddRange(transientFaces);

                        int created = 0;
                        foreach (ObjectId sourceId in sourceIds)
                        {
                            Curve? curve = GetCurve(context.Transaction, sourceId);
                            if (curve is Line face)
                            {
                                if (DrawWallHelper.IsWallCap(face))
                                    continue; // cap sẽ được bo tự động ở bước cleanup

                                Line? opposite = FindOppositeOpenWallFace(face, allFaces, thickness);
                                Point3d guide = opposite != null
                                    ? GetPointAwayFromOppositeFace(face, opposite, thickness)
                                    : GetGuidePointOnSideOf(face, sidePoint, thickness);
                                created += AppendOffsetOnSide(face, guide, thickness, context);
                            }
                            else if (curve != null)
                            {
                                // Arc / polyline / spline: offset về phía điểm chỉ dẫn như cũ
                                created += AppendOffsetOnSide(curve, sidePoint, thickness, context);
                            }
                        }

                        return created;
                    }
                    finally
                    {
                        foreach (Line face in transientFaces)
                            face.Dispose();
                    }
                },
                isolate: true);
        }
        private sealed class StuccoEdit
        {
            public StuccoEdit(Line line, bool selected)
            {
                Line = line;
                Selected = selected;
                OriginalStart = line.StartPoint;
                OriginalEnd = line.EndPoint;
                Start = OriginalStart;
                End = OriginalEnd;
                Direction = (OriginalEnd - OriginalStart).GetNormal();
                Shift = new Vector3d(0.0, 0.0, 0.0);
            }

            public Line Line { get; }
            public bool Selected { get; }
            public Point3d OriginalStart { get; }
            public Point3d OriginalEnd { get; }
            public Vector3d Direction { get; }
            public Point3d Start { get; set; }
            public Point3d End { get; set; }
            public Vector3d Shift { get; set; }
            public bool Moved { get; set; }
            public bool Dirty { get; set; }

            public Point3d MovedAnchor => Moved ? OriginalStart + Shift : OriginalStart;
        }
        public static int ChangeStuccoThickness(Database database, ObjectId[] stuccoIds, double referenceThickness, double newThickness, string targetLayerName)
        {
            ValidateArguments(database, newThickness, targetLayerName);
            if (stuccoIds == null || stuccoIds.Length == 0)
                throw new ArgumentException("Chưa chọn cạnh vữa.", nameof(stuccoIds));

            using Transaction transaction = database.TransactionManager.StartTransaction();
            ObjectId layerId = GetTargetLayerId(transaction, database, targetLayerName);
            BlockTableRecord space = (BlockTableRecord)transaction.GetObject(database.CurrentSpaceId, OpenMode.ForWrite);

            List<WallFaceRef> walls = FilterWallLayerFaces(CollectWallFaceRefs(transaction, database, layerId));
            if (walls.Count == 0)
                throw new InvalidOperationException("Không tìm thấy line tường trong bản vẽ.");

            // 1) Gom toàn bộ line vữa trên layer FN (đánh dấu các line được chọn)
            HashSet<ObjectId> selectedIds = new HashSet<ObjectId>(stuccoIds);
            List<StuccoEdit> edits = new List<StuccoEdit>();
            foreach (ObjectId id in space)
            {
                if (id.IsNull || !id.IsValid ||
                    transaction.GetObject(id, OpenMode.ForRead) is not Line line ||
                    line.IsErased || line.LayerId != layerId ||
                    line.StartPoint.DistanceTo(line.EndPoint) <= MinimumConnectionTolerance)
                {
                    continue;
                }

                edits.Add(new StuccoEdit(line, selectedIds.Contains(id)));
            }

            double largest = Math.Max(Math.Abs(referenceThickness), Math.Abs(newThickness));
            double maximumDistance = JoinLimit(largest, 5.0);
            int movedCount = 0;
            foreach (StuccoEdit edit in edits)
            {
                if (!edit.Selected ||
                    !TryFindStuccoBaseFace(edit, walls, maximumDistance, out Vector3d normal, out double currentDistance))
                {
                    continue;
                }

                Vector3d shift = normal * (newThickness - currentDistance);
                if (shift.Length <= MinimumConnectionTolerance)
                    continue; // đã đúng độ dày

                edit.Shift = shift;
                edit.Moved = true;
                movedCount++;
            }

            if (movedCount == 0)
                throw new InvalidOperationException("Không tìm thấy mặt tường gốc của các cạnh vữa đã chọn (hoặc độ dày đã đúng).");

            // 3) Nối lại hai đầu của từng cạnh đã dịch chuyển
            double connectionTolerance = FineTolerance(largest, 0.05);
            double miterLimit = MiterLimit(largest);
            List<(Point3d From, Point3d To)> connectors = new List<(Point3d From, Point3d To)>();
            foreach (StuccoEdit edit in edits)
            {
                if (!edit.Moved)
                    continue;

                AdjustMovedEndpoint(edit, 0, edits, connectionTolerance, miterLimit, connectors);
                AdjustMovedEndpoint(edit, 1, edits, connectionTolerance, miterLimit, connectors);
            }

            // 4) Ghi vào bản vẽ
            foreach (StuccoEdit edit in edits)
            {
                if (!edit.Dirty)
                    continue;

                if (!edit.Line.IsWriteEnabled)
                    edit.Line.UpgradeOpen();
                edit.Line.StartPoint = edit.Start;
                edit.Line.EndPoint = edit.End;
            }

            foreach ((Point3d from, Point3d to) in connectors)
            {
                Line connector = new Line(from, to)
                {
                    LayerId = layerId,
                    ColorIndex = 256
                };
                space.AppendEntity(connector);
                transaction.AddNewlyCreatedDBObject(connector, true);
            }

            transaction.Commit();
            return movedCount;
        }

        private static List<WallFaceRef> FilterWallLayerFaces(List<WallFaceRef> all)
        {
            HashSet<ObjectId> wallLayers = new HashSet<ObjectId>(all.Where(f => f.Tagged).Select(f => f.LayerId));
            return wallLayers.Count > 0
                ? all.FindAll(f => f.Tagged || wallLayers.Contains(f.LayerId))
                : all;
        }

        private static bool TryFindStuccoBaseFace(StuccoEdit edit, List<WallFaceRef> walls, double maximumDistance, out Vector3d normal, out double distance)
        {
            normal = new Vector3d(0.0, 0.0, 0.0);
            distance = 0.0;
            Vector3d vector = edit.OriginalEnd - edit.OriginalStart;
            double length = vector.Length;
            Point3d midpoint = edit.OriginalStart + vector * 0.5;
            double best = double.PositiveInfinity;

            foreach (WallFaceRef wall in walls)
            {
                if (wall.IsCap)
                    continue;

                Vector3d wallVector = wall.End - wall.Start;
                double wallLength = wallVector.Length;
                if (wallLength <= MinimumConnectionTolerance)
                    continue;

                Vector3d wallDirection = wallVector.GetNormal();
                if (Math.Abs(edit.Direction.DotProduct(wallDirection)) < 1.0 - ParallelDotTolerance)
                    continue;

                Vector3d wallNormal = new Vector3d(-wallDirection.Y, wallDirection.X, 0.0);
                double signed = (midpoint - wall.Start).DotProduct(wallNormal);
                double absolute = Math.Abs(signed);
                if (absolute <= MinimumConnectionTolerance || absolute > maximumDistance || absolute >= best)
                    continue;

                double first = (edit.OriginalStart - wall.Start).DotProduct(wallDirection);
                double second = (edit.OriginalEnd - wall.Start).DotProduct(wallDirection);
                double overlap = Math.Min(wallLength, Math.Max(first, second)) - Math.Max(0.0, Math.Min(first, second));
                double minimumOverlap = Math.Max(MinimumConnectionTolerance * 100.0, Math.Min(length, wallLength) * MinimumWallFaceOverlapRatio);
                if (overlap < minimumOverlap)
                    continue;

                best = absolute;
                distance = absolute;
                normal = signed >= 0.0 ? wallNormal : -wallNormal;
            }

            return !double.IsPositiveInfinity(best);
        }

        private static void AdjustMovedEndpoint(StuccoEdit moved, int endIndex, List<StuccoEdit> edits, double connectionTolerance, double miterLimit, List<(Point3d From, Point3d To)> connectors)
        {
            Point3d original = endIndex == 0 ? moved.OriginalStart : moved.OriginalEnd;
            Point3d translated = original + moved.Shift;
            Point3d result = translated;

            StuccoEdit? corner = null;
            Point3d cornerTarget = translated;
            double cornerDistance = double.PositiveInfinity;
            StuccoEdit? straight = null;

            foreach (StuccoEdit other in edits)
            {
                if (ReferenceEquals(other, moved))
                    continue;

                double distance = DistanceToSegment(original, other.OriginalStart, other.OriginalEnd);
                if (distance > connectionTolerance)
                    continue;

                if (Math.Abs(moved.Direction.DotProduct(other.Direction)) >= 1.0 - ParallelDotTolerance)
                {
                    straight ??= other; // tiếp nối thẳng hàng
                    continue;
                }

                if (distance >= cornerDistance ||
                    !TryIntersectInfiniteLines(moved.OriginalStart + moved.Shift, moved.Direction, other.MovedAnchor, other.Direction, out Point3d target) ||
                    target.DistanceTo(translated) > miterLimit)
                {
                    continue;
                }

                corner = other;
                cornerTarget = target;
                cornerDistance = distance;
            }

            if (corner != null)
            {
                result = cornerTarget;
                if (!corner.Moved)
                {
                    // Góc nối đầu-đầu: kéo luôn đầu mút của cạnh kề về giao điểm mới (ngã ba thì giữ nguyên)
                    double toStart = original.DistanceTo(corner.OriginalStart);
                    double toEnd = original.DistanceTo(corner.OriginalEnd);
                    if (Math.Min(toStart, toEnd) <= connectionTolerance)
                    {
                        if (toStart <= toEnd)
                            corner.Start = cornerTarget;
                        else
                            corner.End = cornerTarget;
                        corner.Dirty = true;
                    }
                }
            }
            else if (straight != null)
            {
                // Thẳng hàng nhưng lệch bậc độ dày -> thêm đoạn nối vuông góc
                Point3d otherPoint = original + straight.Shift;
                bool differentShift = (moved.Shift - straight.Shift).Length > MinimumConnectionTolerance;
                bool firstOfPair = !straight.Moved || edits.IndexOf(moved) < edits.IndexOf(straight);
                if (differentShift && firstOfPair)
                    connectors.Add((translated, otherPoint));
            }

            if (endIndex == 0)
                moved.Start = result;
            else
                moved.End = result;
            moved.Dirty = true;
        }

        private static bool TryIntersectInfiniteLines(Point3d firstPoint, Vector3d firstDirection, Point3d secondPoint, Vector3d secondDirection, out Point3d intersection)
        {
            intersection = firstPoint;
            double denominator = Cross2d(firstDirection.X, firstDirection.Y, secondDirection.X, secondDirection.Y);
            if (Math.Abs(denominator) <= 1e-6)
                return false;

            double parameter = Cross2d(secondPoint.X - firstPoint.X, secondPoint.Y - firstPoint.Y, secondDirection.X, secondDirection.Y) / denominator;
            intersection = new Point3d(
                firstPoint.X + firstDirection.X * parameter,
                firstPoint.Y + firstDirection.Y * parameter,
                firstPoint.Z);
            return true;
        }

        private static double DistanceToSegment(Point3d point, Point3d start, Point3d end)
        {
            Vector3d vector = end - start;
            double lengthSquared = vector.DotProduct(vector);
            if (lengthSquared <= MinimumConnectionTolerance * MinimumConnectionTolerance)
                return point.DistanceTo(start);

            double parameter = Math.Max(0.0, Math.Min(1.0, (point - start).DotProduct(vector) / lengthSquared));
            return point.DistanceTo(start + vector * parameter);
        }

        public static int CreateStuccoForBoundary(Database database, ObjectId[] boundaryIds, Point3d interiorPoint, double thickness, string targetLayerName)
        {
            return RunStucco(database, boundaryIds, thickness, targetLayerName,
                "Chưa có đường biên phòng.",
                "Không thể tạo vữa mặt trong.",
                context =>
                {
                    int created = 0;
                    foreach (ObjectId boundaryId in boundaryIds)
                    {
                        Curve? curve = GetCurve(context.Transaction, boundaryId);
                        if (curve == null)
                            continue;

                        created += curve is Polyline polyline && CanOffsetBySegments(polyline)
                            ? AppendPolylineSegmentOffsets(polyline, interiorPoint, thickness, context)
                            : AppendOffsetOnSide(curve, interiorPoint, thickness, context);
                    }

                    return created;
                });
        }

        public static int CreateStuccoForSelection(Database database, ObjectId[] sourceIds, Point3d sidePoint, double thickness, string targetLayerName)
        {
            return RunStucco(database, sourceIds, thickness, targetLayerName,
                "Chưa chọn đường bao mặt ngoài.",
                "Các đường đã chọn không thể tạo vữa mặt ngoài.",
                context =>
                {
                    List<StuccoModel.LineSegment> lineSegments = new List<StuccoModel.LineSegment>();
                    int created = 0;

                    foreach (ObjectId sourceId in sourceIds)
                    {
                        Curve? curve = GetCurve(context.Transaction, sourceId);
                        if (curve is Line line)
                            lineSegments.Add(new StuccoModel.LineSegment(line.StartPoint, line.EndPoint));
                        else if (curve != null)
                            created += AppendOffsetOnSide(curve, sidePoint, thickness, context);
                    }

                    List<Polyline> linePaths = BuildSelectedLinePaths(lineSegments, FineTolerance(thickness, 0.01));
                    try
                    {
                        foreach (Polyline path in linePaths)
                            created += AppendOffsetOnSide(path, sidePoint, thickness, context);
                    }
                    finally
                    {
                        foreach (Polyline path in linePaths)
                            path.Dispose();
                    }

                    return created;
                });
        }

        public static int CreateStuccoForOpenWalls(Database database, ObjectId[] sourceIds, double thickness, string targetLayerName)
        {
            return RunStucco(database, sourceIds, thickness, targetLayerName,
                "Chưa quét chọn tường hở.",
                "Không tìm thấy cặp mặt tường hở hợp lệ trong vùng chọn.",
                context =>
                {
                    List<Line> wallFaces = new List<Line>();
                    List<Line> wallCaps = new List<Line>();
                    List<Line> transientFaces = new List<Line>();
                    List<StuccoModel.WallStrip> wallStrips = new List<StuccoModel.WallStrip>();
                    int created = 0;

                    try
                    {
                        foreach (ObjectId sourceId in sourceIds)
                        {
                            Curve? curve = GetCurve(context.Transaction, sourceId);
                            if (curve == null || curve.LayerId == context.LayerId)
                                continue;

                            if (curve is Line line)
                            {
                                if (DrawWallHelper.IsWallCap(line))
                                    wallCaps.Add(line);
                                else if (line.StartPoint.DistanceTo(line.EndPoint) > MinimumConnectionTolerance)
                                    wallFaces.Add(line);
                            }
                            else if (curve is Polyline polyline)
                            {
                                CollectStraightPolylineSegments(polyline, transientFaces);
                            }
                        }

                        wallFaces.AddRange(transientFaces);
                        foreach (Line face in wallFaces)
                        {
                            Line? oppositeFace = FindOppositeOpenWallFace(face, wallFaces, thickness);
                            if (oppositeFace == null)
                                continue;

                            AddOpenWallStrip(face, oppositeFace, wallStrips, thickness);
                            Point3d guidePoint = GetPointAwayFromOppositeFace(face, oppositeFace, thickness);
                            created += AppendOffsetOnSide(face, guidePoint, thickness, context);
                        }

                        foreach (Line cap in wallCaps)
                        {
                            if (TryGetOpenWallCapGuidePoint(cap, wallFaces, thickness, out Point3d guidePoint))
                                created += AppendOffsetOnSide(cap, guidePoint, thickness, context);
                        }

                        if (created > 0)
                            TrimStuccoInsideWallIntersections(context, wallStrips, thickness);
                        return created;
                    }
                    finally
                    {
                        foreach (Line face in transientFaces)
                            face.Dispose();
                    }
                });
        }

        private static int RunStucco(Database database, ObjectId[] sourceIds, double thickness, string targetLayerName, string emptySourceMessage, string noResultMessage, Func<StuccoContext, int> build, bool isolate = false)
        {
            ValidateArguments(database, thickness, targetLayerName);
            if (sourceIds == null || sourceIds.Length == 0)
                throw new ArgumentException(emptySourceMessage, nameof(sourceIds));

            using Transaction transaction = database.TransactionManager.StartTransaction();
            StuccoContext context = new StuccoContext(
                database,
                transaction,
                GetTargetLayerId(transaction, database, targetLayerName),
                (BlockTableRecord)transaction.GetObject(database.CurrentSpaceId, OpenMode.ForWrite));

            int createdCount = build(context);
            if (createdCount == 0)
                throw new InvalidOperationException(noResultMessage);

            CleanupStuccoGeometry(context, new HashSet<ObjectId>(sourceIds), thickness, isolate);
            transaction.Commit();
            return createdCount;
        }

        private static Curve? GetCurve(Transaction transaction, ObjectId id)
        {
            if (id.IsNull || !id.IsValid)
                return null;
            return transaction.GetObject(id, OpenMode.ForRead) as Curve;
        }


        private static List<WallFaceRef> CollectWallFaceRefs(Transaction transaction, Database database, ObjectId stuccoLayerId)
        {
            List<WallFaceRef> result = new List<WallFaceRef>();
            BlockTableRecord space = (BlockTableRecord)transaction.GetObject(database.CurrentSpaceId, OpenMode.ForRead);
            foreach (ObjectId id in space)
            {
                if (transaction.GetObject(id, OpenMode.ForRead) is not Curve curve || curve.IsErased || curve.LayerId == stuccoLayerId)
                    continue;

                if (curve is Line line)
                {
                    if (line.StartPoint.DistanceTo(line.EndPoint) <= MinimumConnectionTolerance)
                        continue;

                    bool isCap = DrawWallHelper.IsWallCap(line);
                    result.Add(new WallFaceRef(id, line.StartPoint, line.EndPoint,
                        isCap || !string.IsNullOrEmpty(DrawWallHelper.GetWallSegmentId(line)), isCap, line.LayerId));
                }
                else if (curve is Polyline polyline)
                {
                    int count = polyline.Closed ? polyline.NumberOfVertices : polyline.NumberOfVertices - 1;
                    for (int index = 0; index < count; index++)
                    {
                        if (Math.Abs(polyline.GetBulgeAt(index)) > 1e-10)
                            continue;

                        Point3d start = polyline.GetPoint3dAt(index);
                        Point3d end = polyline.GetPoint3dAt((index + 1) % polyline.NumberOfVertices);
                        if (start.DistanceTo(end) > MinimumConnectionTolerance)
                            result.Add(new WallFaceRef(id, start, end, false, false, polyline.LayerId));
                    }
                }
            }

            return result;
        }

        private static List<FaceSpan> FindRoomFaces(List<WallFaceRef> all, Point3d origin, double thickness)
        {
            // Chỉ xét các line nằm trên layer tường (layer của line do WW tạo)
            HashSet<ObjectId> wallLayers = new HashSet<ObjectId>(all.Where(f => f.Tagged).Select(f => f.LayerId));
            List<WallFaceRef> faces = wallLayers.Count > 0
                ? all.FindAll(f => f.Tagged || wallLayers.Contains(f.LayerId))
                : all;

            double sameLineTolerance = Math.Max(Math.Abs(thickness) * 0.5, MinimumConnectionTolerance * 100.0);
            double seedDistance = Math.Max(Math.Abs(thickness) * 2.0, 1.0);
            double seedSeparation = seedDistance * 3.0;

            List<FaceSpan> merged = new List<FaceSpan>();
            List<Point3d> visited = new List<Point3d> { origin };
            Queue<Point3d> pending = new Queue<Point3d>();
            pending.Enqueue(origin);
            int processed = 0;

            while (pending.Count > 0 && processed < MaximumRoomSeeds)
            {
                Point3d seed = pending.Dequeue();
                bool isOrigin = processed == 0;
                List<Point3d> newSeeds = new List<Point3d>();
                List<FaceSpan>? spans = CastVisibility(faces, seed, isOrigin ? 720 : 360, isOrigin, sameLineTolerance, seedDistance, newSeeds);
                processed++;
                if (spans == null)
                    continue; // điểm quan sát này nhìn ra ngoài (rò qua cửa) -> bỏ

                foreach (FaceSpan span in spans)
                {
                    if (!isOrigin)
                        span.ResetWeight();
                    MergeSpan(merged, span, sameLineTolerance);
                }

                foreach (Point3d candidate in newSeeds)
                {
                    if (visited.Exists(point => point.DistanceTo(candidate) <= seedSeparation))
                        continue;
                    visited.Add(candidate);
                    pending.Enqueue(candidate);
                }
            }

            double minimumLength = MinimumConnectionTolerance * 100.0;
            merged.RemoveAll(span =>
                span.Face.IsCap ||
                (span.S1 - span.S0) * span.Face.Start.DistanceTo(span.Face.End) <= minimumLength);

            return SelectConnectedSpans(merged, Math.Max(Math.Abs(thickness) * 1.5, 0.1));
        }

        private static List<FaceSpan>? CastVisibility(List<WallFaceRef> faces, Point3d origin, int coarseRays, bool isOrigin, double sameLineTolerance, double seedDistance, List<Point3d> newSeeds)
        {
            const double twoPi = Math.PI * 2.0;
            List<RaySample> samples = new List<RaySample>();
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
            double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;

            int missedRays = 0;
            double firstMissDegrees = 0.0;
            for (int k = 0; k < coarseRays; k++)
            {
                double angle = k * twoPi / coarseRays;
                if (!TryRayHit(faces, origin, angle, out WallFaceRef? face, out double distance) || face == null)
                {
                    if (missedRays++ == 0)
                        firstMissDegrees = angle * 180.0 / Math.PI;
                    continue;
                }

                Point3d hitPoint = new Point3d(origin.X + Math.Cos(angle) * distance, origin.Y + Math.Sin(angle) * distance, origin.Z);
                samples.Add(new RaySample(angle, face, hitPoint, distance));
                minX = Math.Min(minX, hitPoint.X);
                maxX = Math.Max(maxX, hitPoint.X);
                minY = Math.Min(minY, hitPoint.Y);
                maxY = Math.Max(maxY, hitPoint.Y);
            }

            int allowedMisses = Math.Max(1, MaximumMissedRays * coarseRays / 720);
            if (missedRays > allowedMisses || samples.Count == 0)
            {
                if (!isOrigin)
                    return null;

                throw new InvalidOperationException(
                    $"Phòng không kín: {missedRays}/{coarseRays} tia lọt ra ngoài (ví dụ hướng {firstMissDegrees:0}°). Kiểm tra khe hở/cửa hoặc line tường khác layer.");
            }

            const double margin = 1.0;
            List<WallFaceRef> local = faces.FindAll(f =>
                Math.Max(f.Start.X, f.End.X) >= minX - margin && Math.Min(f.Start.X, f.End.X) <= maxX + margin &&
                Math.Max(f.Start.Y, f.End.Y) >= minY - margin && Math.Min(f.Start.Y, f.End.Y) <= maxY + margin);

            foreach (WallFaceRef face in local)
            {
                foreach (Point3d point in new[] { face.Start, face.End })
                {
                    double baseAngle = Math.Atan2(point.Y - origin.Y, point.X - origin.X);
                    foreach (double delta in new[] { -1e-5, 1e-5 })
                    {
                        double angle = (baseAngle + delta) % twoPi;
                        if (angle < 0.0)
                            angle += twoPi;
                        if (TryRayHit(local, origin, angle, out WallFaceRef? hit, out double distance) && hit != null)
                        {
                            Point3d hitPoint = new Point3d(origin.X + Math.Cos(angle) * distance, origin.Y + Math.Sin(angle) * distance, origin.Z);
                            samples.Add(new RaySample(angle, hit, hitPoint, distance));
                        }
                    }
                }
            }

            samples.Sort((a, b) => a.Angle.CompareTo(b.Angle));

            List<FaceSpan> spans = new List<FaceSpan>();
            FaceSpan? current = null;
            foreach (RaySample sample in samples)
            {
                if (current != null && SameWallLine(current.Face, sample.Face, sameLineTolerance))
                {
                    current.Extend(sample.Face, sample.Hit);
                    continue;
                }

                if (current != null)
                    spans.Add(current);
                current = new FaceSpan(sample.Face, sample.Hit, origin);
            }

            if (current != null)
            {
                if (spans.Count > 0 && SameWallLine(spans[0].Face, current.Face, sameLineTolerance))
                    spans[0].Merge(current); // đoạn nối vòng qua góc 0
                else
                    spans.Add(current);
            }

            for (int index = 0; index < samples.Count; index++)
            {
                RaySample first = samples[index];
                RaySample second = samples[(index + 1) % samples.Count];
                if (SameWallLine(first.Face, second.Face, sameLineTolerance))
                    continue;

                RaySample near = first.Distance <= second.Distance ? first : second;
                RaySample far = first.Distance <= second.Distance ? second : first;
                double gap = far.Distance - near.Distance;
                if (gap <= seedDistance * 2.0)
                    continue;

                double reach = near.Distance + Math.Min(seedDistance, gap * 0.5);
                newSeeds.Add(new Point3d(
                    origin.X + Math.Cos(far.Angle) * reach,
                    origin.Y + Math.Sin(far.Angle) * reach,
                    origin.Z));
            }

            return spans;
        }

        private static void MergeSpan(List<FaceSpan> result, FaceSpan span, double tolerance)
        {
            foreach (FaceSpan existing in result)
            {
                if (!SameWallLine(existing.Face, span.Face, tolerance))
                    continue;

                double length = existing.Face.Start.DistanceTo(existing.Face.End);
                if (length <= MinimumConnectionTolerance)
                    continue;

                Vector3d vector = span.Face.End - span.Face.Start;
                double first = existing.ProjectPoint(span.Face.Start + vector * span.S0);
                double second = existing.ProjectPoint(span.Face.Start + vector * span.S1);
                double slack = tolerance / length;
                if (Math.Max(first, second) < existing.S0 - slack || Math.Min(first, second) > existing.S1 + slack)
                    continue;

                existing.Merge(span);
                return;
            }

            result.Add(span);
        }

        private static List<FaceSpan> SelectConnectedSpans(List<FaceSpan> spans, double tolerance)
        {
            int count = spans.Count;
            if (count <= 1)
                return spans;

            int[] parent = Enumerable.Range(0, count).ToArray();
            int Find(int node)
            {
                while (parent[node] != node)
                {
                    parent[node] = parent[parent[node]];
                    node = parent[node];
                }

                return node;
            }

            for (int first = 0; first < count; first++)
            {
                for (int second = first + 1; second < count; second++)
                {
                    if (SpansTouch(spans[first], spans[second], tolerance))
                        parent[Find(first)] = Find(second);
                }
            }

            Dictionary<int, int> weights = new Dictionary<int, int>();
            for (int index = 0; index < count; index++)
            {
                int root = Find(index);
                weights.TryGetValue(root, out int weight);
                weights[root] = weight + spans[index].Weight;
            }

            int bestRoot = weights.OrderByDescending(pair => pair.Value).First().Key;
            double keepWeight = Math.Max(1.0, weights.Values.Sum() * 0.2);
            List<FaceSpan> selected = new List<FaceSpan>();
            for (int index = 0; index < count; index++)
            {
                int root = Find(index);
                if (root == bestRoot || weights[root] >= keepWeight)
                    selected.Add(spans[index]);
            }

            return selected.Count > 0 ? selected : spans;
        }

        private static bool SpansTouch(FaceSpan first, FaceSpan second, double tolerance)
        {
            Point3d firstStart = first.PointAt(first.S0);
            Point3d firstEnd = first.PointAt(first.S1);
            Point3d secondStart = second.PointAt(second.S0);
            Point3d secondEnd = second.PointAt(second.S1);
            return DistanceToSegment(firstStart, secondStart, secondEnd) <= tolerance ||
                   DistanceToSegment(firstEnd, secondStart, secondEnd) <= tolerance ||
                   DistanceToSegment(secondStart, firstStart, firstEnd) <= tolerance ||
                   DistanceToSegment(secondEnd, firstStart, firstEnd) <= tolerance;
        }

        private static bool SameWallLine(WallFaceRef first, WallFaceRef second, double tolerance)
        {
            if (ReferenceEquals(first, second))
                return true;

            Vector3d firstVector = first.End - first.Start;
            Vector3d secondVector = second.End - second.Start;
            if (firstVector.Length <= MinimumConnectionTolerance || secondVector.Length <= MinimumConnectionTolerance)
                return false;

            Vector3d direction = firstVector.GetNormal();
            if (Math.Abs(direction.DotProduct(secondVector.GetNormal())) < 1.0 - ParallelDotTolerance)
                return false;

            return DistanceToInfiniteLine(second.Start, first.Start, direction) <= tolerance &&
                   DistanceToInfiniteLine(second.End, first.Start, direction) <= tolerance;
        }

        private static bool TryRayHit(List<WallFaceRef> faces, Point3d origin, double angle, out WallFaceRef? hitFace, out double hitDistance)
        {
            double dx = Math.Cos(angle);
            double dy = Math.Sin(angle);
            hitFace = null;
            hitDistance = double.PositiveInfinity;

            foreach (WallFaceRef face in faces)
            {
                double ex = face.End.X - face.Start.X;
                double ey = face.End.Y - face.Start.Y;
                double denominator = dx * ey - dy * ex;
                if (Math.Abs(denominator) < 1e-12)
                    continue; // song song

                double wx = face.Start.X - origin.X;
                double wy = face.Start.Y - origin.Y;
                double t = (wx * ey - wy * ex) / denominator; // khoảng cách dọc tia
                double s = (wx * dy - wy * dx) / denominator; // tham số dọc mặt tường
                if (t > 1e-9 && s >= -1e-9 && s <= 1.0 + 1e-9 && t < hitDistance)
                {
                    hitDistance = t;
                    hitFace = face;
                }
            }

            return hitFace != null;
        }

        private static Point3d GetGuidePointOnSideOf(Line face, Point3d sidePoint, double thickness)
        {
            Vector3d vector = face.EndPoint - face.StartPoint;
            Vector3d direction = vector.GetNormal();
            Vector3d normal = new Vector3d(-direction.Y, direction.X, 0.0);
            if ((sidePoint - face.StartPoint).DotProduct(normal) < 0.0)
                normal = -normal;

            double guideDistance = Math.Max(Math.Abs(thickness) * 2.0, 1.0);
            return face.StartPoint + vector * 0.5 + normal * guideDistance;
        }


        private static bool CanOffsetBySegments(Polyline polyline)
        {
            if (!polyline.Closed || polyline.NumberOfVertices < 3)
                return false;

            for (int index = 0; index < polyline.NumberOfVertices; index++)
            {
                if (Math.Abs(polyline.GetBulgeAt(index)) > 1e-10)
                    return false;
            }

            return Math.Abs(GetPolylineSignedArea(polyline)) > MinimumConnectionTolerance;
        }

        private static int AppendPolylineSegmentOffsets(Polyline polyline, Point3d roomPoint, double thickness, StuccoContext context)
        {
            bool roomIsInside = IsPointInsidePolyline(polyline, roomPoint);
            bool polygonInteriorIsLeft = GetPolylineSignedArea(polyline) > 0.0;
            double guideDistance = Math.Max(Math.Abs(thickness) * 2.0, 1.0);
            Line?[] offsetLines = new Line?[polyline.NumberOfVertices];
            int createdCount = 0;

            for (int index = 0; index < polyline.NumberOfVertices; index++)
            {
                Point3d start = polyline.GetPoint3dAt(index);
                Point3d end = polyline.GetPoint3dAt((index + 1) % polyline.NumberOfVertices);
                Vector3d vector = end - start;
                if (vector.Length <= MinimumConnectionTolerance)
                    continue;

                Vector3d direction = vector.GetNormal();
                Vector3d leftNormal = new Vector3d(-direction.Y, direction.X, 0.0);
                Vector3d targetNormal = polygonInteriorIsLeft ? leftNormal : -leftNormal;
                if (!roomIsInside)
                    targetNormal = -targetNormal;

                Point3d guidePoint = start + vector * 0.5 + targetNormal * guideDistance;
                using Line segment = new Line(start, end);
                int firstCreatedIndex = context.CreatedIds.Count;
                createdCount += AppendOffsetOnSide(segment, guidePoint, thickness, context);
                if (context.CreatedIds.Count > firstCreatedIndex)
                    offsetLines[index] = context.Transaction.GetObject(context.CreatedIds[firstCreatedIndex], OpenMode.ForWrite) as Line;
            }

            MiterClosedOffsetLoop(polyline, offsetLines, thickness);
            return createdCount;
        }

        private static void MiterClosedOffsetLoop(Polyline source, Line?[] offsetLines, double thickness)
        {
            double miterLimit = MiterLimit(thickness);
            for (int index = 0; index < offsetLines.Length; index++)
            {
                Line? first = offsetLines[index];
                Line? second = offsetLines[(index + 1) % offsetLines.Length];
                if (first == null || second == null || !TryGetLineIntersection(first, second, out Point3d intersection, out _, out _))
                    continue;

                Point3d sourceCorner = source.GetPoint3dAt((index + 1) % source.NumberOfVertices);
                if (intersection.DistanceTo(sourceCorner) > miterLimit)
                    continue;

                MoveNearestEndpoint(first, sourceCorner, intersection);
                MoveNearestEndpoint(second, sourceCorner, intersection);
            }
        }

        private static void MoveNearestEndpoint(Line line, Point3d expectedEndpoint, Point3d intersection)
        {
            if (line.StartPoint.DistanceTo(expectedEndpoint) <= line.EndPoint.DistanceTo(expectedEndpoint))
                line.StartPoint = intersection;
            else
                line.EndPoint = intersection;
        }

        private static double GetPolylineSignedArea(Polyline polyline)
        {
            double area = 0.0;
            for (int index = 0; index < polyline.NumberOfVertices; index++)
            {
                Point3d current = polyline.GetPoint3dAt(index);
                Point3d next = polyline.GetPoint3dAt((index + 1) % polyline.NumberOfVertices);
                area += current.X * next.Y - next.X * current.Y;
            }

            return area * 0.5;
        }

        private static bool IsPointInsidePolyline(Polyline polyline, Point3d point)
        {
            bool inside = false;
            int previousIndex = polyline.NumberOfVertices - 1;
            for (int index = 0; index < polyline.NumberOfVertices; index++)
            {
                Point3d current = polyline.GetPoint3dAt(index);
                Point3d previous = polyline.GetPoint3dAt(previousIndex);
                bool crossesRay = (current.Y > point.Y) != (previous.Y > point.Y);
                if (crossesRay)
                {
                    double intersectionX = (previous.X - current.X) *
                                           (point.Y - current.Y) /
                                           (previous.Y - current.Y) +
                                           current.X;
                    if (point.X < intersectionX)
                        inside = !inside;
                }

                previousIndex = index;
            }

            return inside;
        }


        private static Line? FindOppositeOpenWallFace(Line face, List<Line> candidates, double thickness)
        {
            string? segmentId = DrawWallHelper.GetWallSegmentId(face);
            string? side = DrawWallHelper.GetWallSideMarker(face);
            double maximumWidth = JoinLimit(thickness, MaximumOpenWallWidthFactor);
            BestFace tagged = new BestFace();
            BestFace any = new BestFace();

            foreach (Line candidate in candidates)
            {
                if (ReferenceEquals(candidate, face) ||
                    !TryGetParallelWallFaceRelation(face, candidate, maximumWidth, out double distance, out double overlap))
                {
                    continue;
                }

                any.Offer(candidate, distance, overlap);
                if (IsOppositeSideOfSameSegment(candidate, segmentId, side))
                    tagged.Offer(candidate, distance, overlap);
            }

            return tagged.Face ?? any.Face;
        }

        private static bool IsOppositeSideOfSameSegment(Line candidate, string? segmentId, string? side)
        {
            if (string.IsNullOrEmpty(segmentId) ||
                !string.Equals(segmentId, DrawWallHelper.GetWallSegmentId(candidate), StringComparison.Ordinal))
            {
                return false;
            }

            string? candidateSide = DrawWallHelper.GetWallSideMarker(candidate);
            return string.IsNullOrEmpty(side) || !string.Equals(side, candidateSide, StringComparison.Ordinal);
        }

        private static bool TryGetParallelWallFaceRelation(Line first, Line second, double maximumWidth, out double distance, out double overlap)
        {
            distance = double.PositiveInfinity;
            overlap = 0.0;
            Vector3d firstVector = first.EndPoint - first.StartPoint;
            Vector3d secondVector = second.EndPoint - second.StartPoint;
            double firstLength = firstVector.Length;
            double secondLength = secondVector.Length;
            if (firstLength <= MinimumConnectionTolerance || secondLength <= MinimumConnectionTolerance)
                return false;

            Vector3d direction = firstVector.GetNormal();
            if (Math.Abs(direction.DotProduct(secondVector.GetNormal())) < 1.0 - ParallelDotTolerance)
                return false;

            double startDistance = DistanceToInfiniteLine(second.StartPoint, first.StartPoint, direction);
            double endDistance = DistanceToInfiniteLine(second.EndPoint, first.StartPoint, direction);
            distance = (startDistance + endDistance) * 0.5;
            if (distance <= MinimumConnectionTolerance || distance > maximumWidth)
                return false;

            double firstProjection = (second.StartPoint - first.StartPoint).DotProduct(direction);
            double secondProjection = (second.EndPoint - first.StartPoint).DotProduct(direction);
            double overlapStart = Math.Max(0.0, Math.Min(firstProjection, secondProjection));
            double overlapEnd = Math.Min(firstLength, Math.Max(firstProjection, secondProjection));
            overlap = Math.Max(0.0, overlapEnd - overlapStart);
            double minimumOverlap = Math.Max(MinimumConnectionTolerance * 100.0, Math.Min(firstLength, secondLength) * MinimumWallFaceOverlapRatio);
            return overlap >= minimumOverlap;
        }

        private static Point3d GetPointAwayFromOppositeFace(Line face, Line oppositeFace, double thickness)
        {
            Vector3d faceVector = face.EndPoint - face.StartPoint;
            Vector3d oppositeVector = oppositeFace.EndPoint - oppositeFace.StartPoint;
            Point3d midpoint = face.StartPoint + faceVector * 0.5;
            Vector3d oppositeDirection = oppositeVector.GetNormal();
            Point3d projection = oppositeFace.StartPoint + oppositeDirection * (midpoint - oppositeFace.StartPoint).DotProduct(oppositeDirection);
            Vector3d awayVector = midpoint - projection;
            if (awayVector.Length <= MinimumConnectionTolerance)
            {
                Vector3d direction = faceVector.GetNormal();
                awayVector = new Vector3d(-direction.Y, direction.X, 0.0);
            }

            double guideDistance = Math.Max(Math.Abs(thickness) * 2.0, 1.0);
            return midpoint + awayVector.GetNormal() * guideDistance;
        }

        private static bool TryGetOpenWallCapGuidePoint(Line cap, List<Line> wallFaces, double thickness, out Point3d guidePoint)
        {
            guidePoint = Point3d.Origin;
            double tolerance = FineTolerance(thickness, 0.1);
            if (!TryGetWallFaceInwardDirection(cap.StartPoint, wallFaces, null, tolerance, out Line? firstFace, out Vector3d firstInward) ||
                !TryGetWallFaceInwardDirection(cap.EndPoint, wallFaces, firstFace, tolerance, out _, out Vector3d secondInward) ||
                firstInward.GetNormal().DotProduct(secondInward.GetNormal()) < 1.0 - ParallelDotTolerance)
            {
                return false;
            }

            Vector3d inward = firstInward.GetNormal() + secondInward.GetNormal();
            if (inward.Length <= MinimumConnectionTolerance)
                return false;

            Point3d midpoint = cap.StartPoint + (cap.EndPoint - cap.StartPoint) * 0.5;
            double guideDistance = Math.Max(Math.Abs(thickness) * 2.0, 1.0);
            guidePoint = midpoint - inward.GetNormal() * guideDistance;
            return true;
        }

        private static bool TryGetWallFaceInwardDirection(Point3d endpoint, List<Line> wallFaces, Line? excludedFace, double tolerance, out Line? connectedFace, out Vector3d inward)
        {
            connectedFace = null;
            inward = new Vector3d(0.0, 0.0, 0.0);
            double bestDistance = double.PositiveInfinity;

            foreach (Line face in wallFaces)
            {
                if (ReferenceEquals(face, excludedFace))
                    continue;

                double startDistance = endpoint.DistanceTo(face.StartPoint);
                double endDistance = endpoint.DistanceTo(face.EndPoint);
                double distance = Math.Min(startDistance, endDistance);
                if (distance > tolerance || distance >= bestDistance)
                    continue;

                Vector3d candidate = startDistance <= endDistance
                    ? face.EndPoint - face.StartPoint
                    : face.StartPoint - face.EndPoint;
                if (candidate.Length <= MinimumConnectionTolerance)
                    continue;

                connectedFace = face;
                inward = candidate;
                bestDistance = distance;
            }

            return connectedFace != null;
        }

        private static void AddOpenWallStrip(Line firstFace, Line secondFace, List<StuccoModel.WallStrip> strips, double thickness)
        {
            Vector3d faceVector = firstFace.EndPoint - firstFace.StartPoint;
            if (faceVector.Length <= MinimumConnectionTolerance)
                return;

            Point3d origin = firstFace.StartPoint;
            Vector3d direction = faceVector.GetNormal();
            Vector3d normal = new Vector3d(-direction.Y, direction.X, 0.0);
            Point3d[] points = { firstFace.StartPoint, firstFace.EndPoint, secondFace.StartPoint, secondFace.EndPoint };
            double minimumAlong = double.PositiveInfinity;
            double maximumAlong = double.NegativeInfinity;
            double minimumAcross = double.PositiveInfinity;
            double maximumAcross = double.NegativeInfinity;

            foreach (Point3d point in points)
            {
                Vector3d offset = point - origin;
                double along = offset.DotProduct(direction);
                double across = offset.DotProduct(normal);
                minimumAlong = Math.Min(minimumAlong, along);
                maximumAlong = Math.Max(maximumAlong, along);
                minimumAcross = Math.Min(minimumAcross, across);
                maximumAcross = Math.Max(maximumAcross, across);
            }

            StuccoModel.WallStrip strip = new StuccoModel.WallStrip(origin, direction, normal, minimumAlong, maximumAlong, minimumAcross, maximumAcross);
            double tolerance = FineTolerance(thickness, 0.01);
            if (!strips.Exists(existing => SameWallStrip(existing, strip, tolerance)))
                strips.Add(strip);
        }

        private static bool SameWallStrip(StuccoModel.WallStrip first, StuccoModel.WallStrip second, double tolerance)
        {
            return first.Center.DistanceTo(second.Center) <= tolerance &&
                   Math.Abs(first.Length - second.Length) <= tolerance &&
                   Math.Abs(first.Width - second.Width) <= tolerance &&
                   Math.Abs(first.Direction.DotProduct(second.Direction)) >= 1.0 - ParallelDotTolerance;
        }

        private static void TrimStuccoInsideWallIntersections(StuccoContext context, List<StuccoModel.WallStrip> strips, double thickness)
        {
            if (strips.Count < 2)
                return;

            double finish = Math.Abs(thickness);
            double tolerance = FineTolerance(thickness, 0.001);
            ObjectId[] originalIds = context.CreatedIds.ToArray();
            foreach (ObjectId objectId in originalIds)
            {
                if (objectId.IsNull || !objectId.IsValid ||
                    context.Transaction.GetObject(objectId, OpenMode.ForWrite) is not Line line ||
                    line.IsErased || line.LayerId != context.LayerId)
                {
                    continue;
                }

                int ownerIndex = FindOwningWallStrip(line, strips, finish, tolerance);
                if (ownerIndex < 0)
                    continue;

                Vector3d originalVector = line.EndPoint - line.StartPoint;
                if (originalVector.Length <= tolerance)
                    continue;

                List<int> trimmingStripIndexes = new List<int>();
                List<double> cutParameters = new List<double> { 0.0, 1.0 };
                Point3d lineMidpoint = line.StartPoint + originalVector * 0.5;
                for (int stripIndex = 0; stripIndex < strips.Count; stripIndex++)
                {
                    if (stripIndex == ownerIndex ||
                        Math.Abs(strips[ownerIndex].Direction.DotProduct(strips[stripIndex].Direction)) >= 1.0 - ParallelDotTolerance)
                    {
                        continue;
                    }

                    int previousCutCount = cutParameters.Count;
                    AddWallStripCutParameters(line, strips[stripIndex], finish, tolerance, cutParameters);
                    if (cutParameters.Count > previousCutCount || IsPointInsideExpandedWallStrip(lineMidpoint, strips[stripIndex], finish, tolerance))
                        trimmingStripIndexes.Add(stripIndex);
                }

                if (trimmingStripIndexes.Count == 0)
                    continue;

                cutParameters.Sort();
                List<(Point3d Start, Point3d End)> keptSegments = new List<(Point3d Start, Point3d End)>();
                for (int index = 0; index < cutParameters.Count - 1; index++)
                {
                    Point3d start = line.StartPoint + originalVector * cutParameters[index];
                    Point3d end = line.StartPoint + originalVector * cutParameters[index + 1];
                    if (start.DistanceTo(end) <= tolerance)
                        continue;

                    Point3d midpoint = start + (end - start) * 0.5;
                    if (!trimmingStripIndexes.Exists(stripIndex => IsPointInsideExpandedWallStrip(midpoint, strips[stripIndex], finish, tolerance)))
                        keptSegments.Add((start, end));
                }

                ReplaceLineWithSegments(context, line, keptSegments);
            }
        }

        private static int FindOwningWallStrip(Line line, List<StuccoModel.WallStrip> strips, double finish, double tolerance)
        {
            Vector3d lineVector = line.EndPoint - line.StartPoint;
            if (lineVector.Length <= tolerance)
                return -1;

            Vector3d lineDirection = lineVector.GetNormal();
            Point3d midpoint = line.StartPoint + lineVector * 0.5;
            double boundaryTolerance = Math.Max(tolerance * 10.0, finish * 0.25);
            int bestIndex = -1;
            double bestDistance = double.PositiveInfinity;

            for (int index = 0; index < strips.Count; index++)
            {
                StuccoModel.WallStrip strip = strips[index];
                Vector3d offset = midpoint - strip.Origin;
                double along = offset.DotProduct(strip.Direction);
                double across = offset.DotProduct(strip.Normal);
                double parallelDot = Math.Abs(lineDirection.DotProduct(strip.Direction));
                double distance;

                if (parallelDot >= 1.0 - ParallelDotTolerance && along >= strip.MinimumAlong - finish - tolerance && along <= strip.MaximumAlong + finish + tolerance)
                {
                    distance = Math.Min(Math.Abs(across - (strip.MinimumAcross - finish)), Math.Abs(across - (strip.MaximumAcross + finish)));
                }
                else if (parallelDot <= ParallelDotTolerance && across >= strip.MinimumAcross - finish - tolerance && across <= strip.MaximumAcross + finish + tolerance)
                {
                    distance = Math.Min(Math.Abs(along - (strip.MinimumAlong - finish)), Math.Abs(along - (strip.MaximumAlong + finish)));
                }
                else
                {
                    continue;
                }

                if (distance <= boundaryTolerance && distance < bestDistance)
                {
                    bestIndex = index;
                    bestDistance = distance;
                }
            }

            return bestIndex;
        }

        private static void AddWallStripCutParameters(Line line, StuccoModel.WallStrip strip, double finish, double tolerance, List<double> parameters)
        {
            Point3d[] corners = GetExpandedWallStripCorners(strip, finish);
            double lineLength = line.StartPoint.DistanceTo(line.EndPoint);
            for (int edgeIndex = 0; edgeIndex < corners.Length; edgeIndex++)
            {
                using Line edge = new Line(corners[edgeIndex], corners[(edgeIndex + 1) % corners.Length]);
                if (TryGetLineIntersection(line, edge, out _, out double lineParameter, out double edgeParameter) &&
                    IsParameterOnSegment(lineParameter) && IsParameterOnSegment(edgeParameter))
                {
                    AddInteriorCutParameter(parameters, lineParameter, lineLength, tolerance);
                }
            }
        }

        private static Point3d[] GetExpandedWallStripCorners(StuccoModel.WallStrip strip, double finish)
        {
            return new[]
            {
                GetWallStripPoint(strip, strip.MinimumAlong - finish, strip.MinimumAcross - finish),
                GetWallStripPoint(strip, strip.MaximumAlong + finish, strip.MinimumAcross - finish),
                GetWallStripPoint(strip, strip.MaximumAlong + finish, strip.MaximumAcross + finish),
                GetWallStripPoint(strip, strip.MinimumAlong - finish, strip.MaximumAcross + finish)
            };
        }

        private static Point3d GetWallStripPoint(StuccoModel.WallStrip strip, double along, double across)
        {
            return strip.Origin + strip.Direction * along + strip.Normal * across;
        }

        private static bool IsPointInsideExpandedWallStrip(Point3d point, StuccoModel.WallStrip strip, double finish, double tolerance)
        {
            Vector3d offset = point - strip.Origin;
            double along = offset.DotProduct(strip.Direction);
            double across = offset.DotProduct(strip.Normal);
            return along > strip.MinimumAlong - finish + tolerance &&
                   along < strip.MaximumAlong + finish - tolerance &&
                   across > strip.MinimumAcross - finish + tolerance &&
                   across < strip.MaximumAcross + finish - tolerance;
        }

        private static void ReplaceLineWithSegments(StuccoContext context, Line line, List<(Point3d Start, Point3d End)> segments)
        {
            if (segments.Count == 0)
            {
                line.Erase(true);
                return;
            }

            Point3d firstStart = segments[0].Start;
            Point3d firstEnd = segments[0].End;
            for (int index = 1; index < segments.Count; index++)
                AppendLinePiece(context, line, segments[index].Start, segments[index].End, true);

            line.StartPoint = firstStart;
            line.EndPoint = firstEnd;
        }

        /// <summary>Tạo line mới cùng thuộc tính với line nguồn và thêm vào không gian hiện tại.</summary>
        private static Line AppendLinePiece(StuccoContext context, Line source, Point3d start, Point3d end, bool trackCreated)
        {
            Line piece = new Line(start, end)
            {
                LayerId = source.LayerId,
                Color = source.Color,
                LineWeight = source.LineWeight,
                LinetypeId = source.LinetypeId,
                LinetypeScale = source.LinetypeScale,
                Transparency = source.Transparency
            };
            context.Space.AppendEntity(piece);
            context.Transaction.AddNewlyCreatedDBObject(piece, true);
            if (trackCreated)
                context.CreatedIds.Add(piece.ObjectId);
            return piece;
        }


        private static List<Polyline> BuildSelectedLinePaths(List<StuccoModel.LineSegment> segments, double tolerance)
        {
            List<Polyline> paths = new List<Polyline>();
            bool[] used = new bool[segments.Count];

            for (int seedIndex = 0; seedIndex < segments.Count; seedIndex++)
            {
                if (used[seedIndex])
                    continue;

                used[seedIndex] = true;
                List<Point3d> points = new List<Point3d> { segments[seedIndex].Start, segments[seedIndex].End };
                while (TryExtendSelectedPath(points, segments, used, true, tolerance) || TryExtendSelectedPath(points, segments, used, false, tolerance))
                {
                }

                bool closed = points.Count > 2 && points[0].DistanceTo(points[^1]) <= tolerance;
                if (closed)
                    points.RemoveAt(points.Count - 1);

                Polyline path = new Polyline(points.Count) { Elevation = points[0].Z, Closed = closed };
                for (int index = 0; index < points.Count; index++)
                    path.AddVertexAt(index, new Point2d(points[index].X, points[index].Y), 0.0, 0.0, 0.0);
                paths.Add(path);
            }

            return paths;
        }

        private static bool TryExtendSelectedPath(List<Point3d> points, List<StuccoModel.LineSegment> segments, bool[] used, bool extendEnd, double tolerance)
        {
            Point3d endpoint = extendEnd ? points[^1] : points[0];
            int bestIndex = -1;
            bool matchedStart = true;
            double bestDistance = double.PositiveInfinity;

            for (int index = 0; index < segments.Count; index++)
            {
                if (used[index])
                    continue;

                double startDistance = endpoint.DistanceTo(segments[index].Start);
                if (startDistance <= tolerance && startDistance < bestDistance)
                {
                    bestIndex = index;
                    matchedStart = true;
                    bestDistance = startDistance;
                }

                double endDistance = endpoint.DistanceTo(segments[index].End);
                if (endDistance <= tolerance && endDistance < bestDistance)
                {
                    bestIndex = index;
                    matchedStart = false;
                    bestDistance = endDistance;
                }
            }

            if (bestIndex < 0)
                return false;

            used[bestIndex] = true;
            Point3d nextPoint = matchedStart ? segments[bestIndex].End : segments[bestIndex].Start;
            if (extendEnd)
                points.Add(nextPoint);
            else
                points.Insert(0, nextPoint);
            return true;
        }


        private static void CleanupStuccoGeometry(StuccoContext context, HashSet<ObjectId> excludedIds, double thickness, bool isolate = false)
        {
            List<Line> stuccoLines = new List<Line>();
            List<Line> polylineSegments = new List<Line>();
            HashSet<ObjectId> collectedIds = new HashSet<ObjectId>();

            try
            {
                void Collect(ObjectId id) => CollectStuccoEntity(
                    context.Transaction, id, excludedIds, context.LayerId, collectedIds, stuccoLines, polylineSegments);

                if (!isolate)
                {
                    foreach (ObjectId id in context.Space)
                        Collect(id);
                }

                foreach (ObjectId id in context.CreatedIds)
                    Collect(id);

                double mergeTolerance = FineTolerance(thickness, 0.001);
                RemoveLinesCoveredByPolylineSegments(stuccoLines, polylineSegments, mergeTolerance);
                MergeCollinearLineGaps(stuccoLines, polylineSegments, thickness, mergeTolerance);
                TrimOffsetLineIntersections(stuccoLines, thickness);
                ConnectLineEndpointsToReferences(stuccoLines, polylineSegments, thickness);
                RemoveLinesCoveredByPolylineSegments(stuccoLines, polylineSegments, mergeTolerance);
                RemoveDuplicateStuccoLines(stuccoLines, mergeTolerance);

                List<Curve> sourceCurves = GetSourceCurves(context.Transaction, excludedIds);
                CreateStuccoEndCaps(context, sourceCurves, stuccoLines, polylineSegments, thickness, mergeTolerance);
                SplitLinesAtIntersections(context, stuccoLines, polylineSegments, sourceCurves, mergeTolerance);
            }
            finally
            {
                foreach (Line segment in polylineSegments)
                    segment.Dispose();
            }
        }

        private static List<Curve> GetSourceCurves(Transaction transaction, HashSet<ObjectId> ids)
        {
            List<Curve> curves = new List<Curve>();
            foreach (ObjectId id in ids)
            {
                Curve? curve = GetCurve(transaction, id);
                if (curve != null && !curve.IsErased)
                    curves.Add(curve);
            }

            return curves;
        }

        private static void CollectStuccoEntity(Transaction transaction, ObjectId objectId, HashSet<ObjectId> excludedIds, ObjectId targetLayerId, HashSet<ObjectId> collectedIds, List<Line> stuccoLines, List<Line> polylineSegments)
        {
            if (objectId.IsNull ||
                !objectId.IsValid ||
                excludedIds.Contains(objectId) ||
                !collectedIds.Add(objectId))
            {
                return;
            }

            DBObject source = transaction.GetObject(objectId, OpenMode.ForRead);
            if (source is Line line && !line.IsErased && line.LayerId == targetLayerId)
            {
                if (!line.IsWriteEnabled)
                    line.UpgradeOpen();
                stuccoLines.Add(line);
            }
            else if (source is Polyline polyline && !polyline.IsErased && polyline.LayerId == targetLayerId)
            {
                CollectStraightPolylineSegments(polyline, polylineSegments);
            }
        }

        private static void CollectStraightPolylineSegments(Polyline polyline, List<Line> segments)
        {
            int segmentCount = polyline.Closed
                ? polyline.NumberOfVertices
                : Math.Max(0, polyline.NumberOfVertices - 1);
            for (int index = 0; index < segmentCount; index++)
            {
                if (Math.Abs(polyline.GetBulgeAt(index)) > 1e-10)
                    continue;

                Point3d start = polyline.GetPoint3dAt(index);
                Point3d end = polyline.GetPoint3dAt((index + 1) % polyline.NumberOfVertices);
                if (start.DistanceTo(end) > MinimumConnectionTolerance)
                    segments.Add(new Line(start, end));
            }
        }

        private static void RemoveLinesCoveredByPolylineSegments(List<Line> lines, List<Line> references, double tolerance)
        {
            foreach (Line line in lines)
            {
                if (GetCoveredLength(line, references, tolerance) >= line.StartPoint.DistanceTo(line.EndPoint) * 0.98)
                    line.Erase(true);
            }

            lines.RemoveAll(line => line.IsErased);
        }

        private static double GetCoveredLength(Line line, List<Line> references, double tolerance)
        {
            Vector3d lineVector = line.EndPoint - line.StartPoint;
            if (lineVector.Length <= tolerance)
                return 0.0;

            Vector3d direction = lineVector.GetNormal();
            List<(double Start, double End)> intervals = new List<(double Start, double End)>();

            foreach (Line reference in references)
            {
                Vector3d referenceVector = reference.EndPoint - reference.StartPoint;
                if (referenceVector.Length <= tolerance ||
                    Math.Abs(direction.DotProduct(referenceVector.GetNormal())) < 1.0 - ParallelDotTolerance ||
                    DistanceToInfiniteLine(reference.StartPoint, line.StartPoint, direction) > tolerance ||
                    DistanceToInfiniteLine(reference.EndPoint, line.StartPoint, direction) > tolerance)
                {
                    continue;
                }

                double first = (reference.StartPoint - line.StartPoint).DotProduct(direction);
                double second = (reference.EndPoint - line.StartPoint).DotProduct(direction);
                double start = Math.Max(0.0, Math.Min(first, second));
                double end = Math.Min(lineVector.Length, Math.Max(first, second));
                if (end - start > tolerance)
                    intervals.Add((start, end));
            }

            if (intervals.Count == 0)
                return 0.0;

            intervals.Sort((first, second) => first.Start.CompareTo(second.Start));
            double coveredLength = 0.0;
            double currentStart = intervals[0].Start;
            double currentEnd = intervals[0].End;
            for (int index = 1; index < intervals.Count; index++)
            {
                if (intervals[index].Start <= currentEnd + tolerance)
                {
                    currentEnd = Math.Max(currentEnd, intervals[index].End);
                    continue;
                }

                coveredLength += currentEnd - currentStart;
                currentStart = intervals[index].Start;
                currentEnd = intervals[index].End;
            }

            return coveredLength + currentEnd - currentStart;
        }

        private static void RemoveDuplicateStuccoLines(List<Line> lines, double tolerance)
        {
            for (int firstIndex = 0; firstIndex < lines.Count; firstIndex++)
            {
                Line first = lines[firstIndex];
                if (first.IsErased)
                    continue;

                for (int secondIndex = firstIndex + 1; secondIndex < lines.Count; secondIndex++)
                {
                    Line second = lines[secondIndex];
                    if (!second.IsErased && SameUndirectedSegment(first, second, tolerance))
                        second.Erase(true);
                }
            }

            lines.RemoveAll(line => line.IsErased);
        }

        private static bool SameUndirectedSegment(Line first, Line second, double tolerance)
        {
            return
                (first.StartPoint.DistanceTo(second.StartPoint) <= tolerance &&
                 first.EndPoint.DistanceTo(second.EndPoint) <= tolerance) ||
                (first.StartPoint.DistanceTo(second.EndPoint) <= tolerance &&
                 first.EndPoint.DistanceTo(second.StartPoint) <= tolerance);
        }

        // ----- Gộp các đoạn thẳng hàng bị đứt -----

        private static void MergeCollinearLineGaps(List<Line> lines, List<Line> references, double thickness, double tolerance)
        {
            double maximumGap = Math.Max(tolerance, Math.Abs(thickness) * MaximumWallWidthFactor);
            double blockerJoinDistance = JoinLimit(thickness, 2.0);

            bool merged;
            do
            {
                merged = false;
                for (int firstIndex = 0; firstIndex < lines.Count && !merged; firstIndex++)
                {
                    Line first = lines[firstIndex];
                    if (first.IsErased)
                        continue;

                    for (int secondIndex = firstIndex + 1; secondIndex < lines.Count; secondIndex++)
                    {
                        Line second = lines[secondIndex];
                        if (second.IsErased ||
                            !TryGetCollinearUnion(first, second, tolerance, out Point3d unionStart, out Point3d unionEnd, out double gapStart, out double gapEnd) ||
                            gapEnd - gapStart > maximumGap)
                        {
                            continue;
                        }

                        if (gapEnd - gapStart > tolerance &&
                            HasPerpendicularBlocker(first, second, lines, references, gapStart, gapEnd, blockerJoinDistance, tolerance))
                        {
                            continue;
                        }

                        first.StartPoint = unionStart;
                        first.EndPoint = unionEnd;
                        second.Erase(true);
                        merged = true;
                        break;
                    }
                }
            }
            while (merged);

            lines.RemoveAll(line => line.IsErased);
        }

        private static bool TryGetCollinearUnion(Line first, Line second, double tolerance, out Point3d unionStart, out Point3d unionEnd, out double gapStart, out double gapEnd)
        {
            unionStart = Point3d.Origin;
            unionEnd = Point3d.Origin;
            gapStart = 0.0;
            gapEnd = 0.0;

            Vector3d firstVector = first.EndPoint - first.StartPoint;
            Vector3d secondVector = second.EndPoint - second.StartPoint;
            if (firstVector.Length <= tolerance || secondVector.Length <= tolerance)
                return false;

            Vector3d direction = firstVector.GetNormal();
            if (Math.Abs(direction.DotProduct(secondVector.GetNormal())) < 1.0 - ParallelDotTolerance ||
                DistanceToInfiniteLine(second.StartPoint, first.StartPoint, direction) > tolerance ||
                DistanceToInfiniteLine(second.EndPoint, first.StartPoint, direction) > tolerance)
            {
                return false;
            }

            double firstStart = 0.0;
            double firstEnd = firstVector.Length;
            double secondFirst = (second.StartPoint - first.StartPoint).DotProduct(direction);
            double secondSecond = (second.EndPoint - first.StartPoint).DotProduct(direction);
            double secondStart = Math.Min(secondFirst, secondSecond);
            double secondEnd = Math.Max(secondFirst, secondSecond);

            if (firstEnd < secondStart)
            {
                gapStart = firstEnd;
                gapEnd = secondStart;
            }
            else if (secondEnd < firstStart)
            {
                gapStart = secondEnd;
                gapEnd = firstStart;
            }

            unionStart = first.StartPoint + direction * Math.Min(firstStart, secondStart);
            unionEnd = first.StartPoint + direction * Math.Max(firstEnd, secondEnd);
            return true;
        }

        private static bool HasPerpendicularBlocker(Line first, Line second, List<Line> lines, List<Line> references, double gapStart, double gapEnd, double maximumJoinDistance, double tolerance)
        {
            foreach (Line candidate in lines)
            {
                if (ReferenceEquals(candidate, first) || ReferenceEquals(candidate, second) || candidate.IsErased)
                    continue;
                if (CanBlockCollinearGap(first, candidate, gapStart, gapEnd, maximumJoinDistance, tolerance))
                    return true;
            }

            foreach (Line reference in references)
            {
                if (CanBlockCollinearGap(first, reference, gapStart, gapEnd, maximumJoinDistance, tolerance))
                    return true;
            }

            return false;
        }

        private static bool CanBlockCollinearGap(Line gapLine, Line candidate, double gapStart, double gapEnd, double maximumJoinDistance, double tolerance)
        {
            if (!TryGetLineIntersection(gapLine, candidate, out Point3d intersection, out _, out double candidateParameter))
                return false;

            Vector3d direction = (gapLine.EndPoint - gapLine.StartPoint).GetNormal();
            double gapParameter = (intersection - gapLine.StartPoint).DotProduct(direction);
            if (gapParameter < gapStart - tolerance || gapParameter > gapEnd + tolerance)
                return false;

            return IsParameterOnSegment(candidateParameter) ||
                   Math.Min(candidate.StartPoint.DistanceTo(intersection), candidate.EndPoint.DistanceTo(intersection)) <= maximumJoinDistance;
        }

        // ----- Cắt/kéo dài các đầu mút về giao điểm -----

        private static void TrimOffsetLineIntersections(List<Line> lines, double thickness)
        {
            if (lines.Count < 2)
                return;

            EndpointSnaps snaps = new EndpointSnaps(lines.Count);
            double cornerJoinDistance = MiterLimit(thickness);
            double branchJoinDistance = JoinLimit(thickness, 2.0);

            for (int firstIndex = 0; firstIndex < lines.Count; firstIndex++)
            {
                for (int secondIndex = firstIndex + 1; secondIndex < lines.Count; secondIndex++)
                {
                    Line first = lines[firstIndex];
                    Line second = lines[secondIndex];
                    if (!TryGetLineIntersection(first, second, out Point3d intersection, out double firstParameter, out double secondParameter))
                        continue;

                    // Góc: cả hai đầu mút đều gần giao điểm -> kéo cả hai về đó
                    if (IsIntersectionNearEndpoint(first, intersection, cornerJoinDistance) &&
                        IsIntersectionNearEndpoint(second, intersection, cornerJoinDistance))
                    {
                        snaps.Offer(first, firstIndex, intersection, cornerJoinDistance);
                        snaps.Offer(second, secondIndex, intersection, cornerJoinDistance);
                        continue;
                    }

                    // Ngã ba: đầu mút của line này chạm thân line kia
                    if (IsParameterOnSegment(secondParameter) && IsIntersectionNearEndpoint(first, intersection, branchJoinDistance))
                        snaps.Offer(first, firstIndex, intersection, branchJoinDistance);
                    if (IsParameterOnSegment(firstParameter) && IsIntersectionNearEndpoint(second, intersection, branchJoinDistance))
                        snaps.Offer(second, secondIndex, intersection, branchJoinDistance);
                }
            }

            snaps.Apply(lines);
        }

        private static void ConnectLineEndpointsToReferences(List<Line> lines, List<Line> references, double thickness)
        {
            if (lines.Count == 0 || references.Count == 0)
                return;

            EndpointSnaps snaps = new EndpointSnaps(lines.Count);
            double maximumJoinDistance = JoinLimit(thickness, 2.0);

            for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                Line line = lines[lineIndex];
                foreach (Line reference in references)
                {
                    if (!TryGetLineIntersection(line, reference, out Point3d intersection, out _, out double referenceParameter))
                        continue;

                    bool referenceCanJoin = IsParameterOnSegment(referenceParameter) ||
                                            IsIntersectionNearEndpoint(reference, intersection, maximumJoinDistance);
                    if (referenceCanJoin && IsIntersectionNearEndpoint(line, intersection, maximumJoinDistance))
                        snaps.Offer(line, lineIndex, intersection, maximumJoinDistance);
                }
            }

            snaps.Apply(lines);
        }

        // ----- Bo cap các đầu mút hở -----

        private static void CreateStuccoEndCaps(StuccoContext context, List<Curve> sourceCurves, List<Line> lines, List<Line> references, double thickness, double tolerance)
        {
            if (sourceCurves.Count == 0)
                return;

            List<Line> allSegments = new List<Line>();
            allSegments.AddRange(lines);
            allSegments.AddRange(references);
            List<Line> caps = new List<Line>();
            double maximumCapLength = JoinLimit(thickness, 2.5);
            double connectionTolerance = Math.Max(tolerance, Math.Abs(thickness) * 0.1);

            foreach (Line segment in allSegments)
            {
                if (segment.IsErased)
                    continue;

                TryAppendEndCap(segment.StartPoint, segment, allSegments, sourceCurves, caps, context, maximumCapLength, connectionTolerance);
                TryAppendEndCap(segment.EndPoint, segment, allSegments, sourceCurves, caps, context, maximumCapLength, connectionTolerance);
            }

            lines.AddRange(caps);
        }

        private static void TryAppendEndCap(Point3d endpoint, Line ownerSegment, List<Line> allSegments, List<Curve> sourceCurves, List<Line> caps, StuccoContext context, double maximumCapLength, double tolerance)
        {
            if (IsEndpointConnected(endpoint, ownerSegment, allSegments, tolerance))
                return;

            Point3d closestPoint = Point3d.Origin;
            double closestDistance = double.PositiveInfinity;
            foreach (Curve sourceCurve in sourceCurves)
            {
                try
                {
                    Point3d candidate = sourceCurve.GetClosestPointTo(endpoint, false);
                    double distance = endpoint.DistanceTo(candidate);
                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        closestPoint = candidate;
                    }
                }
                catch
                {
                }
            }

            if (closestDistance <= tolerance || closestDistance > maximumCapLength)
                return;

            Vector3d ownerVector = ownerSegment.EndPoint - ownerSegment.StartPoint;
            Vector3d capVector = closestPoint - endpoint;
            if (ownerVector.Length <= tolerance ||
                capVector.Length <= tolerance ||
                Math.Abs(ownerVector.GetNormal().DotProduct(capVector.GetNormal())) > CapPerpendicularDotTolerance)
            {
                return;
            }

            Line cap = new Line(endpoint, closestPoint)
            {
                LayerId = context.LayerId,
                ColorIndex = 256
            };
            if (caps.Exists(existing => SameUndirectedSegment(existing, cap, tolerance)))
            {
                cap.Dispose();
                return;
            }

            context.Space.AppendEntity(cap);
            context.Transaction.AddNewlyCreatedDBObject(cap, true);
            caps.Add(cap);
        }

        private static bool IsEndpointConnected(Point3d endpoint, Line ownerSegment, List<Line> segments, double tolerance)
        {
            foreach (Line segment in segments)
            {
                if (ReferenceEquals(segment, ownerSegment) || segment.IsErased)
                    continue;
                if (DistanceToLineSegment(endpoint, segment) <= tolerance)
                    return true;
            }

            return false;
        }

        private static double DistanceToLineSegment(Point3d point, Line segment)
        {
            Vector3d vector = segment.EndPoint - segment.StartPoint;
            double lengthSquared = vector.DotProduct(vector);
            if (lengthSquared <= MinimumConnectionTolerance * MinimumConnectionTolerance)
                return point.DistanceTo(segment.StartPoint);

            double parameter = (point - segment.StartPoint).DotProduct(vector) / lengthSquared;
            parameter = Math.Max(0.0, Math.Min(1.0, parameter));
            return point.DistanceTo(segment.StartPoint + vector * parameter);
        }

        // ----- Tách line tại giao điểm -----

        private static void SplitLinesAtIntersections(StuccoContext context, List<Line> lines, List<Line> references, List<Curve> sourceCurves, double tolerance)
        {
            Dictionary<Line, List<double>> cutParameters = new Dictionary<Line, List<double>>();
            foreach (Line line in lines)
            {
                if (!line.IsErased)
                    cutParameters[line] = new List<double> { 0.0, 1.0 };
            }

            for (int firstIndex = 0; firstIndex < lines.Count; firstIndex++)
            {
                Line first = lines[firstIndex];
                if (first.IsErased)
                    continue;

                for (int secondIndex = firstIndex + 1; secondIndex < lines.Count; secondIndex++)
                {
                    Line second = lines[secondIndex];
                    if (second.IsErased ||
                        !TryGetLineIntersection(first, second, out _, out double firstParameter, out double secondParameter) ||
                        !IsParameterOnSegment(firstParameter) ||
                        !IsParameterOnSegment(secondParameter))
                    {
                        continue;
                    }

                    AddInteriorCutParameter(cutParameters[first], firstParameter, first.StartPoint.DistanceTo(first.EndPoint), tolerance);
                    AddInteriorCutParameter(cutParameters[second], secondParameter, second.StartPoint.DistanceTo(second.EndPoint), tolerance);
                }
            }

            foreach (Line line in lines)
            {
                if (line.IsErased || !cutParameters.TryGetValue(line, out List<double>? parameters))
                    continue;

                parameters.Sort();
                if (parameters.Count <= 2)
                    continue;

                Point3d originalStart = line.StartPoint;
                Vector3d originalVector = line.EndPoint - originalStart;
                double lineLength = originalVector.Length;
                if (lineLength <= tolerance)
                    continue;

                double parameterTolerance = tolerance / lineLength;
                bool wroteFirst = false;

                for (int index = 0; index < parameters.Count - 1; index++)
                {
                    double startParameter = parameters[index];
                    double endParameter = parameters[index + 1];
                    Point3d start = originalStart + originalVector * startParameter;
                    Point3d end = originalStart + originalVector * endParameter;
                    if (start.DistanceTo(end) <= tolerance)
                        continue;

                    // Đoạn chạm đầu mút gốc mà đầu tự do không được neo vào đâu -> bỏ (mẩu thừa)
                    bool startIsOriginalEndpoint = startParameter <= parameterTolerance;
                    bool endIsOriginalEndpoint = endParameter >= 1.0 - parameterTolerance;
                    if (startIsOriginalEndpoint ^ endIsOriginalEndpoint)
                    {
                        Point3d freeEndpoint = startIsOriginalEndpoint ? start : end;
                        if (!IsPointAnchored(freeEndpoint, line, lines, references, sourceCurves, tolerance))
                            continue;
                    }

                    if (!wroteFirst)
                    {
                        line.StartPoint = start;
                        line.EndPoint = end;
                        wroteFirst = true;
                    }
                    else
                    {
                        AppendLinePiece(context, line, start, end, false);
                    }
                }

                if (!wroteFirst)
                    line.Erase(true);
            }
        }

        private static bool IsPointAnchored(Point3d point, Line owner, List<Line> lines, List<Line> references, List<Curve> sourceCurves, double tolerance)
        {
            foreach (Line candidate in lines)
            {
                if (ReferenceEquals(candidate, owner) || candidate.IsErased)
                    continue;
                if (point.DistanceTo(candidate.StartPoint) <= tolerance || point.DistanceTo(candidate.EndPoint) <= tolerance)
                    return true;
            }

            foreach (Line reference in references)
            {
                if (DistanceToLineSegment(point, reference) <= tolerance)
                    return true;
            }

            foreach (Curve sourceCurve in sourceCurves)
            {
                try
                {
                    if (point.DistanceTo(sourceCurve.GetClosestPointTo(point, false)) <= tolerance)
                        return true;
                }
                catch
                {
                }
            }

            return false;
        }

        private static void AddInteriorCutParameter(List<double> parameters, double parameter, double lineLength, double tolerance)
        {
            if (lineLength <= tolerance)
                return;

            double parameterTolerance = tolerance / lineLength;
            if (parameter <= parameterTolerance || parameter >= 1.0 - parameterTolerance)
                return;
            if (parameters.Exists(value => Math.Abs(value - parameter) <= parameterTolerance))
                return;

            parameters.Add(Math.Max(0.0, Math.Min(1.0, parameter)));
        }

        // -----------------------------------------------------------------------------------
        // OFFSET MỘT CURVE VỀ PHÍA ĐIỂM CHỈ DẪN
        // -----------------------------------------------------------------------------------

        private static int AppendOffsetOnSide(Curve sourceCurve, Point3d sidePoint, double thickness, StuccoContext context)
        {
            List<Entity> positiveOffsets = TryCreateOffsets(sourceCurve, thickness, out Exception? positiveError);
            List<Entity> negativeOffsets = TryCreateOffsets(sourceCurve, -thickness, out Exception? negativeError);

            if (positiveOffsets.Count == 0 && negativeOffsets.Count == 0)
            {
                string detail = positiveError?.Message ?? negativeError?.Message ?? "Không có kết quả offset.";
                throw new InvalidOperationException($"Không thể offset biên tường: {detail}");
            }

            double positiveScore = GetDistanceScore(positiveOffsets, sidePoint);
            double negativeScore = GetDistanceScore(negativeOffsets, sidePoint);
            bool usePositive = positiveScore <= negativeScore;
            List<Entity> chosenOffsets = usePositive ? positiveOffsets : negativeOffsets;
            DisposeTransientEntities(usePositive ? negativeOffsets : positiveOffsets);

            if (chosenOffsets.Count == 0 || double.IsPositiveInfinity(Math.Min(positiveScore, negativeScore)))
            {
                DisposeTransientEntities(chosenOffsets);
                throw new InvalidOperationException("Không xác định được phía offset từ điểm chỉ dẫn.");
            }

            int createdCount = 0;
            try
            {
                foreach (Entity offsetEntity in chosenOffsets)
                {
                    offsetEntity.SetDatabaseDefaults(context.Database);
                    offsetEntity.LayerId = context.LayerId;
                    offsetEntity.ColorIndex = 256;
                    context.Space.AppendEntity(offsetEntity);
                    context.Transaction.AddNewlyCreatedDBObject(offsetEntity, true);
                    context.CreatedIds.Add(offsetEntity.ObjectId);
                    createdCount++;
                }

                return createdCount;
            }
            catch
            {
                for (int index = createdCount; index < chosenOffsets.Count; index++)
                    chosenOffsets[index].Dispose();
                throw;
            }
        }

        private static List<Entity> TryCreateOffsets(Curve sourceCurve, double distance, out Exception? error)
        {
            List<Entity> entities = new List<Entity>();
            error = null;

            try
            {
                DBObjectCollection offsets = sourceCurve.GetOffsetCurves(distance);
                foreach (DBObject offset in offsets)
                {
                    if (offset is Entity entity)
                        entities.Add(entity);
                    else
                        offset?.Dispose();
                }
            }
            catch (Exception ex)
            {
                error = ex;
                DisposeTransientEntities(entities);
                entities.Clear();
            }

            return entities;
        }

        private static double GetDistanceScore(IEnumerable<Entity> entities, Point3d sidePoint)
        {
            double score = double.PositiveInfinity;
            foreach (Entity entity in entities)
            {
                if (entity is not Curve curve)
                    continue;

                try
                {
                    score = Math.Min(score, curve.GetClosestPointTo(sidePoint, false).DistanceTo(sidePoint));
                }
                catch
                {
                }
            }

            return score;
        }

        private static void DisposeTransientEntities(IEnumerable<Entity> entities)
        {
            foreach (Entity entity in entities)
                entity.Dispose();
        }

        // -----------------------------------------------------------------------------------
        // HÌNH HỌC & DUNG SAI DÙNG CHUNG
        // -----------------------------------------------------------------------------------

        /// <summary>Khoảng nối tối đa (sàn 0.1) = |bề dày| x hệ số.</summary>
        private static double JoinLimit(double thickness, double factor)
        {
            return Math.Max(MinimumConnectionTolerance * 1000.0, Math.Abs(thickness) * factor);
        }

        /// <summary>Dung sai mịn (sàn 0.01) = |bề dày| x hệ số.</summary>
        private static double FineTolerance(double thickness, double factor)
        {
            return Math.Max(MinimumConnectionTolerance * 100.0, Math.Abs(thickness) * factor);
        }

        /// <summary>Khoảng nối tối đa ở góc (miter).</summary>
        private static double MiterLimit(double thickness)
        {
            return Math.Max(MinimumConnectionTolerance * 1000.0, Math.Abs(thickness) * MiterLimitFactor + 1.0);
        }

        private static double GetEndpointJoinAllowance(double lineLength, double maximumJoinDistance)
        {
            return Math.Min(maximumJoinDistance, Math.Max(maximumJoinDistance * 0.5, lineLength * 0.35));
        }

        private static bool IsIntersectionNearEndpoint(Line line, Point3d intersection, double maximumJoinDistance)
        {
            double endpointDistance = Math.Min(line.StartPoint.DistanceTo(intersection), line.EndPoint.DistanceTo(intersection));
            return endpointDistance <= GetEndpointJoinAllowance(line.StartPoint.DistanceTo(line.EndPoint), maximumJoinDistance);
        }

        private static bool IsParameterOnSegment(double parameter)
        {
            const double parameterTolerance = 1e-9;
            return parameter >= -parameterTolerance && parameter <= 1.0 + parameterTolerance;
        }

        private static bool TryGetLineIntersection(Line first, Line second, out Point3d intersection, out double firstParameter, out double secondParameter)
        {
            intersection = Point3d.Origin;
            firstParameter = double.NaN;
            secondParameter = double.NaN;

            double firstDx = first.EndPoint.X - first.StartPoint.X;
            double firstDy = first.EndPoint.Y - first.StartPoint.Y;
            double secondDx = second.EndPoint.X - second.StartPoint.X;
            double secondDy = second.EndPoint.Y - second.StartPoint.Y;
            double denominator = Cross2d(firstDx, firstDy, secondDx, secondDy);
            double firstLength = Math.Sqrt(firstDx * firstDx + firstDy * firstDy);
            double secondLength = Math.Sqrt(secondDx * secondDx + secondDy * secondDy);

            if (firstLength <= MinimumConnectionTolerance ||
                secondLength <= MinimumConnectionTolerance ||
                Math.Abs(denominator) <= firstLength * secondLength * 1e-10)
            {
                return false;
            }

            double originDx = second.StartPoint.X - first.StartPoint.X;
            double originDy = second.StartPoint.Y - first.StartPoint.Y;
            firstParameter = Cross2d(originDx, originDy, secondDx, secondDy) / denominator;
            secondParameter = Cross2d(originDx, originDy, firstDx, firstDy) / denominator;
            double x = first.StartPoint.X + firstParameter * firstDx;
            double y = first.StartPoint.Y + firstParameter * firstDy;
            double z = (first.StartPoint.Z + second.StartPoint.Z) * 0.5;
            intersection = new Point3d(x, y, z);
            return true;
        }

        private static double DistanceToInfiniteLine(Point3d point, Point3d linePoint, Vector3d normalizedDirection)
        {
            Vector3d offset = point - linePoint;
            return Math.Abs(offset.X * normalizedDirection.Y - offset.Y * normalizedDirection.X);
        }

        private static double Cross2d(double firstX, double firstY, double secondX, double secondY)
        {
            return firstX * secondY - firstY * secondX;
        }

        private static ObjectId GetTargetLayerId(Transaction transaction, Database database, string targetLayerName)
        {
            LayerTable layerTable = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
            if (!layerTable.Has(targetLayerName))
                throw new InvalidOperationException($"Không tìm thấy layer '{targetLayerName}'.");

            ObjectId targetLayerId = layerTable[targetLayerName];
            LayerTableRecord targetLayer = (LayerTableRecord)transaction.GetObject(targetLayerId, OpenMode.ForRead);
            if (targetLayer.IsLocked)
                throw new InvalidOperationException($"Layer '{targetLayer.Name}' đang khóa. Hãy mở khóa trước khi chạy FN.");

            return targetLayerId;
        }

        private static void ValidateArguments(Database database, double thickness, string targetLayerName)
        {
            if (database == null)
                throw new ArgumentNullException(nameof(database));
            if (thickness <= 0.0 || double.IsNaN(thickness) || double.IsInfinity(thickness))
                throw new ArgumentOutOfRangeException(nameof(thickness), "Chiều dày vữa phải lớn hơn 0.");
            if (string.IsNullOrWhiteSpace(targetLayerName))
                throw new ArgumentException("Tên layer FN không hợp lệ.", nameof(targetLayerName));
        }
    }
}