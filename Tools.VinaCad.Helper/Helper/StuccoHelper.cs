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
        private const double MaximumWallWidthFactor = 50.0;

        public static string EnsureLayer(
            Database database,
            string requestedLayerName,
            short colorIndex)
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

        public static int CreateStuccoForBoundary(
            Database database,
            ObjectId[] boundaryIds,
            Point3d interiorPoint,
            double thickness,
            string targetLayerName)
        {
            ValidateArguments(database, thickness, targetLayerName);
            if (boundaryIds == null || boundaryIds.Length == 0)
                throw new ArgumentException("Chưa có đường biên phòng.", nameof(boundaryIds));

            using Transaction transaction = database.TransactionManager.StartTransaction();
            ObjectId targetLayerId = GetTargetLayerId(transaction, database, targetLayerName);
            BlockTableRecord ownerSpace = (BlockTableRecord)transaction.GetObject(
                database.CurrentSpaceId,
                OpenMode.ForWrite);

            HashSet<ObjectId> boundaryIdSet = new HashSet<ObjectId>(boundaryIds);
            double maximumWallWidth = Math.Max(
                MinimumConnectionTolerance * 1000.0,
                Math.Abs(thickness) * MaximumWallWidthFactor);
            double boundaryMatchDistance = Math.Max(
                MinimumConnectionTolerance * 100.0,
                Math.Abs(thickness) * 0.5);
            List<Line> transientBoundarySegments = new List<Line>();
            List<Line> transientWallLines = new List<Line>();
            List<Line> transientOppositeLines = new List<Line>();
            List<ObjectId> createdIds = new List<ObjectId>();

            try
            {
                List<StuccoModel.WallEdge> wallEdges = CollectWallEdges(
                    transaction,
                    ownerSpace,
                    boundaryIdSet,
                    targetLayerId,
                    transientWallLines);

                int createdCount = 0;
                foreach (ObjectId boundaryId in boundaryIds)
                {
                    if (boundaryId.IsNull || !boundaryId.IsValid)
                        continue;

                    DBObject boundaryObject = transaction.GetObject(boundaryId, OpenMode.ForRead);
                    if (boundaryObject is not Curve boundaryCurve)
                        continue;

                    createdCount += AppendOffsetOnSide(
                        boundaryCurve,
                        interiorPoint,
                        thickness,
                        database,
                        transaction,
                        ownerSpace,
                        targetLayerId,
                        createdIds);

                    if (boundaryCurve is Polyline boundaryPolyline)
                        CollectBoundaryLineSegments(boundaryPolyline, transientBoundarySegments);
                }

                int oppositeCreatedCount = 0;
                foreach (Line boundarySegment in transientBoundarySegments)
                {
                    StuccoModel.WallEdge? innerEdge = FindBoundarySourceEdge(
                        boundarySegment,
                        wallEdges,
                        boundaryMatchDistance);
                    if (!innerEdge.HasValue)
                        continue;

                    StuccoModel.WallEdge? oppositeEdge = FindOppositeWallEdge(
                        boundarySegment,
                        innerEdge.Value,
                        wallEdges,
                        interiorPoint,
                        maximumWallWidth);
                    if (!oppositeEdge.HasValue)
                        continue;

                    Line oppositeLine;
                    if (oppositeEdge.Value.IsClosedBoundary)
                    {
                        oppositeLine = new Line(
                            oppositeEdge.Value.Geometry.StartPoint,
                            oppositeEdge.Value.Geometry.EndPoint);
                    }
                    else if (!TryClipLineToOverlap(
                                 boundarySegment,
                                 oppositeEdge.Value.Geometry,
                                 out oppositeLine))
                    {
                        continue;
                    }

                    transientOppositeLines.Add(oppositeLine);
                    Point3d outsidePoint = GetPointAwayFromOtherFace(
                        oppositeLine,
                        boundarySegment,
                        thickness);
                    oppositeCreatedCount += AppendOffsetOnSide(
                        oppositeLine,
                        outsidePoint,
                        thickness,
                        database,
                        transaction,
                        ownerSpace,
                        targetLayerId,
                        createdIds);
                }

                if (createdCount == 0)
                    throw new InvalidOperationException("Không thể tạo vữa mặt trong.");
                if (oppositeCreatedCount == 0)
                    throw new InvalidOperationException(
                        "Không tìm thấy mặt tường đối diện để tạo vữa mặt ngoài.");

                createdCount += oppositeCreatedCount;

                CleanupStuccoGeometry(
                    transaction,
                    ownerSpace,
                    boundaryIdSet,
                    createdIds,
                    targetLayerId,
                    thickness);
                transaction.Commit();
                return createdCount;
            }
            finally
            {
                foreach (Line segment in transientBoundarySegments)
                    segment.Dispose();
                foreach (Line line in transientWallLines)
                    line.Dispose();
                foreach (Line line in transientOppositeLines)
                    line.Dispose();
            }
        }

        private static void CleanupStuccoGeometry(
            Transaction transaction,
            BlockTableRecord currentSpace,
            HashSet<ObjectId> excludedIds,
            List<ObjectId> createdIds,
            ObjectId targetLayerId,
            double thickness)
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
            }
            finally
            {
                foreach (Line segment in polylineSegments)
                    segment.Dispose();
            }
        }

        private static void CollectStuccoEntity(
            Transaction transaction,
            ObjectId objectId,
            HashSet<ObjectId> excludedIds,
            ObjectId targetLayerId,
            HashSet<ObjectId> collectedIds,
            List<Line> stuccoLines,
            List<Line> polylineSegments)
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

        private static void CollectStraightPolylineSegments(
            Polyline polyline,
            List<Line> segments)
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

        private static void RemoveLinesCoveredByPolylineSegments(
            List<Line> lines,
            List<Line> references,
            double tolerance)
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

        private static double GetCoveredLength(
            Line line,
            List<Line> references,
            double tolerance)
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

        private static void ConnectLineEndpointsToReferences(
            List<Line> lines,
            List<Line> references,
            double thickness)
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

        private static void RemoveDuplicateStuccoLines(
            List<Line> lines,
            double tolerance)
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

        private static void MergeCollinearLineGaps(
            List<Line> lines,
            List<Line> references,
            double thickness,
            double tolerance)
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

        private static bool TryGetCollinearUnion(
            Line first,
            Line second,
            double tolerance,
            out Point3d unionStart,
            out Point3d unionEnd,
            out double gapStart,
            out double gapEnd)
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

        private static bool HasPerpendicularBlocker(
            Line first,
            Line second,
            List<Line> lines,
            List<Line> references,
            double gapStart,
            double gapEnd,
            double maximumJoinDistance,
            double tolerance)
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

        private static bool CanBlockCollinearGap(
            Line gapLine,
            Line candidate,
            double gapStart,
            double gapEnd,
            double maximumJoinDistance,
            double tolerance)
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

        private static bool SameUndirectedSegment(
            Line first,
            Line second,
            double tolerance)
        {
            return
                (first.StartPoint.DistanceTo(second.StartPoint) <= tolerance &&
                 first.EndPoint.DistanceTo(second.EndPoint) <= tolerance) ||
                (first.StartPoint.DistanceTo(second.EndPoint) <= tolerance &&
                 first.EndPoint.DistanceTo(second.StartPoint) <= tolerance);
        }

        private static List<StuccoModel.WallEdge> CollectWallEdges(
            Transaction transaction,
            BlockTableRecord currentSpace,
            HashSet<ObjectId> excludedIds,
            ObjectId targetLayerId,
            List<Line> transientLines)
        {
            List<StuccoModel.WallEdge> edges = new List<StuccoModel.WallEdge>();

            foreach (ObjectId objectId in currentSpace)
            {
                if (excludedIds.Contains(objectId))
                    continue;

                DBObject source = transaction.GetObject(objectId, OpenMode.ForRead);
                if (source is Line line &&
                    !line.IsErased &&
                    line.LayerId != targetLayerId &&
                    !DrawWallHelper.IsWallCap(line))
                {
                    AddWallEdge(
                        line.LayerId,
                        line.StartPoint,
                        line.EndPoint,
                        false,
                        edges,
                        transientLines);
                }
                else if (source is Polyline polyline &&
                         !polyline.IsErased &&
                         polyline.LayerId != targetLayerId)
                {
                    int segmentCount = polyline.Closed
                        ? polyline.NumberOfVertices
                        : Math.Max(0, polyline.NumberOfVertices - 1);
                    for (int index = 0; index < segmentCount; index++)
                    {
                        if (Math.Abs(polyline.GetBulgeAt(index)) > 1e-10)
                            continue;

                        AddWallEdge(
                            polyline.LayerId,
                            polyline.GetPoint3dAt(index),
                            polyline.GetPoint3dAt((index + 1) % polyline.NumberOfVertices),
                            polyline.Closed,
                            edges,
                            transientLines);
                    }
                }
            }

            return edges;
        }

        private static void AddWallEdge(
            ObjectId layerId,
            Point3d start,
            Point3d end,
            bool isClosedBoundary,
            List<StuccoModel.WallEdge> edges,
            List<Line> transientLines)
        {
            if (start.DistanceTo(end) <= MinimumConnectionTolerance)
                return;

            Line geometry = new Line(start, end);
            transientLines.Add(geometry);
            edges.Add(new StuccoModel.WallEdge(
                layerId,
                geometry,
                isClosedBoundary));
        }

        private static void CollectBoundaryLineSegments(
            Polyline boundary,
            List<Line> segments)
        {
            int segmentCount = boundary.Closed
                ? boundary.NumberOfVertices
                : Math.Max(0, boundary.NumberOfVertices - 1);
            for (int index = 0; index < segmentCount; index++)
            {
                if (Math.Abs(boundary.GetBulgeAt(index)) > 1e-10)
                    continue;

                Point3d start = boundary.GetPoint3dAt(index);
                Point3d end = boundary.GetPoint3dAt((index + 1) % boundary.NumberOfVertices);
                if (start.DistanceTo(end) > MinimumConnectionTolerance)
                    segments.Add(new Line(start, end));
            }
        }

        private static StuccoModel.WallEdge? FindBoundarySourceEdge(
            Line boundarySegment,
            List<StuccoModel.WallEdge> candidates,
            double maximumMatchDistance)
        {
            Vector3d boundaryVector = boundarySegment.EndPoint - boundarySegment.StartPoint;
            if (boundaryVector.Length <= MinimumConnectionTolerance)
                return null;

            Vector3d direction = boundaryVector.GetNormal();
            StuccoModel.WallEdge? bestCandidate = null;
            double bestDistance = double.PositiveInfinity;
            double bestOverlap = 0.0;
            foreach (StuccoModel.WallEdge candidate in candidates)
            {
                if (!TryGetLineOverlap(
                        boundarySegment,
                        candidate.Geometry,
                        out double overlapStart,
                        out double overlapEnd))
                    continue;

                double distance = DistanceToInfiniteLine(
                    candidate.Geometry.StartPoint,
                    boundarySegment.StartPoint,
                    direction);
                double overlap = overlapEnd - overlapStart;
                if (distance > maximumMatchDistance ||
                    distance > bestDistance + MinimumConnectionTolerance ||
                    (Math.Abs(distance - bestDistance) <= MinimumConnectionTolerance &&
                     overlap <= bestOverlap))
                {
                    continue;
                }

                bestDistance = distance;
                bestOverlap = overlap;
                bestCandidate = candidate;
            }

            return bestCandidate;
        }

        private static StuccoModel.WallEdge? FindOppositeWallEdge(
            Line boundarySegment,
            StuccoModel.WallEdge innerEdge,
            List<StuccoModel.WallEdge> candidates,
            Point3d interiorPoint,
            double maximumWallWidth)
        {
            Vector3d direction = (boundarySegment.EndPoint - boundarySegment.StartPoint).GetNormal();
            double interiorSide = GetSignedSide(
                interiorPoint,
                boundarySegment.StartPoint,
                direction);
            StuccoModel.WallEdge? bestCandidate = null;
            double bestDistance = double.PositiveInfinity;
            double bestOverlap = 0.0;
            bool bestMatchesBoundaryKind = false;

            foreach (StuccoModel.WallEdge candidate in candidates)
            {
                if (candidate.LayerId != innerEdge.LayerId ||
                    !TryGetLineOverlap(
                        boundarySegment,
                        candidate.Geometry,
                        out double overlapStart,
                        out double overlapEnd))
                {
                    continue;
                }

                double distance = DistanceToInfiniteLine(
                    candidate.Geometry.StartPoint,
                    innerEdge.Geometry.StartPoint,
                    direction);
                Point3d candidateMidpoint = new Point3d(
                    (candidate.Geometry.StartPoint.X + candidate.Geometry.EndPoint.X) * 0.5,
                    (candidate.Geometry.StartPoint.Y + candidate.Geometry.EndPoint.Y) * 0.5,
                    (candidate.Geometry.StartPoint.Z + candidate.Geometry.EndPoint.Z) * 0.5);
                double candidateSide = GetSignedSide(
                    candidateMidpoint,
                    boundarySegment.StartPoint,
                    direction);
                double overlap = overlapEnd - overlapStart;
                bool matchesBoundaryKind =
                    candidate.IsClosedBoundary == innerEdge.IsClosedBoundary;
                if (distance <= MinimumConnectionTolerance ||
                    distance > maximumWallWidth ||
                    (Math.Abs(interiorSide) > MinimumConnectionTolerance &&
                     interiorSide * candidateSide >= 0.0))
                {
                    continue;
                }

                if (bestCandidate.HasValue &&
                    ((bestMatchesBoundaryKind && !matchesBoundaryKind) ||
                     (bestMatchesBoundaryKind == matchesBoundaryKind &&
                      (distance > bestDistance + MinimumConnectionTolerance ||
                       (Math.Abs(distance - bestDistance) <= MinimumConnectionTolerance &&
                        overlap <= bestOverlap)))))
                {
                    continue;
                }

                bestDistance = distance;
                bestOverlap = overlap;
                bestMatchesBoundaryKind = matchesBoundaryKind;
                bestCandidate = candidate;
            }

            return bestCandidate;
        }

        private static double GetSignedSide(
            Point3d point,
            Point3d linePoint,
            Vector3d normalizedDirection)
        {
            Vector3d offset = point - linePoint;
            return normalizedDirection.X * offset.Y -
                   normalizedDirection.Y * offset.X;
        }

        private static bool TryClipLineToOverlap(
            Line reference,
            Line candidate,
            out Line clippedLine)
        {
            clippedLine = null;
            if (!TryGetLineOverlap(reference, candidate, out double overlapStart, out double overlapEnd))
                return false;

            Vector3d referenceDirection = (reference.EndPoint - reference.StartPoint).GetNormal();
            Vector3d candidateDirection = (candidate.EndPoint - candidate.StartPoint).GetNormal();
            double candidateStartProjection =
                (candidate.StartPoint - reference.StartPoint).DotProduct(referenceDirection);
            double directionFactor = candidateDirection.DotProduct(referenceDirection);
            if (Math.Abs(directionFactor) <= MinimumConnectionTolerance)
                return false;

            Point3d start = candidate.StartPoint +
                            candidateDirection * ((overlapStart - candidateStartProjection) / directionFactor);
            Point3d end = candidate.StartPoint +
                          candidateDirection * ((overlapEnd - candidateStartProjection) / directionFactor);
            if (start.DistanceTo(end) <= MinimumConnectionTolerance)
                return false;

            clippedLine = new Line(start, end);
            return true;
        }

        private static bool TryGetLineOverlap(
            Line reference,
            Line candidate,
            out double overlapStart,
            out double overlapEnd)
        {
            overlapStart = 0.0;
            overlapEnd = 0.0;

            Vector3d referenceVector = reference.EndPoint - reference.StartPoint;
            Vector3d candidateVector = candidate.EndPoint - candidate.StartPoint;
            if (referenceVector.Length <= MinimumConnectionTolerance ||
                candidateVector.Length <= MinimumConnectionTolerance)
            {
                return false;
            }

            Vector3d direction = referenceVector.GetNormal();
            if (Math.Abs(direction.DotProduct(candidateVector.GetNormal())) <
                1.0 - ParallelDotTolerance)
            {
                return false;
            }

            double candidateStart =
                (candidate.StartPoint - reference.StartPoint).DotProduct(direction);
            double candidateEnd =
                (candidate.EndPoint - reference.StartPoint).DotProduct(direction);
            overlapStart = Math.Max(0.0, Math.Min(candidateStart, candidateEnd));
            overlapEnd = Math.Min(referenceVector.Length, Math.Max(candidateStart, candidateEnd));
            return overlapEnd - overlapStart > MinimumConnectionTolerance;
        }

        private static double DistanceToInfiniteLine(
            Point3d point,
            Point3d linePoint,
            Vector3d normalizedDirection)
        {
            Vector3d offset = point - linePoint;
            return Math.Abs(offset.X * normalizedDirection.Y - offset.Y * normalizedDirection.X);
        }

        private static Point3d GetPointAwayFromOtherFace(
            Line face,
            Line otherFace,
            double thickness)
        {
            Point3d otherMidpoint = new Point3d(
                (otherFace.StartPoint.X + otherFace.EndPoint.X) * 0.5,
                (otherFace.StartPoint.Y + otherFace.EndPoint.Y) * 0.5,
                (otherFace.StartPoint.Z + otherFace.EndPoint.Z) * 0.5);
            Point3d facePoint = face.GetClosestPointTo(otherMidpoint, false);
            Point3d otherPoint = otherFace.GetClosestPointTo(facePoint, false);
            Vector3d awayVector = facePoint - otherPoint;
            if (awayVector.Length <= MinimumConnectionTolerance)
            {
                Vector3d direction = (face.EndPoint - face.StartPoint).GetNormal();
                awayVector = new Vector3d(-direction.Y, direction.X, 0.0);
            }

            double guideDistance = Math.Max(Math.Abs(thickness) * 2.0, 1.0);
            return facePoint + awayVector.GetNormal() * guideDistance;
        }

        private static int AppendOffsetOnSide(
            Curve sourceCurve,
            Point3d sidePoint,
            double thickness,
            Database database,
            Transaction transaction,
            BlockTableRecord ownerSpace,
            ObjectId targetLayerId,
            List<ObjectId> createdIds)
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

        private static void TrimOffsetLineIntersections(
            List<Line> lines,
            double thickness)
        {
            if (lines.Count < 2)
                return;

            Point3d?[] snappedPoints = new Point3d?[lines.Count * 2];
            double[] bestDistances = new double[lines.Count * 2];
            for (int index = 0; index < bestDistances.Length; index++)
                bestDistances[index] = double.PositiveInfinity;

            double cornerJoinDistance = Math.Max(
                MinimumConnectionTolerance * 1000.0,
                Math.Abs(thickness) * MaximumWallWidthFactor);
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

        private static bool TryGetLineIntersection(
            Line first,
            Line second,
            out Point3d intersection,
            out double firstParameter,
            out double secondParameter)
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

        private static bool IsIntersectionNearEndpoint(
            Line line,
            Point3d intersection,
            double maximumJoinDistance)
        {
            double endpointDistance = Math.Min(
                line.StartPoint.DistanceTo(intersection),
                line.EndPoint.DistanceTo(intersection));
            double lineLength = line.StartPoint.DistanceTo(line.EndPoint);
            return endpointDistance <= GetEndpointJoinAllowance(lineLength, maximumJoinDistance);
        }

        private static void StoreTrimCandidate(
            Line line,
            int lineIndex,
            Point3d intersection,
            double maximumJoinDistance,
            Point3d?[] snappedPoints,
            double[] bestDistances)
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

        private static ObjectId GetTargetLayerId(
            Transaction transaction,
            Database database,
            string targetLayerName)
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

        private static void StoreNearestSnap(
            int endpointIndex,
            Point3d candidate,
            double distance,
            Point3d?[] snappedPoints,
            double[] bestDistances)
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

        private static void ValidateArguments(
            Database database,
            double thickness,
            string targetLayerName)
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
                entity?.Dispose();
        }

    }
}
