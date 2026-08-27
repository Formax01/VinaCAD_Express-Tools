using System;
using System.Collections.Generic;
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
        private const double CapPerpendicularDotTolerance = 0.2;
        private const double MiterLimitFactor = 10.0;

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

        public static int CreateStuccoForBoundary(Database database, ObjectId[] boundaryIds, Point3d interiorPoint, double thickness, string targetLayerName)
        {
            ValidateArguments(database, thickness, targetLayerName);
            if (boundaryIds == null || boundaryIds.Length == 0)
                throw new ArgumentException("Chưa có đường biên phòng.", nameof(boundaryIds));

            using Transaction transaction = database.TransactionManager.StartTransaction();
            ObjectId targetLayerId = GetTargetLayerId(transaction, database, targetLayerName);
            BlockTableRecord ownerSpace = (BlockTableRecord)transaction.GetObject(database.CurrentSpaceId, OpenMode.ForWrite);
            HashSet<ObjectId> boundaryIdSet = new HashSet<ObjectId>(boundaryIds);
            List<ObjectId> createdIds = new List<ObjectId>();
            int createdCount = 0;

            foreach (ObjectId boundaryId in boundaryIds)
            {
                if (boundaryId.IsNull || !boundaryId.IsValid)
                    continue;
                if (transaction.GetObject(boundaryId, OpenMode.ForRead) is not Curve boundaryCurve)
                    continue;

                if (boundaryCurve is Polyline boundaryPolyline && CanOffsetBySegments(boundaryPolyline))
                    createdCount += AppendPolylineSegmentOffsets(boundaryPolyline, interiorPoint, thickness, database, transaction, ownerSpace, targetLayerId, createdIds);
                else
                    createdCount += AppendOffsetOnSide(boundaryCurve, interiorPoint, thickness, database, transaction, ownerSpace, targetLayerId, createdIds);
            }

            if (createdCount == 0)
                throw new InvalidOperationException("Không thể tạo vữa mặt trong.");

            CleanupStuccoGeometry(transaction, ownerSpace, boundaryIdSet, createdIds, targetLayerId, thickness);
            transaction.Commit();
            return createdCount;
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

        private static int AppendPolylineSegmentOffsets(Polyline polyline, Point3d roomPoint, double thickness, Database database, Transaction transaction, BlockTableRecord ownerSpace, ObjectId targetLayerId, List<ObjectId> createdIds)
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

                Point3d midpoint = start + vector * 0.5;
                Point3d guidePoint = midpoint + targetNormal * guideDistance;
                using Line segment = new Line(start, end);
                int firstCreatedIndex = createdIds.Count;
                createdCount += AppendOffsetOnSide(segment, guidePoint, thickness, database, transaction, ownerSpace, targetLayerId, createdIds);
                for (int createdIndex = firstCreatedIndex; createdIndex < createdIds.Count; createdIndex++)
                {
                    if (transaction.GetObject(createdIds[createdIndex], OpenMode.ForWrite) is Line offsetLine)
                    {
                        offsetLines[index] = offsetLine;
                        break;
                    }
                }
            }

            MiterClosedOffsetLoop(polyline, offsetLines, thickness);
            return createdCount;
        }

        private static void MiterClosedOffsetLoop(Polyline source, Line?[] offsetLines, double thickness)
        {
            double miterLimit = Math.Max(MinimumConnectionTolerance * 1000.0, Math.Abs(thickness) * MiterLimitFactor + 1.0);
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

        public static int CreateStuccoForSelection(Database database, ObjectId[] sourceIds, Point3d sidePoint, double thickness, string targetLayerName)
        {
            ValidateArguments(database, thickness, targetLayerName);
            if (sourceIds == null || sourceIds.Length == 0)
                throw new ArgumentException("Chưa chọn đường bao mặt ngoài.", nameof(sourceIds));

            using Transaction transaction = database.TransactionManager.StartTransaction();
            ObjectId targetLayerId = GetTargetLayerId(transaction, database, targetLayerName);
            BlockTableRecord ownerSpace = (BlockTableRecord)transaction.GetObject(database.CurrentSpaceId, OpenMode.ForWrite);
            HashSet<ObjectId> sourceIdSet = new HashSet<ObjectId>(sourceIds);
            List<ObjectId> createdIds = new List<ObjectId>();
            List<StuccoModel.LineSegment> lineSegments = new List<StuccoModel.LineSegment>();
            int createdCount = 0;

            foreach (ObjectId sourceId in sourceIds)
            {
                if (sourceId.IsNull || !sourceId.IsValid)
                    continue;
                if (transaction.GetObject(sourceId, OpenMode.ForRead) is not Curve sourceCurve)
                    continue;

                if (sourceCurve is Line line)
                    lineSegments.Add(new StuccoModel.LineSegment(line.StartPoint, line.EndPoint));
                else
                    createdCount += AppendOffsetOnSide(sourceCurve, sidePoint, thickness, database, transaction, ownerSpace, targetLayerId, createdIds);
            }

            double connectionTolerance = Math.Max(MinimumConnectionTolerance * 100.0, Math.Abs(thickness) * 0.01);
            List<Polyline> linePaths = BuildSelectedLinePaths(lineSegments, connectionTolerance);
            try
            {
                foreach (Polyline path in linePaths)
                    createdCount += AppendOffsetOnSide(path, sidePoint, thickness, database, transaction, ownerSpace, targetLayerId, createdIds);

                if (createdCount == 0)
                    throw new InvalidOperationException("Các đường đã chọn không thể tạo vữa mặt ngoài.");

                CleanupStuccoGeometry(transaction, ownerSpace, sourceIdSet, createdIds, targetLayerId, thickness);
                transaction.Commit();
                return createdCount;
            }
            finally
            {
                foreach (Polyline path in linePaths)
                    path.Dispose();
            }
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

        private static void CleanupStuccoGeometry(Transaction transaction, BlockTableRecord currentSpace, HashSet<ObjectId> excludedIds, List<ObjectId> createdIds, ObjectId targetLayerId, double thickness)
        {
            List<Line> stuccoLines = new List<Line>();
            List<Line> polylineSegments = new List<Line>();
            HashSet<ObjectId> collectedIds = new HashSet<ObjectId>();

            try
            {
                foreach (ObjectId objectId in currentSpace)
                    CollectStuccoEntity(
                        transaction,
                        objectId,
                        excludedIds,
                        targetLayerId,
                        collectedIds,
                        stuccoLines,
                        polylineSegments);

                foreach (ObjectId objectId in createdIds)
                    CollectStuccoEntity(
                        transaction,
                        objectId,
                        excludedIds,
                        targetLayerId,
                        collectedIds,
                        stuccoLines,
                        polylineSegments);

                double mergeTolerance = Math.Max(
                    MinimumConnectionTolerance * 100.0,
                    Math.Abs(thickness) * 0.001);
                RemoveLinesCoveredByPolylineSegments(
                    stuccoLines,
                    polylineSegments,
                    mergeTolerance);
                stuccoLines.RemoveAll(line => line.IsErased);

                MergeCollinearLineGaps(
                    stuccoLines,
                    polylineSegments,
                    thickness,
                    mergeTolerance);
                stuccoLines.RemoveAll(line => line.IsErased);

                TrimOffsetLineIntersections(stuccoLines, thickness);
                ConnectLineEndpointsToReferences(
                    stuccoLines,
                    polylineSegments,
                    thickness);
                RemoveLinesCoveredByPolylineSegments(
                    stuccoLines,
                    polylineSegments,
                    mergeTolerance);
                stuccoLines.RemoveAll(line => line.IsErased);
                RemoveDuplicateStuccoLines(stuccoLines, mergeTolerance);
                stuccoLines.RemoveAll(line => line.IsErased);

                List<Curve> sourceCurves = GetSourceCurves(transaction, excludedIds);

                CreateStuccoEndCaps(
                    transaction,
                    currentSpace,
                    sourceCurves,
                    targetLayerId,
                    stuccoLines,
                    polylineSegments,
                    thickness,
                    mergeTolerance);
                stuccoLines.RemoveAll(line => line.IsErased);

                SplitLinesAtIntersections(
                    transaction,
                    currentSpace,
                    stuccoLines,
                    polylineSegments,
                    sourceCurves,
                    mergeTolerance);
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
                if (id.IsNull || !id.IsValid)
                    continue;
                if (transaction.GetObject(id, OpenMode.ForRead) is Curve curve && !curve.IsErased)
                    curves.Add(curve);
            }

            return curves;
        }

        private static void CreateStuccoEndCaps(Transaction transaction, BlockTableRecord ownerSpace, List<Curve> sourceCurves, ObjectId targetLayerId, List<Line> lines, List<Line> references, double thickness, double tolerance)
        {
            if (sourceCurves.Count == 0)
                return;

            List<Line> allSegments = new List<Line>();
            allSegments.AddRange(lines);
            allSegments.AddRange(references);
            List<Line> caps = new List<Line>();
            double maximumCapLength = Math.Max(MinimumConnectionTolerance * 1000.0, Math.Abs(thickness) * 2.5);
            double connectionTolerance = Math.Max(tolerance, Math.Abs(thickness) * 0.1);

            foreach (Line segment in allSegments)
            {
                if (segment.IsErased)
                    continue;

                TryAppendEndCap(segment.StartPoint, segment, allSegments, sourceCurves, caps, transaction, ownerSpace, targetLayerId, maximumCapLength, connectionTolerance);
                TryAppendEndCap(segment.EndPoint, segment, allSegments, sourceCurves, caps, transaction, ownerSpace, targetLayerId, maximumCapLength, connectionTolerance);
            }

            lines.AddRange(caps);
        }

        private static void TryAppendEndCap(Point3d endpoint, Line ownerSegment, List<Line> allSegments, List<Curve> sourceCurves, List<Line> caps, Transaction transaction, BlockTableRecord ownerSpace, ObjectId targetLayerId, double maximumCapLength, double tolerance)
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
                LayerId = targetLayerId,
                ColorIndex = 256
            };
            if (caps.Exists(existing => SameUndirectedSegment(existing, cap, tolerance)))
            {
                cap.Dispose();
                return;
            }

            ownerSpace.AppendEntity(cap);
            transaction.AddNewlyCreatedDBObject(cap, true);
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
            Point3d projection = segment.StartPoint + vector * parameter;
            return point.DistanceTo(projection);
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
            if (source is Line line &&
                !line.IsErased &&
                line.LayerId == targetLayerId)
            {
                if (!line.IsWriteEnabled)
                    line.UpgradeOpen();
                stuccoLines.Add(line);
            }
            else if (source is Polyline polyline &&
                     !polyline.IsErased &&
                     polyline.LayerId == targetLayerId)
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
                if (GetCoveredLength(line, references, tolerance) >=
                    line.StartPoint.DistanceTo(line.EndPoint) * 0.98)
                {
                    line.Erase(true);
                }
            }
        }

        private static double GetCoveredLength(Line line, List<Line> references, double tolerance)
        {
            Vector3d lineVector = line.EndPoint - line.StartPoint;
            if (lineVector.Length <= tolerance)
                return 0.0;

            Vector3d direction = lineVector.GetNormal();
            List<(double Start, double End)> intervals =
                new List<(double Start, double End)>();

            foreach (Line reference in references)
            {
                Vector3d referenceVector = reference.EndPoint - reference.StartPoint;
                if (referenceVector.Length <= tolerance ||
                    Math.Abs(direction.DotProduct(referenceVector.GetNormal())) <
                    1.0 - ParallelDotTolerance ||
                    DistanceToInfiniteLine(reference.StartPoint, line.StartPoint, direction) > tolerance ||
                    DistanceToInfiniteLine(reference.EndPoint, line.StartPoint, direction) > tolerance)
                {
                    continue;
                }

                double first =
                    (reference.StartPoint - line.StartPoint).DotProduct(direction);
                double second =
                    (reference.EndPoint - line.StartPoint).DotProduct(direction);
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

        private static void ConnectLineEndpointsToReferences(List<Line> lines, List<Line> references, double thickness)
        {
            if (lines.Count == 0 || references.Count == 0)
                return;

            Point3d?[] snappedPoints = new Point3d?[lines.Count * 2];
            double[] bestDistances = new double[lines.Count * 2];
            for (int index = 0; index < bestDistances.Length; index++)
                bestDistances[index] = double.PositiveInfinity;

            double maximumJoinDistance = Math.Max(
                MinimumConnectionTolerance * 1000.0,
                Math.Abs(thickness) * 2.0);

            for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                Line line = lines[lineIndex];
                foreach (Line reference in references)
                {
                    if (!TryGetLineIntersection(
                            line,
                            reference,
                            out Point3d intersection,
                            out _,
                            out double referenceParameter))
                    {
                        continue;
                    }

                    bool referenceCanJoin =
                        IsParameterOnSegment(referenceParameter) ||
                        IsIntersectionNearEndpoint(
                            reference,
                            intersection,
                            maximumJoinDistance);
                    if (!referenceCanJoin ||
                        !IsIntersectionNearEndpoint(line, intersection, maximumJoinDistance))
                    {
                        continue;
                    }

                    StoreTrimCandidate(
                        line,
                        lineIndex,
                        intersection,
                        maximumJoinDistance,
                        snappedPoints,
                        bestDistances);
                }
            }

            for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                Point3d? start = snappedPoints[lineIndex * 2];
                Point3d? end = snappedPoints[lineIndex * 2 + 1];
                if (start.HasValue)
                    lines[lineIndex].StartPoint = start.Value;
                if (end.HasValue)
                    lines[lineIndex].EndPoint = end.Value;
            }
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
        }

        private static void MergeCollinearLineGaps(List<Line> lines, List<Line> references, double thickness, double tolerance)
        {
            double maximumGap = Math.Max(
                tolerance,
                Math.Abs(thickness) * MaximumWallWidthFactor);
            double blockerJoinDistance = Math.Max(
                MinimumConnectionTolerance * 1000.0,
                Math.Abs(thickness) * 2.0);

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
                            !TryGetCollinearUnion(
                                first,
                                second,
                                tolerance,
                                out Point3d unionStart,
                                out Point3d unionEnd,
                                out double gapStart,
                                out double gapEnd) ||
                            gapEnd - gapStart > maximumGap)
                        {
                            continue;
                        }

                        if (gapEnd - gapStart > tolerance &&
                            HasPerpendicularBlocker(
                                first,
                                second,
                                lines,
                                references,
                                gapStart,
                                gapEnd,
                                blockerJoinDistance,
                                tolerance))
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
        }

        private static bool TryGetCollinearUnion(Line first, Line second, double tolerance, out Point3d unionStart, out Point3d unionEnd, out double gapStart, out double gapEnd)
        {
            unionStart = Point3d.Origin;
            unionEnd = Point3d.Origin;
            gapStart = 0.0;
            gapEnd = 0.0;

            Vector3d firstVector = first.EndPoint - first.StartPoint;
            Vector3d secondVector = second.EndPoint - second.StartPoint;
            if (firstVector.Length <= tolerance ||
                secondVector.Length <= tolerance)
            {
                return false;
            }

            Vector3d direction = firstVector.GetNormal();
            if (Math.Abs(direction.DotProduct(secondVector.GetNormal())) <
                    1.0 - ParallelDotTolerance ||
                DistanceToInfiniteLine(
                    second.StartPoint,
                    first.StartPoint,
                    direction) > tolerance ||
                DistanceToInfiniteLine(
                    second.EndPoint,
                    first.StartPoint,
                    direction) > tolerance)
            {
                return false;
            }

            double firstStart = 0.0;
            double firstEnd = firstVector.Length;
            double secondFirst =
                (second.StartPoint - first.StartPoint).DotProduct(direction);
            double secondSecond =
                (second.EndPoint - first.StartPoint).DotProduct(direction);
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

            double unionStartParameter = Math.Min(firstStart, secondStart);
            double unionEndParameter = Math.Max(firstEnd, secondEnd);
            unionStart = first.StartPoint + direction * unionStartParameter;
            unionEnd = first.StartPoint + direction * unionEndParameter;
            return true;
        }

        private static bool HasPerpendicularBlocker(Line first, Line second, List<Line> lines, List<Line> references, double gapStart, double gapEnd, double maximumJoinDistance, double tolerance)
        {
            foreach (Line candidate in lines)
            {
                if (ReferenceEquals(candidate, first) ||
                    ReferenceEquals(candidate, second) ||
                    candidate.IsErased)
                {
                    continue;
                }

                if (CanBlockCollinearGap(
                        first,
                        candidate,
                        gapStart,
                        gapEnd,
                        maximumJoinDistance,
                        tolerance))
                {
                    return true;
                }
            }

            foreach (Line reference in references)
            {
                if (CanBlockCollinearGap(
                        first,
                        reference,
                        gapStart,
                        gapEnd,
                        maximumJoinDistance,
                        tolerance))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool CanBlockCollinearGap(Line gapLine, Line candidate, double gapStart, double gapEnd, double maximumJoinDistance, double tolerance)
        {
            if (!TryGetLineIntersection(
                    gapLine,
                    candidate,
                    out Point3d intersection,
                    out _,
                    out double candidateParameter))
            {
                return false;
            }

            Vector3d direction = (gapLine.EndPoint - gapLine.StartPoint).GetNormal();
            double gapParameter =
                (intersection - gapLine.StartPoint).DotProduct(direction);
            if (gapParameter < gapStart - tolerance ||
                gapParameter > gapEnd + tolerance)
            {
                return false;
            }

            return IsParameterOnSegment(candidateParameter) ||
                   Math.Min(
                       candidate.StartPoint.DistanceTo(intersection),
                       candidate.EndPoint.DistanceTo(intersection)) <= maximumJoinDistance;
        }

        private static bool SameUndirectedSegment(Line first, Line second, double tolerance)
        {
            return
                (first.StartPoint.DistanceTo(second.StartPoint) <= tolerance &&
                 first.EndPoint.DistanceTo(second.EndPoint) <= tolerance) ||
                (first.StartPoint.DistanceTo(second.EndPoint) <= tolerance &&
                 first.EndPoint.DistanceTo(second.StartPoint) <= tolerance);
        }

        private static void SplitLinesAtIntersections(Transaction transaction, BlockTableRecord ownerSpace, List<Line> lines, List<Line> references, List<Curve> sourceCurves, double tolerance)
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
                        !TryGetLineIntersection(
                            first,
                            second,
                            out _,
                            out double firstParameter,
                            out double secondParameter) ||
                        !IsParameterOnSegment(firstParameter) ||
                        !IsParameterOnSegment(secondParameter))
                    {
                        continue;
                    }

                    AddInteriorCutParameter(
                        cutParameters[first],
                        firstParameter,
                        first.StartPoint.DistanceTo(first.EndPoint),
                        tolerance);
                    AddInteriorCutParameter(
                        cutParameters[second],
                        secondParameter,
                        second.StartPoint.DistanceTo(second.EndPoint),
                        tolerance);
                }
            }

            foreach (Line line in lines)
            {
                if (line.IsErased)
                    continue;
                if (!cutParameters.TryGetValue(line, out List<double>? parameters))
                    continue;

                parameters.Sort();
                if (parameters.Count <= 2)
                    continue;

                Point3d originalStart = line.StartPoint;
                Point3d originalEnd = line.EndPoint;
                Vector3d originalVector = originalEnd - originalStart;
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
                        Line piece = new Line(start, end)
                        {
                            LayerId = line.LayerId,
                            Color = line.Color,
                            LineWeight = line.LineWeight,
                            LinetypeId = line.LinetypeId,
                            LinetypeScale = line.LinetypeScale,
                            Transparency = line.Transparency
                        };
                        ownerSpace.AppendEntity(piece);
                        transaction.AddNewlyCreatedDBObject(piece, true);
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
                if (point.DistanceTo(candidate.StartPoint) <= tolerance ||
                    point.DistanceTo(candidate.EndPoint) <= tolerance)
                {
                    return true;
                }
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

        private static double DistanceToInfiniteLine(Point3d point, Point3d linePoint, Vector3d normalizedDirection)
        {
            Vector3d offset = point - linePoint;
            return Math.Abs(offset.X * normalizedDirection.Y - offset.Y * normalizedDirection.X);
        }

        private static int AppendOffsetOnSide(Curve sourceCurve, Point3d sidePoint, double thickness, Database database, Transaction transaction, BlockTableRecord ownerSpace, ObjectId targetLayerId, List<ObjectId> createdIds)
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
            List<Entity> chosenOffsets = positiveScore <= negativeScore ? positiveOffsets : negativeOffsets;
            List<Entity> rejectedOffsets = ReferenceEquals(chosenOffsets, positiveOffsets)
                ? negativeOffsets
                : positiveOffsets;

            DisposeTransientEntities(rejectedOffsets);

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
                    offsetEntity.SetDatabaseDefaults(database);
                    offsetEntity.LayerId = targetLayerId;
                    offsetEntity.ColorIndex = 256;
                    ownerSpace.AppendEntity(offsetEntity);
                    transaction.AddNewlyCreatedDBObject(offsetEntity, true);
                    createdIds.Add(offsetEntity.ObjectId);
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

        private static void TrimOffsetLineIntersections(List<Line> lines, double thickness)
        {
            if (lines.Count < 2)
                return;

            Point3d?[] snappedPoints = new Point3d?[lines.Count * 2];
            double[] bestDistances = new double[lines.Count * 2];
            for (int index = 0; index < bestDistances.Length; index++)
                bestDistances[index] = double.PositiveInfinity;

            double cornerJoinDistance = Math.Max(
                MinimumConnectionTolerance * 1000.0,
                Math.Abs(thickness) * MiterLimitFactor + 1.0);
            double branchJoinDistance = Math.Max(
                MinimumConnectionTolerance * 1000.0,
                Math.Abs(thickness) * 2.0);

            for (int firstIndex = 0; firstIndex < lines.Count; firstIndex++)
            {
                for (int secondIndex = firstIndex + 1; secondIndex < lines.Count; secondIndex++)
                {
                    if (!TryGetLineIntersection(
                            lines[firstIndex],
                            lines[secondIndex],
                            out Point3d intersection,
                            out double firstParameter,
                            out double secondParameter))
                    {
                        continue;
                    }

                    bool firstNearCorner = IsIntersectionNearEndpoint(
                        lines[firstIndex],
                        intersection,
                        cornerJoinDistance);
                    bool secondNearCorner = IsIntersectionNearEndpoint(
                        lines[secondIndex],
                        intersection,
                        cornerJoinDistance);
                    bool firstIntersectsSegment = IsParameterOnSegment(firstParameter);
                    bool secondIntersectsSegment = IsParameterOnSegment(secondParameter);

                    if (firstNearCorner && secondNearCorner)
                    {
                        StoreTrimCandidate(
                            lines[firstIndex],
                            firstIndex,
                            intersection,
                            cornerJoinDistance,
                            snappedPoints,
                            bestDistances);
                        StoreTrimCandidate(
                            lines[secondIndex],
                            secondIndex,
                            intersection,
                            cornerJoinDistance,
                            snappedPoints,
                            bestDistances);
                        continue;
                    }

                    if (secondIntersectsSegment &&
                        IsIntersectionNearEndpoint(
                            lines[firstIndex],
                            intersection,
                            branchJoinDistance))
                    {
                        StoreTrimCandidate(
                            lines[firstIndex],
                            firstIndex,
                            intersection,
                            branchJoinDistance,
                            snappedPoints,
                            bestDistances);
                    }

                    if (firstIntersectsSegment &&
                        IsIntersectionNearEndpoint(
                            lines[secondIndex],
                            intersection,
                            branchJoinDistance))
                    {
                        StoreTrimCandidate(
                            lines[secondIndex],
                            secondIndex,
                            intersection,
                            branchJoinDistance,
                            snappedPoints,
                            bestDistances);
                    }
                }
            }

            for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                Point3d? snappedStart = snappedPoints[lineIndex * 2];
                Point3d? snappedEnd = snappedPoints[lineIndex * 2 + 1];
                if (snappedStart.HasValue)
                    lines[lineIndex].StartPoint = snappedStart.Value;
                if (snappedEnd.HasValue)
                    lines[lineIndex].EndPoint = snappedEnd.Value;
            }
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

        private static bool IsParameterOnSegment(double parameter)
        {
            const double parameterTolerance = 1e-9;
            return parameter >= -parameterTolerance &&
                   parameter <= 1.0 + parameterTolerance;
        }

        private static bool IsIntersectionNearEndpoint(Line line, Point3d intersection, double maximumJoinDistance)
        {
            double endpointDistance = Math.Min(
                line.StartPoint.DistanceTo(intersection),
                line.EndPoint.DistanceTo(intersection));
            double lineLength = line.StartPoint.DistanceTo(line.EndPoint);
            return endpointDistance <= GetEndpointJoinAllowance(lineLength, maximumJoinDistance);
        }

        private static void StoreTrimCandidate(Line line, int lineIndex, Point3d intersection, double maximumJoinDistance, Point3d?[] snappedPoints, double[] bestDistances)
        {
            double startDistance = line.StartPoint.DistanceTo(intersection);
            double endDistance = line.EndPoint.DistanceTo(intersection);
            bool useStart = startDistance <= endDistance;
            double endpointDistance = Math.Min(startDistance, endDistance);
            double lineLength = line.StartPoint.DistanceTo(line.EndPoint);
            double allowedDistance = GetEndpointJoinAllowance(lineLength, maximumJoinDistance);
            if (endpointDistance > allowedDistance)
                return;

            int endpointIndex = lineIndex * 2 + (useStart ? 0 : 1);
            StoreNearestSnap(
                endpointIndex,
                intersection,
                endpointDistance,
                snappedPoints,
                bestDistances);
        }

        private static ObjectId GetTargetLayerId(Transaction transaction, Database database, string targetLayerName)
        {
            LayerTable layerTable = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
            if (!layerTable.Has(targetLayerName))
                throw new InvalidOperationException($"Không tìm thấy layer '{targetLayerName}'.");

            ObjectId targetLayerId = layerTable[targetLayerName];
            LayerTableRecord targetLayer = (LayerTableRecord)transaction.GetObject(
                targetLayerId,
                OpenMode.ForRead);
            if (targetLayer.IsLocked)
                throw new InvalidOperationException(
                    $"Layer '{targetLayer.Name}' đang khóa. Hãy mở khóa trước khi chạy FN.");

            return targetLayerId;
        }

        private static double GetEndpointJoinAllowance(double lineLength, double maximumJoinDistance)
        {
            return Math.Min(
                maximumJoinDistance,
                Math.Max(maximumJoinDistance * 0.5, lineLength * 0.35));
        }

        private static void StoreNearestSnap(int endpointIndex, Point3d candidate, double distance, Point3d?[] snappedPoints, double[] bestDistances)
        {
            if (distance >= bestDistances[endpointIndex])
                return;

            bestDistances[endpointIndex] = distance;
            snappedPoints[endpointIndex] = candidate;
        }

        private static double Cross2d(double firstX, double firstY, double secondX, double secondY)
        {
            return firstX * secondY - firstY * secondX;
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
                    Point3d closestPoint = curve.GetClosestPointTo(sidePoint, false);
                    score = Math.Min(score, closestPoint.DistanceTo(sidePoint));
                }
                catch
                {
                }
            }

            return score;
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

        private static void DisposeTransientEntities(IEnumerable<Entity> entities)
        {
            foreach (Entity entity in entities)
                entity.Dispose();
        }
    }
}
