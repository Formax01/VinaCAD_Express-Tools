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
    public class TrimFixWallAction
    {
        private const double Tolerance = 0.001;
        private const double ParallelDotTolerance = 0.999961923;
        private const double DefaultHealDistance = 600.0;
        private const double MaximumSupportedThickness = 5000.0;
        private const double CoplanarTolerance = 0.01;

        private const double OvershootFactor = 1.5;
        private const double MinOvershoot = 100.0;

        private const double MaxGapHeal = 1200.0;

        private const int MaxPasses = 4;

        private const double MaxStaggerFactor = 3.0;

        private const bool CapSquaredFreeEnds = true;

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

                ObjectId[] ids = selection.Value.GetObjectIds();
                int wallCount = 0, changed = 0, healed = 0, junctions = 0;
                for (int pass = 0; pass < MaxPasses; pass++)
                {
                    RepairSummary s = Repair(database, ids);
                    if (pass == 0) wallCount = s.WallLineCount;
                    if (wallCount == 0) break;

                    changed += s.ChangedLineCount;
                    healed += s.HealedGapCount;
                    junctions = Math.Max(junctions, s.JunctionCount);

                    // Ổn định: lượt này không đổi gì thì dừng
                    if (s.ChangedLineCount == 0 && s.HealedGapCount == 0) break;
                    ids = s.NextSelection;
                }

                RepairSummary summary = new RepairSummary
                {
                    WallLineCount = wallCount,
                    ChangedLineCount = changed,
                    JunctionCount = junctions,
                    HealedGapCount = healed
                };

                if (summary.WallLineCount == 0)
                {
                    editor.WriteMessage("\nKhông tìm thấy cặp nét tường hợp lệ trong vùng chọn.");
                    return;
                }

                editor.WriteMessage(
                    $"\nĐã sửa {summary.ChangedLineCount} line, " +
                    $"{summary.JunctionCount} giao tường, " +
                    $"{summary.HealedGapCount} khoảng hở ");
                editor.UpdateScreen();
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(TrimFixWallAction), ex);
                MessageBox.Show($"Lỗi TW: {ex.Message}", StringDefinition.TITLE_ERROR);
            }
        }

        private static RepairSummary Repair(Database database, ObjectId[] selectedIds)
        {
            using Transaction transaction = database.TransactionManager.StartTransaction();

            List<LineRecord> selectedLines = ReadLines(transaction, selectedIds);
            HashSet<ObjectId> taggedWallLayers = selectedLines
                .Where(line => !line.IsCap && !string.IsNullOrEmpty(line.Side))
                .Select(line => line.LayerId)
                .ToHashSet();

            Dictionary<ObjectId, string> layerNames = ReadLayerNames(transaction, database);

            // [FIX #6] Fallback nhiều tầng: layer có tag -> layer tên WALL/TUONG -> mọi layer của line được chọn
            HashSet<ObjectId> acceptedLayers = taggedWallLayers;
            if (acceptedLayers.Count == 0)
            {
                acceptedLayers = selectedLines
                    .Where(line => IsWallLayerName(layerNames.GetValueOrDefault(line.LayerId)))
                    .Select(line => line.LayerId)
                    .ToHashSet();
            }
            if (acceptedLayers.Count == 0)
            {
                acceptedLayers = selectedLines
                    .Select(line => line.LayerId)
                    .ToHashSet();
            }

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

            double collinearHealDistance = Math.Max(DefaultHealDistance, typicalThickness * 3.5);
            double windowPadding = Math.Max(50.0, typicalThickness * 3.0);
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

            int healedGaps = HealSingleFaceGaps(walls, caps, window, collinearHealDistance, typicalThickness);
            healedGaps += HealCollinearGaps(initialPairs, caps, window, collinearHealDistance);

            List<Point3d> junctionPoints = new List<Point3d>();
            SnapWallEndpoints(walls, caps, window, thicknessByLine, junctionPoints);

            List<WallPair> repairedPairs = initialPairs
                .Where(pair => pairedIds.Contains(pair.First.Id) && pairedIds.Contains(pair.Second.Id))
                .ToList();

            Dictionary<ObjectId, List<SegmentPiece>> output = BuildOutput(walls, repairedPairs, caps, window, junctionPoints, thicknessByLine);

            ReplaceResult replaceResult = ReplaceChangedLines(transaction, database, walls, output);

            int erasedCaps = EraseObsoleteCaps(transaction, caps, junctionPoints, typicalThickness, repairedPairs, output);

            List<ObjectId> createdIds = new List<ObjectId>();
            int normalizedLines = NormalizeCollinearResults(transaction, database, replaceResult.ResultIds, typicalThickness, createdIds);

            ObjectId[] nextSelection = selectedIds
                .Concat(replaceResult.ResultIds)
                .Concat(createdIds)
                .Distinct()
                .Where(id => IsAlive(transaction, id))
                .ToArray();

            int cappedEnds = SquareAndCapFreeEnds(transaction, database, nextSelection, acceptedLayers, typicalThickness);

            transaction.Commit();
            return new RepairSummary
            {
                WallLineCount = walls.Count,
                ChangedLineCount = replaceResult.ChangedCount + erasedCaps + normalizedLines + cappedEnds,
                JunctionCount = CountDistinctPoints(junctionPoints),
                HealedGapCount = healedGaps,
                NextSelection = nextSelection
            };
        }

        private static int SquareAndCapFreeEnds(Transaction transaction, Database database, IEnumerable<ObjectId> ids, IReadOnlySet<ObjectId> acceptedLayers, double typicalThickness)
        {
            List<LineRecord> all = ReadLines(transaction, ids).Where(l => acceptedLayers.Contains(l.LayerId)).ToList();
            List<LineRecord> walls = all.Where(l => !l.IsCap).ToList();
            if (walls.Count == 0) return 0;

            RepairWindow window = RepairWindow.FromLines(walls, Math.Max(50.0, typicalThickness * 3.0));
            List<LineRecord> caps = ReadCapsInWindow(transaction, database, acceptedLayers, window, all.Where(l => l.IsCap));
            List<WallPair> pairs = FindWallPairs(walls);
            int changed = 0;

            List<LineRecord> obstacles = ReadObstacleLines(transaction, database, window);
            Dictionary<ObjectId, LineRecord> obstacleById = obstacles.ToDictionary(o => o.Id);

            foreach (WallPair pair in pairs)
            {
                LineRecord a = pair.First;
                LineRecord b = pair.Second;
                Vector3d dir = (a.End - a.Start).GetNormal();
                Point3d origin = a.Start;
                double maxStagger = Math.Max(5.0, pair.Width * MaxStaggerFactor);
                double capTol = Math.Max(1.0, pair.Width * 0.05);

                for (int side = 0; side < 2; side++)
                {
                    bool high = side == 1;
                    Point3d pa = EndAlong(a, origin, dir, high, out double sa, out bool aIsStart);
                    Point3d pb = EndAlong(b, origin, dir, high, out double sb, out bool bIsStart);
                    double diff = Math.Abs(sa - sb);
                    bool square = diff <= Tolerance;
                    if (diff > maxStagger || (square && !CapSquaredFreeEnds)) continue;

                    bool aLonger = high ? sa > sb : sa < sb;
                    LineRecord longer = aLonger ? a : b;
                    Point3d longPt = aLonger ? pa : pb;
                    double longS = aLonger ? sa : sb;
                    bool longIsStart = aLonger ? aIsStart : bIsStart;
                    Point3d shortPt = aLonger ? pb : pa;
                    double shortS = aLonger ? sb : sa;

                    Vector3d u = (longer.End - longer.Start).GetNormal();
                    double cosA = u.DotProduct(dir);
                    if (Math.Abs(cosA) < Tolerance) continue;
                    Point3d newLongPt = square ? longPt : longPt + u * ((shortS - longS) / cosA);

                    if (caps.Any(c => c.Start.DistanceTo(longPt) <= capTol || c.End.DistanceTo(longPt) <= capTol ||
                                      c.Start.DistanceTo(shortPt) <= capTol || c.End.DistanceTo(shortPt) <= capTol))
                        continue;

                    bool wallContinues = obstacles.Any(o =>
                        o.Id != a.Id && o.Id != b.Id &&
                        Math.Abs((o.End - o.Start).GetNormal().DotProduct(dir)) > ParallelDotTolerance &&
                        (DistanceToSeg(longPt, o.Start, o.End) <= capTol || DistanceToSeg(shortPt, o.Start, o.End) <= capTol));
                    if (wallContinues) continue;

                    bool occupied = obstacles.Any(o =>
                        o.Id != a.Id && o.Id != b.Id &&
                        Math.Abs((o.End - o.Start).GetNormal().DotProduct(dir)) <= ParallelDotTolerance &&
                        (SegsTouchOrCross(newLongPt, longPt, o.Start, o.End, 1.0) ||
                         SegsTouchOrCross(newLongPt, shortPt, o.Start, o.End, 1.0)));
                    if (occupied) continue;

                    Line ent = (Line)transaction.GetObject(longer.Id, square ? OpenMode.ForRead : OpenMode.ForWrite);
                    if (!square)
                    {
                        if (longIsStart) { ent.StartPoint = newLongPt; longer.Start = newLongPt; }
                        else { ent.EndPoint = newLongPt; longer.End = newLongPt; }
                        if (obstacleById.TryGetValue(longer.Id, out LineRecord ob))
                        {
                            if (longIsStart) ob.Start = newLongPt; else ob.End = newLongPt;
                        }
                    }

                    BlockTableRecord owner = (BlockTableRecord)transaction.GetObject(ent.OwnerId, OpenMode.ForWrite);
                    Line cap = new Line(newLongPt, shortPt)
                    {
                        LayerId = ent.LayerId,
                        Color = ent.Color,
                        LineWeight = ent.LineWeight,
                        LinetypeId = ent.LinetypeId,
                        LinetypeScale = ent.LinetypeScale,
                        Transparency = ent.Transparency
                    };
                    owner.AppendEntity(cap);
                    transaction.AddNewlyCreatedDBObject(cap, true);
                    DrawWallHelper.TagAsCap(transaction, database, cap);

                    caps.Add(new LineRecord
                    {
                        Id = cap.ObjectId,
                        Entity = cap,
                        LayerId = cap.LayerId,
                        IsCap = true,
                        OriginalStart = newLongPt,
                        OriginalEnd = shortPt,
                        Start = newLongPt,
                        End = shortPt
                    });
                    changed++;
                }
            }

            return changed;
        }

        private static List<LineRecord> ReadObstacleLines(Transaction transaction, Database database, RepairWindow window)
        {
            List<ObjectId> ids = new List<ObjectId>();
            BlockTableRecord space = (BlockTableRecord)transaction.GetObject(database.CurrentSpaceId, OpenMode.ForRead);
            foreach (ObjectId id in space)
            {
                if (transaction.GetObject(id, OpenMode.ForRead) is not Line line || line.IsErased) continue;
                Point3d s = line.StartPoint, e = line.EndPoint;
                if (s.DistanceTo(e) <= Tolerance || DrawWallHelper.IsWallCap(line)) continue;
                if (!window.Intersects(Math.Min(s.X, e.X), Math.Min(s.Y, e.Y), Math.Max(s.X, e.X), Math.Max(s.Y, e.Y))) continue;
                ids.Add(id);
            }
            return ReadLines(transaction, ids);
        }

        private static Point3d EndAlong(LineRecord line, Point3d origin, Vector3d dir, bool high, out double station, out bool isStart)
        {
            double s0 = (line.Start - origin).DotProduct(dir);
            double s1 = (line.End - origin).DotProduct(dir);
            bool startIsHigh = s0 >= s1;
            isStart = high ? startIsHigh : !startIsHigh;
            station = isStart ? s0 : s1;
            return isStart ? line.Start : line.End;
        }

        private static double DistanceToSeg(Point3d pt, Point3d start, Point3d end)
        {
            Vector3d v = end - start;
            Vector3d w = pt - start;
            double c1 = w.DotProduct(v);
            if (c1 <= 0) return pt.DistanceTo(start);
            double c2 = v.DotProduct(v);
            if (c2 <= c1) return pt.DistanceTo(end);
            return pt.DistanceTo(start + v * (c1 / c2));
        }

        private static bool SegsTouchOrCross(Point3d a, Point3d b, Point3d c, Point3d d, double tol)
        {
            if (DistanceToSeg(a, c, d) <= tol || DistanceToSeg(b, c, d) <= tol ||
                DistanceToSeg(c, a, b) <= tol || DistanceToSeg(d, a, b) <= tol)
                return true;

            double ab1 = Cross2(a, b, c), ab2 = Cross2(a, b, d);
            double cd1 = Cross2(c, d, a), cd2 = Cross2(c, d, b);
            return ((ab1 > 0 && ab2 < 0) || (ab1 < 0 && ab2 > 0)) &&
                   ((cd1 > 0 && cd2 < 0) || (cd1 < 0 && cd2 > 0));
        }

        private static double Cross2(Point3d a, Point3d b, Point3d c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

        private static bool IsAlive(Transaction transaction, ObjectId id)
        {
            try { return !transaction.GetObject(id, OpenMode.ForRead, true).IsErased; }
            catch { return false; }
        }

        private static bool IsInsideAnotherWall(Point3d point, LineRecord source, IEnumerable<WallPair> pairs)
        {
            foreach (WallPair pair in pairs)
            {
                if (pair.First.Id == source.Id || pair.Second.Id == source.Id ||
                    pair.First.LayerId != source.LayerId ||
                    !AreCoplanar(source, pair.First) || !AreCoplanar(source, pair.Second))
                    continue;

                // Chỉ nét CẮT NGANG lõi mới bị xóa; nét song song/thẳng hàng thì bỏ qua
                if (AreParallel(source, pair.First)) continue;

                double d1 = DistancePointToInfiniteLine(point, pair.First);
                double d2 = DistancePointToInfiniteLine(point, pair.Second);
                double widthTolerance = Math.Max(2.0, pair.Width * 0.05);
                if (Math.Abs(d1 + d2 - pair.Width) > widthTolerance) continue;

                // Phải nằm THẬT SỰ bên trong, không nằm trên mặt tường
                double edge = Math.Max(1.0, pair.Width * 0.02);
                if (d1 <= edge || d2 <= edge) continue;

                double projTol = Math.Max(Tolerance * 10.0, pair.Width * 0.1);
                if (ProjectionFallsOnSegment(point, pair.First, projTol) ||
                    ProjectionFallsOnSegment(point, pair.Second, projTol))
                    return true;
            }

            return false;
        }

        private static Dictionary<ObjectId, List<SegmentPiece>> BuildOutput(
            List<LineRecord> lines,
            List<WallPair> wallPairs,
            IReadOnlyCollection<LineRecord> caps,
            RepairWindow window,
            ICollection<Point3d> junctionPoints,
            IReadOnlyDictionary<ObjectId, double> thicknessByLine)
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
                    if (first.LayerId != second.LayerId || !AreCoplanar(first, second) || AreParallel(first, second))
                        continue;

                    if (TrySegmentIntersection(first, second, out Point3d intersection) && window.Contains(intersection))
                    {
                        cuts[first.Id].Add(intersection);
                        cuts[second.Id].Add(intersection);
                        junctionPoints.Add(intersection);
                    }
                }
            }

            Dictionary<ObjectId, List<SegmentPiece>> output = new Dictionary<ObjectId, List<SegmentPiece>>();

            foreach (LineRecord line in lines)
            {
                List<Point3d> ordered = cuts[line.Id]
                    .OrderBy(point => Station(line, point))
                    .Aggregate(new List<Point3d>(), (points, point) =>
                    {
                        if (points.Count == 0 || points[^1].DistanceTo(point) > Tolerance) points.Add(point);
                        return points;
                    });

                List<SegInfo> segs = new List<SegInfo>();
                for (int i = 0; i < ordered.Count - 1; i++)
                {
                    Point3d s = ordered[i];
                    Point3d e = ordered[i + 1];
                    if (s.DistanceTo(e) <= Tolerance) continue;

                    Point3d m = Midpoint(s, e);
                    bool inWindow = window.Contains(m);
                    bool core = inWindow && IsInsideAnotherWall(m, line, wallPairs);
                    bool cap = inWindow && IsOnObsoleteJunctionCap(m, line, wallPairs, caps);
                    segs.Add(new SegInfo { Start = s, End = e, Core = core, Remove = core || cap });
                }

                double thickness = GetWallThickness(line.Id, thicknessByLine);
                double tailLimit = Math.Max(MinOvershoot, thickness * OvershootFactor);

                void TrimTail(int tailIdx, int neighborIdx, Point3d freeEnd)
                {
                    SegInfo tail = segs[tailIdx];
                    if (tail.Remove || !segs[neighborIdx].Core) return;
                    if (tail.Start.DistanceTo(tail.End) > tailLimit) return;
                    if (HasCapAtEndpoint(caps, line, freeEnd, thickness)) return;
                    tail.Remove = true;
                }

                if (segs.Count >= 2)
                {
                    TrimTail(0, 1, segs[0].Start);
                    TrimTail(segs.Count - 1, segs.Count - 2, segs[^1].End);
                }

                List<SegmentPiece> pieces = new List<SegmentPiece>();
                SegmentPiece? active = null;
                foreach (SegInfo seg in segs)
                {
                    if (seg.Remove)
                    {
                        if (active != null)
                        {
                            pieces.Add(active);
                            active = null;
                        }
                        continue;
                    }

                    if (active == null) active = new SegmentPiece { Start = seg.Start, End = seg.End };
                    else active.End = seg.End;
                }

                if (active != null) pieces.Add(active);
                output[line.Id] = pieces;
            }

            return output;
        }

        private static int EraseObsoleteCaps(
            Transaction transaction,
            IEnumerable<LineRecord> caps,
            IReadOnlyCollection<Point3d> junctionPoints,
            double wallThickness,
            List<WallPair> wallPairs,
            IReadOnlyDictionary<ObjectId, List<SegmentPiece>> output)
        {
            if (junctionPoints.Count == 0) return 0;

            double radius = Math.Max(10.0, wallThickness * 2.0);
            double attachTol = Math.Max(1.0, wallThickness * 0.02);
            List<Point3d> ends = output.Values
                .SelectMany(p => p)
                .SelectMany(s => new[] { s.Start, s.End })
                .ToList();
            int erased = 0;

            foreach (LineRecord cap in caps)
            {
                DBObject value = transaction.GetObject(cap.Id, OpenMode.ForWrite);
                if (value.IsErased) continue;

                Point3d middle = Midpoint(cap.Start, cap.End);

                bool attached = ends.Any(e => e.DistanceTo(cap.Start) <= attachTol) &&
                                ends.Any(e => e.DistanceTo(cap.End) <= attachTol);
                bool nearJunction = !attached && junctionPoints.Any(point => point.DistanceTo(middle) <= radius);
                bool swallowedByWall = IsInsideAnotherWall(middle, cap, wallPairs);

                if (nearJunction || swallowedByWall)
                {
                    value.Erase(true);
                    erased++;
                }
            }

            return erased;
        }

        private static bool ProjectionFallsOnSegment(Point3d point, LineRecord line, double customTolerance)
        {
            Vector3d direction = (line.End - line.Start).GetNormal();
            double station = (point - line.Start).DotProduct(direction);
            double length = line.Start.DistanceTo(line.End);
            return station >= -customTolerance && station <= length + customTolerance;
        }

        private static List<LineRecord> ReadLines(Transaction transaction, IEnumerable<ObjectId> selectedIds)
        {
            List<LineRecord> result = new List<LineRecord>();
            foreach (ObjectId id in selectedIds)
            {
                DBObject value = transaction.GetObject(id, OpenMode.ForRead);
                if (value is not Line line || line.StartPoint.DistanceTo(line.EndPoint) <= Tolerance) continue;
                result.Add(new LineRecord { Id = id, Entity = line, LayerId = line.LayerId, Side = DrawWallHelper.GetWallSideMarker(line), SegmentId = DrawWallHelper.GetWallSegmentId(line), IsCap = DrawWallHelper.IsWallCap(line), OriginalStart = line.StartPoint, OriginalEnd = line.EndPoint, Start = line.StartPoint, End = line.EndPoint });
            }
            return result;
        }

        private static List<LineRecord> ReadCapsInWindow(Transaction transaction, Database database, IReadOnlySet<ObjectId> acceptedLayers, RepairWindow window, IEnumerable<LineRecord> selectedCaps)
        {
            Dictionary<ObjectId, LineRecord> result = selectedCaps.ToDictionary(cap => cap.Id);
            BlockTableRecord currentSpace = (BlockTableRecord)transaction.GetObject(database.CurrentSpaceId, OpenMode.ForRead);
            foreach (ObjectId id in currentSpace)
            {
                if (result.ContainsKey(id) || transaction.GetObject(id, OpenMode.ForRead) is not Line line || line.IsErased || !acceptedLayers.Contains(line.LayerId) || !DrawWallHelper.IsWallCap(line) || line.StartPoint.DistanceTo(line.EndPoint) <= Tolerance) continue;
                Point3d middle = Midpoint(line.StartPoint, line.EndPoint);
                if (!window.Contains(middle)) continue;
                result[id] = new LineRecord { Id = id, Entity = line, LayerId = line.LayerId, Side = DrawWallHelper.GetWallSideMarker(line), SegmentId = DrawWallHelper.GetWallSegmentId(line), IsCap = true, OriginalStart = line.StartPoint, OriginalEnd = line.EndPoint, Start = line.StartPoint, End = line.EndPoint };
            }
            return result.Values.ToList();
        }

        private static Dictionary<ObjectId, string> ReadLayerNames(Transaction transaction, Database database)
        {
            Dictionary<ObjectId, string> result = new Dictionary<ObjectId, string>();
            LayerTable layers = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
            foreach (ObjectId id in layers)
            {
                LayerTableRecord layer = (LayerTableRecord)transaction.GetObject(id, OpenMode.ForRead);
                result[id] = layer.Name ?? string.Empty;
            }
            return result;
        }

        private static bool IsWallLayerName(string? layerName)
        {
            if (string.IsNullOrWhiteSpace(layerName)) return false;
            string normalized = layerName.ToUpperInvariant();
            return normalized.Contains("WALL") || normalized.Contains("TUONG");
        }

        private static List<WallPair> FindWallPairs(List<LineRecord> lines)
        {
            List<WallPair> taggedPairs = FindTaggedWallPairs(lines);
            HashSet<ObjectId> taggedPairIds = taggedPairs.SelectMany(pair => new[] { pair.First.Id, pair.Second.Id }).ToHashSet();
            List<LineRecord> legacyLines = lines.Where(line => string.IsNullOrEmpty(line.SegmentId) && !taggedPairIds.Contains(line.Id)).ToList();
            taggedPairs.AddRange(FindLegacyWallPairs(legacyLines));
            return taggedPairs;
        }

        private static List<WallPair> FindTaggedWallPairs(List<LineRecord> lines)
        {
            List<WallPair> result = new List<WallPair>();
            foreach (IGrouping<string, LineRecord> segment in lines.Where(line => !string.IsNullOrEmpty(line.SegmentId)).GroupBy(line => line.SegmentId!))
            {
                List<PairOption> options = new List<PairOption>();
                List<LineRecord> members = segment.ToList();
                for (int i = 0; i < members.Count; i++)
                {
                    for (int j = i + 1; j < members.Count; j++)
                    {
                        LineRecord first = members[i];
                        LineRecord second = members[j];
                        if (!CanBeGeometricWallPair(first, second)) continue;
                        double width = DistancePointToInfiniteLine(second.Start, first);
                        double overlap = ProjectedOverlap(first, second);
                        if (width <= Tolerance || width > MaximumSupportedThickness || overlap <= Tolerance) continue;
                        options.Add(new PairOption { First = first, Second = second, Width = width, Overlap = overlap });
                    }
                }
                foreach (PairOption option in options.OrderByDescending(item => item.Overlap).ThenBy(item => item.Width).ThenBy(item => MakePairKey(item.First.Id, item.Second.Id)))
                {
                    result.Add(new WallPair { First = option.First, Second = option.Second, Width = option.Width });
                }
            }
            return result;
        }

        private static List<WallPair> FindLegacyWallPairs(List<LineRecord> lines)
        {
            Dictionary<ObjectId, PairCandidate> nearest = new Dictionary<ObjectId, PairCandidate>();
            for (int i = 0; i < lines.Count; i++)
            {
                for (int j = i + 1; j < lines.Count; j++)
                {
                    LineRecord first = lines[i];
                    LineRecord second = lines[j];
                    if (!CanBeGeometricWallPair(first, second)) continue;
                    double width = DistancePointToInfiniteLine(second.Start, first);
                    double overlap = ProjectedOverlap(first, second);
                    if (width <= Tolerance || width > MaximumSupportedThickness || overlap <= Tolerance) continue;
                    UpdateNearest(nearest, first, second, width, overlap);
                    UpdateNearest(nearest, second, first, width, overlap);
                }
            }
            List<WallPair> result = new List<WallPair>();
            HashSet<string> handled = new HashSet<string>();
            foreach (LineRecord line in lines)
            {
                if (!nearest.TryGetValue(line.Id, out PairCandidate? pair) ||
                    !nearest.TryGetValue(pair.Other.Id, out PairCandidate? reverse)) continue;

                bool mutual = reverse.Other.Id == line.Id;
                bool sameWidth = Math.Abs(reverse.Width - pair.Width) <= Math.Max(1.0, pair.Width * 0.02);
                if (!mutual && !sameWidth) continue;

                string key = MakePairKey(line.Id, pair.Other.Id);
                if (!handled.Add(key)) continue;
                result.Add(new WallPair { First = line, Second = pair.Other, Width = pair.Width });
            }
            return result;
        }

        private static bool CanBeGeometricWallPair(LineRecord first, LineRecord second) => first.LayerId == second.LayerId && AreCoplanar(first, second) && AreParallel(first, second) && CanFormWallPair(first, second);

        private static void UpdateNearest(IDictionary<ObjectId, PairCandidate> nearest, LineRecord source, LineRecord other, double width, double overlap)
        {
            if (!nearest.TryGetValue(source.Id, out PairCandidate? current) || width < current.Width - Tolerance || (Math.Abs(width - current.Width) <= Tolerance && (overlap > current.Overlap + Tolerance || (Math.Abs(overlap - current.Overlap) <= Tolerance && string.CompareOrdinal(other.Id.ToString(), current.Other.Id.ToString()) < 0))))
            {
                nearest[source.Id] = new PairCandidate { Other = other, Width = width, Overlap = overlap };
            }
        }

        private static string MakePairKey(ObjectId first, ObjectId second)
        {
            string a = first.ToString(); string b = second.ToString();
            return string.CompareOrdinal(a, b) <= 0 ? a + "|" + b : b + "|" + a;
        }

        private static int HealCollinearGaps(IReadOnlyList<WallPair> wallPairs, IReadOnlyCollection<LineRecord> caps, RepairWindow window, double searchDistance)
        {
            int healed = 0;
            for (int i = 0; i < wallPairs.Count; i++)
            {
                for (int j = i + 1; j < wallPairs.Count; j++)
                {
                    WallPair firstWall = wallPairs[i]; WallPair secondWall = wallPairs[j];
                    if (WallPairsShareFace(firstWall, secondWall) || !WallPairsCanHeal(firstWall, secondWall) || firstWall.First.LayerId != secondWall.First.LayerId || !AreCoplanar(firstWall.First, secondWall.First)) continue;
                    if (!TryMatchWallFaces(firstWall, secondWall, out LineRecord firstFaceA, out LineRecord secondFaceA, out LineRecord firstFaceB, out LineRecord secondFaceB)) continue;
                    if (!AreCollinear(firstFaceA, secondFaceA) || !AreCollinear(firstFaceB, secondFaceB) || ProjectedOverlap(firstFaceA, secondFaceA) > Tolerance || ProjectedOverlap(firstFaceB, secondFaceB) > Tolerance) continue;
                    EndpointMatch faceAMatch = GetClosestEndpoints(firstFaceA, secondFaceA);
                    EndpointMatch faceBMatch = GetClosestEndpoints(firstFaceB, secondFaceB);
                    double localThickness = Math.Max(firstWall.Width, secondWall.Width);
                    bool metadataConfirmsSameWall = !string.IsNullOrEmpty(GetPairSegmentId(firstWall));

                    bool sameWidth = Math.Abs(firstWall.Width - secondWall.Width) <= Math.Max(1.0, localThickness * 0.02);
                    double localSearchDistance = metadataConfirmsSameWall
                        ? Math.Max(searchDistance, MaxGapHeal)
                        : (sameWidth ? MaxGapHeal : Math.Max(5.0, Math.Min(25.0, localThickness * 0.1)));

                    if (faceAMatch.Distance <= Tolerance || faceBMatch.Distance <= Tolerance || faceAMatch.Distance > localSearchDistance || faceBMatch.Distance > localSearchDistance || Math.Abs(faceAMatch.Distance - faceBMatch.Distance) > Math.Max(5.0, Math.Max(firstWall.Width, secondWall.Width) * 0.1)) continue;
                    Point3d jointA = Midpoint(faceAMatch.FirstPoint, faceAMatch.SecondPoint);
                    Point3d jointB = Midpoint(faceBMatch.FirstPoint, faceBMatch.SecondPoint);
                    Point3d wallCenter = Midpoint(jointA, jointB);
                    if (!window.Contains(wallCenter) || HasProtectingCap(caps, wallCenter, Math.Max(firstWall.Width, secondWall.Width))) continue;
                    SetEndpoint(firstFaceA, faceAMatch.FirstIsStart, jointA);
                    SetEndpoint(secondFaceA, faceAMatch.SecondIsStart, jointA);
                    SetEndpoint(firstFaceB, faceBMatch.FirstIsStart, jointB);
                    SetEndpoint(secondFaceB, faceBMatch.SecondIsStart, jointB);
                    healed++;
                }
            }
            return healed;
        }

        private static int HealSingleFaceGaps(IReadOnlyList<LineRecord> walls, IReadOnlyCollection<LineRecord> caps, RepairWindow window, double searchDistance, double typicalThickness)
        {
            int healed = 0;
            for (int i = 0; i < walls.Count; i++)
            {
                LineRecord first = walls[i];
                bool firstTagged = !string.IsNullOrEmpty(first.SegmentId) && !string.IsNullOrEmpty(first.Side);
                for (int j = i + 1; j < walls.Count; j++)
                {
                    LineRecord second = walls[j];
                    if (first.LayerId != second.LayerId || !AreCoplanar(first, second) ||
                        !AreCollinear(first, second) || ProjectedOverlap(first, second) > Tolerance) continue;

                    bool secondTagged = !string.IsNullOrEmpty(second.SegmentId) && !string.IsNullOrEmpty(second.Side);
                    bool sameWall = firstTagged && secondTagged
                        ? first.SegmentId == second.SegmentId && first.Side == second.Side
                        : !firstTagged && !secondTagged;
                    if (!sameWall) continue;

                    EndpointMatch match = GetClosestEndpoints(first, second);
                    if (match.Distance <= Tolerance || match.Distance > MaxGapHeal) continue;
                    Point3d joint = Midpoint(match.FirstPoint, match.SecondPoint);
                    if (!window.Contains(joint) || HasProtectingCap(caps, joint, typicalThickness)) continue;

                    // Mặt đối diện phải liền mạch suốt khe
                    double tw = Math.Max(typicalThickness, 1.0);
                    bool oppositeContinuous = walls.Any(op =>
                        op.Id != first.Id && op.Id != second.Id && op.LayerId == first.LayerId &&
                        AreParallel(first, op) &&
                        (firstTagged
                            ? op.SegmentId == first.SegmentId && !string.IsNullOrEmpty(op.Side) && op.Side != first.Side
                            : string.IsNullOrEmpty(op.SegmentId)) &&
                        DistancePointToInfiniteLine(match.FirstPoint, op) is double d && d > Tolerance && d <= tw * 1.5 &&
                        ProjectionFallsOnSegment(match.FirstPoint, op, Tolerance) &&
                        ProjectionFallsOnSegment(match.SecondPoint, op, Tolerance));
                    if (!oppositeContinuous) continue;

                    SetEndpoint(first, match.FirstIsStart, joint);
                    SetEndpoint(second, match.SecondIsStart, joint);
                    healed++;
                }
            }
            return healed;
        }

        private static bool WallPairsShareFace(WallPair first, WallPair second) => first.First.Id == second.First.Id || first.First.Id == second.Second.Id || first.Second.Id == second.First.Id || first.Second.Id == second.Second.Id;
        private static bool WallPairsCanHeal(WallPair first, WallPair second)
        {
            string? firstSegment = GetPairSegmentId(first); string? secondSegment = GetPairSegmentId(second);
            bool hasMetadata = !string.IsNullOrEmpty(firstSegment) || !string.IsNullOrEmpty(secondSegment);
            return !hasMetadata || (!string.IsNullOrEmpty(firstSegment) && firstSegment == secondSegment);
        }
        private static string? GetPairSegmentId(WallPair pair) => !string.IsNullOrEmpty(pair.First.SegmentId) ? pair.First.SegmentId : pair.Second.SegmentId;
        private static bool TryMatchWallFaces(WallPair firstWall, WallPair secondWall, out LineRecord firstFaceA, out LineRecord secondFaceA, out LineRecord firstFaceB, out LineRecord secondFaceB)
        {
            firstFaceA = firstWall.First; firstFaceB = firstWall.Second;
            bool directCompatible = SidesAreCompatible(firstWall.First, secondWall.First) && SidesAreCompatible(firstWall.Second, secondWall.Second);
            bool crossedCompatible = SidesAreCompatible(firstWall.First, secondWall.Second) && SidesAreCompatible(firstWall.Second, secondWall.First);
            double directScore = directCompatible ? GetClosestEndpoints(firstWall.First, secondWall.First).Distance + GetClosestEndpoints(firstWall.Second, secondWall.Second).Distance : double.MaxValue;
            double crossedScore = crossedCompatible ? GetClosestEndpoints(firstWall.First, secondWall.Second).Distance + GetClosestEndpoints(firstWall.Second, secondWall.First).Distance : double.MaxValue;
            if (directScore == double.MaxValue && crossedScore == double.MaxValue) { secondFaceA = secondWall.First; secondFaceB = secondWall.Second; return false; }
            if (directScore <= crossedScore) { secondFaceA = secondWall.First; secondFaceB = secondWall.Second; } else { secondFaceA = secondWall.Second; secondFaceB = secondWall.First; }
            return true;
        }
        private static bool HasProtectingCap(IEnumerable<LineRecord> caps, Point3d wallCenter, double wallThickness) => caps.Any(cap => Midpoint(cap.Start, cap.End).DistanceTo(wallCenter) <= Math.Max(5.0, wallThickness * 0.75));

        private static int SnapWallEndpoints(List<LineRecord> lines, IReadOnlyCollection<LineRecord> caps, RepairWindow window, IReadOnlyDictionary<ObjectId, double> thicknessByLine, ICollection<Point3d> junctionPoints)
        {
            List<EndpointMoveCandidate> candidates = new List<EndpointMoveCandidate>();
            foreach (LineRecord line in lines)
            {
                for (int endpointIndex = 0; endpointIndex < 2; endpointIndex++)
                {
                    Point3d endpoint = endpointIndex == 0 ? line.Start : line.End; Point3d otherEndpoint = endpointIndex == 0 ? line.End : line.Start;
                    bool sourceIsStart = endpointIndex == 0;
                    double sourceSearchDistance = Math.Max(15.0, GetWallThickness(line.Id, thicknessByLine) * 3.5);
                    bool endpointIsClosedByCap = HasCapAtEndpoint(caps, line, endpoint, GetWallThickness(line.Id, thicknessByLine));
                    bool endpointAlreadyTouchesWall = EndpointTouchesExistingWall(line, endpoint, lines);
                    bool sourceEndpointCanMove = !endpointIsClosedByCap && !endpointAlreadyTouchesWall;

                    foreach (LineRecord target in lines)
                    {
                        if (target.Id == line.Id || target.LayerId != line.LayerId || !AreCoplanar(line, target) || AreParallel(line, target)) continue;
                        if (!TryInfiniteIntersection(line, target, out Point3d intersection) || !window.Contains(intersection)) continue;

                        double fromEndpoint = endpoint.DistanceTo(intersection);
                        if (fromEndpoint > sourceSearchDistance || fromEndpoint > otherEndpoint.DistanceTo(intersection) + Tolerance) continue;

                        double targetStartDistance = target.Start.DistanceTo(intersection); double targetEndDistance = target.End.DistanceTo(intersection);
                        bool targetStartIsClosest = targetStartDistance <= targetEndDistance;
                        double targetEndpointDistance = Math.Min(targetStartDistance, targetEndDistance);
                        bool onTarget = IsPointOnSegment(intersection, target.Start, target.End, 1.0);
                        double endpointTolerance = Math.Max(Tolerance * 10.0, Math.Min(1.0, target.Start.DistanceTo(target.End) * 0.001));
                        bool onTargetInterior = onTarget && targetStartDistance > endpointTolerance && targetEndDistance > endpointTolerance;
                        double targetSearchDistance = Math.Max(15.0, GetWallThickness(target.Id, thicknessByLine) * 3.5);
                        Point3d targetEndpoint = targetStartIsClosest ? target.Start : target.End;
                        bool targetEndpointCanMove = !HasCapAtEndpoint(caps, target, targetEndpoint, GetWallThickness(target.Id, thicknessByLine)) && !EndpointTouchesExistingWall(target, targetEndpoint, lines);

                        if (sourceEndpointCanMove && targetEndpointCanMove && !onTargetInterior && targetEndpointDistance <= targetSearchDistance && IsOutwardExtension(endpoint, otherEndpoint, intersection) && IsOutwardExtension(targetEndpoint, targetStartIsClosest ? target.End : target.Start, intersection) && HasCompatibleCornerMovement(endpoint, otherEndpoint, targetStartIsClosest ? target.Start : target.End, targetStartIsClosest ? target.End : target.Start, intersection))
                        {
                            candidates.Add(new EndpointMoveCandidate { Point = intersection, IsCorner = true, Score = fromEndpoint + targetEndpointDistance, StableKey = MakeEndpointMoveKey(line, sourceIsStart, target, targetStartIsClosest), Endpoints = new List<EndpointReference> { new EndpointReference { Line = line, IsStart = sourceIsStart }, new EndpointReference { Line = target, IsStart = targetStartIsClosest } } });
                        }
                        else if (sourceEndpointCanMove && onTargetInterior && IsOutwardExtension(endpoint, otherEndpoint, intersection))
                        {
                            candidates.Add(new EndpointMoveCandidate { Point = intersection, IsCorner = false, Score = fromEndpoint, StableKey = MakeEndpointMoveKey(line, sourceIsStart, target, targetStartIsClosest), Endpoints = new List<EndpointReference> { new EndpointReference { Line = line, IsStart = sourceIsStart } } });
                        }
                    }
                }
            }

            int changed = 0;
            HashSet<string> claimedEndpoints = new HashSet<string>();
            foreach (EndpointMoveCandidate candidate in candidates.OrderBy(item => item.IsCorner ? 0 : 1).ThenBy(item => item.Score).ThenBy(item => item.StableKey, StringComparer.Ordinal))
            {
                List<string> endpointKeys = candidate.Endpoints.Select(endpoint => MakeEndpointKey(endpoint.Line.Id, endpoint.IsStart)).ToList();
                if (endpointKeys.Any(claimedEndpoints.Contains)) continue;
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

        private static bool EndpointTouchesExistingWall(LineRecord source, Point3d endpoint, IEnumerable<LineRecord> lines)
        {
            double connectionTolerance = Tolerance * 10.0;
            foreach (LineRecord target in lines)
            {
                if (target.Id == source.Id || target.LayerId != source.LayerId || !AreCoplanar(source, target) || AreParallel(source, target)) continue;
                if (DistancePointToInfiniteLine(endpoint, target) <= connectionTolerance && IsPointOnSegment(endpoint, target.Start, target.End, connectionTolerance)) return true;
            }
            return false;
        }

        private static bool IsOutwardExtension(Point3d endpoint, Point3d otherEndpoint, Point3d target) => (target - endpoint).DotProduct((endpoint - otherEndpoint).GetNormal()) >= -Tolerance;
        private static double GetWallThickness(ObjectId lineId, IReadOnlyDictionary<ObjectId, double> thicknessByLine) => thicknessByLine.TryGetValue(lineId, out double thickness) ? thickness : 0.0;
        private static bool HasCapAtEndpoint(IEnumerable<LineRecord> caps, LineRecord wallFace, Point3d endpoint, double wallThickness) => caps.Any(cap => cap.LayerId == wallFace.LayerId && AreCoplanar(wallFace, cap) && (cap.Start.DistanceTo(endpoint) <= Math.Max(Tolerance * 10.0, Math.Min(5.0, Math.Max(1.0, wallThickness * 0.025))) || cap.End.DistanceTo(endpoint) <= Math.Max(Tolerance * 10.0, Math.Min(5.0, Math.Max(1.0, wallThickness * 0.025)))));
        private static string MakeEndpointMoveKey(LineRecord source, bool sourceIsStart, LineRecord target, bool targetIsStart) => MakeEndpointKey(source.Id, sourceIsStart) + "|" + MakeEndpointKey(target.Id, targetIsStart);
        private static string MakeEndpointKey(ObjectId id, bool isStart) => id + (isStart ? ":S" : ":E");

        private static bool IsOnObsoleteJunctionCap(Point3d point, LineRecord source, IReadOnlyCollection<WallPair> wallPairs, IReadOnlyCollection<LineRecord> caps, double pointTolerance = 1.0)
        {
            foreach (LineRecord cap in caps)
            {
                if (cap.LayerId != source.LayerId || !AreCoplanar(source, cap) || !AreParallel(source, cap) || !IsPointOnSegment(point, cap.Start, cap.End, pointTolerance)) continue;
                if (!ProjectionFallsOnSegment(point, cap, Tolerance)) continue;
                if (wallPairs.Any(pair => CapClosesWallPair(cap, pair))) return true;
            }
            return false;
        }

        private static bool CapClosesWallPair(LineRecord cap, WallPair pair)
        {
            if (cap.LayerId != pair.First.LayerId || !AreCoplanar(cap, pair.First) || !AreCoplanar(cap, pair.Second)) return false;
            double endpointTolerance = Math.Max(Tolerance * 10.0, Math.Min(5.0, Math.Max(1.0, pair.Width * 0.025)));
            bool direct = IsNearEitherEndpoint(cap.Start, pair.First, endpointTolerance) && IsNearEitherEndpoint(cap.End, pair.Second, endpointTolerance);
            bool reversed = IsNearEitherEndpoint(cap.Start, pair.Second, endpointTolerance) && IsNearEitherEndpoint(cap.End, pair.First, endpointTolerance);
            return direct || reversed;
        }

        private static bool IsNearEitherEndpoint(Point3d point, LineRecord line, double tolerance) => point.DistanceTo(line.Start) <= tolerance || point.DistanceTo(line.End) <= tolerance;

        private static ReplaceResult ReplaceChangedLines(Transaction transaction, Database database, IEnumerable<LineRecord> sources, IReadOnlyDictionary<ObjectId, List<SegmentPiece>> output)
        {
            ReplaceResult result = new ReplaceResult();
            foreach (LineRecord source in sources)
            {
                List<SegmentPiece> pieces = output[source.Id];
                if (IsUnchanged(source, pieces)) { result.ResultIds.Add(source.Id); continue; }
                Line sourceEntity = (Line)transaction.GetObject(source.Id, OpenMode.ForWrite);
                BlockTableRecord owner = (BlockTableRecord)transaction.GetObject(sourceEntity.OwnerId, OpenMode.ForWrite);
                foreach (SegmentPiece piece in pieces)
                {
                    if (piece.Start.DistanceTo(piece.End) <= Tolerance) continue;
                    Line replacement = new Line(piece.Start, piece.End) { LayerId = sourceEntity.LayerId, Color = sourceEntity.Color, LineWeight = sourceEntity.LineWeight, LinetypeId = sourceEntity.LinetypeId, LinetypeScale = sourceEntity.LinetypeScale, Transparency = sourceEntity.Transparency };
                    owner.AppendEntity(replacement);
                    transaction.AddNewlyCreatedDBObject(replacement, true);
                    DrawWallHelper.CopyWallMetadata(transaction, database, sourceEntity, replacement);
                    result.ResultIds.Add(replacement.ObjectId);
                }
                sourceEntity.Erase(true);
                result.ChangedCount++;
            }
            return result;
        }

        private static int NormalizeCollinearResults(Transaction transaction, Database database, IEnumerable<ObjectId> candidateIds, double wallThickness, ICollection<ObjectId> createdIds)
        {
            List<NormalizeLine> lines = new List<NormalizeLine>();
            foreach (ObjectId id in candidateIds.Distinct())
            {
                try
                {
                    DBObject value = transaction.GetObject(id, OpenMode.ForRead);
                    if (value.IsErased || value is not Line line || line.StartPoint.DistanceTo(line.EndPoint) <= Tolerance || DrawWallHelper.IsWallCap(line)) continue;
                    lines.Add(new NormalizeLine { Id = id, Entity = line, Start = line.StartPoint, End = line.EndPoint, LayerId = line.LayerId, Side = DrawWallHelper.GetWallSideMarker(line), SegmentId = DrawWallHelper.GetWallSegmentId(line), IsCap = false });
                }
                catch { }
            }
            List<List<NormalizeLine>> collinearGroups = GroupCollinearLines(lines);
            double joinTolerance = Math.Max(Tolerance * 10.0, Math.Min(1.0, wallThickness * 0.001));
            int normalized = 0;
            foreach (List<NormalizeLine> group in collinearGroups)
            {
                NormalizeLine seed = group[0];
                Vector3d axis = (seed.End - seed.Start).GetNormal();
                Point3d origin = seed.Start;
                List<LineInterval> intervals = group.Select(line => CreateInterval(line, origin, axis)).OrderBy(interval => interval.StartStation).ToList();
                List<LineIntervalCluster> clusters = new List<LineIntervalCluster>();
                foreach (LineInterval interval in intervals)
                {
                    LineIntervalCluster? active = clusters.LastOrDefault();
                    if (active == null || interval.StartStation > active.EndStation + joinTolerance) clusters.Add(new LineIntervalCluster { StartStation = interval.StartStation, EndStation = interval.EndStation, Lines = new List<NormalizeLine> { interval.Line } });
                    else { active.EndStation = Math.Max(active.EndStation, interval.EndStation); active.Lines.Add(interval.Line); }
                }
                foreach (LineIntervalCluster cluster in clusters)
                {
                    Point3d mergedStart = origin + axis * cluster.StartStation;
                    Point3d mergedEnd = origin + axis * cluster.EndStation;
                    if (cluster.Lines.Count == 1 && SameSegment(cluster.Lines[0], mergedStart, mergedEnd)) continue;
                    if (HasInteriorJunction(mergedStart, mergedEnd, lines, cluster.Lines)) continue;
                    NormalizeLine source = cluster.Lines.OrderBy(line => line.IsCap ? 1 : 0).ThenBy(line => string.IsNullOrEmpty(line.Side) ? 1 : 0).ThenByDescending(line => line.Start.DistanceTo(line.End)).First();
                    Line sourceEntity = (Line)transaction.GetObject(source.Id, OpenMode.ForRead);
                    BlockTableRecord owner = (BlockTableRecord)transaction.GetObject(sourceEntity.OwnerId, OpenMode.ForWrite);
                    Line merged = new Line(mergedStart, mergedEnd) { LayerId = sourceEntity.LayerId, Color = sourceEntity.Color, LineWeight = sourceEntity.LineWeight, LinetypeId = sourceEntity.LinetypeId, LinetypeScale = sourceEntity.LinetypeScale, Transparency = sourceEntity.Transparency };
                    owner.AppendEntity(merged);
                    transaction.AddNewlyCreatedDBObject(merged, true);
                    DrawWallHelper.CopyWallMetadata(transaction, database, sourceEntity, merged);
                    createdIds.Add(merged.ObjectId);
                    foreach (NormalizeLine oldLine in cluster.Lines)
                    {
                        DBObject oldEntity = transaction.GetObject(oldLine.Id, OpenMode.ForWrite);
                        if (!oldEntity.IsErased) oldEntity.Erase(true);
                    }
                    normalized++;
                }
            }
            return normalized;
        }

        private static List<List<NormalizeLine>> GroupCollinearLines(IReadOnlyList<NormalizeLine> lines)
        {
            List<List<NormalizeLine>> groups = new List<List<NormalizeLine>>();
            bool[] used = new bool[lines.Count];
            for (int i = 0; i < lines.Count; i++)
            {
                if (used[i]) continue;
                List<NormalizeLine> group = new List<NormalizeLine> { lines[i] }; used[i] = true;
                for (int j = i + 1; j < lines.Count; j++)
                {
                    if (used[j] || lines[i].LayerId != lines[j].LayerId || !MetadataAllowsMerge(lines[i], lines[j]) || !AreCollinear(lines[i], lines[j])) continue;
                    group.Add(lines[j]); used[j] = true;
                }
                groups.Add(group);
            }
            return groups;
        }

        private static bool AreCollinear(NormalizeLine first, NormalizeLine second) 
            => Math.Abs((first.End - first.Start).GetNormal().DotProduct((second.End - second.Start).GetNormal())) > ParallelDotTolerance && DistancePointToInfiniteLine(second.Start, first.Start, (first.End - first.Start).GetNormal()) <= Tolerance * 10.0 && DistancePointToInfiniteLine(second.End, first.Start, (first.End - first.Start).GetNormal()) <= Tolerance * 10.0;
       
        private static bool HasInteriorJunction(Point3d start, Point3d end, IReadOnlyCollection<NormalizeLine> allLines, IReadOnlyCollection<NormalizeLine> clusterLines)
        {
            foreach (NormalizeLine other in allLines)
            {
                if (clusterLines.Contains(other) || AreCollinear(new NormalizeLine { Start = start, End = end }, other)) continue;
                if (!TryIntersection(start, end, other.Start, other.End, true, out Point3d junction)) continue;
                if (junction.DistanceTo(start) > Tolerance * 10.0 && junction.DistanceTo(end) > Tolerance * 10.0) return true;
            }
            return false;
        }
        private static bool MetadataAllowsMerge(NormalizeLine first, NormalizeLine second) 
            => (!first.IsCap && !second.IsCap) && (string.IsNullOrEmpty(first.SegmentId) && string.IsNullOrEmpty(second.SegmentId) ? (string.IsNullOrEmpty(first.Side) || string.IsNullOrEmpty(second.Side) || first.Side == second.Side) : (!string.IsNullOrEmpty(first.SegmentId) && first.SegmentId == second.SegmentId && !string.IsNullOrEmpty(first.Side) && first.Side == second.Side));
        private static LineInterval CreateInterval(NormalizeLine line, Point3d origin, Vector3d axis) 
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

        private static bool SameSegment(NormalizeLine line, Point3d start, Point3d end) 
            => (line.Start.DistanceTo(start) <= Tolerance && line.End.DistanceTo(end) <= Tolerance) || (line.Start.DistanceTo(end) <= Tolerance && line.End.DistanceTo(start) <= Tolerance);
       
        private static bool IsUnchanged(LineRecord source, IReadOnlyList<SegmentPiece> output)
            => output.Count == 1 && ((source.OriginalStart.DistanceTo(output[0].Start) <= Tolerance && source.OriginalEnd.DistanceTo(output[0].End) <= Tolerance) || (source.OriginalStart.DistanceTo(output[0].End) <= Tolerance && source.OriginalEnd.DistanceTo(output[0].Start) <= Tolerance));
       
        private static int CountDistinctPoints(IEnumerable<Point3d> points) 
        {
            List<Point3d> distinct = new List<Point3d>();
            foreach (Point3d point in points)
                if (!distinct.Any(existing => existing.DistanceTo(point) <= Tolerance)) distinct.Add(point); return distinct.Count; 
        }

        private static bool SidesAreCompatible(LineRecord first, LineRecord second) 
            => string.IsNullOrEmpty(first.Side) || string.IsNullOrEmpty(second.Side) || first.Side == second.Side;

        private static bool CanFormWallPair(LineRecord first, LineRecord second) 
            => string.IsNullOrEmpty(first.Side) || string.IsNullOrEmpty(second.Side) || first.Side != second.Side;
        private static bool AreParallel(LineRecord first, LineRecord second)
            => Math.Abs((first.End - first.Start).GetNormal().DotProduct((second.End - second.Start).GetNormal())) > ParallelDotTolerance;
       
        private static bool AreCollinear(LineRecord first, LineRecord second)
            => AreParallel(first, second) && AreCoplanar(first, second) && DistancePointToInfiniteLine(second.Start, first) <= Tolerance * 10.0 && DistancePointToInfiniteLine(second.End, first) <= Tolerance * 10.0;
        private static bool AreCoplanar(LineRecord first, LineRecord second)
            => Math.Max(Math.Max(first.Start.Z, first.End.Z), Math.Max(second.Start.Z, second.End.Z)) - Math.Min(Math.Min(first.Start.Z, first.End.Z), Math.Min(second.Start.Z, second.End.Z)) <= CoplanarTolerance;
        private static double ProjectedOverlap(LineRecord first, LineRecord second) 
        {
            Vector3d dir = (first.End - first.Start).GetNormal();
            double start = (second.Start - first.Start).DotProduct(dir);
            double end = (second.End - first.Start).DotProduct(dir);

            return
                Math.Min(first.Start.DistanceTo(first.End),
                Math.Max(start, end)) - Math.Max(0.0, Math.Min(start, end));
        }

        private static double DistancePointToInfiniteLine(Point3d point, LineRecord line)
            => DistancePointToInfiniteLine(point, line.Start, (line.End - line.Start).GetNormal());

        private static double DistancePointToInfiniteLine(Point3d point, Point3d lineStart, Vector3d direction)
            => ((point - lineStart) - direction * (point - lineStart).DotProduct(direction)).Length;

        private static double Station(LineRecord line, Point3d point)
            => (point - line.Start).DotProduct((line.End - line.Start).GetNormal());

        private static bool TryInfiniteIntersection(LineRecord first, LineRecord second, out Point3d intersection)
            => TryIntersection(first.Start, first.End, second.Start, second.End, false, out intersection);

        private static bool TrySegmentIntersection(LineRecord first, LineRecord second, out Point3d intersection) 
            => TryIntersection(first.Start, first.End, second.Start, second.End, true, out intersection);

        private static bool TryIntersection(LineRecord first, LineRecord second, bool requireSegments, out Point3d intersection) 
            => TryIntersection(first.Start, first.End, second.Start, second.End, requireSegments, out intersection);

        private static bool TryIntersection(Point3d firstStart, Point3d firstEnd, Point3d secondStart, Point3d secondEnd, bool requireSegments, out Point3d intersection)
        { 
            double ax = firstEnd.X - firstStart.X; 
            double ay = firstEnd.Y - firstStart.Y; 
            double bx = secondEnd.X - secondStart.X;
            double by = secondEnd.Y - secondStart.Y;
            double denominator = ax * by - ay * bx; 
            intersection = Point3d.Origin; if (Math.Abs(denominator) <= Tolerance) 

                return false; 

            double dx = secondStart.X - firstStart.X; 
            double dy = secondStart.Y - firstStart.Y;
            double fp = (dx * by - dy * bx) / denominator;
            double sp = (dx * ay - dy * ax) / denominator; 

            if (requireSegments && (fp < -Tolerance || fp > 1.0 + Tolerance || sp < -Tolerance || sp > 1.0 + Tolerance)) 
                return false; 
            
            intersection = new Point3d(firstStart.X + fp * ax, firstStart.Y + fp * ay, firstStart.Z + fp * (firstEnd.Z - firstStart.Z)); 
            return true;

        }
        private static bool IsPointOnSegment(Point3d point, Point3d start, Point3d end, double tolerance) 
            => Math.Abs(point.DistanceTo(start) + point.DistanceTo(end) - start.DistanceTo(end)) <= tolerance;

        private static bool HasCompatibleCornerMovement(Point3d endpoint, Point3d otherEndpoint, Point3d targetEndpoint, Point3d targetOtherEndpoint, Point3d intersection)
            => (intersection - endpoint).DotProduct((endpoint - otherEndpoint).GetNormal()) >= -Tolerance && (intersection - targetEndpoint).DotProduct((targetEndpoint - targetOtherEndpoint).GetNormal()) >= -Tolerance;

        private static EndpointMatch GetClosestEndpoints(LineRecord first, LineRecord second) 
            => new[] 
            { 
                CreateEndpointMatch(first.Start, true, second.Start, true), 
                CreateEndpointMatch(first.Start, true, second.End, false), 
                CreateEndpointMatch(first.End, false, second.Start, true), 
                CreateEndpointMatch(first.End, false, second.End, false) 
            }.OrderBy(c => c.Distance).First();

        private static EndpointMatch CreateEndpointMatch(Point3d first, bool firstIsStart, Point3d second, bool secondIsStart)
            => new EndpointMatch { FirstPoint = first, FirstIsStart = firstIsStart, SecondPoint = second, SecondIsStart = secondIsStart, Distance = first.DistanceTo(second) };

        private static void SetEndpoint(LineRecord line, bool start, Point3d point)
        { 
            if 
                (start) line.Start = point; 
            else 
                line.End = point; }
        private static Point3d Midpoint(Point3d first, Point3d second)
            => new Point3d((first.X + second.X) / 2.0, (first.Y + second.Y) / 2.0, (first.Z + second.Z) / 2.0);

        private static double Median(IEnumerable<double> values) 
        { 
            var o = values.OrderBy(v => v).ToList();
            if 
                (o.Count == 0) return 0.0; int m = o.Count / 2;
            return o.Count % 2 == 0 ? (o[m - 1] + o[m]) / 2.0 : o[m]; }

        
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

        private sealed class SegmentPiece
        { 
            public Point3d Start { get; set; }
            public Point3d End { get; set; }
        }

        private sealed class SegInfo
        {
            public Point3d Start;
            public Point3d End;
            public bool Core;
            public bool Remove;
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
            public bool SecondIsStart; public double Distance;
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

            public static RepairWindow FromLines(IReadOnlyCollection<LineRecord> lines, double padding)
            {
                double minX = lines.Min(line => Math.Min(line.Start.X, line.End.X)) - padding; 
                double minY = lines.Min(line => Math.Min(line.Start.Y, line.End.Y)) - padding;
                double maxX = lines.Max(line => Math.Max(line.Start.X, line.End.X)) + padding;
                double maxY = lines.Max(line => Math.Max(line.Start.Y, line.End.Y)) + padding; 

                return new RepairWindow(new Point3d(minX, minY, 0.0), new Point3d(maxX, maxY, 0.0));
            }

            public bool Intersects(double minX, double minY, double maxX, double maxY)
                => !(maxX < _minX - Tolerance || minX > _maxX + Tolerance || maxY < _minY - Tolerance || minY > _maxY + Tolerance);

            public bool Contains(Point3d point)
                => point.X >= _minX - Tolerance && point.X <= _maxX + Tolerance && point.Y >= _minY - Tolerance && point.Y <= _maxY + Tolerance;
        }

        private sealed class RepairSummary 
        {
            public int WallLineCount { get; init; }
            public int ChangedLineCount { get; init; }
            public int JunctionCount { get; init; }
            public int HealedGapCount { get; init; }
            public ObjectId[] NextSelection { get; init; } = Array.Empty<ObjectId>();
        }
    }
}