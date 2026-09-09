using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.Model;

namespace Tools.VinaCad.Helper.Helper
{
    public static class OpenDoorHelper
    {
        private const double Tolerance = 0.001;
        private const double ParallelDotTolerance = 0.002;
        private const string DoorAppName = "VINACAD_WALL_DOOR";
        private const string DoorLayerName = "Door";
        private const string DoorAttributeTag = "A";

        public static ObjectId CreateOpening(Database database, ObjectId selectedWallId, Point3d pickedPoint, DoorStyleSelection selection)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (selection == null || selection.Style == null) throw new ArgumentNullException(nameof(selection));
            if (selection.Width <= Tolerance) throw new ArgumentOutOfRangeException(nameof(selection), "Chiều rộng lỗ mở phải lớn hơn 0.");
            if (selection.Height <= Tolerance) throw new ArgumentOutOfRangeException(nameof(selection), "Chiều cao cửa phải lớn hơn 0.");
            if (selection.WallThickness <= Tolerance) throw new ArgumentOutOfRangeException(nameof(selection), "Chiều dày tường phải lớn hơn 0.");
            if (selection.UseEdgeDistance && selection.EdgeDistance < 0)
                throw new ArgumentOutOfRangeException(nameof(selection), "Khoảng cách từ mép không được âm.");

            ObjectId blockDefinitionId = GetOrImportDoorAsset(database, selection.Style);
            using Transaction transaction = database.TransactionManager.StartTransaction();
            BlockTableRecord currentSpace = (BlockTableRecord)transaction.GetObject(database.CurrentSpaceId, OpenMode.ForWrite);
            ObjectId doorLayerId = EnsureDoorLayer(transaction, database);
            NormalizeDoorBlockColors(transaction, blockDefinitionId);
            ResolveWallPair(
                database,
                transaction,
                currentSpace,
                selectedWallId,
                pickedPoint,
                selection.WallThickness,
                out Line selectedWall,
                out Line pairedWall);

            Vector3d direction = selectedWall.EndPoint - selectedWall.StartPoint;
            if (direction.Length <= Tolerance) throw new InvalidOperationException("Đoạn tường được chọn quá ngắn.");
            direction = direction.GetNormal();
            Point3d origin = selectedWall.StartPoint;

            GetStationInterval(selectedWall, origin, direction, out double selectedStart, out double selectedEnd);
            GetStationInterval(pairedWall, origin, direction, out double pairedStart, out double pairedEnd);
            double availableStart = Math.Max(selectedStart, pairedStart);
            double availableEnd = Math.Min(selectedEnd, pairedEnd);
            double centerStation = (pickedPoint - origin).DotProduct(direction);
            Point3d selectedProjection = PointAtStation(selectedWall, origin, direction, centerStation);
            Point3d pairedProjection = PointAtStation(pairedWall, origin, direction, centerStation);
            double actualWallThickness = selectedProjection.DistanceTo(pairedProjection);
            double edgeClearance = Math.Max(100.0, actualWallThickness);
            ResolveOpeningInterval(
                availableStart,
                availableEnd,
                centerStation,
                selection,
                out double openingStartStation,
                out double openingEndStation);

            double boundaryClearance = selection.UseEdgeDistance ? 0.0 : edgeClearance;
            if (openingStartStation < availableStart + boundaryClearance || openingEndStation > availableEnd - boundaryClearance)
                throw new InvalidOperationException("Không đủ chiều dài tường để đặt lỗ mở tại vị trí đã chọn.");

            Point3d selectedCutStart = PointAtStation(selectedWall, origin, direction, openingStartStation);
            Point3d selectedCutEnd = PointAtStation(selectedWall, origin, direction, openingEndStation);
            Point3d pairedCutStart = PointAtStation(pairedWall, origin, direction, openingStartStation);
            Point3d pairedCutEnd = PointAtStation(pairedWall, origin, direction, openingEndStation);
            Point3d centerStart = MidPoint(selectedCutStart, pairedCutStart);
            Point3d centerEnd = MidPoint(selectedCutEnd, pairedCutEnd);
            string segmentId = DrawWallHelper.GetWallSegmentId(selectedWall) ?? string.Empty;
            if (OverlapsExistingOpening(transaction, currentSpace, segmentId, centerStart, centerEnd))
                throw new InvalidOperationException("Vị trí này chồng lên cửa đi đã đặt trước đó.");
            Point3d clearanceStart = MidPoint(
                PointAtStation(selectedWall, origin, direction, openingStartStation - edgeClearance),
                PointAtStation(pairedWall, origin, direction, openingStartStation - edgeClearance));
            Point3d clearanceEnd = MidPoint(
                PointAtStation(selectedWall, origin, direction, openingEndStation + edgeClearance),
                PointAtStation(pairedWall, origin, direction, openingEndStation + edgeClearance));
            if (CrossesAnotherWall(transaction, currentSpace, selectedWall, pairedWall, clearanceStart, clearanceEnd))
                throw new InvalidOperationException("Lỗ mở quá gần góc hoặc giao tường T/X.");

            SplitWallLine(database, transaction, currentSpace, selectedWall, selectedCutStart, selectedCutEnd);
            SplitWallLine(database, transaction, currentSpace, pairedWall, pairedCutStart, pairedCutEnd);
            AppendCap(database, transaction, currentSpace, selectedWall.LayerId, selectedCutStart, pairedCutStart);
            AppendCap(database, transaction, currentSpace, selectedWall.LayerId, selectedCutEnd, pairedCutEnd);

            GetAssetHorizontalBounds(
                transaction,
                blockDefinitionId,
                out double sourceMinX,
                out double sourceMaxX,
                out double sourceWidth,
                out double? sourceHingeY,
                out double sourceLabelY);
            AttributeDefinition doorAttribute = EnsureDoorAttributeDefinition(
                transaction,
                blockDefinitionId,
                string.Empty,
                new Point3d((sourceMinX + sourceMaxX) * 0.5, sourceLabelY, 0.0),
                sourceWidth * 0.08);
            double assetScale = selection.Width / sourceWidth;
            double scaleX = selection.ReverseAlongWall ? -assetScale : assetScale;
            double scaleY = selection.MirrorAcrossWall ? -assetScale : assetScale;
            Vector3d normal = new Vector3d(-direction.Y, direction.X, 0.0);
            Point3d targetRoot = centerStart;
            double sourceRootX = selection.ReverseAlongWall ? sourceMaxX : sourceMinX;
            Point3d insertionPoint = targetRoot
                - direction * (sourceRootX * scaleX)
                - normal * (sourceHingeY.GetValueOrDefault() * scaleY);
            BlockReference blockReference = new BlockReference(insertionPoint, blockDefinitionId)
            {
                LayerId = doorLayerId,
                ColorIndex = 256,
                Rotation = Math.Atan2(direction.Y, direction.X),
                ScaleFactors = new Scale3d(scaleX, scaleY, assetScale)
            };
            currentSpace.AppendEntity(blockReference);
            transaction.AddNewlyCreatedDBObject(blockReference, true);
            AddDoorAttribute(transaction, blockReference, doorAttribute, string.Empty);
            TagOpening(transaction, database, blockReference, segmentId, selection, centerStart, centerEnd);

            selectedWall.UpgradeOpen();
            pairedWall.UpgradeOpen();
            selectedWall.Erase();
            pairedWall.Erase();
            transaction.Commit();
            return blockReference.ObjectId;
        }

        private static void ResolveOpeningInterval(
            double availableStart,
            double availableEnd,
            double pickedStation,
            DoorStyleSelection selection,
            out double openingStart,
            out double openingEnd)
        {
            if (selection.PlaceAtWallCenter)
            {
                double wallCenter = (availableStart + availableEnd) * 0.5;
                openingStart = wallCenter - selection.Width * 0.5;
                openingEnd = wallCenter + selection.Width * 0.5;
                return;
            }

            if (!selection.UseEdgeDistance)
            {
                openingStart = pickedStation - selection.Width * 0.5;
                openingEnd = pickedStation + selection.Width * 0.5;
                return;
            }

            bool useStartEdge = Math.Abs(pickedStation - availableStart) <= Math.Abs(availableEnd - pickedStation);
            if (useStartEdge)
            {
                openingStart = availableStart + selection.EdgeDistance;
                openingEnd = openingStart + selection.Width;
            }
            else
            {
                openingEnd = availableEnd - selection.EdgeDistance;
                openingStart = openingEnd - selection.Width;
            }
        }

        private static void ResolveWallPair(
            Database database,
            Transaction transaction,
            BlockTableRecord currentSpace,
            ObjectId selectedWallId,
            Point3d pickedPoint,
            double expectedThickness,
            out Line selectedWall,
            out Line pairedWall)
        {
            DBObject selectedObject = transaction.GetObject(selectedWallId, OpenMode.ForRead);
            if (selectedObject is BlockReference blockReference)
            {
                ExplodeWallBlock(database, transaction, currentSpace, blockReference, pickedPoint, expectedThickness, out selectedWall, out pairedWall);
                return;
            }

            if (selectedObject is not Entity selectedEntity || !TryGetWallFace(selectedEntity, out _, out _))
                throw new InvalidOperationException("Hãy chọn Line, Polyline thẳng hoặc block tường hợp lệ.");
            if (selectedEntity is Line selectedLine && DrawWallHelper.IsWallCap(selectedLine))
                throw new InvalidOperationException("Đường được chọn là nét bịt đầu tường, không phải mặt tường.");

            Entity pairedEntity = FindPairedWall(transaction, currentSpace, selectedEntity, pickedPoint, expectedThickness)
                ?? throw new InvalidOperationException("Không tìm thấy mặt tường song song tương ứng. Hãy chọn tường được tạo bởi WW.");
            selectedWall = MaterializeWallLine(database, transaction, currentSpace, selectedEntity);
            pairedWall = MaterializeWallLine(database, transaction, currentSpace, pairedEntity);
        }

        private static Entity? FindPairedWall(Transaction transaction, BlockTableRecord currentSpace, Entity selectedWall, Point3d pickedPoint, double expectedThickness)
        {
            if (!TryGetWallFace(selectedWall, out Point3d selectedStartPoint, out Point3d selectedEndPoint))
                return null;
            Vector3d selectedVector = selectedEndPoint - selectedStartPoint;
            if (selectedVector.Length <= Tolerance) return null;
            Vector3d direction = selectedVector.GetNormal();
            Line? selectedLine = selectedWall as Line;
            string segmentId = selectedLine == null ? string.Empty : DrawWallHelper.GetWallSegmentId(selectedLine) ?? string.Empty;
            string side = selectedLine == null ? string.Empty : DrawWallHelper.GetWallSideMarker(selectedLine) ?? string.Empty;
            double pickedStation = (pickedPoint - selectedStartPoint).DotProduct(direction);
            double maximumDistance = Math.Max(expectedThickness * 1.5, Tolerance * 100.0);
            Entity? best = null;
            double bestScore = double.PositiveInfinity;

            foreach (ObjectId objectId in currentSpace)
            {
                if (objectId == selectedWall.ObjectId || transaction.GetObject(objectId, OpenMode.ForRead) is not Entity candidate || candidate.IsErased ||
                    !TryGetWallFace(candidate, out Point3d candidateStartPoint, out Point3d candidateEndPoint) ||
                    candidate is Line candidateLine && DrawWallHelper.IsWallCap(candidateLine))
                    continue;
                Vector3d candidateVector = candidateEndPoint - candidateStartPoint;
                if (candidateVector.Length <= Tolerance || Math.Abs(direction.DotProduct(candidateVector.GetNormal())) < 1.0 - ParallelDotTolerance)
                    continue;

                string candidateSegmentId = candidate is Line line ? DrawWallHelper.GetWallSegmentId(line) ?? string.Empty : string.Empty;
                string candidateSide = candidate is Line wallLine ? DrawWallHelper.GetWallSideMarker(wallLine) ?? string.Empty : string.Empty;
                if (!string.IsNullOrEmpty(segmentId) && (!string.Equals(segmentId, candidateSegmentId, StringComparison.Ordinal) || string.Equals(side, candidateSide, StringComparison.Ordinal)))
                    continue;
                if (string.IsNullOrEmpty(segmentId) && candidate.LayerId != selectedWall.LayerId)
                    continue;

                GetStationInterval(candidateStartPoint, candidateEndPoint, selectedStartPoint, direction, out double start, out double end);
                if (pickedStation < start - Tolerance || pickedStation > end + Tolerance)
                    continue;
                Point3d selectedProjection = selectedStartPoint + direction * pickedStation;
                Point3d candidateProjection = PointAtStation(candidateStartPoint, candidateEndPoint, selectedStartPoint, direction, pickedStation);
                double distance = selectedProjection.DistanceTo(candidateProjection);
                if (distance <= Tolerance || string.IsNullOrEmpty(segmentId) && distance > maximumDistance)
                    continue;

                double score = string.IsNullOrEmpty(segmentId) ? Math.Abs(distance - expectedThickness) : distance;
                if (score >= bestScore) continue;
                best = candidate;
                bestScore = score;
            }

            return best;
        }

        private static bool TryGetWallFace(Entity entity, out Point3d start, out Point3d end)
        {
            if (entity is Line line)
            {
                start = line.StartPoint;
                end = line.EndPoint;
                return start.DistanceTo(end) > Tolerance;
            }

            if (entity is Polyline polyline && !polyline.Closed && polyline.NumberOfVertices == 2 && Math.Abs(polyline.GetBulgeAt(0)) <= Tolerance)
            {
                start = polyline.GetPoint3dAt(0);
                end = polyline.GetPoint3dAt(1);
                return start.DistanceTo(end) > Tolerance;
            }

            start = Point3d.Origin;
            end = Point3d.Origin;
            return false;
        }

        private static Line MaterializeWallLine(Database database, Transaction transaction, BlockTableRecord currentSpace, Entity source)
        {
            if (source is Line line) return line;
            if (!TryGetWallFace(source, out Point3d start, out Point3d end))
                throw new InvalidOperationException("Polyline tường phải là một đoạn thẳng gồm đúng 2 đỉnh.");

            Line replacement = CreateLineLike(source, start, end);
            currentSpace.AppendEntity(replacement);
            transaction.AddNewlyCreatedDBObject(replacement, true);
            if (source.XData != null)
                replacement.XData = new ResultBuffer(source.XData.AsArray());
            source.UpgradeOpen();
            source.Erase();
            return replacement;
        }

        private static Line CreateLineLike(Entity source, Point3d start, Point3d end)
        {
            return new Line(start, end)
            {
                LayerId = source.LayerId,
                Color = source.Color,
                LineWeight = source.LineWeight,
                LinetypeId = source.LinetypeId,
                LinetypeScale = source.LinetypeScale,
                Transparency = source.Transparency
            };
        }

        private static void ExplodeWallBlock(
            Database database,
            Transaction transaction,
            BlockTableRecord currentSpace,
            BlockReference blockReference,
            Point3d pickedPoint,
            double expectedThickness,
            out Line selectedWall,
            out Line pairedWall)
        {
            DBObjectCollection exploded = new DBObjectCollection();
            blockReference.Explode(exploded);
            List<Entity> entities = exploded.Cast<DBObject>().OfType<Entity>().ToList();
            if (!TrySelectBlockFacePair(entities, pickedPoint, expectedThickness, out Entity? firstFace, out Entity? secondFace))
            {
                foreach (DBObject item in exploded) item.Dispose();
                throw new InvalidOperationException("Block tường phải chứa hai mặt Line/Polyline thẳng, song song và đúng chiều dày.");
            }

            selectedWall = null!;
            pairedWall = null!;
            foreach (DBObject item in exploded)
            {
                if (item is not Entity entity)
                {
                    item.Dispose();
                    continue;
                }

                if (ReferenceEquals(entity, firstFace) || ReferenceEquals(entity, secondFace))
                {
                    if (!TryGetWallFace(entity, out Point3d start, out Point3d end))
                    {
                        entity.Dispose();
                        continue;
                    }

                    Line faceLine = CreateLineLike(entity, start, end);
                    faceLine.LayerId = blockReference.LayerId;
                    currentSpace.AppendEntity(faceLine);
                    transaction.AddNewlyCreatedDBObject(faceLine, true);
                    entity.Dispose();

                    if (ReferenceEquals(entity, firstFace)) selectedWall = faceLine;
                    else pairedWall = faceLine;
                    continue;
                }

                currentSpace.AppendEntity(entity);
                transaction.AddNewlyCreatedDBObject(entity, true);
            }

            blockReference.UpgradeOpen();
            blockReference.Erase();
        }

        private static bool TrySelectBlockFacePair(
            IReadOnlyList<Entity> entities,
            Point3d pickedPoint,
            double expectedThickness,
            out Entity? firstFace,
            out Entity? secondFace)
        {
            firstFace = null;
            secondFace = null;
            double bestScore = double.PositiveInfinity;
            double maximumThickness = Math.Max(expectedThickness * 2.0, 100.0);

            for (int firstIndex = 0; firstIndex < entities.Count; firstIndex++)
            {
                if (!TryGetWallFace(entities[firstIndex], out Point3d firstStart, out Point3d firstEnd)) continue;
                Vector3d firstVector = firstEnd - firstStart;
                Vector3d direction = firstVector.GetNormal();
                for (int secondIndex = firstIndex + 1; secondIndex < entities.Count; secondIndex++)
                {
                    if (!TryGetWallFace(entities[secondIndex], out Point3d secondStart, out Point3d secondEnd)) continue;
                    Vector3d secondVector = secondEnd - secondStart;
                    if (Math.Abs(direction.DotProduct(secondVector.GetNormal())) < 1.0 - ParallelDotTolerance) continue;

                    GetStationInterval(secondStart, secondEnd, firstStart, direction, out double secondMin, out double secondMax);
                    double overlapStart = Math.Max(0.0, secondMin);
                    double overlapEnd = Math.Min(firstVector.Length, secondMax);
                    double overlap = overlapEnd - overlapStart;
                    if (overlap <= Tolerance) continue;
                    double sampleStation = Math.Max(overlapStart, Math.Min(overlapEnd, (pickedPoint - firstStart).DotProduct(direction)));
                    Point3d onFirst = firstStart + direction * sampleStation;
                    Point3d onSecond = PointAtStation(secondStart, secondEnd, firstStart, direction, sampleStation);
                    double thickness = onFirst.DistanceTo(onSecond);
                    if (thickness <= Tolerance || thickness > maximumThickness) continue;

                    double pickDistance = Math.Min(DistanceToSegment(pickedPoint, firstStart, firstEnd), DistanceToSegment(pickedPoint, secondStart, secondEnd));
                    double score = Math.Abs(thickness - expectedThickness) + pickDistance * 0.1 - overlap * 0.001;
                    if (score >= bestScore) continue;
                    bestScore = score;
                    firstFace = entities[firstIndex];
                    secondFace = entities[secondIndex];
                }
            }

            return firstFace != null && secondFace != null;
        }

        private static double DistanceToSegment(Point3d point, Point3d start, Point3d end)
        {
            Vector3d vector = end - start;
            double lengthSquared = vector.DotProduct(vector);
            if (lengthSquared <= Tolerance) return point.DistanceTo(start);
            double parameter = Math.Max(0.0, Math.Min(1.0, (point - start).DotProduct(vector) / lengthSquared));
            return point.DistanceTo(start + vector * parameter);
        }

        private static Point3d MidPoint(Point3d first, Point3d second)
        {
            return new Point3d(
                (first.X + second.X) * 0.5,
                (first.Y + second.Y) * 0.5,
                (first.Z + second.Z) * 0.5);
        }

        private static void GetStationInterval(Line line, Point3d origin, Vector3d direction, out double start, out double end)
        {
            GetStationInterval(line.StartPoint, line.EndPoint, origin, direction, out start, out end);
        }

        private static void GetStationInterval(Point3d lineStart, Point3d lineEnd, Point3d origin, Vector3d direction, out double start, out double end)
        {
            double first = (lineStart - origin).DotProduct(direction);
            double second = (lineEnd - origin).DotProduct(direction);
            start = Math.Min(first, second);
            end = Math.Max(first, second);
        }

        private static Point3d PointAtStation(Line line, Point3d origin, Vector3d direction, double station)
        {
            return PointAtStation(line.StartPoint, line.EndPoint, origin, direction, station);
        }

        private static Point3d PointAtStation(Point3d lineStart, Point3d lineEnd, Point3d origin, Vector3d direction, double station)
        {
            double startStation = (lineStart - origin).DotProduct(direction);
            double endStation = (lineEnd - origin).DotProduct(direction);
            double stationSpan = endStation - startStation;
            if (Math.Abs(stationSpan) <= Tolerance) return lineStart;
            double parameter = (station - startStation) / stationSpan;
            return lineStart + (lineEnd - lineStart) * parameter;
        }

        private static void SplitWallLine(Database database, Transaction transaction, BlockTableRecord currentSpace, Line source, Point3d firstCut, Point3d secondCut)
        {
            Vector3d sourceVector = source.EndPoint - source.StartPoint;
            double lengthSquared = sourceVector.DotProduct(sourceVector);
            double firstParameter = (firstCut - source.StartPoint).DotProduct(sourceVector) / lengthSquared;
            double secondParameter = (secondCut - source.StartPoint).DotProduct(sourceVector) / lengthSquared;
            Point3d nearCut = firstParameter <= secondParameter ? firstCut : secondCut;
            Point3d farCut = firstParameter <= secondParameter ? secondCut : firstCut;
            AppendWallFragment(database, transaction, currentSpace, source, source.StartPoint, nearCut);
            AppendWallFragment(database, transaction, currentSpace, source, farCut, source.EndPoint);
        }

        private static void AppendWallFragment(Database database, Transaction transaction, BlockTableRecord currentSpace, Line source, Point3d start, Point3d end)
        {
            if (start.DistanceTo(end) <= Tolerance) return;
            Line fragment = new Line(start, end)
            {
                LayerId = source.LayerId,
                Color = source.Color,
                LineWeight = source.LineWeight,
                LinetypeId = source.LinetypeId,
                LinetypeScale = source.LinetypeScale,
                Transparency = source.Transparency
            };
            currentSpace.AppendEntity(fragment);
            transaction.AddNewlyCreatedDBObject(fragment, true);
            DrawWallHelper.CopyWallMetadata(transaction, database, source, fragment);
        }

        private static void AppendCap(Database database, Transaction transaction, BlockTableRecord currentSpace, ObjectId layerId, Point3d start, Point3d end)
        {
            if (start.DistanceTo(end) <= Tolerance) return;
            Line cap = new Line(start, end) { LayerId = layerId };
            currentSpace.AppendEntity(cap);
            transaction.AddNewlyCreatedDBObject(cap, true);
            DrawWallHelper.TagAsCap(transaction, database, cap);
        }

        private static bool CrossesAnotherWall(Transaction transaction, BlockTableRecord currentSpace, Line selectedWall, Line pairedWall, Point3d openingStart, Point3d openingEnd)
        {
            foreach (ObjectId objectId in currentSpace)
            {
                if (objectId == selectedWall.ObjectId || objectId == pairedWall.ObjectId ||
                    transaction.GetObject(objectId, OpenMode.ForRead) is not Entity entity || entity.IsErased ||
                    !TryGetWallFace(entity, out Point3d lineStart, out Point3d lineEnd) ||
                    entity is Line cap && DrawWallHelper.IsWallCap(cap))
                    continue;
                if (entity.LayerId != selectedWall.LayerId &&
                    (entity is not Line wallLine || string.IsNullOrEmpty(DrawWallHelper.GetWallSegmentId(wallLine))))
                    continue;
                if (TryGetFiniteIntersection(openingStart, openingEnd, lineStart, lineEnd, out Point3d intersection) &&
                    intersection.DistanceTo(openingStart) > Tolerance && intersection.DistanceTo(openingEnd) > Tolerance)
                    return true;
            }
            return false;
        }

        private static bool TryGetFiniteIntersection(Point3d firstStart, Point3d firstEnd, Point3d secondStart, Point3d secondEnd, out Point3d intersection)
        {
            intersection = Point3d.Origin;
            double firstDx = firstEnd.X - firstStart.X;
            double firstDy = firstEnd.Y - firstStart.Y;
            double secondDx = secondEnd.X - secondStart.X;
            double secondDy = secondEnd.Y - secondStart.Y;
            double denominator = firstDx * secondDy - firstDy * secondDx;
            if (Math.Abs(denominator) <= Tolerance * Math.Max(1.0, Math.Sqrt((firstDx * firstDx + firstDy * firstDy) * (secondDx * secondDx + secondDy * secondDy))))
                return false;
            double dx = secondStart.X - firstStart.X;
            double dy = secondStart.Y - firstStart.Y;
            double firstParameter = (dx * secondDy - dy * secondDx) / denominator;
            double secondParameter = (dx * firstDy - dy * firstDx) / denominator;
            if (firstParameter < -Tolerance || firstParameter > 1.0 + Tolerance || secondParameter < -Tolerance || secondParameter > 1.0 + Tolerance)
                return false;
            intersection = firstStart + (firstEnd - firstStart) * firstParameter;
            return true;
        }

        private static bool OverlapsExistingOpening(Transaction transaction, BlockTableRecord currentSpace, string segmentId, Point3d start, Point3d end)
        {
            foreach (ObjectId objectId in currentSpace)
            {
                if (transaction.GetObject(objectId, OpenMode.ForRead) is not BlockReference blockReference || blockReference.IsErased ||
                    !TryReadOpening(blockReference, out string existingSegmentId, out Point3d existingStart, out Point3d existingEnd))
                    continue;
                if (!string.IsNullOrEmpty(segmentId) && !string.Equals(segmentId, existingSegmentId, StringComparison.Ordinal))
                    continue;
                if (CollinearIntervalsOverlap(start, end, existingStart, existingEnd))
                    return true;
            }
            return false;
        }

        private static bool CollinearIntervalsOverlap(Point3d firstStart, Point3d firstEnd, Point3d secondStart, Point3d secondEnd)
        {
            Vector3d direction = firstEnd - firstStart;
            if (direction.Length <= Tolerance) return false;
            direction = direction.GetNormal();
            Vector3d secondDirection = secondEnd - secondStart;
            if (secondDirection.Length <= Tolerance || Math.Abs(direction.DotProduct(secondDirection.GetNormal())) < 1.0 - ParallelDotTolerance)
                return false;
            Vector3d normal = new Vector3d(-direction.Y, direction.X, 0.0);
            if (Math.Abs((secondStart - firstStart).DotProduct(normal)) > Tolerance * 10.0)
                return false;
            double firstLength = firstStart.DistanceTo(firstEnd);
            double secondA = (secondStart - firstStart).DotProduct(direction);
            double secondB = (secondEnd - firstStart).DotProduct(direction);
            return Math.Max(0.0, Math.Min(secondA, secondB)) < Math.Min(firstLength, Math.Max(secondA, secondB)) - Tolerance;
        }

        private static ObjectId GetOrImportDoorAsset(Database database, DoorStyleModel style)
        {
            if (style == null || string.IsNullOrWhiteSpace(style.AssetPath) || !File.Exists(style.AssetPath))
                throw new FileNotFoundException("Không tìm thấy bản vẽ cửa đã chọn.", style?.AssetPath);
            string safeName = new string(style.Id.Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray());
            string blockName = "DOOR_ASSET_" + safeName;

            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                BlockTable blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
                if (blockTable.Has(blockName)) return blockTable[blockName];
            }

            try
            {
                using Database sourceDatabase = new Database(false, true);
                sourceDatabase.ReadDwgFile(style.AssetPath, FileShare.Read, true, null);
                ObjectId importedId = database.Insert(blockName, sourceDatabase, true);
                if (importedId.IsNull) throw new InvalidOperationException("Không thể import bản vẽ cửa.");
                return importedId;
            }
            catch (System.Exception exception)
            {
                throw new InvalidOperationException($"Không thể đọc asset cửa {Path.GetFileName(style.AssetPath)}: {exception.Message}", exception);
            }
        }

        private static void GetAssetHorizontalBounds(
            Transaction transaction,
            ObjectId blockDefinitionId,
            out double minimumX,
            out double maximumX,
            out double width,
            out double? sourceHingeY,
            out double sourceLabelY)
        {
            BlockTableRecord definition = (BlockTableRecord)transaction.GetObject(blockDefinitionId, OpenMode.ForRead);
            List<(Extents3d Bounds, double? HingeY)> curves = new List<(Extents3d, double?)>();
            List<Extents3d> details = new List<Extents3d>();
            foreach (ObjectId objectId in definition)
            {
                if (transaction.GetObject(objectId, OpenMode.ForRead) is not Entity entity) continue;
                CollectDoorAssetBounds(entity, curves, details, 0);
            }
            List<(Extents3d Bounds, double? HingeY)> significantCurves = new List<(Extents3d, double?)>();
            if (curves.Count > 0)
            {
                double largestSpan = curves.Max(item => Math.Max(
                    item.Bounds.MaxPoint.X - item.Bounds.MinPoint.X,
                    item.Bounds.MaxPoint.Y - item.Bounds.MinPoint.Y));
                significantCurves = curves.Where(item => Math.Max(
                    item.Bounds.MaxPoint.X - item.Bounds.MinPoint.X,
                    item.Bounds.MaxPoint.Y - item.Bounds.MinPoint.Y) >= largestSpan * 0.45).ToList();
            }
            List<Extents3d> bounds = significantCurves.Select(item => item.Bounds).ToList();
            if (bounds.Count > 0)
            {
                double curveMinimumX = bounds.Min(item => item.MinPoint.X);
                double curveMaximumX = bounds.Max(item => item.MaxPoint.X);
                double nearbyDistance = (curveMaximumX - curveMinimumX) * 0.25;
                bounds.AddRange(details.Where(item =>
                    item.MaxPoint.X >= curveMinimumX - nearbyDistance &&
                    item.MinPoint.X <= curveMaximumX + nearbyDistance));
            }
            else
            {
                bounds.AddRange(details);
            }
            if (bounds.Count == 0)
                throw new InvalidOperationException("Asset cửa không có cung hoặc hình học cánh cửa hợp lệ.");

            minimumX = bounds.Min(item => item.MinPoint.X);
            maximumX = bounds.Max(item => item.MaxPoint.X);
            width = maximumX - minimumX;
            if (double.IsInfinity(minimumX) || double.IsNaN(width) || width <= Tolerance)
                throw new InvalidOperationException("Asset cửa không có hình học hợp lệ theo trục X.");

            List<double> hingeCoordinates = significantCurves
                .Where(item => item.HingeY.HasValue)
                .Select(item => item.HingeY!.Value)
                .ToList();
            sourceHingeY = hingeCoordinates.Count > 0 ? hingeCoordinates.Average() : null;
            sourceLabelY = significantCurves.Count > 0
                ? (significantCurves.Min(item => item.Bounds.MinPoint.Y) +
                   significantCurves.Max(item => item.Bounds.MaxPoint.Y)) * 0.5
                : 0.0;
        }

        private static void CollectDoorAssetBounds(
            Entity entity,
            List<(Extents3d Bounds, double? HingeY)> curves,
            List<Extents3d> details,
            int depth)
        {
            try
            {
                Extents3d extents = entity.GeometricExtents;
                if (entity is Arc arc)
                {
                    curves.Add((extents, arc.Center.Y));
                    return;
                }
                if (entity is Circle)
                {
                    curves.Add((extents, null));
                    return;
                }
                if (entity is Line line &&
                    Math.Abs(line.EndPoint.Y - line.StartPoint.Y) >
                    Math.Abs(line.EndPoint.X - line.StartPoint.X) * 0.05)
                {
                    details.Add(extents);
                    return;
                }
            }
            catch
            {
                return;
            }

            if (depth >= 4) return;
            DBObjectCollection exploded = new DBObjectCollection();
            try
            {
                entity.Explode(exploded);
            }
            catch
            {
                return;
            }
            foreach (DBObject item in exploded)
            {
                try
                {
                    if (item is Entity child) CollectDoorAssetBounds(child, curves, details, depth + 1);
                }
                finally
                {
                    item.Dispose();
                }
            }
        }

        private static ObjectId EnsureDoorLayer(Transaction transaction, Database database)
        {
            LayerTable table = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
            LayerTableRecord layer;
            if (table.Has(DoorLayerName))
            {
                layer = (LayerTableRecord)transaction.GetObject(table[DoorLayerName], OpenMode.ForWrite);
            }
            else
            {
                table.UpgradeOpen();
                layer = new LayerTableRecord { Name = DoorLayerName };
                table.Add(layer);
                transaction.AddNewlyCreatedDBObject(layer, true);
            }

            layer.Color = Teigha.Colors.Color.FromColorIndex(Teigha.Colors.ColorMethod.ByAci, 2);
            layer.IsOff = false;
            layer.IsFrozen = false;
            layer.IsLocked = false;
            return layer.ObjectId;
        }

        private static void NormalizeDoorBlockColors(Transaction transaction, ObjectId blockDefinitionId)
        {
            BlockTableRecord definition = (BlockTableRecord)transaction.GetObject(blockDefinitionId, OpenMode.ForRead);
            LayerTable layers = (LayerTable)transaction.GetObject(definition.Database.LayerTableId, OpenMode.ForRead);
            ObjectId layerZeroId = layers["0"];
            foreach (ObjectId objectId in definition)
            {
                if (transaction.GetObject(objectId, OpenMode.ForWrite) is Entity entity)
                {
                    entity.LayerId = layerZeroId;
                    entity.ColorIndex = 0;
                }
            }
        }

        private static AttributeDefinition EnsureDoorAttributeDefinition(
            Transaction transaction,
            ObjectId blockDefinitionId,
            string defaultValue,
            Point3d position,
            double height)
        {
            BlockTableRecord definition = (BlockTableRecord)transaction.GetObject(blockDefinitionId, OpenMode.ForRead);
            foreach (ObjectId objectId in definition)
            {
                if (transaction.GetObject(objectId, OpenMode.ForRead) is AttributeDefinition existing &&
                    string.Equals(existing.Tag, DoorAttributeTag, StringComparison.OrdinalIgnoreCase))
                {
                    existing.UpgradeOpen();
                    existing.Invisible = false;
                    existing.TextString = defaultValue;
                    existing.Position = position;
                    existing.Height = height;
                    return existing;
                }
            }

            definition.UpgradeOpen();
            AttributeDefinition attribute = new AttributeDefinition
            {
                Position = position,
                Tag = DoorAttributeTag,
                TextString = defaultValue,
                Height = height,
                Invisible = false,
                Constant = false
            };
            attribute.SetDatabaseDefaults(definition.Database);
            definition.AppendEntity(attribute);
            transaction.AddNewlyCreatedDBObject(attribute, true);
            return attribute;
        }

        private static void AddDoorAttribute(
            Transaction transaction,
            BlockReference blockReference,
            AttributeDefinition definition,
            string value)
        {
            AttributeReference attribute = new AttributeReference();
            attribute.SetAttributeFromBlock(definition, blockReference.BlockTransform);
            attribute.TextString = value;
            attribute.Invisible = false;
            blockReference.AttributeCollection.AppendAttribute(attribute);
            transaction.AddNewlyCreatedDBObject(attribute, true);
        }

        private static void EnsureRegApp(Transaction transaction, Database database)
        {
            RegAppTable table = (RegAppTable)transaction.GetObject(database.RegAppTableId, OpenMode.ForRead);
            if (table.Has(DoorAppName)) return;
            table.UpgradeOpen();
            RegAppTableRecord record = new RegAppTableRecord { Name = DoorAppName };
            table.Add(record);
            transaction.AddNewlyCreatedDBObject(record, true);
        }

        private static void TagOpening(Transaction transaction, Database database, BlockReference blockReference, string segmentId, DoorStyleSelection selection, Point3d start, Point3d end)
        {
            EnsureRegApp(transaction, database);
            blockReference.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, DoorAppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, segmentId),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, selection.Style.Id),
                new TypedValue((int)DxfCode.ExtendedDataReal, selection.Width),
                new TypedValue((int)DxfCode.ExtendedDataReal, selection.Height),
                new TypedValue((int)DxfCode.ExtendedDataReal, start.X),
                new TypedValue((int)DxfCode.ExtendedDataReal, start.Y),
                new TypedValue((int)DxfCode.ExtendedDataReal, start.Z),
                new TypedValue((int)DxfCode.ExtendedDataReal, end.X),
                new TypedValue((int)DxfCode.ExtendedDataReal, end.Y),
                new TypedValue((int)DxfCode.ExtendedDataReal, end.Z),
                new TypedValue((int)DxfCode.ExtendedDataReal, selection.WallThickness),
                new TypedValue((int)DxfCode.ExtendedDataInteger16, selection.ReverseAlongWall ? 1 : 0),
                new TypedValue((int)DxfCode.ExtendedDataInteger16, selection.MirrorAcrossWall ? 1 : 0));
        }

        private static bool TryReadOpening(BlockReference blockReference, out string segmentId, out Point3d start, out Point3d end)
        {
            segmentId = string.Empty;
            start = Point3d.Origin;
            end = Point3d.Origin;
            try
            {
                ResultBuffer buffer = blockReference.GetXDataForApplication(DoorAppName);
                if (buffer == null) return false;
                List<double> values = new List<double>();
                foreach (TypedValue value in buffer)
                {
                    if (value.TypeCode == (int)DxfCode.ExtendedDataAsciiString && string.IsNullOrEmpty(segmentId))
                        segmentId = value.Value as string ?? string.Empty;
                    else if (value.TypeCode == (int)DxfCode.ExtendedDataReal)
                        values.Add(Convert.ToDouble(value.Value, CultureInfo.InvariantCulture));
                }
                if (values.Count < 8) return false;
                start = new Point3d(values[2], values[3], values[4]);
                end = new Point3d(values[5], values[6], values[7]);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
