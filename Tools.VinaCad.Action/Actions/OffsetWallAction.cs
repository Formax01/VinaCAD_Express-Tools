using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using PrLogTrackingSystem;
using System;
using System.Collections.Generic;
using System.Linq;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.Resources.Definitions;
using Tools.VinaCad.Helper.Helper;
using Tools.VinaCad.Modeling;
using Application = Prima.VinaCAD.ApplicationServices.Application;
using MessageBox = System.Windows.MessageBox;

namespace Tools.VinaCAD.Action.Actions
{
    /// <summary>
    /// Creates a new parallel double-line wall at a centerline-to-centerline
    /// distance. The source wall is never modified by this command.
    /// </summary>
    public class OffsetWallAction
    {
        private const double Tolerance = 0.001;
        private const double ParallelTolerance = 0.002;
        private const double DefaultOffsetDistance = 500.0;

        public void Execute()
        {
            Document? document = Application.DocumentManager.MdiActiveDocument;
            if (document == null) return;

            Editor editor = document.Editor;
            Database database = document.Database;

            try
            {
                PromptDistanceOptions distanceOptions = new PromptDistanceOptions($"\nKhoảng cách offset tường (tâm-tâm) <{DefaultOffsetDistance:0.##}>: ")
                {
                    AllowNegative = false,
                    AllowZero = false,
                    DefaultValue = DefaultOffsetDistance,
                    UseDefaultValue = true
                };
                PromptDoubleResult distanceResult = editor.GetDistance(distanceOptions);
                if (distanceResult.Status != PromptStatus.OK ||
                    distanceResult.Value <= Tolerance)
                    return;

                double offsetDistance = distanceResult.Value;
                PromptKeywordOptions junctionOptions = new PromptKeywordOptions("\nXử lý nút giao [Giữ/Auto] <Giữ>: ")
                {
                    AllowNone = true,
                    AppendKeywordsToMessage = false
                };
                junctionOptions.Keywords.Add("Keep", "Giữ", "Giữ");
                junctionOptions.Keywords.Add("Auto", "Auto", "Auto");
                junctionOptions.Keywords.Default = "Keep";

                PromptResult junctionResult = editor.GetKeywords(junctionOptions);
                if (junctionResult.Status != PromptStatus.OK &&
                    junctionResult.Status != PromptStatus.None)
                    return;

                bool autoHealJunctions =
                    junctionResult.Status == PromptStatus.OK &&
                    string.Equals(
                        junctionResult.StringResult,
                        "Auto",
                        StringComparison.OrdinalIgnoreCase);
                int createdCount = 0;
                editor.WriteMessage($"\nWWO: Khoảng cách tâm-tâm = {offsetDistance:0.##}. " +$"Xử lý nút giao = {(autoHealJunctions ? "Tự động" : "Giữ nguyên")}. " +"Chọn tường rồi chỉ phía offset.");

                while (true)
                {
                    PromptEntityOptions entityOptions = new PromptEntityOptions("\nChọn một mặt tường <Kết thúc>: ");
                    entityOptions.SetRejectMessage("\nWWO chỉ nhận đường thẳng Line của tường.");
                    entityOptions.AddAllowedClass(typeof(Line), true);

                    PromptEntityResult entityResult = editor.GetEntity(entityOptions);
                    if (entityResult.Status != PromptStatus.OK)
                        break;

                    if (!TryReadWallDefinition(
                            database,
                            entityResult.ObjectId,
                            out WallDefinition? preview,
                            out string validationMessage))
                    {
                        editor.WriteMessage($"\nKhông thể offset: {validationMessage}");
                        continue;
                    }

                    PromptPointOptions sideOptions = new PromptPointOptions("\nChọn điểm về phía cần offset: ")
                    {
                        UseBasePoint = true,
                        BasePoint = Midpoint(preview.CenterStart, preview.CenterEnd),
                        AllowArbitraryInput = false
                    };
                    PromptPointResult sideResult = editor.GetPoint(sideOptions);
                    if (sideResult.Status != PromptStatus.OK)
                        break;

                    if (CreateOffsetWall(
                            database,
                            entityResult.ObjectId,
                            sideResult.Value,
                            offsetDistance,
                            autoHealJunctions,
                            out string resultMessage))
                    {
                        createdCount++;
                        editor.UpdateScreen();
                        editor.WriteMessage($"\n{resultMessage}");
                    }
                    else
                    {
                        editor.WriteMessage($"\nKhông thể offset: {resultMessage}");
                    }
                }

                editor.WriteMessage($"\nWWO: Đã tạo {createdCount} tường song song.");
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(OffsetWallAction), ex);
                MessageBox.Show($"Lỗi WWO: {ex.Message}", StringDefinition.TITLE_ERROR);
            }
        }

        private static bool CreateOffsetWall(
            Database database,
            ObjectId selectedId,
            Point3d sidePoint,
            double offsetDistance,
            bool autoHealJunctions,
            out string message)
        {
            message = string.Empty;

            using Transaction transaction = database.TransactionManager.StartTransaction();
            try
            {
                if (!TryReadWallDefinition(
                        transaction,
                        database,
                        selectedId,
                        out WallDefinition? source,
                        out message))
                    return false;

                Vector3d direction = (source.CenterEnd - source.CenterStart).GetNormal();
                Vector3d normal = new Vector3d(-direction.Y, direction.X, 0.0);
                double side = (sidePoint - source.CenterStart).DotProduct(normal);
                if (Math.Abs(side) <= Tolerance)
                {
                    message = "điểm chỉ hướng nằm trên tim tường; hãy chọn rõ một phía.";
                    return false;
                }

                Vector3d offset = normal * (Math.Sign(side) * offsetDistance);
                Point3d newCenterStart = source.CenterStart + offset;
                Point3d newCenterEnd = source.CenterEnd + offset;

                if (!autoHealJunctions)
                {
                    TrimOffsetWallToInsideFace(
                        source,
                        side,
                        direction,
                        offset,
                        ref newCenterStart,
                        ref newCenterEnd);
                }

                DrawWallHelper.CalculateWallLines(
                    newCenterStart,
                    newCenterEnd,
                    source.Thickness,
                    WallAlignment.Center,
                    out Point3d firstStart,
                    out Point3d firstEnd,
                    out Point3d secondStart,
                    out Point3d secondEnd);

                List<Line> modelSpaceLines = ReadModelSpaceLines(
                    transaction,
                    database,
                    includeWallCaps: true);
                List<Line> allLines = modelSpaceLines
                    .Where(line => !DrawWallHelper.IsWallCap(line))
                    .ToList();
                bool startConnected = false;
                bool endConnected = false;
                LineCutRequest? startJunctionCut = null;
                LineCutRequest? endJunctionCut = null;
                HashSet<ObjectId> connectedHostIds = new HashSet<ObjectId>();
                int connectedBranchCount = 0;

                if (autoHealJunctions)
                {
                    // A wall offset can move beyond the old host endpoints by the
                    // full offset distance. Search exactly that corridor so both
                    // faces of the host can be extended into a proper L junction.
                    double endConnectionDistance =
                        offsetDistance + source.Thickness * 2.0;
                    // Giới hạn tìm kiếm theo đúng hành lang từ tường nguồn tới tường
                    // offset. Không dùng bán kính toàn cục, nhưng vẫn cho WW-style
                    // smart snap bắt mọi đầu tường thực sự hướng tới tường mới.
                    double branchConnectionDistance =
                        offsetDistance + source.Thickness * 2.0;
                    startConnected = TryConnectWallEnd(
                        transaction,
                        database,
                        allLines,
                        source.LayerId,
                        source.Thickness,
                        endConnectionDistance,
                        offset,
                        newCenterStart,
                        newCenterEnd,
                        ref firstStart,
                        ref secondStart,
                        out startJunctionCut,
                        out HashSet<ObjectId> startConnectedHostIds);
                    endConnected = TryConnectWallEnd(
                        transaction,
                        database,
                        allLines,
                        source.LayerId,
                        source.Thickness,
                        endConnectionDistance,
                        offset,
                        newCenterEnd,
                        newCenterStart,
                        ref firstEnd,
                        ref secondEnd,
                        out endJunctionCut,
                        out HashSet<ObjectId> endConnectedHostIds);

                    connectedHostIds.UnionWith(startConnectedHostIds);
                    connectedHostIds.UnionWith(endConnectedHostIds);

                    connectedBranchCount = ConnectNearbyWallBranches(
                        transaction,
                        database,
                        allLines,
                        modelSpaceLines
                            .Where(DrawWallHelper.IsWallCap)
                            .ToList(),
                        source.LayerId,
                        source.Thickness,
                        branchConnectionDistance,
                        connectedHostIds,
                        firstStart,
                        firstEnd,
                        secondStart,
                        secondEnd);
                }

                if (WallAlreadyExists(
                        allLines,
                        source.LayerId,
                        firstStart,
                        firstEnd,
                        secondStart,
                        secondEnd))
                {
                    message = "đã tồn tại tường trùng tại vị trí offset.";
                    return false;
                }

                Line selected = (Line)transaction.GetObject(selectedId, OpenMode.ForRead);
                BlockTableRecord owner = (BlockTableRecord)transaction.GetObject(
                    selected.OwnerId,
                    OpenMode.ForWrite);
                List<ObjectId> createdIds = DrawWallHelper.CreateWallLines(
                    transaction,
                    database,
                    owner,
                    source.LayerId,
                    firstStart,
                    firstEnd,
                    secondStart,
                    secondEnd);

                Line firstLine = (Line)transaction.GetObject(createdIds[0], OpenMode.ForWrite);
                Line secondLine = (Line)transaction.GetObject(createdIds[1], OpenMode.ForWrite);
                source.FirstProperties.Apply(firstLine);
                source.SecondProperties.Apply(secondLine);

                int healedIntersectionCount = autoHealJunctions
                    ? HealOffsetWallIntersections(
                        transaction,
                        database,
                        firstLine,
                        secondLine,
                        allLines,
                        source.LayerId,
                        new[] { startJunctionCut, endJunctionCut }
                            .Where(cut => cut != null)
                            .Select(cut => cut!)
                            .ToList(),
                        connectedHostIds)
                    : 0;

                // Create caps from the exact new endpoints. Nearby source walls can
                // no longer interfere with cap detection.
                if (!startConnected)
                {
                    ObjectId startCapId = DrawWallHelper.CreateWallCap(
                        transaction,
                        database,
                        owner,
                        source.LayerId,
                        firstStart,
                        secondStart);
                    Line startCap = (Line)transaction.GetObject(
                        startCapId,
                        OpenMode.ForWrite);
                    source.FirstProperties.Apply(startCap);
                }

                if (!endConnected)
                {
                    ObjectId endCapId = DrawWallHelper.CreateWallCap(
                        transaction,
                        database,
                        owner,
                        source.LayerId,
                        firstEnd,
                        secondEnd);
                    Line endCap = (Line)transaction.GetObject(
                        endCapId,
                        OpenMode.ForWrite);
                    source.FirstProperties.Apply(endCap);
                }

                transaction.Commit();

                message = autoHealJunctions? $"Đã tạo tường song song cách tim {offsetDistance:0.##}. " +$"Đã nối {connectedBranchCount} đầu tường và xóa nét trong " +$"{healedIntersectionCount} vùng giao.": $"Đã tạo nguyên trạng tường song song cách tim " +$"{offsetDistance:0.##}; không xử lý nút giao.";
                return true;
            }
            catch (Exception ex)
            {
                transaction.Abort();
                Logger.Info(nameof(CreateOffsetWall), ex);
                message = ex.Message;
                return false;
            }
        }

        private static bool TryReadWallDefinition(
            Database database,
            ObjectId selectedId,
            out WallDefinition? definition,
            out string message)
        {
            using Transaction transaction = database.TransactionManager.StartTransaction();
            bool result = TryReadWallDefinition(
                transaction,
                database,
                selectedId,
                out definition,
                out message);
            transaction.Commit();
            return result;
        }

        private static bool TryReadWallDefinition(
            Transaction transaction,
            Database database,
            ObjectId selectedId,
            out WallDefinition? definition,
            out string message)
        {
            definition = null;
            message = string.Empty;

            Line? selected = transaction.GetObject(selectedId, OpenMode.ForRead) as Line;
            if (selected == null || selected.IsErased || DrawWallHelper.IsWallCap(selected))
            {
                message = "đối tượng không phải mặt tường hợp lệ.";
                return false;
            }

            List<Line> lines = ReadModelSpaceLines(transaction, database);
            string? segmentId = DrawWallHelper.GetWallSegmentId(selected);
            string? selectedSide = DrawWallHelper.GetWallSideMarker(selected);

            Line? opposite;
            Point3d centerStart;
            Point3d centerEnd;
            Point3d selectedFaceStart;
            Point3d selectedFaceEnd;
            Point3d oppositeFaceStart;
            Point3d oppositeFaceEnd;

            if (!string.IsNullOrEmpty(segmentId))
            {
                if (string.IsNullOrEmpty(selectedSide))
                {
                    message = "metadata mặt tường không đầy đủ.";
                    return false;
                }

                List<Line> segmentMembers = lines.Where(line =>
                    line.LayerId == selected.LayerId &&
                    DrawWallHelper.GetWallSegmentId(line) == segmentId &&
                    AreParallel(selected, line)).ToList();
                opposite = segmentMembers
                    .Where(line =>
                        line.ObjectId != selected.ObjectId &&
                        !string.IsNullOrEmpty(DrawWallHelper.GetWallSideMarker(line)) &&
                        DrawWallHelper.GetWallSideMarker(line) != selectedSide)
                    .OrderBy(line => DistanceToInfiniteLine(line.StartPoint, selected))
                    .FirstOrDefault();
                if (opposite == null)
                {
                    message = "không tìm thấy mặt đối diện cùng SegmentId.";
                    return false;
                }

                // CleanupIntersections của WW có thể chia một mặt tường thành
                // nhiều mảnh nhưng giữ nguyên SegmentId. Không được ghép line đang
                // chọn với một mảnh đối diện ngẫu nhiên: cách đó làm tim tường bị
                // co một nửa và WWO sinh ra các dải đứng rời như ảnh lỗi. Tái dựng
                // đầy đủ miền min/max của từng phía trên hai đường thẳng gốc rồi
                // mới lấy trung bình để có đúng chiều dài logic của tường.
                List<Line> selectedSideMembers = segmentMembers
                    .Where(line =>
                        DrawWallHelper.GetWallSideMarker(line) == selectedSide &&
                        AreCollinear(selected, line))
                    .ToList();
                string? oppositeSide = DrawWallHelper.GetWallSideMarker(opposite);
                List<Line> oppositeSideMembers = segmentMembers
                    .Where(line =>
                        DrawWallHelper.GetWallSideMarker(line) == oppositeSide &&
                        AreCollinear(opposite, line))
                    .ToList();
                Vector3d sourceAxis =
                    (selected.EndPoint - selected.StartPoint).GetNormal();
                if (!TryGetAggregateFaceEndpoints(
                        selectedSideMembers,
                        selected.StartPoint,
                        sourceAxis,
                        out selectedFaceStart,
                        out selectedFaceEnd) ||
                    !TryGetAggregateFaceEndpoints(
                        oppositeSideMembers,
                        selected.StartPoint,
                        sourceAxis,
                        out oppositeFaceStart,
                        out oppositeFaceEnd))
                {
                    message = "không thể tái dựng đầy đủ hai mặt tường.";
                    return false;
                }

                centerStart = Midpoint(selectedFaceStart, oppositeFaceStart);
                centerEnd = Midpoint(selectedFaceEnd, oppositeFaceEnd);
            }
            else
            {
                opposite = FindLegacyPairedFace(selected, lines);
                if (opposite == null)
                {
                    message = "không tìm thấy mặt tường song song tương ứng.";
                    return false;
                }

                GetLegacyCenterLine(
                    selected,
                    opposite,
                    out centerStart,
                    out centerEnd);
                OrderFaceEndpoints(
                    selected,
                    centerStart,
                    out selectedFaceStart,
                    out selectedFaceEnd);
                OrderFaceEndpoints(
                    opposite,
                    centerStart,
                    out oppositeFaceStart,
                    out oppositeFaceEnd);
            }

            double thickness = DistanceToInfiniteLine(opposite.StartPoint, selected);
            if (thickness <= Tolerance)
            {
                message = "hai mặt tường đang trùng nhau.";
                return false;
            }

            Vector3d centerDirection = (centerEnd - centerStart).GetNormal();
            Vector3d centerNormal = new Vector3d(
                -centerDirection.Y,
                centerDirection.X,
                0.0);
            Point3d selectedMiddle = Midpoint(selected.StartPoint, selected.EndPoint);
            bool selectedIsFirst =
                (selectedMiddle - centerStart).DotProduct(centerNormal) >= 0.0;

            EntityProperties selectedProperties = EntityProperties.From(selected);
            EntityProperties oppositeProperties = EntityProperties.From(opposite);
            definition = new WallDefinition
            {
                CenterStart = centerStart,
                CenterEnd = centerEnd,
                FirstFaceStart = selectedIsFirst
                    ? selectedFaceStart
                    : oppositeFaceStart,
                FirstFaceEnd = selectedIsFirst
                    ? selectedFaceEnd
                    : oppositeFaceEnd,
                SecondFaceStart = selectedIsFirst
                    ? oppositeFaceStart
                    : selectedFaceStart,
                SecondFaceEnd = selectedIsFirst
                    ? oppositeFaceEnd
                    : selectedFaceEnd,
                Thickness = thickness,
                LayerId = selected.LayerId,
                FirstProperties = selectedIsFirst
                    ? selectedProperties
                    : oppositeProperties,
                SecondProperties = selectedIsFirst
                    ? oppositeProperties
                    : selectedProperties
            };
            return true;
        }

        private static Line? FindLegacyPairedFace(
            Line selected,
            IEnumerable<Line> lines)
        {
            return lines
                .Where(line =>
                    line.ObjectId != selected.ObjectId &&
                    line.LayerId == selected.LayerId &&
                    string.IsNullOrEmpty(DrawWallHelper.GetWallSegmentId(line)) &&
                    AreParallelAndOverlapping(selected, line))
                .OrderBy(line => DistanceToInfiniteLine(line.StartPoint, selected))
                .FirstOrDefault();
        }

        private static void GetLegacyCenterLine(
            Line first,
            Line second,
            out Point3d centerStart,
            out Point3d centerEnd)
        {
            double direct = first.StartPoint.DistanceTo(second.StartPoint) +
                            first.EndPoint.DistanceTo(second.EndPoint);
            double crossed = first.StartPoint.DistanceTo(second.EndPoint) +
                             first.EndPoint.DistanceTo(second.StartPoint);

            if (direct <= crossed)
            {
                centerStart = Midpoint(first.StartPoint, second.StartPoint);
                centerEnd = Midpoint(first.EndPoint, second.EndPoint);
            }
            else
            {
                centerStart = Midpoint(first.StartPoint, second.EndPoint);
                centerEnd = Midpoint(first.EndPoint, second.StartPoint);
            }
        }

        private static bool TryGetAggregateFaceEndpoints(
            IEnumerable<Line> facePieces,
            Point3d stationOrigin,
            Vector3d axis,
            out Point3d startPoint,
            out Point3d endPoint)
        {
            startPoint = Point3d.Origin;
            endPoint = Point3d.Origin;
            List<Line> pieces = facePieces
                .Where(line => !line.IsErased)
                .ToList();
            if (pieces.Count == 0 || axis.Length <= Tolerance)
                return false;

            axis = axis.GetNormal();
            double minimumStation = pieces
                .SelectMany(line => new[] { line.StartPoint, line.EndPoint })
                .Min(point => (point - stationOrigin).DotProduct(axis));
            double maximumStation = pieces
                .SelectMany(line => new[] { line.StartPoint, line.EndPoint })
                .Max(point => (point - stationOrigin).DotProduct(axis));
            if (maximumStation - minimumStation <= Tolerance)
                return false;

            Line reference = pieces[0];
            double referenceStation =
                (reference.StartPoint - stationOrigin).DotProduct(axis);
            startPoint = reference.StartPoint +
                         axis * (minimumStation - referenceStation);
            endPoint = reference.StartPoint +
                       axis * (maximumStation - referenceStation);
            return true;
        }

        private static List<Line> ReadModelSpaceLines(
            Transaction transaction,
            Database database,
            bool includeWallCaps = false)
        {
            BlockTable table = (BlockTable)transaction.GetObject(
                database.BlockTableId,
                OpenMode.ForRead);
            BlockTableRecord modelSpace = (BlockTableRecord)transaction.GetObject(
                table[BlockTableRecord.ModelSpace],
                OpenMode.ForRead);

            List<Line> lines = new List<Line>();
            foreach (ObjectId id in modelSpace)
            {
                if (transaction.GetObject(id, OpenMode.ForRead) is Line line &&
                    !line.IsErased &&
                    (includeWallCaps || !DrawWallHelper.IsWallCap(line)))
                    lines.Add(line);
            }
            return lines;
        }

        private static bool WallAlreadyExists(
            IEnumerable<Line> lines,
            ObjectId layerId,
            Point3d firstStart,
            Point3d firstEnd,
            Point3d secondStart,
            Point3d secondEnd)
        {
            List<Line> layerLines = lines
                .Where(line => line.LayerId == layerId)
                .ToList();
            bool firstExists = IsSegmentCovered(
                layerLines, firstStart, firstEnd);
            bool secondExists = IsSegmentCovered(
                layerLines, secondStart, secondEnd);
            return firstExists && secondExists;
        }

        private static int HealOffsetWallIntersections(
            Transaction transaction,
            Database database,
            Line firstNewFace,
            Line secondNewFace,
            IReadOnlyList<Line> allLines,
            ObjectId layerId,
            IReadOnlyList<LineCutRequest> junctionCuts,
            IReadOnlyCollection<ObjectId> connectedHostIds)
        {
            Dictionary<ObjectId, LineCutSet> cutsByLine =
                new Dictionary<ObjectId, LineCutSet>();
            List<Line> layerFaces = allLines
                .Where(line => line.LayerId == layerId && !line.IsErased)
                .ToList();
            layerFaces.Add(firstNewFace);
            layerFaces.Add(secondNewFace);
            int intersectionCount = 0;

            // Giao chữ T tại đầu tường chỉ cần mở mặt gần của tường chủ.
            // Không cắt mặt ngoài/far face và không cắt hai mặt tường mới xuyên
            // qua thân tường chủ; đó chính là đường bao hợp của hai hình tường.
            foreach (LineCutRequest junctionCut in junctionCuts)
            {
                if (junctionCut.Face.IsErased) continue;
                if (AddLineCut(
                        cutsByLine,
                        junctionCut.Face,
                        junctionCut.FirstPoint,
                        junctionCut.SecondPoint))
                    intersectionCount++;
            }

            List<WallBand> wallBands = BuildWallBands(layerFaces);
            HashSet<ObjectId> seedIds =
                new HashSet<ObjectId>(connectedHostIds)
            {
                firstNewFace.ObjectId,
                secondNewFace.ObjectId
            };

            // Lan truyền từ tường offset qua toàn bộ chuỗi giao nhau của cụm bị
            // tác động. Một nhánh vừa extend có thể xuyên qua tường thứ hai, rồi
            // tường thứ hai lại cần được cắt ở chính giao đó; tìm kiếm một cấp sẽ
            // bỏ sót mặt thứ hai và để lại đường chạy xuyên như ảnh lỗi.
            HashSet<ObjectId> linesToProcess = new HashSet<ObjectId>(seedIds);
            Queue<Line> pendingLines = new Queue<Line>(layerFaces.Where(line =>
                seedIds.Contains(line.ObjectId)));
            while (pendingLines.Count > 0)
            {
                Line seed = pendingLines.Dequeue();
                foreach (Line candidate in layerFaces)
                {
                    if (candidate.ObjectId == seed.ObjectId ||
                        linesToProcess.Contains(candidate.ObjectId) ||
                        AreParallel(seed, candidate))
                        continue;

                    if (TrySegmentIntersection(seed, candidate, out _))
                    {
                        linesToProcess.Add(candidate.ObjectId);
                        pendingLines.Enqueue(candidate);
                    }
                }
            }

            foreach (Line line in layerFaces.Where(line =>
                         linesToProcess.Contains(line.ObjectId)))
            {
                Vector3d vector = line.EndPoint - line.StartPoint;
                if (vector.Length <= Tolerance) continue;
                Vector3d direction = vector.GetNormal();
                List<double> stations = new List<double> { 0.0, vector.Length };

                foreach (Line other in layerFaces)
                {
                    if (other.ObjectId == line.ObjectId ||
                        AreParallel(line, other) ||
                        !TrySegmentIntersection(line, other, out Point3d point))
                        continue;

                    double station = (point - line.StartPoint)
                        .DotProduct(direction);
                    if (station > Tolerance &&
                        station < vector.Length - Tolerance)
                        stations.Add(station);
                }

                List<double> orderedStations = stations
                    .OrderBy(station => station)
                    .Aggregate(
                        new List<double>(),
                        (unique, station) =>
                        {
                            if (unique.Count == 0 ||
                                station - unique[^1] > Tolerance)
                                unique.Add(station);
                            return unique;
                        });

                double sampleOffset = GetBoundarySampleOffset(
                    line,
                    wallBands);
                Vector3d normal = new Vector3d(
                    -direction.Y,
                    direction.X,
                    0.0);
                for (int index = 0;
                     index < orderedStations.Count - 1;
                     index++)
                {
                    double start = orderedStations[index];
                    double end = orderedStations[index + 1];
                    if (end - start <= Tolerance) continue;

                    Point3d midpoint = line.StartPoint +
                                       direction * ((start + end) / 2.0);
                    bool materialOnFirstSide = IsInsideWallUnion(
                        midpoint + normal * sampleOffset,
                        wallBands);
                    bool materialOnSecondSide = IsInsideWallUnion(
                        midpoint - normal * sampleOffset,
                        wallBands);
                    if (!materialOnFirstSide || !materialOnSecondSide)
                        continue;

                    if (AddLineCut(
                            cutsByLine,
                            line,
                            line.StartPoint + direction * start,
                            line.StartPoint + direction * end))
                        intersectionCount++;
                }
            }

            foreach (LineCutSet cutSet in cutsByLine.Values)
                ApplyLineCuts(transaction, database, cutSet);

            return intersectionCount;
        }

        private static List<WallBand> BuildWallBands(
            IReadOnlyList<Line> layerFaces)
        {
            List<WallBand> bands = new List<WallBand>();
            HashSet<ObjectId> pairedIds = new HashSet<ObjectId>();

            IEnumerable<IGrouping<string, Line>> metadataGroups = layerFaces
                .Select(line => new
                {
                    Line = line,
                    SegmentId = DrawWallHelper.GetWallSegmentId(line)
                })
                .Where(item => !string.IsNullOrEmpty(item.SegmentId))
                .GroupBy(item => item.SegmentId!, item => item.Line);

            foreach (IGrouping<string, Line> segmentGroup in metadataGroups)
            {
                List<List<Line>> sides = segmentGroup
                    .Select(line => new
                    {
                        Line = line,
                        Side = DrawWallHelper.GetWallSideMarker(line)
                    })
                    .Where(item => !string.IsNullOrEmpty(item.Side))
                    .GroupBy(item => item.Side!, item => item.Line)
                    .Select(group => group.ToList())
                    .Where(group => group.Count > 0)
                    .ToList();
                if (sides.Count < 2) continue;

                Line firstReference = sides[0][0];
                Line secondReference = sides[1]
                    .OrderBy(line => DistanceToInfiniteLine(
                        line.StartPoint,
                        firstReference))
                    .First();
                List<Line> firstPieces = sides[0]
                    .Where(line => AreCollinear(firstReference, line))
                    .ToList();
                List<Line> secondPieces = sides[1]
                    .Where(line => AreCollinear(secondReference, line))
                    .ToList();

                if (!TryCreateWallBand(
                        firstPieces,
                        secondPieces,
                        out WallBand? band))
                    continue;

                bands.Add(band!);
                pairedIds.UnionWith(band!.FaceIds);
            }

            // Bản vẽ cũ chưa có XData vẫn được heal theo cặp song song gần nhất.
            List<Line> legacyFaces = layerFaces
                .Where(line =>
                    !pairedIds.Contains(line.ObjectId) &&
                    string.IsNullOrEmpty(DrawWallHelper.GetWallSegmentId(line)))
                .ToList();
            foreach (Line first in legacyFaces)
            {
                if (pairedIds.Contains(first.ObjectId)) continue;
                Line? second = FindLegacyPairedFace(first, legacyFaces.Where(
                    line => !pairedIds.Contains(line.ObjectId)));
                if (second == null ||
                    !TryCreateWallBand(
                        new[] { first },
                        new[] { second },
                        out WallBand? legacyBand))
                    continue;

                bands.Add(legacyBand!);
                pairedIds.UnionWith(legacyBand!.FaceIds);
            }

            return bands;
        }

        private static bool TryCreateWallBand(
            IReadOnlyCollection<Line> firstPieces,
            IReadOnlyCollection<Line> secondPieces,
            out WallBand? band)
        {
            band = null;
            if (firstPieces.Count == 0 || secondPieces.Count == 0)
                return false;

            Line firstReference = firstPieces.First();
            Vector3d vector =
                firstReference.EndPoint - firstReference.StartPoint;
            if (vector.Length <= Tolerance) return false;
            Vector3d axis = vector.GetNormal();
            Point3d origin = firstReference.StartPoint;
            if (!TryGetAggregateFaceEndpoints(
                    firstPieces,
                    origin,
                    axis,
                    out Point3d firstStart,
                    out Point3d firstEnd) ||
                !TryGetAggregateFaceEndpoints(
                    secondPieces,
                    origin,
                    axis,
                    out Point3d secondStart,
                    out Point3d secondEnd))
                return false;

            double thickness = DistancePointToInfiniteLine(
                secondStart,
                firstStart,
                axis);
            if (thickness <= Tolerance) return false;

            band = new WallBand
            {
                Boundary = new[]
                {
                    firstStart,
                    firstEnd,
                    secondEnd,
                    secondStart
                },
                Thickness = thickness,
                FaceIds = new HashSet<ObjectId>(
                    firstPieces.Select(line => line.ObjectId)
                        .Concat(secondPieces.Select(line => line.ObjectId)))
            };
            return true;
        }

        private static double GetBoundarySampleOffset(
            Line line,
            IReadOnlyList<WallBand> wallBands)
        {
            double thickness = wallBands
                .Where(band => band.FaceIds.Contains(line.ObjectId))
                .Select(band => band.Thickness)
                .DefaultIfEmpty(1.0)
                .Min();
            return Math.Max(
                Tolerance * 20.0,
                Math.Min(1.0, thickness * 0.05));
        }

        private static bool IsInsideWallUnion(
            Point3d point,
            IReadOnlyList<WallBand> wallBands)
        {
            return wallBands.Any(band =>
                IsInsideConvexBoundary(point, band.Boundary));
        }

        private static bool IsInsideConvexBoundary(
            Point3d point,
            IReadOnlyList<Point3d> boundary)
        {
            if (boundary.Count < 3) return false;

            bool hasPositive = false;
            bool hasNegative = false;
            for (int index = 0; index < boundary.Count; index++)
            {
                Point3d first = boundary[index];
                Point3d second = boundary[(index + 1) % boundary.Count];
                double cross =
                    (second.X - first.X) * (point.Y - first.Y) -
                    (second.Y - first.Y) * (point.X - first.X);
                double edgeTolerance = Tolerance * Math.Max(
                    1.0,
                    first.DistanceTo(second));
                if (cross > edgeTolerance) hasPositive = true;
                if (cross < -edgeTolerance) hasNegative = true;
                if (hasPositive && hasNegative) return false;
            }
            return true;
        }

        private static bool TrySegmentIntersection(
            Line first,
            Line second,
            out Point3d intersection)
        {
            return TryInfiniteIntersection(
                       first.StartPoint,
                       first.EndPoint,
                       second.StartPoint,
                       second.EndPoint,
                       out intersection) &&
                   ProjectionFallsOnSegment(
                       intersection,
                       first.StartPoint,
                       first.EndPoint) &&
                   ProjectionFallsOnSegment(
                       intersection,
                       second.StartPoint,
                       second.EndPoint);
        }

        private static bool ProjectionFallsOnSegment(
            Point3d point,
            Point3d start,
            Point3d end)
        {
            Vector3d vector = end - start;
            if (vector.Length <= Tolerance) return false;

            Vector3d direction = vector.GetNormal();
            double station = (point - start).DotProduct(direction);
            return station >= -Tolerance && station <= vector.Length + Tolerance;
        }

        private static bool AddLineCut(
            IDictionary<ObjectId, LineCutSet> cutsByLine,
            Line line,
            Point3d firstPoint,
            Point3d secondPoint)
        {
            Vector3d vector = line.EndPoint - line.StartPoint;
            if (vector.Length <= Tolerance) return false;

            Vector3d direction = vector.GetNormal();
            double firstStation = (firstPoint - line.StartPoint)
                .DotProduct(direction);
            double secondStation = (secondPoint - line.StartPoint)
                .DotProduct(direction);
            double cutStart = Math.Max(
                0.0,
                Math.Min(firstStation, secondStation));
            double cutEnd = Math.Min(
                vector.Length,
                Math.Max(firstStation, secondStation));
            if (cutEnd - cutStart <= Tolerance) return false;

            if (!cutsByLine.TryGetValue(line.ObjectId, out LineCutSet? cutSet))
            {
                cutSet = new LineCutSet { Source = line };
                cutsByLine.Add(line.ObjectId, cutSet);
            }
            cutSet.Intervals.Add((cutStart, cutEnd));
            return true;
        }

        private static void ApplyLineCuts(
            Transaction transaction,
            Database database,
            LineCutSet cutSet)
        {
            Line source = (Line)transaction.GetObject(
                cutSet.Source.ObjectId,
                OpenMode.ForWrite);
            if (source.IsErased || cutSet.Intervals.Count == 0) return;

            Vector3d vector = source.EndPoint - source.StartPoint;
            if (vector.Length <= Tolerance) return;
            double lineLength = vector.Length;
            Vector3d direction = vector.GetNormal();

            List<(double Start, double End)> merged =
                new List<(double Start, double End)>();
            foreach ((double start, double end) in cutSet.Intervals
                .OrderBy(interval => interval.Start))
            {
                if (merged.Count == 0 ||
                    start > merged[^1].End + Tolerance)
                {
                    merged.Add((start, end));
                    continue;
                }

                (double previousStart, double previousEnd) = merged[^1];
                merged[^1] = (
                    previousStart,
                    Math.Max(previousEnd, end));
            }

            BlockTableRecord owner = (BlockTableRecord)transaction.GetObject(
                source.OwnerId,
                OpenMode.ForWrite);
            EntityProperties properties = EntityProperties.From(source);
            double pieceStart = 0.0;
            foreach ((double cutStart, double cutEnd) in merged)
            {
                AppendLinePiece(
                    transaction,
                    database,
                    owner,
                    source,
                    properties,
                    direction,
                    pieceStart,
                    cutStart);
                pieceStart = Math.Max(pieceStart, cutEnd);
            }
            AppendLinePiece(
                transaction,
                database,
                owner,
                source,
                properties,
                direction,
                pieceStart,
                lineLength);
            source.Erase();
        }

        private static void AppendLinePiece(
            Transaction transaction,
            Database database,
            BlockTableRecord owner,
            Line source,
            EntityProperties properties,
            Vector3d direction,
            double startStation,
            double endStation)
        {
            if (endStation - startStation <= Tolerance) return;

            Line piece = new Line(
                source.StartPoint + direction * startStation,
                source.StartPoint + direction * endStation)
            {
                LayerId = source.LayerId
            };
            owner.AppendEntity(piece);
            transaction.AddNewlyCreatedDBObject(piece, true);
            properties.Apply(piece);
            DrawWallHelper.CopyWallMetadata(
                transaction,
                database,
                source,
                piece);
        }

        private static int ConnectNearbyWallBranches(
            Transaction transaction,
            Database database,
            IReadOnlyList<Line> allLines,
            IReadOnlyList<Line> wallCaps,
            ObjectId layerId,
            double wallThickness,
            double searchDistance,
            IReadOnlyCollection<ObjectId> excludedLineIds,
            Point3d newFirstStart,
            Point3d newFirstEnd,
            Point3d newSecondStart,
            Point3d newSecondEnd)
        {
            Vector3d newWallDirection = newFirstEnd - newFirstStart;
            if (newWallDirection.Length <= Tolerance) return 0;
            newWallDirection = newWallDirection.GetNormal();

            List<Line> layerFaces = allLines
                .Where(line =>
                    line.LayerId == layerId &&
                    !excludedLineIds.Contains(line.ObjectId))
                .ToList();
            HashSet<ObjectId> processed = new HashSet<ObjectId>();
            int connectedEndCount = 0;

            foreach (Line first in layerFaces)
            {
                if (processed.Contains(first.ObjectId)) continue;

                Vector3d firstDirection = first.EndPoint - first.StartPoint;
                if (firstDirection.Length <= Tolerance ||
                    Math.Abs(firstDirection.GetNormal().DotProduct(newWallDirection)) >=
                    1.0 - ParallelTolerance)
                    continue;

                Line? second = FindPairedWallFaceForConnection(first, layerFaces);
                if (second == null || processed.Contains(second.ObjectId))
                    continue;

                processed.Add(first.ObjectId);
                processed.Add(second.ObjectId);

                double direct = first.StartPoint.DistanceTo(second.StartPoint) +
                                first.EndPoint.DistanceTo(second.EndPoint);
                double crossed = first.StartPoint.DistanceTo(second.EndPoint) +
                                 first.EndPoint.DistanceTo(second.StartPoint);
                bool secondIsReversed = crossed < direct;
                Point3d secondAtStart = secondIsReversed
                    ? second.EndPoint
                    : second.StartPoint;
                Point3d secondAtEnd = secondIsReversed
                    ? second.StartPoint
                    : second.EndPoint;

                bool connectsAtStart = TryConnectBranchEnd(
                    first.StartPoint,
                    first.EndPoint,
                    secondAtStart,
                    secondAtEnd,
                    newFirstStart,
                    newFirstEnd,
                    newSecondStart,
                    newSecondEnd,
                    wallThickness,
                    searchDistance,
                    out Point3d snappedFirstStart,
                    out Point3d snappedSecondStart);
                bool connectsAtEnd = TryConnectBranchEnd(
                    first.EndPoint,
                    first.StartPoint,
                    secondAtEnd,
                    secondAtStart,
                    newFirstStart,
                    newFirstEnd,
                    newSecondStart,
                    newSecondEnd,
                    wallThickness,
                    searchDistance,
                    out Point3d snappedFirstEnd,
                    out Point3d snappedSecondEnd);

                if (!connectsAtStart && !connectsAtEnd) continue;

                Line writableFirst = (Line)transaction.GetObject(
                    first.ObjectId,
                    OpenMode.ForWrite);
                Line writableSecond = (Line)transaction.GetObject(
                    second.ObjectId,
                    OpenMode.ForWrite);

                if (connectsAtStart)
                {
                    Point3d oldFirst = writableFirst.StartPoint;
                    Point3d oldSecond = secondIsReversed
                        ? writableSecond.EndPoint
                        : writableSecond.StartPoint;
                    writableFirst.StartPoint = snappedFirstStart;
                    if (secondIsReversed)
                        writableSecond.EndPoint = snappedSecondStart;
                    else
                        writableSecond.StartPoint = snappedSecondStart;
                    EraseWallCapAt(
                        transaction,
                        wallCaps,
                        oldFirst,
                        oldSecond);
                    connectedEndCount++;
                }

                if (connectsAtEnd)
                {
                    Point3d oldFirst = writableFirst.EndPoint;
                    Point3d oldSecond = secondIsReversed
                        ? writableSecond.StartPoint
                        : writableSecond.EndPoint;
                    writableFirst.EndPoint = snappedFirstEnd;
                    if (secondIsReversed)
                        writableSecond.StartPoint = snappedSecondEnd;
                    else
                        writableSecond.EndPoint = snappedSecondEnd;
                    EraseWallCapAt(
                        transaction,
                        wallCaps,
                        oldFirst,
                        oldSecond);
                    connectedEndCount++;
                }

                DrawWallHelper.UpdateWallPairMetadata(
                    transaction,
                    database,
                    writableFirst,
                    writableSecond);
            }

            return connectedEndCount;
        }

        private static Line? FindPairedWallFaceForConnection(
            Line selected,
            IEnumerable<Line> lines)
        {
            string? segmentId = DrawWallHelper.GetWallSegmentId(selected);
            string? selectedSide = DrawWallHelper.GetWallSideMarker(selected);
            if (!string.IsNullOrEmpty(segmentId) &&
                !string.IsNullOrEmpty(selectedSide))
            {
                return lines
                    .Where(line =>
                        line.ObjectId != selected.ObjectId &&
                        DrawWallHelper.GetWallSegmentId(line) == segmentId &&
                        DrawWallHelper.GetWallSideMarker(line) != selectedSide &&
                        AreParallelAndOverlapping(selected, line))
                    .OrderBy(line => DistanceToInfiniteLine(
                        line.StartPoint,
                        selected))
                    .FirstOrDefault();
            }

            return FindLegacyPairedFace(selected, lines);
        }

        private static bool TryConnectBranchEnd(
            Point3d firstEndpoint,
            Point3d firstOtherEndpoint,
            Point3d secondEndpoint,
            Point3d secondOtherEndpoint,
            Point3d newFirstStart,
            Point3d newFirstEnd,
            Point3d newSecondStart,
            Point3d newSecondEnd,
            double wallThickness,
            double searchDistance,
            out Point3d snappedFirst,
            out Point3d snappedSecond)
        {
            snappedFirst = firstEndpoint;
            snappedSecond = secondEndpoint;

            // Giống smart snap của WW: cả hai mặt của một tường phải cùng kéo
            // được theo tia đầu mút và cắt đủ hai mặt của tường offset. Việc dựa
            // vào giao hình học thật tránh bỏ sót khi tim tường nguồn đã bị WW/TW
            // chia nhỏ hoặc XData vẫn mang chiều dài cũ.
            return TrySnapBranchFaceEndpoint(
                       firstEndpoint,
                       firstOtherEndpoint,
                       newFirstStart,
                       newFirstEnd,
                       newSecondStart,
                       newSecondEnd,
                       wallThickness,
                       searchDistance,
                       out snappedFirst) &&
                   TrySnapBranchFaceEndpoint(
                       secondEndpoint,
                       secondOtherEndpoint,
                       newFirstStart,
                       newFirstEnd,
                       newSecondStart,
                       newSecondEnd,
                       wallThickness,
                       searchDistance,
                       out snappedSecond);
        }

        private static bool TrySnapBranchFaceEndpoint(
            Point3d endpoint,
            Point3d otherEndpoint,
            Point3d newFirstStart,
            Point3d newFirstEnd,
            Point3d newSecondStart,
            Point3d newSecondEnd,
            double wallThickness,
            double searchDistance,
            out Point3d snapped)
        {
            snapped = endpoint;
            Vector3d ray = endpoint - otherEndpoint;
            if (ray.Length <= Tolerance) return false;
            Vector3d outward = ray.GetNormal();
            List<(Point3d Point, double Movement)> intersections =
                new List<(Point3d, double)>();

            Point3d[] starts = { newFirstStart, newSecondStart };
            Point3d[] ends = { newFirstEnd, newSecondEnd };
            for (int index = 0; index < starts.Length; index++)
            {
                if (!TryInfiniteIntersection(
                        endpoint,
                        endpoint + outward,
                        starts[index],
                        ends[index],
                        out Point3d intersection))
                    continue;

                Line temporaryFace = new Line(starts[index], ends[index]);
                bool liesOnWall = ProjectionFallsOnExpandedSegment(
                    intersection,
                    temporaryFace,
                    wallThickness * 2.0);
                temporaryFace.Dispose();
                if (!liesOnWall) continue;

                double movement = (intersection - endpoint).DotProduct(outward);
                if (movement >= -searchDistance && movement <= searchDistance)
                    intersections.Add((intersection, movement));
            }

            if (intersections.Count != 2) return false;
            (Point3d Point, double Movement) farthest = intersections
                .OrderByDescending(item => item.Movement)
                .First();
            if (farthest.Movement < -Tolerance) return false;

            snapped = farthest.Point;
            return true;
        }

        private static void EraseWallCapAt(
            Transaction transaction,
            IEnumerable<Line> wallCaps,
            Point3d firstEndpoint,
            Point3d secondEndpoint)
        {
            const double capTolerance = 1.0;
            foreach (Line cap in wallCaps)
            {
                if (cap.IsErased) continue;
                bool direct =
                    cap.StartPoint.DistanceTo(firstEndpoint) <= capTolerance &&
                    cap.EndPoint.DistanceTo(secondEndpoint) <= capTolerance;
                bool crossed =
                    cap.StartPoint.DistanceTo(secondEndpoint) <= capTolerance &&
                    cap.EndPoint.DistanceTo(firstEndpoint) <= capTolerance;
                if (!direct && !crossed) continue;

                DBObject writableCap = transaction.GetObject(
                    cap.ObjectId,
                    OpenMode.ForWrite);
                writableCap.Erase();
            }
        }

        private static bool TryConnectWallEnd(
            Transaction transaction,
            Database database,
            IReadOnlyList<Line> allLines,
            ObjectId layerId,
            double wallThickness,
            double searchDistance,
            Vector3d offset,
            Point3d centerEndpoint,
            Point3d otherCenterEndpoint,
            ref Point3d firstFaceEndpoint,
            ref Point3d secondFaceEndpoint,
            out LineCutRequest? junctionCut,
            out HashSet<ObjectId> connectedHostIds)
        {
            junctionCut = null;
            connectedHostIds = new HashSet<ObjectId>();
            Vector3d wallDirection = centerEndpoint - otherCenterEndpoint;
            if (wallDirection.Length <= Tolerance) return false;
            Vector3d outward = wallDirection.GetNormal();
            double backwardAllowance = wallThickness * 2.0 + Tolerance;

            List<HostCandidate> candidates = new List<HostCandidate>();
            foreach (Line line in allLines.Where(line => line.LayerId == layerId))
            {
                Vector3d targetDirection = line.EndPoint - line.StartPoint;
                if (targetDirection.Length <= Tolerance ||
                    Math.Abs(outward.DotProduct(targetDirection.GetNormal())) >=
                    1.0 - ParallelTolerance)
                    continue;

                if (!TryInfiniteIntersection(
                        centerEndpoint,
                        centerEndpoint + outward,
                        line.StartPoint,
                        line.EndPoint,
                        out Point3d intersection))
                    continue;

                double movement = (intersection - centerEndpoint).DotProduct(outward);
                if (movement < -backwardAllowance ||
                    movement > searchDistance ||
                    !ProjectionFallsOnExpandedSegment(
                        intersection,
                        line,
                        searchDistance))
                    continue;

                candidates.Add(new HostCandidate
                {
                    Face = line,
                    Movement = movement,
                    LongitudinalGap = DistanceAlongLineToSegment(
                        intersection,
                        line)
                });
            }

            HostCandidate? nearest = candidates
                .OrderBy(candidate => Math.Abs(candidate.Movement))
                .ThenBy(candidate => candidate.LongitudinalGap)
                .FirstOrDefault();
            if (nearest == null) return false;

            List<Line> hostFaces = GetHostFaces(nearest.Face, allLines);
            if (hostFaces.Count < 2) return false;

            List<HostCandidate> orderedHostFaces = hostFaces
                .Select(face =>
                {
                    if (!TryInfiniteIntersection(
                            centerEndpoint,
                            centerEndpoint + outward,
                            face.StartPoint,
                            face.EndPoint,
                            out Point3d point))
                        return null;

                    double movement = (point - centerEndpoint)
                        .DotProduct(outward);
                    if (movement < -backwardAllowance ||
                        movement > searchDistance ||
                        !ProjectionFallsOnExpandedSegment(
                            point,
                            face,
                            searchDistance))
                        return null;

                    return new HostCandidate
                    {
                        Face = face,
                        Movement = movement,
                        LongitudinalGap = DistanceAlongLineToSegment(
                            point,
                            face)
                    };
                })
                .Where(candidate => candidate != null)
                .Select(candidate => candidate!)
                .GroupBy(candidate => Math.Round(
                    candidate.Movement / Math.Max(Tolerance, 0.001)))
                .Select(group => group
                    .OrderBy(candidate => candidate.LongitudinalGap)
                    .First())
                .OrderBy(candidate => candidate.Movement)
                .ToList();
            if (orderedHostFaces.Count < 2 || offset.Length <= Tolerance)
                return false;

            HostCandidate nearHost = orderedHostFaces.First();
            HostCandidate farHost = orderedHostFaces.Last();
            Vector3d offsetDirection = offset.GetNormal();
            bool firstFaceIsFartherOffset =
                (firstFaceEndpoint - secondFaceEndpoint)
                    .DotProduct(offsetDirection) > 0.0;

            // Nếu cả hai mặt của tường chủ còn chạy qua phía offset, đầu tường
            // mới đang đâm vào GIỮA tường chủ (giao T), không phải góc L. Với T,
            // cả hai mặt mới phải dừng ở mặt gần và chỉ mở một khe trên mặt đó.
            // Logic cũ ép một mặt tới near face và mặt kia tới far face, tạo cap
            // xiên/đóng hình chữ nhật khi tim nguồn nằm giữa bề dày tường chủ.
            bool isTJunction = HostContinuesPastOffsetSide(
                                   nearHost.Face,
                                   firstFaceEndpoint,
                                   secondFaceEndpoint,
                                   outward,
                                   offsetDirection,
                                   wallThickness) &&
                               HostContinuesPastOffsetSide(
                                   farHost.Face,
                                   firstFaceEndpoint,
                                   secondFaceEndpoint,
                                   outward,
                                   offsetDirection,
                                   wallThickness);
            if (isTJunction)
            {
                if (!TryGetWallEndIntersection(
                        firstFaceEndpoint,
                        outward,
                        nearHost.Face,
                        backwardAllowance,
                        searchDistance,
                        out Point3d tFirst) ||
                    !TryGetWallEndIntersection(
                        secondFaceEndpoint,
                        outward,
                        nearHost.Face,
                        backwardAllowance,
                        searchDistance,
                        out Point3d tSecond) ||
                    !ProjectionFallsOnSegment(
                        tFirst,
                        nearHost.Face.StartPoint,
                        nearHost.Face.EndPoint) ||
                    !ProjectionFallsOnSegment(
                        tSecond,
                        nearHost.Face.StartPoint,
                        nearHost.Face.EndPoint))
                    return false;

                firstFaceEndpoint = tFirst;
                secondFaceEndpoint = tSecond;
                junctionCut = new LineCutRequest
                {
                    Face = nearHost.Face,
                    FirstPoint = tFirst,
                    SecondPoint = tSecond
                };
                connectedHostIds.UnionWith(
                    hostFaces.Select(face => face.ObjectId));
                return true;
            }

            Line firstHostFace = firstFaceIsFartherOffset
                ? farHost.Face
                : nearHost.Face;
            Line secondHostFace = firstFaceIsFartherOffset
                ? nearHost.Face
                : farHost.Face;

            if (!TryGetCornerIntersection(
                    firstFaceEndpoint,
                    outward,
                    firstHostFace,
                    backwardAllowance,
                    searchDistance,
                    out Point3d snappedFirst) ||
                !TryGetCornerIntersection(
                    secondFaceEndpoint,
                    outward,
                    secondHostFace,
                    backwardAllowance,
                    searchDistance,
                    out Point3d snappedSecond))
                return false;

            ExtendHostFaceTo(transaction, firstHostFace, snappedFirst);
            ExtendHostFaceTo(transaction, secondHostFace, snappedSecond);
            Line writableFirstHost = (Line)transaction.GetObject(
                firstHostFace.ObjectId,
                OpenMode.ForWrite);
            Line writableSecondHost = (Line)transaction.GetObject(
                secondHostFace.ObjectId,
                OpenMode.ForWrite);
            DrawWallHelper.UpdateWallPairMetadata(
                transaction,
                database,
                writableFirstHost,
                writableSecondHost);
            firstFaceEndpoint = snappedFirst;
            secondFaceEndpoint = snappedSecond;
            connectedHostIds.UnionWith(
                hostFaces.Select(face => face.ObjectId));
            return true;
        }

        private static bool TryGetCornerIntersection(
            Point3d newFaceEndpoint,
            Vector3d outward,
            Line hostFace,
            double backwardAllowance,
            double searchDistance,
            out Point3d intersection)
        {
            return TryGetWallEndIntersection(
                newFaceEndpoint,
                outward,
                hostFace,
                backwardAllowance,
                searchDistance,
                out intersection);
        }

        private static bool TryGetWallEndIntersection(
            Point3d newFaceEndpoint,
            Vector3d outward,
            Line hostFace,
            double backwardAllowance,
            double searchDistance,
            out Point3d intersection)
        {
            intersection = newFaceEndpoint;
            if (!TryInfiniteIntersection(
                    newFaceEndpoint,
                    newFaceEndpoint + outward,
                    hostFace.StartPoint,
                    hostFace.EndPoint,
                    out Point3d candidate))
                return false;

            double movement = (candidate - newFaceEndpoint)
                .DotProduct(outward);
            if (movement < -backwardAllowance ||
                movement > searchDistance ||
                !ProjectionFallsOnExpandedSegment(
                    candidate,
                    hostFace,
                    searchDistance))
                return false;

            intersection = candidate;
            return true;
        }

        private static bool HostContinuesPastOffsetSide(
            Line hostFace,
            Point3d firstFaceEndpoint,
            Point3d secondFaceEndpoint,
            Vector3d outward,
            Vector3d offsetDirection,
            double wallThickness)
        {
            Point3d fartherOffsetEndpoint =
                (firstFaceEndpoint - secondFaceEndpoint)
                    .DotProduct(offsetDirection) >= 0.0
                    ? firstFaceEndpoint
                    : secondFaceEndpoint;
            if (!TryInfiniteIntersection(
                    fartherOffsetEndpoint,
                    fartherOffsetEndpoint + outward,
                    hostFace.StartPoint,
                    hostFace.EndPoint,
                    out Point3d boundary) ||
                !ProjectionFallsOnSegment(
                    boundary,
                    hostFace.StartPoint,
                    hostFace.EndPoint))
                return false;

            double continuation = Math.Max(
                (hostFace.StartPoint - boundary).DotProduct(offsetDirection),
                (hostFace.EndPoint - boundary).DotProduct(offsetDirection));
            double continuationTolerance = Math.Max(
                Tolerance * 10.0,
                wallThickness * 0.05);
            return continuation > continuationTolerance;
        }

        private static void ExtendHostFaceTo(
            Transaction transaction,
            Line hostFace,
            Point3d intersection)
        {
            if (ProjectionFallsOnSegment(
                    intersection,
                    hostFace.StartPoint,
                    hostFace.EndPoint))
                return;

            Line writableHost = (Line)transaction.GetObject(
                hostFace.ObjectId,
                OpenMode.ForWrite);
            if (intersection.DistanceTo(writableHost.StartPoint) <=
                intersection.DistanceTo(writableHost.EndPoint))
                writableHost.StartPoint = intersection;
            else
                writableHost.EndPoint = intersection;
        }

        private static List<Line> GetHostFaces(
            Line selectedHostFace,
            IReadOnlyList<Line> allLines)
        {
            string? segmentId = DrawWallHelper.GetWallSegmentId(selectedHostFace);
            if (!string.IsNullOrEmpty(segmentId))
            {
                // Cùng lý do như trong TryReadWallDefinition: nếu tường chủ đã bị
                // xén thành nhiều đoạn cùng segmentId, chỉ lấy các đoạn thực sự
                // chồng lấn với mặt vừa tìm thấy (selectedHostFace), tránh gộp
                // nhầm một mảnh khác nằm xa dọc theo cùng tường.
                return allLines.Where(line =>
                    line.LayerId == selectedHostFace.LayerId &&
                    DrawWallHelper.GetWallSegmentId(line) == segmentId &&
                    (line.ObjectId == selectedHostFace.ObjectId ||
                     AreParallelAndOverlapping(selectedHostFace, line))).ToList();
            }

            Line? opposite = FindLegacyPairedFace(selectedHostFace, allLines);
            return opposite == null
                ? new List<Line>()
                : new List<Line> { selectedHostFace, opposite };
        }

        private static bool TrySnapFaceEndpoint(
            Point3d endpoint,
            Vector3d outward,
            IEnumerable<Line> hostFaces,
            double wallThickness,
            double searchDistance,
            out Point3d snapped)
        {
            snapped = endpoint;

            List<(Point3d Point, double Movement)> matches =
                new List<(Point3d, double)>();
            foreach (Line hostFace in hostFaces)
            {
                if (!TryInfiniteIntersection(
                        endpoint,
                        endpoint + outward,
                        hostFace.StartPoint,
                        hostFace.EndPoint,
                        out Point3d intersection) ||
                    !ProjectionFallsOnExpandedSegment(
                        intersection,
                        hostFace,
                        wallThickness * 2.0))
                    continue;

                double movement = (intersection - endpoint).DotProduct(outward);
                if (movement >= -searchDistance &&
                    movement <= searchDistance &&
                    !matches.Any(match =>
                        match.Point.DistanceTo(intersection) <= Tolerance))
                    matches.Add((intersection, movement));
            }

            if (matches.Count < 2) return false;

            // A T connection must reach the far face of the host wall, not stop
            // at the first boundary and leave the two wall interiors disconnected.
            snapped = matches
                .OrderByDescending(match => match.Movement)
                .First().Point;
            return true;
        }

        private static bool ProjectionFallsOnExpandedSegment(
            Point3d point,
            Line line,
            double extension)
        {
            Vector3d direction = (line.EndPoint - line.StartPoint).GetNormal();
            double station = (point - line.StartPoint).DotProduct(direction);
            double length = line.StartPoint.DistanceTo(line.EndPoint);
            return station >= -extension && station <= length + extension;
        }

        private static double DistanceAlongLineToSegment(
            Point3d point,
            Line line)
        {
            Vector3d vector = line.EndPoint - line.StartPoint;
            if (vector.Length <= Tolerance)
                return double.MaxValue;

            Vector3d direction = vector.GetNormal();
            double station = (point - line.StartPoint).DotProduct(direction);
            if (station < 0.0) return -station;
            if (station > vector.Length) return station - vector.Length;
            return 0.0;
        }

        private static bool TryInfiniteIntersection(
            Point3d firstStart,
            Point3d firstEnd,
            Point3d secondStart,
            Point3d secondEnd,
            out Point3d intersection)
        {
            double ax = firstEnd.X - firstStart.X;
            double ay = firstEnd.Y - firstStart.Y;
            double bx = secondEnd.X - secondStart.X;
            double by = secondEnd.Y - secondStart.Y;
            double denominator = ax * by - ay * bx;

            intersection = Point3d.Origin;
            if (Math.Abs(denominator) <= Tolerance) return false;

            double dx = secondStart.X - firstStart.X;
            double dy = secondStart.Y - firstStart.Y;
            double parameter = (dx * by - dy * bx) / denominator;
            intersection = new Point3d(
                firstStart.X + parameter * ax,
                firstStart.Y + parameter * ay,
                firstStart.Z + parameter * (firstEnd.Z - firstStart.Z));
            return true;
        }

        private static bool IsSegmentCovered(
            IEnumerable<Line> lines,
            Point3d start,
            Point3d end)
        {
            Vector3d desired = end - start;
            if (desired.Length <= Tolerance) return false;

            Vector3d axis = desired.GetNormal();
            double desiredLength = desired.Length;
            List<(double Start, double End)> intervals = new List<(double, double)>();

            foreach (Line line in lines)
            {
                Vector3d actual = line.EndPoint - line.StartPoint;
                if (actual.Length <= Tolerance ||
                    Math.Abs(axis.DotProduct(actual.GetNormal())) <
                    1.0 - ParallelTolerance ||
                    DistancePointToInfiniteLine(line.StartPoint, start, axis) >
                    Tolerance * 10.0)
                    continue;

                double first = (line.StartPoint - start).DotProduct(axis);
                double second = (line.EndPoint - start).DotProduct(axis);
                intervals.Add((Math.Min(first, second), Math.Max(first, second)));
            }

            double coveredTo = 0.0;
            foreach ((double intervalStart, double intervalEnd) in intervals
                .OrderBy(interval => interval.Start))
            {
                if (intervalEnd < -Tolerance ||
                    intervalStart > desiredLength + Tolerance)
                    continue;
                if (intervalStart > coveredTo + Tolerance)
                    return false;
                coveredTo = Math.Max(coveredTo, intervalEnd);
                if (coveredTo >= desiredLength - Tolerance)
                    return true;
            }

            return false;
        }

        private static bool AreParallel(Line first, Line second)
        {
            Vector3d firstVector = first.EndPoint - first.StartPoint;
            Vector3d secondVector = second.EndPoint - second.StartPoint;
            return firstVector.Length > Tolerance &&
                   secondVector.Length > Tolerance &&
                   Math.Abs(firstVector.GetNormal().DotProduct(secondVector.GetNormal())) >=
                   1.0 - ParallelTolerance;
        }

        private static bool AreCollinear(Line first, Line second)
        {
            if (!AreParallel(first, second)) return false;

            double collinearTolerance = Math.Max(Tolerance * 10.0, 0.01);
            return DistancePointToInfiniteLine(second.StartPoint, first) <=
                       collinearTolerance &&
                   DistancePointToInfiniteLine(second.EndPoint, first) <=
                       collinearTolerance;
        }

        private static bool AreParallelAndOverlapping(Line first, Line second)
        {
            if (!AreParallel(first, second)) return false;

            Vector3d direction = (first.EndPoint - first.StartPoint).GetNormal();
            double length = first.StartPoint.DistanceTo(first.EndPoint);
            double secondStart = (second.StartPoint - first.StartPoint)
                .DotProduct(direction);
            double secondEnd = (second.EndPoint - first.StartPoint)
                .DotProduct(direction);
            double overlap = Math.Min(length, Math.Max(secondStart, secondEnd)) -
                             Math.Max(0.0, Math.Min(secondStart, secondEnd));
            return overlap > Tolerance;
        }

        private static double DistancePointToInfiniteLine(Point3d point, Line line)
        {
            Vector3d direction = (line.EndPoint - line.StartPoint).GetNormal();
            return DistancePointToInfiniteLine(point, line.StartPoint, direction);
        }

        private static double DistancePointToInfiniteLine(
            Point3d point,
            Point3d lineStart,
            Vector3d direction)
        {
            Vector3d offset = point - lineStart;
            return (offset - direction * offset.DotProduct(direction)).Length;
        }

        private static double DistanceToInfiniteLine(Point3d point, Line line)
        {
            return DistancePointToInfiniteLine(point, line);
        }

        private static Point3d Midpoint(Point3d first, Point3d second)
        {
            return new Point3d(
                (first.X + second.X) / 2.0,
                (first.Y + second.Y) / 2.0,
                (first.Z + second.Z) / 2.0);
        }

        private static void OrderFaceEndpoints(
            Line face,
            Point3d centerStart,
            out Point3d faceStart,
            out Point3d faceEnd)
        {
            if (face.StartPoint.DistanceTo(centerStart) <=
                face.EndPoint.DistanceTo(centerStart))
            {
                faceStart = face.StartPoint;
                faceEnd = face.EndPoint;
            }
            else
            {
                faceStart = face.EndPoint;
                faceEnd = face.StartPoint;
            }
        }

        private static void TrimOffsetWallToInsideFace(
            WallDefinition source,
            double side,
            Vector3d direction,
            Vector3d offset,
            ref Point3d newCenterStart,
            ref Point3d newCenterEnd)
        {
            Point3d towardStart = side > 0.0
                ? source.FirstFaceStart
                : source.SecondFaceStart;
            Point3d towardEnd = side > 0.0
                ? source.FirstFaceEnd
                : source.SecondFaceEnd;
            Point3d awayStart = side > 0.0
                ? source.SecondFaceStart
                : source.FirstFaceStart;
            Point3d awayEnd = side > 0.0
                ? source.SecondFaceEnd
                : source.FirstFaceEnd;

            double towardMinimum = Math.Min(
                (towardStart - source.CenterStart).DotProduct(direction),
                (towardEnd - source.CenterStart).DotProduct(direction));
            double towardMaximum = Math.Max(
                (towardStart - source.CenterStart).DotProduct(direction),
                (towardEnd - source.CenterStart).DotProduct(direction));
            double awayMinimum = Math.Min(
                (awayStart - source.CenterStart).DotProduct(direction),
                (awayEnd - source.CenterStart).DotProduct(direction));
            double awayMaximum = Math.Max(
                (awayStart - source.CenterStart).DotProduct(direction),
                (awayEnd - source.CenterStart).DotProduct(direction));

            double centerLength = source.CenterStart.DistanceTo(source.CenterEnd);
            double trimmedStart = 0.0;
            double trimmedEnd = centerLength;

            // Khi offset về phía mặt ngắn hơn của tường bao, co từng đầu về
            // đúng mép trong. Offset ra ngoài hoặc tường tự do vẫn giữ nguyên.
            if (towardMinimum > awayMinimum + Tolerance)
                trimmedStart = Math.Max(trimmedStart, towardMinimum);
            if (towardMaximum < awayMaximum - Tolerance)
                trimmedEnd = Math.Min(trimmedEnd, towardMaximum);

            if (trimmedEnd - trimmedStart <= Tolerance) return;

            newCenterStart = source.CenterStart + offset + direction * trimmedStart;
            newCenterEnd = source.CenterStart + offset + direction * trimmedEnd;
        }

        private sealed class WallDefinition
        {
            public Point3d CenterStart { get; init; }
            public Point3d CenterEnd { get; init; }
            public Point3d FirstFaceStart { get; init; }
            public Point3d FirstFaceEnd { get; init; }
            public Point3d SecondFaceStart { get; init; }
            public Point3d SecondFaceEnd { get; init; }
            public double Thickness { get; init; }
            public ObjectId LayerId { get; init; }
            public EntityProperties FirstProperties { get; init; } = null!;
            public EntityProperties SecondProperties { get; init; } = null!;
        }

        private sealed class HostCandidate
        {
            public Line Face { get; init; } = null!;
            public double Movement { get; init; }
            public double LongitudinalGap { get; init; }
        }

        private sealed class LineCutRequest
        {
            public Line Face { get; init; } = null!;
            public Point3d FirstPoint { get; init; }
            public Point3d SecondPoint { get; init; }
        }

        private sealed class WallBand
        {
            public IReadOnlyList<Point3d> Boundary { get; init; } =
                Array.Empty<Point3d>();
            public double Thickness { get; init; }
            public HashSet<ObjectId> FaceIds { get; init; } =
                new HashSet<ObjectId>();
        }

        private sealed class LineCutSet
        {
            public Line Source { get; init; } = null!;
            public List<(double Start, double End)> Intervals { get; } =
                new List<(double Start, double End)>();
        }

        private sealed class EntityProperties
        {
            public Teigha.Colors.Color Color { get; private init; } = null!;
            public LineWeight LineWeight { get; private init; }
            public ObjectId LinetypeId { get; private init; }
            public double LinetypeScale { get; private init; }
            public Teigha.Colors.Transparency Transparency { get; private init; }

            public static EntityProperties From(Line line)
            {
                return new EntityProperties
                {
                    Color = line.Color,
                    LineWeight = line.LineWeight,
                    LinetypeId = line.LinetypeId,
                    LinetypeScale = line.LinetypeScale,
                    Transparency = line.Transparency
                };
            }

            public void Apply(Line line)
            {
                line.Color = Color;
                line.LineWeight = LineWeight;
                line.LinetypeId = LinetypeId;
                line.LinetypeScale = LinetypeScale;
                line.Transparency = Transparency;
            }
        }
    }
}
