using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using PrLogTrackingSystem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.Resources.Definitions;
using Tools.VinaCad.Helper.Helper;
using Application = Prima.VinaCAD.ApplicationServices.Application;
using MessageBox = System.Windows.MessageBox;

namespace Tools.VinaCAD.Action.Actions
{
    /// <summary>
    /// Sửa hình học tường trong vùng chọn, ưu tiên metadata do lệnh WW tạo.
    /// </summary>
    public class TrimFixWallAction
    {
        private const double Tolerance = 0.001;
        // cos(0,5°): giới hạn độ lệch để hai line được xem là song song.
        private const double ParallelDotTolerance = 0.999961923;
        private const double DefaultHealDistance = 600.0;
        private const double MaximumSupportedThickness = 5000.0;
        private const double CoplanarTolerance = 0.01;

        public void Execute()
        {
            Document? document = Application.DocumentManager.MdiActiveDocument;
            if (document == null)
                return;

            Editor editor = document.Editor;
            Database database = document.Database;

            try
            {
                editor.WriteMessage("\nTW: Kéo chọn các tường cần sửa, nhấn Enter để thực hiện.");

                TypedValue[] filterValues =
                {
                    new TypedValue((int)DxfCode.Start, "LINE")
                };
                PromptSelectionOptions selectionOptions = new PromptSelectionOptions
                {
                    MessageForAdding = "\nKéo chọn tường: "
                };
                PromptSelectionResult selection = editor.GetSelection(selectionOptions, new SelectionFilter(filterValues));

                if (selection.Status != PromptStatus.OK || selection.Value == null)
                {
                    editor.WriteMessage("\nKhông có line trong vùng chọn.");
                    return;
                }

                RepairSummary summary = Repair(database, selection.Value.GetObjectIds());

                if (summary.WallLineCount == 0)
                {
                    editor.WriteMessage("\nKhông tìm thấy cặp line do WW tạo.");
                    return;
                }

                editor.WriteMessage(
                    $"\nĐã sửa {summary.ChangedLineCount} line, " +
                    $"{summary.JunctionCount} giao tường, " +
                    $"{summary.HealedGapCount} khoảng hở; " +
                    $"nhận diện {summary.ClosedTJunctionCount} giao T kín.");
                editor.UpdateScreen();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(TrimFixWallAction), ex);
                MessageBox.Show($"Lỗi TW: {ex.Message}", StringDefinition.TITLE_ERROR);
            }
        }

        private static RepairSummary Repair(
            Database database,
            ObjectId[] selectedIds)
        {
            using Transaction transaction = database.TransactionManager.StartTransaction();

            List<LineRecord> selectedLines = ReadLines(transaction, selectedIds);
            HashSet<ObjectId> taggedWallLayers = selectedLines
                .Where(line => !line.IsCap && !string.IsNullOrEmpty(line.Side))
                .Select(line => line.LayerId)
                .ToHashSet();

            Dictionary<ObjectId, string> layerNames = ReadLayerNames(transaction, database);
            HashSet<ObjectId> acceptedLayers = taggedWallLayers.Count > 0
                ? taggedWallLayers
                : selectedLines
                    .Where(line => IsWallLayerName(layerNames.GetValueOrDefault(line.LayerId)))
                    .Select(line => line.LayerId)
                    .ToHashSet();

            List<LineRecord> selectedCaps = selectedLines
                .Where(line => line.IsCap && acceptedLayers.Contains(line.LayerId))
                .ToList();
            List<LineRecord> candidates = selectedLines
                .Where(line => !line.IsCap && acceptedLayers.Contains(line.LayerId))
                .ToList();

            List<WallPair> initialPairs = FindWallPairs(candidates);
            HashSet<ObjectId> pairedIds = initialPairs
                .SelectMany(pair => new[] { pair.First.Id, pair.Second.Id })
                .ToHashSet();
            List<LineRecord> walls = candidates
                .Where(line => pairedIds.Contains(line.Id))
                .ToList();

            if (walls.Count == 0)
            {
                transaction.Commit();
                return new RepairSummary();
            }

            double typicalThickness = Median(initialPairs.Select(pair => pair.Width));
            // Chỉ nối khe lớn khi metadata xác nhận các đoạn thuộc cùng một tường.
            double collinearHealDistance = Math.Max(DefaultHealDistance, typicalThickness * 3.0);
            double windowPadding = Math.Max(10.0, typicalThickness * 2.0);
            RepairWindow window = RepairWindow.FromLines(walls, windowPadding);

            List<LineRecord> caps = ReadCapsInWindow(transaction, database, acceptedLayers, window, selectedCaps);

            Dictionary<ObjectId, double> thicknessByLine = initialPairs
                .SelectMany(pair => new[]
                {
                    new KeyValuePair<ObjectId, double>(pair.First.Id, pair.Width),
                    new KeyValuePair<ObjectId, double>(pair.Second.Id, pair.Width)
                })
                .GroupBy(item => item.Key)
                .ToDictionary(group => group.Key, group => Median(group.Select(item => item.Value)));

            // Nối mặt tường bị đứt nếu mặt đối diện vẫn liên tục qua khe.
            int healedGaps = HealSingleFaceGaps(walls, caps, window, collinearHealDistance, typicalThickness);

            healedGaps += HealCollinearGaps(initialPairs, caps, window, collinearHealDistance);

            // Xem các đoạn cùng mặt tường là một host để nhận diện đúng giao T.
            double hostChainJoinDistance = collinearHealDistance;
            List<ClosedTJunction> existingClosedTJunctions = FindClosedTJunctions(walls, initialPairs, hostChainJoinDistance);

            List<Point3d> junctionPoints = new List<Point3d>();
            SnapWallEndpoints(walls, caps, existingClosedTJunctions, window, thicknessByLine, junctionPoints);

            // Nhận diện lại giao T sau khi kéo các đầu tường vào giao điểm.
            List<ClosedTJunction> finalClosedTJunctions = FindClosedTJunctions(walls, initialPairs, hostChainJoinDistance);

            // Giữ cặp A/B ban đầu để tránh ghép nhầm mặt tường sau khi kéo dài.
            List<WallPair> repairedPairs = initialPairs
                .Where(pair => pairedIds.Contains(pair.First.Id) &&
                               pairedIds.Contains(pair.Second.Id))
                .ToList();
            Dictionary<ObjectId, List<SegmentPiece>> output = BuildOutput(walls, repairedPairs, caps, finalClosedTJunctions, window, junctionPoints);

            ReplaceResult replaceResult = ReplaceChangedLines(transaction, database, walls, output);

            int erasedCaps = EraseObsoleteCaps(transaction, caps, junctionPoints, typicalThickness);

            int normalizedLines = NormalizeCollinearResults(transaction, database, replaceResult.ResultIds, typicalThickness);

            transaction.Commit();
            return new RepairSummary
            {
                WallLineCount = walls.Count,
                ChangedLineCount = replaceResult.ChangedCount + erasedCaps + normalizedLines,
                JunctionCount = CountDistinctPoints(junctionPoints),
                HealedGapCount = healedGaps,
                ClosedTJunctionCount = finalClosedTJunctions.Count
            };
        }

        private static List<LineRecord> ReadLines(
            Transaction transaction,
            IEnumerable<ObjectId> selectedIds)
        {
            List<LineRecord> result = new List<LineRecord>();

            foreach (ObjectId id in selectedIds)
            {
                DBObject value = transaction.GetObject(id, OpenMode.ForRead);
                if (value is not Line line ||
                    line.StartPoint.DistanceTo(line.EndPoint) <= Tolerance)
                    continue;

                result.Add(new LineRecord
                {
                    Id = id,
                    Entity = line,
                    LayerId = line.LayerId,
                    Side = DrawWallHelper.GetWallSideMarker(line),
                    SegmentId = DrawWallHelper.GetWallSegmentId(line),
                    IsCap = DrawWallHelper.IsWallCap(line),
                    OriginalStart = line.StartPoint,
                    OriginalEnd = line.EndPoint,
                    Start = line.StartPoint,
                    End = line.EndPoint
                });
            }

            return result;
        }

        private static List<LineRecord> ReadCapsInWindow(
            Transaction transaction,
            Database database,
            IReadOnlySet<ObjectId> acceptedLayers,
            RepairWindow window,
            IEnumerable<LineRecord> selectedCaps)
        {
            Dictionary<ObjectId, LineRecord> result = selectedCaps.ToDictionary(cap => cap.Id);
            BlockTableRecord currentSpace = (BlockTableRecord)transaction.GetObject(
                database.CurrentSpaceId,
                OpenMode.ForRead);

            foreach (ObjectId id in currentSpace)
            {
                if (result.ContainsKey(id) ||
                    transaction.GetObject(id, OpenMode.ForRead) is not Line line ||
                    line.IsErased ||
                    !acceptedLayers.Contains(line.LayerId) ||
                    !DrawWallHelper.IsWallCap(line) ||
                    line.StartPoint.DistanceTo(line.EndPoint) <= Tolerance)
                    continue;

                Point3d middle = Midpoint(line.StartPoint, line.EndPoint);
                if (!window.Contains(middle))
                    continue;

                result[id] = new LineRecord
                {
                    Id = id,
                    Entity = line,
                    LayerId = line.LayerId,
                    Side = DrawWallHelper.GetWallSideMarker(line),
                    SegmentId = DrawWallHelper.GetWallSegmentId(line),
                    IsCap = true,
                    OriginalStart = line.StartPoint,
                    OriginalEnd = line.EndPoint,
                    Start = line.StartPoint,
                    End = line.EndPoint
                };
            }

            return result.Values.ToList();
        }

        private static Dictionary<ObjectId, string> ReadLayerNames(
            Transaction transaction,
            Database database)
        {
            Dictionary<ObjectId, string> result = new Dictionary<ObjectId, string>();
            LayerTable layers = (LayerTable)transaction.GetObject(
                database.LayerTableId,
                OpenMode.ForRead);

            foreach (ObjectId id in layers)
            {
                LayerTableRecord layer =
                    (LayerTableRecord)transaction.GetObject(id, OpenMode.ForRead);
                result[id] = layer.Name ?? string.Empty;
            }

            return result;
        }

        private static bool IsWallLayerName(string? layerName)
        {
            if (string.IsNullOrWhiteSpace(layerName))
                return false;

            string normalized = layerName.ToUpperInvariant();
            return normalized.Contains("WALL") || normalized.Contains("TUONG");
        }

        private static List<WallPair> FindWallPairs(List<LineRecord> lines)
        {
            List<WallPair> taggedPairs = FindTaggedWallPairs(lines);
            HashSet<ObjectId> taggedPairIds = taggedPairs
                .SelectMany(pair => new[] { pair.First.Id, pair.Second.Id })
                .ToHashSet();

            // Line cũ không có SegmentId mới được ghép cặp bằng hình học.
            List<LineRecord> legacyLines = lines
                .Where(line => string.IsNullOrEmpty(line.SegmentId) &&
                               !taggedPairIds.Contains(line.Id))
                .ToList();
            taggedPairs.AddRange(FindLegacyWallPairs(legacyLines));
            return taggedPairs;
        }

        private static List<WallPair> FindTaggedWallPairs(List<LineRecord> lines)
        {
            List<WallPair> result = new List<WallPair>();

            foreach (IGrouping<string, LineRecord> segment in lines
                .Where(line => !string.IsNullOrEmpty(line.SegmentId))
                .GroupBy(line => line.SegmentId!))
            {
                List<PairOption> options = new List<PairOption>();
                List<LineRecord> members = segment.ToList();

                for (int i = 0; i < members.Count; i++)
                {
                    for (int j = i + 1; j < members.Count; j++)
                    {
                        LineRecord first = members[i];
                        LineRecord second = members[j];
                        if (!CanBeGeometricWallPair(first, second))
                            continue;

                        double width = DistancePointToInfiniteLine(second.Start, first);
                        double overlap = ProjectedOverlap(first, second);
                        if (width <= Tolerance ||
                            width > MaximumSupportedThickness ||
                            overlap <= Tolerance)
                            continue;

                        options.Add(new PairOption
                        {
                            First = first,
                            Second = second,
                            Width = width,
                            Overlap = overlap
                        });
                    }
                }

                foreach (PairOption option in options
                    .OrderByDescending(item => item.Overlap)
                    .ThenBy(item => item.Width)
                    .ThenBy(item => MakePairKey(item.First.Id, item.Second.Id)))
                {
                    result.Add(new WallPair
                    {
                        First = option.First,
                        Second = option.Second,
                        Width = option.Width
                    });
                }
            }

            return result;
        }

        private static List<WallPair> FindLegacyWallPairs(List<LineRecord> lines)
        {
            Dictionary<ObjectId, PairCandidate> nearest =
                new Dictionary<ObjectId, PairCandidate>();

            for (int i = 0; i < lines.Count; i++)
            {
                for (int j = i + 1; j < lines.Count; j++)
                {
                    LineRecord first = lines[i];
                    LineRecord second = lines[j];
                    if (!CanBeGeometricWallPair(first, second))
                        continue;

                    double width = DistancePointToInfiniteLine(second.Start, first);
                    double overlap = ProjectedOverlap(first, second);
                    if (width <= Tolerance || width > MaximumSupportedThickness)
                        continue;
                    if (overlap <= Tolerance)
                        continue;

                    UpdateNearest(nearest, first, second, width, overlap);
                    UpdateNearest(nearest, second, first, width, overlap);
                }
            }

            List<WallPair> result = new List<WallPair>();
            HashSet<string> handled = new HashSet<string>();

            foreach (LineRecord line in lines)
            {
                if (!nearest.TryGetValue(line.Id, out PairCandidate? pair) ||
                    !nearest.TryGetValue(pair.Other.Id, out PairCandidate? reverse) ||
                    reverse.Other.Id != line.Id)
                    continue;

                string key = MakePairKey(line.Id, pair.Other.Id);
                if (!handled.Add(key))
                    continue;

                result.Add(new WallPair
                {
                    First = line,
                    Second = pair.Other,
                    Width = pair.Width
                });
            }

            return result;
        }

        private static bool CanBeGeometricWallPair(LineRecord first, LineRecord second)
        {
            return first.LayerId == second.LayerId &&
                   AreCoplanar(first, second) &&
                   AreParallel(first, second) &&
                   CanFormWallPair(first, second);
        }

        private static void UpdateNearest(
            IDictionary<ObjectId, PairCandidate> nearest,
            LineRecord source,
            LineRecord other,
            double width,
            double overlap)
        {
            if (!nearest.TryGetValue(source.Id, out PairCandidate? current) ||
                width < current.Width - Tolerance ||
                (Math.Abs(width - current.Width) <= Tolerance &&
                 (overlap > current.Overlap + Tolerance ||
                  (Math.Abs(overlap - current.Overlap) <= Tolerance &&
                   string.CompareOrdinal(other.Id.ToString(), current.Other.Id.ToString()) < 0))))
            {
                nearest[source.Id] = new PairCandidate
                {
                    Other = other,
                    Width = width,
                    Overlap = overlap
                };
            }
        }

        private static string MakePairKey(ObjectId first, ObjectId second)
        {
            string a = first.ToString();
            string b = second.ToString();
            return string.CompareOrdinal(a, b) <= 0 ? a + "|" + b : b + "|" + a;
        }

        private static int HealCollinearGaps(
            IReadOnlyList<WallPair> wallPairs,
            IReadOnlyCollection<LineRecord> caps,
            RepairWindow window,
            double searchDistance)
        {
            int healed = 0;

            for (int i = 0; i < wallPairs.Count; i++)
            {
                for (int j = i + 1; j < wallPairs.Count; j++)
                {
                    WallPair firstWall = wallPairs[i];
                    WallPair secondWall = wallPairs[j];
                    if (WallPairsShareFace(firstWall, secondWall) ||
                        !WallPairsCanHeal(firstWall, secondWall) ||
                        firstWall.First.LayerId != secondWall.First.LayerId ||
                        !AreCoplanar(firstWall.First, secondWall.First))
                        continue;

                    if (!TryMatchWallFaces(
                            firstWall,
                            secondWall,
                            out LineRecord firstFaceA,
                            out LineRecord secondFaceA,
                            out LineRecord firstFaceB,
                            out LineRecord secondFaceB))
                        continue;

                    if (!AreCollinear(firstFaceA, secondFaceA) ||
                        !AreCollinear(firstFaceB, secondFaceB) ||
                        ProjectedOverlap(firstFaceA, secondFaceA) > Tolerance ||
                        ProjectedOverlap(firstFaceB, secondFaceB) > Tolerance)
                        continue;

                    EndpointMatch faceAMatch = GetClosestEndpoints(firstFaceA, secondFaceA);
                    EndpointMatch faceBMatch = GetClosestEndpoints(firstFaceB, secondFaceB);
                    double localThickness = Math.Max(firstWall.Width, secondWall.Width);
                    bool metadataConfirmsSameWall =
                        !string.IsNullOrEmpty(GetPairSegmentId(firstWall));
                    double localSearchDistance = metadataConfirmsSameWall
                        ? Math.Min(searchDistance, localThickness * 3.0)
                        : Math.Max(5.0, Math.Min(25.0, localThickness * 0.1));
                    if (faceAMatch.Distance <= Tolerance ||
                        faceBMatch.Distance <= Tolerance ||
                        faceAMatch.Distance > localSearchDistance ||
                        faceBMatch.Distance > localSearchDistance ||
                        Math.Abs(faceAMatch.Distance - faceBMatch.Distance) >
                            Math.Max(5.0, Math.Max(firstWall.Width, secondWall.Width) * 0.1))
                        continue;

                    Point3d jointA = Midpoint(faceAMatch.FirstPoint, faceAMatch.SecondPoint);
                    Point3d jointB = Midpoint(faceBMatch.FirstPoint, faceBMatch.SecondPoint);
                    Point3d wallCenter = Midpoint(jointA, jointB);
                    if (!window.Contains(wallCenter) ||
                        HasProtectingCap(caps, wallCenter, Math.Max(firstWall.Width, secondWall.Width)))
                        continue;

                    SetEndpoint(firstFaceA, faceAMatch.FirstIsStart, jointA);
                    SetEndpoint(secondFaceA, faceAMatch.SecondIsStart, jointA);
                    SetEndpoint(firstFaceB, faceBMatch.FirstIsStart, jointB);
                    SetEndpoint(secondFaceB, faceBMatch.SecondIsStart, jointB);
                    healed++;
                }
            }

            return healed;
        }

        private static int HealSingleFaceGaps(
            IReadOnlyList<LineRecord> walls,
            IReadOnlyCollection<LineRecord> caps,
            RepairWindow window,
            double searchDistance,
            double typicalThickness)
        {
            int healed = 0;
            double maximumGap = Math.Min(
                searchDistance,
                Math.Max(25.0, typicalThickness * 3.0));

            for (int i = 0; i < walls.Count; i++)
            {
                LineRecord first = walls[i];
                if (string.IsNullOrEmpty(first.SegmentId) ||
                    string.IsNullOrEmpty(first.Side))
                    continue;

                for (int j = i + 1; j < walls.Count; j++)
                {
                    LineRecord second = walls[j];
                    if (first.LayerId != second.LayerId ||
                        first.SegmentId != second.SegmentId ||
                        first.Side != second.Side ||
                        !AreCoplanar(first, second) ||
                        !AreCollinear(first, second) ||
                        ProjectedOverlap(first, second) > Tolerance)
                        continue;

                    EndpointMatch match = GetClosestEndpoints(first, second);
                    if (match.Distance <= Tolerance || match.Distance > maximumGap)
                        continue;

                    Point3d joint = Midpoint(match.FirstPoint, match.SecondPoint);
                    if (!window.Contains(joint) ||
                        HasProtectingCap(caps, joint, typicalThickness))
                        continue;

                    // Không nối khe nếu mặt đối diện không chạy liên tục qua khe đó.
                    bool oppositeFaceIsContinuous = walls.Any(opposite =>
                        opposite.Id != first.Id &&
                        opposite.Id != second.Id &&
                        opposite.LayerId == first.LayerId &&
                        opposite.SegmentId == first.SegmentId &&
                        !string.IsNullOrEmpty(opposite.Side) &&
                        opposite.Side != first.Side &&
                        AreParallel(first, opposite) &&
                        ProjectionFallsOnSegment(match.FirstPoint, opposite) &&
                        ProjectionFallsOnSegment(match.SecondPoint, opposite));
                    if (!oppositeFaceIsContinuous)
                        continue;

                    SetEndpoint(first, match.FirstIsStart, joint);
                    SetEndpoint(second, match.SecondIsStart, joint);
                    healed++;
                }
            }

            return healed;
        }

        private static bool ProjectionFallsOnSegment(
            Point3d point,
            LineRecord line)
        {
            Vector3d direction = (line.End - line.Start).GetNormal();
            double station = (point - line.Start).DotProduct(direction);
            double length = line.Start.DistanceTo(line.End);
            return station >= -Tolerance && station <= length + Tolerance;
        }

        private static bool WallPairsShareFace(WallPair first, WallPair second)
        {
            return first.First.Id == second.First.Id ||
                   first.First.Id == second.Second.Id ||
                   first.Second.Id == second.First.Id ||
                   first.Second.Id == second.Second.Id;
        }

        private static bool WallPairsCanHeal(WallPair first, WallPair second)
        {
            string? firstSegment = GetPairSegmentId(first);
            string? secondSegment = GetPairSegmentId(second);
            bool hasMetadata = !string.IsNullOrEmpty(firstSegment) ||
                               !string.IsNullOrEmpty(secondSegment);
            return !hasMetadata ||
                   (!string.IsNullOrEmpty(firstSegment) && firstSegment == secondSegment);
        }

        private static string? GetPairSegmentId(WallPair pair)
        {
            return !string.IsNullOrEmpty(pair.First.SegmentId)
                ? pair.First.SegmentId
                : pair.Second.SegmentId;
        }

        private static bool TryMatchWallFaces(
            WallPair firstWall,
            WallPair secondWall,
            out LineRecord firstFaceA,
            out LineRecord secondFaceA,
            out LineRecord firstFaceB,
            out LineRecord secondFaceB)
        {
            firstFaceA = firstWall.First;
            firstFaceB = firstWall.Second;

            bool directCompatible = SidesAreCompatible(firstWall.First, secondWall.First) &&
                                    SidesAreCompatible(firstWall.Second, secondWall.Second);
            bool crossedCompatible = SidesAreCompatible(firstWall.First, secondWall.Second) &&
                                     SidesAreCompatible(firstWall.Second, secondWall.First);

            double directScore = directCompatible
                ? GetClosestEndpoints(firstWall.First, secondWall.First).Distance +
                  GetClosestEndpoints(firstWall.Second, secondWall.Second).Distance
                : double.MaxValue;
            double crossedScore = crossedCompatible
                ? GetClosestEndpoints(firstWall.First, secondWall.Second).Distance +
                  GetClosestEndpoints(firstWall.Second, secondWall.First).Distance
                : double.MaxValue;

            if (directScore == double.MaxValue && crossedScore == double.MaxValue)
            {
                secondFaceA = secondWall.First;
                secondFaceB = secondWall.Second;
                return false;
            }

            if (directScore <= crossedScore)
            {
                secondFaceA = secondWall.First;
                secondFaceB = secondWall.Second;
            }
            else
            {
                secondFaceA = secondWall.Second;
                secondFaceB = secondWall.First;
            }

            return true;
        }

        private static bool HasProtectingCap(
            IEnumerable<LineRecord> caps,
            Point3d wallCenter,
            double wallThickness)
        {
            double radius = Math.Max(5.0, wallThickness * 0.75);
            return caps.Any(cap => Midpoint(cap.Start, cap.End).DistanceTo(wallCenter) <= radius);
        }

        private static List<ClosedTJunction> FindClosedTJunctions(
            IReadOnlyList<LineRecord> lines,
            IReadOnlyList<WallPair> wallPairs,
            double hostChainJoinDistance)
        {
            List<ClosedTJunction> result = new List<ClosedTJunction>();
            HashSet<string> handled = new HashSet<string>();
            List<HostChain> hostChains = BuildHostChains(lines, hostChainJoinDistance);

            foreach (WallPair branchPair in wallPairs)
            {
                foreach (WallEnd wallEnd in GetWallEnds(branchPair))
                {
                    HostChain? host = hostChains
                        .Where(chain =>
                            !chain.MemberIds.Contains(branchPair.First.Id) &&
                            !chain.MemberIds.Contains(branchPair.Second.Id) &&
                            chain.Reference.LayerId == branchPair.First.LayerId &&
                            AreCoplanar(branchPair.First, chain.Reference) &&
                            !AreParallel(branchPair.First, chain.Reference) &&
                            IsPointOnHostChainInterior(wallEnd.FirstPoint, chain) &&
                            IsPointOnHostChainInterior(wallEnd.SecondPoint, chain))
                        .OrderBy(chain =>
                            DistancePointToInfiniteLine(wallEnd.FirstPoint, chain.Reference) +
                            DistancePointToInfiniteLine(wallEnd.SecondPoint, chain.Reference))
                        .ThenBy(chain => chain.Reference.Id.ToString(), StringComparer.Ordinal)
                        .FirstOrDefault();
                    if (host == null)
                        continue;

                    string firstEndpointKey = MakeEndpointKey(
                        wallEnd.FirstLine.Id,
                        wallEnd.FirstIsStart);
                    string secondEndpointKey = MakeEndpointKey(
                        wallEnd.SecondLine.Id,
                        wallEnd.SecondIsStart);
                    string hostKey = string.Join(
                        ",",
                        host.MemberIds
                            .Select(id => id.ToString())
                            .OrderBy(id => id, StringComparer.Ordinal));
                    string junctionKey = hostKey + "|" +
                        (string.CompareOrdinal(firstEndpointKey, secondEndpointKey) <= 0
                            ? firstEndpointKey + "|" + secondEndpointKey
                            : secondEndpointKey + "|" + firstEndpointKey);
                    if (!handled.Add(junctionKey))
                        continue;

                    result.Add(new ClosedTJunction
                    {
                        Host = host.Reference,
                        HostChainIds = host.MemberIds,
                        FirstBranchLine = wallEnd.FirstLine,
                        FirstBranchIsStart = wallEnd.FirstIsStart,
                        FirstPoint = wallEnd.FirstPoint,
                        SecondBranchLine = wallEnd.SecondLine,
                        SecondBranchIsStart = wallEnd.SecondIsStart,
                        SecondPoint = wallEnd.SecondPoint
                    });
                }
            }

            return result;
        }

        // Gộp logic các đoạn đồng tuyến cùng metadata thành một mặt tường host.
        private static List<HostChain> BuildHostChains(
            IReadOnlyList<LineRecord> lines,
            double joinDistance)
        {
            List<HostChain> chains = new List<HostChain>();
            bool[] used = new bool[lines.Count];

            for (int i = 0; i < lines.Count; i++)
            {
                if (used[i])
                    continue;

                List<LineRecord> group = new List<LineRecord> { lines[i] };
                used[i] = true;

                bool addedAny = true;
                while (addedAny)
                {
                    addedAny = false;
                    for (int j = 0; j < lines.Count; j++)
                    {
                        if (used[j])
                            continue;

                        if (group.Any(member =>
                            CanChainHostSegments(member, lines[j], joinDistance)))
                        {
                            group.Add(lines[j]);
                            used[j] = true;
                            addedAny = true;
                        }
                    }
                }

                LineRecord reference = group[0];
                Vector3d axis = (reference.End - reference.Start).GetNormal();
                Point3d origin = reference.Start;
                double minStation = double.MaxValue;
                double maxStation = double.MinValue;
                Point3d chainStart = reference.Start;
                Point3d chainEnd = reference.End;

                foreach (LineRecord member in group)
                {
                    foreach (Point3d point in new[] { member.Start, member.End })
                    {
                        double station = (point - origin).DotProduct(axis);
                        if (station < minStation)
                        {
                            minStation = station;
                            chainStart = point;
                        }
                        if (station > maxStation)
                        {
                            maxStation = station;
                            chainEnd = point;
                        }
                    }
                }

                chains.Add(new HostChain
                {
                    Reference = reference,
                    MemberIds = group.Select(member => member.Id).ToHashSet(),
                    Start = chainStart,
                    End = chainEnd
                });
            }

            return chains;
        }

        private static bool CanChainHostSegments(
            LineRecord first,
            LineRecord second,
            double joinDistance)
        {
            if (first.Id == second.Id ||
                first.LayerId != second.LayerId ||
                !AreCollinear(first, second))
                return false;

            bool hasSegmentMetadata = !string.IsNullOrEmpty(first.SegmentId) ||
                                       !string.IsNullOrEmpty(second.SegmentId);
            if (hasSegmentMetadata)
            {
                // Có metadata: chỉ ghép các đoạn cùng SegmentId và cùng mặt A/B.
                if (string.IsNullOrEmpty(first.SegmentId) ||
                    first.SegmentId != second.SegmentId ||
                    string.IsNullOrEmpty(first.Side) ||
                    first.Side != second.Side)
                    return false;

                return SegmentGap(first, second) <= joinDistance;
            }

            // Không có metadata: chỉ cho phép nối một khe sai số rất nhỏ.
            double legacyGap = Math.Max(5.0, Math.Min(25.0, joinDistance * 0.05));
            return SegmentGap(first, second) <= legacyGap;
        }

        private static double SegmentGap(LineRecord first, LineRecord second)
        {
            Vector3d axis = (first.End - first.Start).GetNormal();
            double firstMax = first.Start.DistanceTo(first.End);
            double secondStartStation = (second.Start - first.Start).DotProduct(axis);
            double secondEndStation = (second.End - first.Start).DotProduct(axis);
            double secondMin = Math.Min(secondStartStation, secondEndStation);
            double secondMax = Math.Max(secondStartStation, secondEndStation);

            if (secondMax < 0.0)
                return -secondMax;
            if (secondMin > firstMax)
                return secondMin - firstMax;
            return 0.0;
        }

        private static bool IsPointOnHostChainInterior(Point3d point, HostChain chain)
        {
            double length = chain.Start.DistanceTo(chain.End);
            double padding = Math.Max(
                Tolerance * 10.0,
                Math.Min(1.0, length * 0.001));
            if (!IsPointOnSegment(point, chain.Start, chain.End, 1.0))
                return false;

            Vector3d axis = (chain.End - chain.Start).GetNormal();
            double station = (point - chain.Start).DotProduct(axis);
            return station > padding && station < length - padding;
        }

        private static List<WallEnd> GetWallEnds(WallPair pair)
        {
            double directScore =
                pair.First.Start.DistanceTo(pair.Second.Start) +
                pair.First.End.DistanceTo(pair.Second.End);
            double crossedScore =
                pair.First.Start.DistanceTo(pair.Second.End) +
                pair.First.End.DistanceTo(pair.Second.Start);

            if (directScore <= crossedScore)
            {
                return new List<WallEnd>
                {
                    CreateWallEnd(pair.First, true, pair.Second, true),
                    CreateWallEnd(pair.First, false, pair.Second, false)
                };
            }

            return new List<WallEnd>
            {
                CreateWallEnd(pair.First, true, pair.Second, false),
                CreateWallEnd(pair.First, false, pair.Second, true)
            };
        }

        private static WallEnd CreateWallEnd(
            LineRecord first,
            bool firstIsStart,
            LineRecord second,
            bool secondIsStart)
        {
            return new WallEnd
            {
                FirstLine = first,
                FirstIsStart = firstIsStart,
                FirstPoint = firstIsStart ? first.Start : first.End,
                SecondLine = second,
                SecondIsStart = secondIsStart,
                SecondPoint = secondIsStart ? second.Start : second.End
            };
        }

        private static bool IsClosedTJunctionEndpoint(
            IEnumerable<ClosedTJunction> junctions,
            ObjectId lineId,
            bool isStart)
        {
            return junctions.Any(junction =>
                (junction.FirstBranchLine.Id == lineId &&
                 junction.FirstBranchIsStart == isStart) ||
                (junction.SecondBranchLine.Id == lineId &&
                 junction.SecondBranchIsStart == isStart));
        }

        private static int SnapWallEndpoints(
            List<LineRecord> lines,
            IReadOnlyCollection<LineRecord> caps,
            IReadOnlyCollection<ClosedTJunction> closedTJunctions,
            RepairWindow window,
            IReadOnlyDictionary<ObjectId, double> thicknessByLine,
            ICollection<Point3d> junctionPoints)
        {
            List<EndpointMoveCandidate> candidates = new List<EndpointMoveCandidate>();

            foreach (LineRecord line in lines)
            {
                for (int endpointIndex = 0; endpointIndex < 2; endpointIndex++)
                {
                    Point3d endpoint = endpointIndex == 0 ? line.Start : line.End;
                    Point3d otherEndpoint = endpointIndex == 0 ? line.End : line.Start;
                    bool sourceIsStart = endpointIndex == 0;
                    double sourceSearchDistance = GetJunctionSearchDistance(
                        line.Id,
                        thicknessByLine);
                    bool endpointIsClosedByCap = HasCapAtEndpoint(
                        caps,
                        line,
                        endpoint,
                        GetWallThickness(line.Id, thicknessByLine));
                    bool endpointIsClosedTJunction = IsClosedTJunctionEndpoint(
                        closedTJunctions,
                        line.Id,
                        sourceIsStart);
                    bool endpointAlreadyTouchesWall = EndpointTouchesExistingWall(
                        line,
                        endpoint,
                        lines);
                    bool sourceEndpointCanMove =
                        !endpointIsClosedByCap &&
                        !endpointIsClosedTJunction &&
                        !endpointAlreadyTouchesWall;

                    foreach (LineRecord target in lines)
                    {
                        if (target.Id == line.Id ||
                            target.LayerId != line.LayerId ||
                            !AreCoplanar(line, target) ||
                            AreParallel(line, target))
                            continue;

                        if (!TryInfiniteIntersection(line, target, out Point3d intersection) ||
                            !window.Contains(intersection))
                            continue;

                        double fromEndpoint = endpoint.DistanceTo(intersection);
                        if (fromEndpoint > sourceSearchDistance ||
                            fromEndpoint > otherEndpoint.DistanceTo(intersection) + Tolerance)
                            continue;

                        double targetStartDistance = target.Start.DistanceTo(intersection);
                        double targetEndDistance = target.End.DistanceTo(intersection);
                        bool targetStartIsClosest = targetStartDistance <= targetEndDistance;
                        double targetEndpointDistance = Math.Min(
                            targetStartDistance,
                            targetEndDistance);
                        bool onTarget = IsPointOnSegment(
                            intersection,
                            target.Start,
                            target.End,
                            1.0);
                        double endpointTolerance = Math.Max(
                            Tolerance * 10.0,
                            Math.Min(1.0, target.Start.DistanceTo(target.End) * 0.001));
                        bool onTargetInterior = onTarget &&
                            targetStartDistance > endpointTolerance &&
                            targetEndDistance > endpointTolerance;
                        double targetSearchDistance = GetJunctionSearchDistance(
                            target.Id,
                            thicknessByLine);
                        Point3d targetEndpoint = targetStartIsClosest
                            ? target.Start
                            : target.End;
                        bool targetEndpointCanMove =
                            !HasCapAtEndpoint(
                                caps,
                                target,
                                targetEndpoint,
                                GetWallThickness(target.Id, thicknessByLine)) &&
                            !IsClosedTJunctionEndpoint(
                                closedTJunctions,
                                target.Id,
                                targetStartIsClosest) &&
                            !EndpointTouchesExistingWall(
                                target,
                                targetEndpoint,
                                lines);

                        if (sourceEndpointCanMove &&
                            targetEndpointCanMove &&
                            !onTargetInterior &&
                            targetEndpointDistance <= targetSearchDistance &&
                            IsOutwardExtension(endpoint, otherEndpoint, intersection) &&
                            IsOutwardExtension(
                                targetEndpoint,
                                targetStartIsClosest ? target.End : target.Start,
                                intersection) &&
                            HasCompatibleCornerMovement(
                                endpoint,
                                otherEndpoint,
                                targetStartIsClosest ? target.Start : target.End,
                                targetStartIsClosest ? target.End : target.Start,
                                intersection))
                        {
                            candidates.Add(new EndpointMoveCandidate
                            {
                                Point = intersection,
                                IsCorner = true,
                                Score = fromEndpoint + targetEndpointDistance,
                                StableKey = MakeEndpointMoveKey(
                                    line,
                                    sourceIsStart,
                                    target,
                                    targetStartIsClosest),
                                Endpoints = new List<EndpointReference>
                                {
                                    new EndpointReference { Line = line, IsStart = sourceIsStart },
                                    new EndpointReference { Line = target, IsStart = targetStartIsClosest }
                                }
                            });
                        }
                        else if (sourceEndpointCanMove &&
                                 onTargetInterior &&
                                 IsOutwardExtension(endpoint, otherEndpoint, intersection))
                        {
                            // Dừng đầu tường tại mặt host gần nhất.
                            candidates.Add(new EndpointMoveCandidate
                            {
                                Point = intersection,
                                IsCorner = false,
                                Score = fromEndpoint,
                                StableKey = MakeEndpointMoveKey(
                                    line,
                                    sourceIsStart,
                                    target,
                                    targetStartIsClosest),
                                Endpoints = new List<EndpointReference>
                                {
                                    new EndpointReference { Line = line, IsStart = sourceIsStart }
                                }
                            });
                        }
                    }
                }
            }

            int changed = 0;
            HashSet<string> claimedEndpoints = new HashSet<string>();
            foreach (EndpointMoveCandidate candidate in candidates
                .OrderBy(item => item.IsCorner ? 0 : 1)
                .ThenBy(item => item.Score)
                .ThenBy(item => item.StableKey, StringComparer.Ordinal))
            {
                List<string> endpointKeys = candidate.Endpoints
                    .Select(endpoint => MakeEndpointKey(endpoint.Line.Id, endpoint.IsStart))
                    .ToList();
                if (endpointKeys.Any(claimedEndpoints.Contains))
                    continue;

                foreach (EndpointReference endpoint in candidate.Endpoints)
                {
                    SetEndpoint(endpoint.Line, endpoint.IsStart, candidate.Point);
                    claimedEndpoints.Add(MakeEndpointKey(endpoint.Line.Id, endpoint.IsStart));
                    changed++;
                }
                junctionPoints.Add(candidate.Point);
            }

            return changed;
        }

        private static bool EndpointTouchesExistingWall(
            LineRecord source,
            Point3d endpoint,
            IEnumerable<LineRecord> lines)
        {
            double connectionTolerance = Tolerance * 10.0;

            foreach (LineRecord target in lines)
            {
                if (target.Id == source.Id ||
                    target.LayerId != source.LayerId ||
                    !AreCoplanar(source, target) ||
                    AreParallel(source, target))
                    continue;

                if (DistancePointToInfiniteLine(endpoint, target) <= connectionTolerance &&
                    IsPointOnSegment(
                        endpoint,
                        target.Start,
                        target.End,
                        connectionTolerance))
                    return true;
            }

            return false;
        }

        private static bool IsOutwardExtension(
            Point3d endpoint,
            Point3d otherEndpoint,
            Point3d target)
        {
            Vector3d outward = endpoint - otherEndpoint;
            if (outward.Length <= Tolerance)
                return false;

            return (target - endpoint).DotProduct(outward.GetNormal()) >= -Tolerance;
        }

        private static double GetJunctionSearchDistance(
            ObjectId lineId,
            IReadOnlyDictionary<ObjectId, double> thicknessByLine)
        {
            return Math.Max(10.0, GetWallThickness(lineId, thicknessByLine) * 2.0);
        }

        private static double GetWallThickness(
            ObjectId lineId,
            IReadOnlyDictionary<ObjectId, double> thicknessByLine)
        {
            return thicknessByLine.TryGetValue(lineId, out double thickness)
                ? thickness
                : 0.0;
        }

        private static bool HasCapAtEndpoint(
            IEnumerable<LineRecord> caps,
            LineRecord wallFace,
            Point3d endpoint,
            double wallThickness)
        {
            double endpointTolerance = Math.Max(
                Tolerance * 10.0,
                Math.Min(5.0, Math.Max(1.0, wallThickness * 0.025)));

            return caps.Any(cap =>
                cap.LayerId == wallFace.LayerId &&
                AreCoplanar(wallFace, cap) &&
                (cap.Start.DistanceTo(endpoint) <= endpointTolerance ||
                 cap.End.DistanceTo(endpoint) <= endpointTolerance));
        }

        private static string MakeEndpointMoveKey(
            LineRecord source,
            bool sourceIsStart,
            LineRecord target,
            bool targetIsStart)
        {
            return MakeEndpointKey(source.Id, sourceIsStart) + "|" +
                   MakeEndpointKey(target.Id, targetIsStart);
        }

        private static string MakeEndpointKey(ObjectId id, bool isStart)
        {
            return id + (isStart ? ":S" : ":E");
        }

        private static Dictionary<ObjectId, List<SegmentPiece>> BuildOutput(
            List<LineRecord> lines,
            List<WallPair> wallPairs,
            IReadOnlyCollection<LineRecord> caps,
            IReadOnlyCollection<ClosedTJunction> closedTJunctions,
            RepairWindow window,
            ICollection<Point3d> junctionPoints)
        {
            Dictionary<ObjectId, List<Point3d>> cuts = lines.ToDictionary(
                line => line.Id,
                line => new List<Point3d> { line.Start, line.End });

            for (int i = 0; i < lines.Count; i++)
            {
                for (int j = i + 1; j < lines.Count; j++)
                {
                    LineRecord first = lines[i];
                    LineRecord second = lines[j];
                    if (first.LayerId != second.LayerId ||
                        !AreCoplanar(first, second) ||
                        AreParallel(first, second))
                        continue;

                    if (TrySegmentIntersection(first, second, out Point3d intersection) &&
                        window.Contains(intersection))
                    {
                        cuts[first.Id].Add(intersection);
                        cuts[second.Id].Add(intersection);
                        junctionPoints.Add(intersection);
                    }
                }
            }

            Dictionary<ObjectId, List<SegmentPiece>> output =
                new Dictionary<ObjectId, List<SegmentPiece>>();

            foreach (LineRecord line in lines)
            {
                List<Point3d> ordered = cuts[line.Id]
                    .OrderBy(point => Station(line, point))
                    .Aggregate(
                        new List<Point3d>(),
                        (points, point) =>
                        {
                            if (points.Count == 0 || points[^1].DistanceTo(point) > Tolerance)
                                points.Add(point);
                            return points;
                        });

                List<SegmentPiece> pieces = new List<SegmentPiece>();
                SegmentPiece? active = null;

                for (int i = 0; i < ordered.Count - 1; i++)
                {
                    Point3d start = ordered[i];
                    Point3d end = ordered[i + 1];
                    if (start.DistanceTo(end) <= Tolerance)
                        continue;

                    Point3d middle = Midpoint(start, end);
                    bool remove = window.Contains(middle) &&
                        (IsInsideAnotherWall(middle, line, wallPairs) ||
                         IsInsideClosedTJunction(
                             middle,
                             line,
                             closedTJunctions) ||
                         IsOnObsoleteJunctionCap(
                             middle,
                             line,
                             wallPairs,
                             caps));

                    if (remove)
                    {
                        if (active != null)
                        {
                            pieces.Add(active);
                            active = null;
                        }
                        continue;
                    }

                    if (active == null)
                        active = new SegmentPiece { Start = start, End = end };
                    else
                        active.End = end;
                }

                if (active != null)
                    pieces.Add(active);

                output[line.Id] = pieces;
            }

            return output;
        }

        private static bool IsInsideClosedTJunction(
            Point3d point,
            LineRecord source,
            IEnumerable<ClosedTJunction> junctions)
        {
            foreach (ClosedTJunction junction in junctions)
            {
                if (!junction.HostChainIds.Contains(source.Id))
                    continue;

                double padding = Math.Max(
                    Tolerance * 10.0,
                    Math.Min(1.0, junction.FirstPoint.DistanceTo(junction.SecondPoint) * 0.01));
                double distanceToFirst = point.DistanceTo(junction.FirstPoint);
                double distanceToSecond = point.DistanceTo(junction.SecondPoint);
                if (distanceToFirst <= padding || distanceToSecond <= padding)
                    continue;

                if (Math.Abs(
                        distanceToFirst + distanceToSecond -
                        junction.FirstPoint.DistanceTo(junction.SecondPoint)) <= 1.0)
                    return true;
            }

            return false;
        }

        private static bool IsOnObsoleteJunctionCap(
            Point3d point,
            LineRecord source,
            IReadOnlyCollection<WallPair> wallPairs,
            IReadOnlyCollection<LineRecord> caps,
            double pointTolerance = 1.0)
        {
            foreach (LineRecord cap in caps)
            {
                if (cap.LayerId != source.LayerId ||
                    !AreCoplanar(source, cap) ||
                    !AreParallel(source, cap) ||
                    !IsPointOnSegment(point, cap.Start, cap.End, pointTolerance))
                    continue;

                double capPadding = Math.Max(
                    Tolerance * 10.0,
                    Math.Min(1.0, cap.Start.DistanceTo(cap.End) * 0.01));
                if (!ProjectionIsStrictlyInside(point, cap, capPadding))
                    continue;

                if (wallPairs.Any(pair => CapClosesWallPair(cap, pair)))
                    return true;
            }

            return false;
        }

        private static bool CapClosesWallPair(LineRecord cap, WallPair pair)
        {
            if (cap.LayerId != pair.First.LayerId ||
                !AreCoplanar(cap, pair.First) ||
                !AreCoplanar(cap, pair.Second))
                return false;

            double endpointTolerance = Math.Max(
                Tolerance * 10.0,
                Math.Min(5.0, Math.Max(1.0, pair.Width * 0.025)));
            bool direct =
                IsNearEitherEndpoint(cap.Start, pair.First, endpointTolerance) &&
                IsNearEitherEndpoint(cap.End, pair.Second, endpointTolerance);
            bool reversed =
                IsNearEitherEndpoint(cap.Start, pair.Second, endpointTolerance) &&
                IsNearEitherEndpoint(cap.End, pair.First, endpointTolerance);
            return direct || reversed;
        }

        private static bool IsNearEitherEndpoint(
            Point3d point,
            LineRecord line,
            double tolerance)
        {
            return point.DistanceTo(line.Start) <= tolerance ||
                   point.DistanceTo(line.End) <= tolerance;
        }

        private static bool IsInsideAnotherWall(
            Point3d point,
            LineRecord source,
            IEnumerable<WallPair> pairs)
        {
            foreach (WallPair pair in pairs)
            {
                if (pair.First.Id == source.Id || pair.Second.Id == source.Id ||
                    pair.First.LayerId != source.LayerId ||
                    !AreCoplanar(source, pair.First) ||
                    !AreCoplanar(source, pair.Second))
                    continue;

                double firstDistance = DistancePointToInfiniteLine(point, pair.First);
                double secondDistance = DistancePointToInfiniteLine(point, pair.Second);
                double widthTolerance = Math.Max(2.0, pair.Width * 0.02);
                if (Math.Abs(firstDistance + secondDistance - pair.Width) > widthTolerance)
                    continue;

                double endpointPadding = Math.Max(
                    Tolerance * 10.0,
                    Math.Min(5.0, pair.Width * 0.025));
                if (ProjectionIsStrictlyInside(point, pair.First, endpointPadding) &&
                    ProjectionIsStrictlyInside(point, pair.Second, endpointPadding))
                    return true;
            }

            return false;
        }

        private static ReplaceResult ReplaceChangedLines(
            Transaction transaction,
            Database database,
            IEnumerable<LineRecord> sources,
            IReadOnlyDictionary<ObjectId, List<SegmentPiece>> output)
        {
            ReplaceResult result = new ReplaceResult();

            foreach (LineRecord source in sources)
            {
                List<SegmentPiece> pieces = output[source.Id];
                if (IsUnchanged(source, pieces))
                {
                    result.ResultIds.Add(source.Id);
                    continue;
                }

                Line sourceEntity =
                    (Line)transaction.GetObject(source.Id, OpenMode.ForWrite);
                BlockTableRecord owner =
                    (BlockTableRecord)transaction.GetObject(sourceEntity.OwnerId, OpenMode.ForWrite);

                foreach (SegmentPiece piece in pieces)
                {
                    if (piece.Start.DistanceTo(piece.End) <= Tolerance)
                        continue;

                    Line replacement = new Line(piece.Start, piece.End)
                    {
                        LayerId = sourceEntity.LayerId,
                        Color = sourceEntity.Color,
                        LineWeight = sourceEntity.LineWeight,
                        LinetypeId = sourceEntity.LinetypeId,
                        LinetypeScale = sourceEntity.LinetypeScale,
                        Transparency = sourceEntity.Transparency
                    };
                    owner.AppendEntity(replacement);
                    transaction.AddNewlyCreatedDBObject(replacement, true);
                    DrawWallHelper.CopyWallMetadata(
                        transaction,
                        database,
                        sourceEntity,
                        replacement);
                    result.ResultIds.Add(replacement.ObjectId);
                }

                sourceEntity.Erase(true);
                result.ChangedCount++;
            }

            return result;
        }

        private static int NormalizeCollinearResults(
            Transaction transaction,
            Database database,
            IEnumerable<ObjectId> candidateIds,
            double wallThickness)
        {
            List<NormalizeLine> lines = new List<NormalizeLine>();

            foreach (ObjectId id in candidateIds.Distinct())
            {
                try
                {
                    DBObject value = transaction.GetObject(id, OpenMode.ForRead);
                    if (value.IsErased || value is not Line line ||
                        line.StartPoint.DistanceTo(line.EndPoint) <= Tolerance ||
                        DrawWallHelper.IsWallCap(line))
                        continue;

                    lines.Add(new NormalizeLine
                    {
                        Id = id,
                        Entity = line,
                        Start = line.StartPoint,
                        End = line.EndPoint,
                        LayerId = line.LayerId,
                        Side = DrawWallHelper.GetWallSideMarker(line),
                        SegmentId = DrawWallHelper.GetWallSegmentId(line),
                        IsCap = false
                    });
                }
                catch
                {
                    // Line nguồn có thể đã được thay thế ở bước trước.
                }
            }

            List<List<NormalizeLine>> collinearGroups = GroupCollinearLines(lines);
            // Chỉ gộp khe sai số nhỏ để không làm mất cửa hoặc lỗ mở thật.
            double joinTolerance = Math.Max(
                Tolerance * 10.0,
                Math.Min(1.0, wallThickness * 0.001));
            int normalized = 0;

            foreach (List<NormalizeLine> group in collinearGroups)
            {
                NormalizeLine seed = group[0];
                Vector3d axis = (seed.End - seed.Start).GetNormal();
                Point3d origin = seed.Start;

                List<LineInterval> intervals = group
                    .Select(line => CreateInterval(line, origin, axis))
                    .OrderBy(interval => interval.StartStation)
                    .ToList();

                List<LineIntervalCluster> clusters = new List<LineIntervalCluster>();
                foreach (LineInterval interval in intervals)
                {
                    LineIntervalCluster? active = clusters.LastOrDefault();
                    if (active == null ||
                        interval.StartStation > active.EndStation + joinTolerance)
                    {
                        clusters.Add(new LineIntervalCluster
                        {
                            StartStation = interval.StartStation,
                            EndStation = interval.EndStation,
                            Lines = new List<NormalizeLine> { interval.Line }
                        });
                    }
                    else
                    {
                        active.EndStation = Math.Max(
                            active.EndStation,
                            interval.EndStation);
                        active.Lines.Add(interval.Line);
                    }
                }

                foreach (LineIntervalCluster cluster in clusters)
                {
                    Point3d mergedStart = origin + axis * cluster.StartStation;
                    Point3d mergedEnd = origin + axis * cluster.EndStation;

                    if (cluster.Lines.Count == 1 &&
                        SameSegment(cluster.Lines[0], mergedStart, mergedEnd))
                        continue;

                    NormalizeLine source = cluster.Lines
                        .OrderBy(line => line.IsCap ? 1 : 0)
                        .ThenBy(line => string.IsNullOrEmpty(line.Side) ? 1 : 0)
                        .ThenByDescending(line => line.Start.DistanceTo(line.End))
                        .First();

                    Line sourceEntity =
                        (Line)transaction.GetObject(source.Id, OpenMode.ForRead);
                    BlockTableRecord owner =
                        (BlockTableRecord)transaction.GetObject(
                            sourceEntity.OwnerId,
                            OpenMode.ForWrite);
                    Line merged = new Line(mergedStart, mergedEnd)
                    {
                        LayerId = sourceEntity.LayerId,
                        Color = sourceEntity.Color,
                        LineWeight = sourceEntity.LineWeight,
                        LinetypeId = sourceEntity.LinetypeId,
                        LinetypeScale = sourceEntity.LinetypeScale,
                        Transparency = sourceEntity.Transparency
                    };
                    owner.AppendEntity(merged);
                    transaction.AddNewlyCreatedDBObject(merged, true);
                    DrawWallHelper.CopyWallMetadata(
                        transaction,
                        database,
                        sourceEntity,
                        merged);

                    foreach (NormalizeLine oldLine in cluster.Lines)
                    {
                        DBObject oldEntity = transaction.GetObject(
                            oldLine.Id,
                            OpenMode.ForWrite);
                        if (!oldEntity.IsErased)
                            oldEntity.Erase(true);
                    }

                    normalized++;
                }
            }

            return normalized;
        }

        private static List<List<NormalizeLine>> GroupCollinearLines(
            IReadOnlyList<NormalizeLine> lines)
        {
            List<List<NormalizeLine>> groups = new List<List<NormalizeLine>>();
            bool[] used = new bool[lines.Count];

            for (int i = 0; i < lines.Count; i++)
            {
                if (used[i])
                    continue;

                List<NormalizeLine> group = new List<NormalizeLine> { lines[i] };
                used[i] = true;

                for (int j = i + 1; j < lines.Count; j++)
                {
                    if (used[j] ||
                        lines[i].LayerId != lines[j].LayerId ||
                        !MetadataAllowsMerge(lines[i], lines[j]) ||
                        !AreCollinear(lines[i], lines[j]))
                        continue;

                    group.Add(lines[j]);
                    used[j] = true;
                }

                groups.Add(group);
            }

            return groups;
        }

        private static bool AreCollinear(NormalizeLine first, NormalizeLine second)
        {
            Vector3d firstDirection = (first.End - first.Start).GetNormal();
            Vector3d secondDirection = (second.End - second.Start).GetNormal();
            if (Math.Abs(firstDirection.DotProduct(secondDirection)) <= ParallelDotTolerance)
                return false;

            return DistancePointToInfiniteLine(second.Start, first.Start, firstDirection) <= Tolerance * 10.0 &&
                   DistancePointToInfiniteLine(second.End, first.Start, firstDirection) <= Tolerance * 10.0;
        }

        private static bool MetadataAllowsMerge(NormalizeLine first, NormalizeLine second)
        {
            if (first.IsCap || second.IsCap)
                return false;

            bool hasSegmentMetadata = !string.IsNullOrEmpty(first.SegmentId) ||
                                      !string.IsNullOrEmpty(second.SegmentId);
            if (hasSegmentMetadata)
            {
                return !string.IsNullOrEmpty(first.SegmentId) &&
                       first.SegmentId == second.SegmentId &&
                       !string.IsNullOrEmpty(first.Side) &&
                       first.Side == second.Side;
            }

            return string.IsNullOrEmpty(first.Side) ||
                   string.IsNullOrEmpty(second.Side) ||
                   first.Side == second.Side;
        }

        private static LineInterval CreateInterval(
            NormalizeLine line,
            Point3d origin,
            Vector3d axis)
        {
            double first = (line.Start - origin).DotProduct(axis);
            double second = (line.End - origin).DotProduct(axis);
            return new LineInterval
            {
                Line = line,
                StartStation = Math.Min(first, second),
                EndStation = Math.Max(first, second)
            };
        }

        private static bool SameSegment(
            NormalizeLine line,
            Point3d start,
            Point3d end)
        {
            return (line.Start.DistanceTo(start) <= Tolerance &&
                    line.End.DistanceTo(end) <= Tolerance) ||
                   (line.Start.DistanceTo(end) <= Tolerance &&
                    line.End.DistanceTo(start) <= Tolerance);
        }

        private static int EraseObsoleteCaps(
            Transaction transaction,
            IEnumerable<LineRecord> caps,
            IReadOnlyCollection<Point3d> junctionPoints,
            double wallThickness)
        {
            if (junctionPoints.Count == 0)
                return 0;

            double radius = Math.Max(5.0, wallThickness * 1.5);
            int erased = 0;

            foreach (LineRecord cap in caps)
            {
                Point3d middle = Midpoint(cap.Start, cap.End);
                if (!junctionPoints.Any(point => point.DistanceTo(middle) <= radius))
                    continue;

                DBObject value = transaction.GetObject(cap.Id, OpenMode.ForWrite);
                if (!value.IsErased)
                {
                    value.Erase(true);
                    erased++;
                }
            }

            return erased;
        }

        private static bool IsUnchanged(
            LineRecord source,
            IReadOnlyList<SegmentPiece> output)
        {
            if (output.Count != 1)
                return false;

            SegmentPiece piece = output[0];
            return (source.OriginalStart.DistanceTo(piece.Start) <= Tolerance &&
                    source.OriginalEnd.DistanceTo(piece.End) <= Tolerance) ||
                   (source.OriginalStart.DistanceTo(piece.End) <= Tolerance &&
                    source.OriginalEnd.DistanceTo(piece.Start) <= Tolerance);
        }

        private static int CountDistinctPoints(IEnumerable<Point3d> points)
        {
            List<Point3d> distinct = new List<Point3d>();
            foreach (Point3d point in points)
            {
                if (!distinct.Any(existing => existing.DistanceTo(point) <= Tolerance))
                    distinct.Add(point);
            }
            return distinct.Count;
        }

        private static bool SidesAreCompatible(LineRecord first, LineRecord second)
        {
            return string.IsNullOrEmpty(first.Side) ||
                   string.IsNullOrEmpty(second.Side) ||
                   first.Side == second.Side;
        }

        private static bool CanFormWallPair(LineRecord first, LineRecord second)
        {
            // Một tường WW hợp lệ gồm một mặt A và một mặt B.
            return string.IsNullOrEmpty(first.Side) ||
                   string.IsNullOrEmpty(second.Side) ||
                   first.Side != second.Side;
        }

        private static bool AreParallel(LineRecord first, LineRecord second)
        {
            Vector3d firstDirection = (first.End - first.Start).GetNormal();
            Vector3d secondDirection = (second.End - second.Start).GetNormal();
            return Math.Abs(firstDirection.DotProduct(secondDirection)) > ParallelDotTolerance;
        }

        private static bool AreCollinear(LineRecord first, LineRecord second)
        {
            return AreParallel(first, second) &&
                   AreCoplanar(first, second) &&
                   DistancePointToInfiniteLine(second.Start, first) <= Tolerance * 10.0 &&
                   DistancePointToInfiniteLine(second.End, first) <= Tolerance * 10.0;
        }

        private static bool AreCoplanar(LineRecord first, LineRecord second)
        {
            double minZ = Math.Min(
                Math.Min(first.Start.Z, first.End.Z),
                Math.Min(second.Start.Z, second.End.Z));
            double maxZ = Math.Max(
                Math.Max(first.Start.Z, first.End.Z),
                Math.Max(second.Start.Z, second.End.Z));
            return maxZ - minZ <= CoplanarTolerance;
        }

        private static double ProjectedOverlap(LineRecord first, LineRecord second)
        {
            Vector3d direction = (first.End - first.Start).GetNormal();
            double firstLength = first.Start.DistanceTo(first.End);
            double secondStart = (second.Start - first.Start).DotProduct(direction);
            double secondEnd = (second.End - first.Start).DotProduct(direction);
            return Math.Min(firstLength, Math.Max(secondStart, secondEnd)) -
                   Math.Max(0.0, Math.Min(secondStart, secondEnd));
        }

        private static double DistancePointToInfiniteLine(Point3d point, LineRecord line)
        {
            Vector3d direction = (line.End - line.Start).GetNormal();
            return DistancePointToInfiniteLine(point, line.Start, direction);
        }

        private static double DistancePointToInfiniteLine(
            Point3d point,
            Point3d lineStart,
            Vector3d direction)
        {
            Vector3d offset = point - lineStart;
            return (offset - direction * offset.DotProduct(direction)).Length;
        }

        private static bool ProjectionIsStrictlyInside(
            Point3d point,
            LineRecord line,
            double padding)
        {
            Vector3d direction = (line.End - line.Start).GetNormal();
            double station = (point - line.Start).DotProduct(direction);
            double length = line.Start.DistanceTo(line.End);
            return station > padding && station < length - padding;
        }

        private static double Station(LineRecord line, Point3d point)
        {
            return (point - line.Start).DotProduct((line.End - line.Start).GetNormal());
        }

        private static bool TryInfiniteIntersection(
            LineRecord first,
            LineRecord second,
            out Point3d intersection)
        {
            return TryIntersection(first, second, false, out intersection);
        }

        private static bool TrySegmentIntersection(
            LineRecord first,
            LineRecord second,
            out Point3d intersection)
        {
            return TryIntersection(first, second, true, out intersection);
        }

        private static bool TryIntersection(
            LineRecord first,
            LineRecord second,
            bool requireSegments,
            out Point3d intersection)
        {
            double ax = first.End.X - first.Start.X;
            double ay = first.End.Y - first.Start.Y;
            double bx = second.End.X - second.Start.X;
            double by = second.End.Y - second.Start.Y;
            double denominator = ax * by - ay * bx;

            intersection = Point3d.Origin;
            if (Math.Abs(denominator) <= Tolerance)
                return false;

            double dx = second.Start.X - first.Start.X;
            double dy = second.Start.Y - first.Start.Y;
            double firstParameter = (dx * by - dy * bx) / denominator;
            double secondParameter = (dx * ay - dy * ax) / denominator;

            if (requireSegments &&
                (firstParameter < -Tolerance || firstParameter > 1.0 + Tolerance ||
                 secondParameter < -Tolerance || secondParameter > 1.0 + Tolerance))
                return false;

            intersection = new Point3d(
                first.Start.X + firstParameter * ax,
                first.Start.Y + firstParameter * ay,
                first.Start.Z + firstParameter * (first.End.Z - first.Start.Z));
            return true;
        }

        private static bool IsPointOnSegment(
            Point3d point,
            Point3d start,
            Point3d end,
            double tolerance)
        {
            return Math.Abs(
                point.DistanceTo(start) + point.DistanceTo(end) - start.DistanceTo(end)) <= tolerance;
        }

        private static bool HasCompatibleCornerMovement(
            Point3d endpoint,
            Point3d otherEndpoint,
            Point3d targetEndpoint,
            Point3d targetOtherEndpoint,
            Point3d intersection)
        {
            Vector3d outward = (endpoint - otherEndpoint).GetNormal();
            Vector3d targetOutward = (targetEndpoint - targetOtherEndpoint).GetNormal();
            double firstMovement = (intersection - endpoint).DotProduct(outward);
            double secondMovement = (intersection - targetEndpoint).DotProduct(targetOutward);
            return firstMovement >= -Tolerance && secondMovement >= -Tolerance;
        }

        private static EndpointMatch GetClosestEndpoints(
            LineRecord first,
            LineRecord second)
        {
            EndpointMatch[] candidates =
            {
                CreateEndpointMatch(first.Start, true, second.Start, true),
                CreateEndpointMatch(first.Start, true, second.End, false),
                CreateEndpointMatch(first.End, false, second.Start, true),
                CreateEndpointMatch(first.End, false, second.End, false)
            };
            return candidates.OrderBy(candidate => candidate.Distance).First();
        }

        private static EndpointMatch CreateEndpointMatch(
            Point3d first,
            bool firstIsStart,
            Point3d second,
            bool secondIsStart)
        {
            return new EndpointMatch
            {
                FirstPoint = first,
                FirstIsStart = firstIsStart,
                SecondPoint = second,
                SecondIsStart = secondIsStart,
                Distance = first.DistanceTo(second)
            };
        }

        private static void SetEndpoint(LineRecord line, bool start, Point3d point)
        {
            if (start)
                line.Start = point;
            else
                line.End = point;
        }

        private static Point3d Midpoint(Point3d first, Point3d second)
        {
            return new Point3d(
                (first.X + second.X) / 2.0,
                (first.Y + second.Y) / 2.0,
                (first.Z + second.Z) / 2.0);
        }

        private static double Median(IEnumerable<double> values)
        {
            List<double> ordered = values.OrderBy(value => value).ToList();
            if (ordered.Count == 0)
                return 0.0;

            int middle = ordered.Count / 2;
            return ordered.Count % 2 == 0
                ? (ordered[middle - 1] + ordered[middle]) / 2.0
                : ordered[middle];
        }

        private sealed class LineRecord
        {
            public ObjectId Id { get; init; }
            public Line Entity { get; init; } = null!;
            public ObjectId LayerId { get; init; }
            public string? Side { get; init; }
            public string? SegmentId { get; init; }
            public bool IsCap { get; init; }
            public Point3d OriginalStart { get; init; }
            public Point3d OriginalEnd { get; init; }
            public Point3d Start { get; set; }
            public Point3d End { get; set; }
        }

        private sealed class WallPair
        {
            public LineRecord First { get; init; } = null!;
            public LineRecord Second { get; init; } = null!;
            public double Width { get; init; }
        }

        private sealed class PairCandidate
        {
            public LineRecord Other { get; init; } = null!;
            public double Width { get; init; }
            public double Overlap { get; init; }
        }

        private sealed class PairOption
        {
            public LineRecord First { get; init; } = null!;
            public LineRecord Second { get; init; } = null!;
            public double Width { get; init; }
            public double Overlap { get; init; }
        }

        private sealed class EndpointMoveCandidate
        {
            public Point3d Point { get; init; }
            public bool IsCorner { get; init; }
            public double Score { get; init; }
            public string StableKey { get; init; } = string.Empty;
            public List<EndpointReference> Endpoints { get; init; } = new List<EndpointReference>();
        }

        private sealed class EndpointReference
        {
            public LineRecord Line { get; init; } = null!;
            public bool IsStart { get; init; }
        }

        private sealed class WallEnd
        {
            public LineRecord FirstLine { get; init; } = null!;
            public bool FirstIsStart { get; init; }
            public Point3d FirstPoint { get; init; }
            public LineRecord SecondLine { get; init; } = null!;
            public bool SecondIsStart { get; init; }
            public Point3d SecondPoint { get; init; }
        }

        private sealed class HostChain
        {
            public LineRecord Reference { get; init; } = null!;
            public HashSet<ObjectId> MemberIds { get; init; } = new HashSet<ObjectId>();
            public Point3d Start { get; init; }
            public Point3d End { get; init; }
        }

        private sealed class ClosedTJunction
        {
            // Host chỉ là line đại diện; HostChainIds chứa toàn bộ đoạn cần trim.
            public LineRecord Host { get; init; } = null!;
            public HashSet<ObjectId> HostChainIds { get; init; } = new HashSet<ObjectId>();
            public LineRecord FirstBranchLine { get; init; } = null!;
            public bool FirstBranchIsStart { get; init; }
            public Point3d FirstPoint { get; init; }
            public LineRecord SecondBranchLine { get; init; } = null!;
            public bool SecondBranchIsStart { get; init; }
            public Point3d SecondPoint { get; init; }
        }

        private sealed class SegmentPiece
        {
            public Point3d Start { get; set; }
            public Point3d End { get; set; }
        }

        private sealed class ReplaceResult
        {
            public int ChangedCount { get; set; }
            public List<ObjectId> ResultIds { get; } = new List<ObjectId>();
        }

        private sealed class NormalizeLine
        {
            public ObjectId Id { get; init; }
            public Line Entity { get; init; } = null!;
            public ObjectId LayerId { get; init; }
            public Point3d Start { get; init; }
            public Point3d End { get; init; }
            public string? Side { get; init; }
            public string? SegmentId { get; init; }
            public bool IsCap { get; init; }
        }

        private sealed class LineInterval
        {
            public NormalizeLine Line { get; init; } = null!;
            public double StartStation { get; init; }
            public double EndStation { get; init; }
        }

        private sealed class LineIntervalCluster
        {
            public double StartStation { get; set; }
            public double EndStation { get; set; }
            public List<NormalizeLine> Lines { get; set; } = new List<NormalizeLine>();
        }

        private struct EndpointMatch
        {
            public Point3d FirstPoint;
            public bool FirstIsStart;
            public Point3d SecondPoint;
            public bool SecondIsStart;
            public double Distance;
        }

        private sealed class RepairWindow
        {
            private readonly double _minX;
            private readonly double _minY;
            private readonly double _maxX;
            private readonly double _maxY;

            public RepairWindow(Point3d first, Point3d second)
            {
                _minX = Math.Min(first.X, second.X);
                _minY = Math.Min(first.Y, second.Y);
                _maxX = Math.Max(first.X, second.X);
                _maxY = Math.Max(first.Y, second.Y);
            }

            public static RepairWindow FromLines(
                IReadOnlyCollection<LineRecord> lines,
                double padding)
            {
                double minX = lines.Min(line => Math.Min(line.Start.X, line.End.X)) - padding;
                double minY = lines.Min(line => Math.Min(line.Start.Y, line.End.Y)) - padding;
                double maxX = lines.Max(line => Math.Max(line.Start.X, line.End.X)) + padding;
                double maxY = lines.Max(line => Math.Max(line.Start.Y, line.End.Y)) + padding;
                return new RepairWindow(
                    new Point3d(minX, minY, 0.0),
                    new Point3d(maxX, maxY, 0.0));
            }

            public bool Contains(Point3d point)
            {
                return point.X >= _minX - Tolerance &&
                       point.X <= _maxX + Tolerance &&
                       point.Y >= _minY - Tolerance &&
                       point.Y <= _maxY + Tolerance;
            }
        }

        private sealed class RepairSummary
        {
            public int WallLineCount { get; init; }
            public int ChangedLineCount { get; init; }
            public int JunctionCount { get; init; }
            public int HealedGapCount { get; init; }
            public int ClosedTJunctionCount { get; init; }
        }
    }
}
