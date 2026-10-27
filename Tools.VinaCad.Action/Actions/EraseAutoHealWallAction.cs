using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.Resources.Definitions;
using Tools.VinaCad.Helper.Helper;
using PrLogTrackingSystem;
using Application = Prima.VinaCAD.ApplicationServices.Application;
using MessageBox = System.Windows.MessageBox;

namespace Tools.VinaCAD.Action.Actions
{
    public class ErasedZoneInfo
    {
        public Extents3d Bounds;
        public Vector3d Direction;
        public string SegmentId;
        public Point3d Start;   
        public Point3d End;    
    }

    public class EraseAutoHealWallAction
    {
        private const double Tolerance = 0.001;
        private const double CollinearAngleTolerance = 0.002;

        private double _zonePadding = 600.0;
        private double _healExpansion = 500.0;
        private double _cornerSearchRadius = 250.0;
        private double _maxStubLength = 500.0;
        private double _maxHealGap = 10.0;
        private double _maxSquareOffset = 15.0;   
        private double _maxBridgeGap = 300.0;     
        private double _wallThickness = 200.0;    

        public void Execute()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            PromptSelectionOptions pso = new PromptSelectionOptions();
            pso.MessageForAdding = "\nQuét chọn các mặt tường cần xóa (Có thể chọn nhiều) -> Nhấn Enter: ";

            PromptSelectionResult psr = ed.GetSelection(pso);
            if (psr.Status != PromptStatus.OK) return;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    Extents3d? totalExtents = null;
                    HashSet<ObjectId> selectedIds = new HashSet<ObjectId>(
                        psr.Value.Cast<SelectedObject>().Where(x => x != null).Select(x => x.ObjectId));
                    int originalCount = selectedIds.Count;

                    BlockTable blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    ConfigureTolerances(tr, modelSpace, selectedIds);

                    ExpandWallPairs(tr, db, modelSpace, selectedIds);

                    List<ObjectId> erasedIds = new List<ObjectId>();
                    List<ErasedZoneInfo> erasedZones = new List<ErasedZoneInfo>();
                    HashSet<Point3d> erasedEndpoints = new HashSet<Point3d>();

                    foreach (ObjectId objectId in selectedIds)
                    {
                        DBObject obj = tr.GetObject(objectId, OpenMode.ForWrite);
                        if (obj == null || obj.IsErased) continue;

                        if (obj is Line selLine)
                        {
                            erasedEndpoints.Add(selLine.StartPoint);
                            erasedEndpoints.Add(selLine.EndPoint);
                            Vector3d dir = (selLine.EndPoint - selLine.StartPoint).GetNormal();
                            string segId = DrawWallHelper.GetWallSegmentId(selLine);

                            try
                            {
                                Extents3d ext = selLine.GeometricExtents;
                                Extents3d zoneBounds = new Extents3d(
                                    new Point3d(ext.MinPoint.X - _zonePadding, ext.MinPoint.Y - _zonePadding, 0),
                                    new Point3d(ext.MaxPoint.X + _zonePadding, ext.MaxPoint.Y + _zonePadding, 0)
                                );

                                erasedZones.Add(new ErasedZoneInfo
                                {
                                    Bounds = zoneBounds,
                                    Direction = dir,
                                    SegmentId = segId,
                                    Start = selLine.StartPoint,
                                    End = selLine.EndPoint
                                });

                                if (totalExtents == null) totalExtents = ext;
                                else totalExtents = new Extents3d(
                                    new Point3d(Math.Min(totalExtents.Value.MinPoint.X, ext.MinPoint.X),
                                                Math.Min(totalExtents.Value.MinPoint.Y, ext.MinPoint.Y), 0),
                                    new Point3d(Math.Max(totalExtents.Value.MaxPoint.X, ext.MaxPoint.X),
                                                Math.Max(totalExtents.Value.MaxPoint.Y, ext.MaxPoint.Y), 0)
                                );
                            }
                            catch {}
                        }
                    }

                    CleanUpStubs(tr, modelSpace, selectedIds, erasedZones, erasedIds, erasedEndpoints);

                    foreach (ObjectId objectId in selectedIds)
                    {
                        DBObject obj = tr.GetObject(objectId, OpenMode.ForWrite);
                        if (!obj.IsErased)
                        {
                            obj.Erase(true);
                            erasedIds.Add(objectId);
                        }
                    }
                    if (totalExtents != null)
                    {
                        HealBrokenWalls(tr, db, modelSpace, totalExtents.Value, erasedZones, erasedEndpoints, erasedIds);
                    }

                    tr.Commit();
                    int autoSelected = Math.Max(0, selectedIds.Count - originalCount);
                    ed.WriteMessage($"\nĐã xóa, bo góc và vá lỗi cho {erasedIds.Count} mảng tường (Bao gồm {autoSelected} mảng vụn/đối diện).");
                }
                catch (Exception ex)
                {
                    tr.Abort();
                    Logger.Info(nameof(EraseAutoHealWallAction), ex);
                    MessageBox.Show($"Lỗi xử lý: {ex.Message}", StringDefinition.TITLE_ERROR);
                }
            }
        }

        private void ConfigureTolerances(Transaction tr, BlockTableRecord modelSpace, HashSet<ObjectId> selectedIds)
        {
            Line selected = selectedIds
                .Select(id => tr.GetObject(id, OpenMode.ForRead) as Line)
                .FirstOrDefault(line => line != null && !line.IsErased && line.Length > Tolerance);
            if (selected == null) return;

            Vector3d direction = (selected.EndPoint - selected.StartPoint).GetNormal();
            string segmentId = DrawWallHelper.GetWallSegmentId(selected);
            string side = DrawWallHelper.GetWallSideMarker(selected);
            double thickness = double.MaxValue;

            foreach (ObjectId id in modelSpace)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not Line candidate || candidate.IsErased ||
                    candidate.ObjectId == selected.ObjectId || candidate.LayerId != selected.LayerId ||
                    DrawWallHelper.IsWallCap(candidate) || candidate.Length <= Tolerance) continue;

                string candidateSegment = DrawWallHelper.GetWallSegmentId(candidate);
                string candidateSide = DrawWallHelper.GetWallSideMarker(candidate);
                if (!string.IsNullOrEmpty(segmentId) &&
                    (candidateSegment != segmentId || candidateSide == side)) continue;
                if (!AreParallelAndOverlapping(selected, candidate)) continue;

                double distance = DistanceToInfiniteLine(candidate.StartPoint, selected.StartPoint, direction);
                if (distance > Tolerance) thickness = Math.Min(thickness, distance);
            }

            if (double.IsInfinity(thickness) || thickness == double.MaxValue) thickness = 200.0;
            thickness = Math.Max(50.0, thickness);

            _zonePadding = thickness * 3.0;
            _healExpansion = thickness * 2.5;
            _cornerSearchRadius = thickness * 1.25;
            _maxStubLength = thickness * 2.5;
            _maxHealGap = Math.Clamp(thickness * 0.05, 5.0, 20.0);
            _maxSquareOffset = thickness * 1.5;   // NEW
            _maxBridgeGap = thickness * 1.5;      // NEW
            _wallThickness = thickness;           // NEW
        }

        private void CleanUpStubs(Transaction tr, BlockTableRecord modelSpace, HashSet<ObjectId> selectedIds, List<ErasedZoneInfo> erasedZones, List<ObjectId> erasedIds, HashSet<Point3d> erasedEndpoints)
        {
            HashSet<string> targetSegIds = new HashSet<string>();
            foreach (var zone in erasedZones)
            {
                if (!string.IsNullOrEmpty(zone.SegmentId)) targetSegIds.Add(zone.SegmentId);
            }

            foreach (ObjectId id in modelSpace)
            {
                if (selectedIds.Contains(id)) continue;

                DBObject obj = tr.GetObject(id, OpenMode.ForRead);
                if (obj is Line line && !line.IsErased && !DrawWallHelper.IsWallCap(line))
                {
                    string segId = DrawWallHelper.GetWallSegmentId(line);
                    if (targetSegIds.Contains(segId))
                    {
                        if (line.Length <= _maxStubLength)
                        {
                            Point3d mid = new Point3d((line.StartPoint.X + line.EndPoint.X) / 2, (line.StartPoint.Y + line.EndPoint.Y) / 2, 0);
                            if (IsInsideAnyZoneBounds(mid, erasedZones))
                            {
                                selectedIds.Add(id);
                                erasedEndpoints.Add(line.StartPoint);   
                                erasedEndpoints.Add(line.EndPoint);     
                            }
                        }
                    }
                }
            }
        }

        private void ExpandWallPairs(Transaction tr, Database db, BlockTableRecord modelSpace, HashSet<ObjectId> selectedIds)
        {
            List<Line> wallLines = new List<Line>();
            foreach (ObjectId id in modelSpace)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is Line line && !line.IsErased && !DrawWallHelper.IsWallCap(line))
                    wallLines.Add(line);
            }

            foreach (ObjectId selectedId in selectedIds.ToList())
            {
                if (tr.GetObject(selectedId, OpenMode.ForRead) is not Line selectedLine || DrawWallHelper.IsWallCap(selectedLine)) continue;

                string segmentId = DrawWallHelper.GetWallSegmentId(selectedLine);
                if (!string.IsNullOrEmpty(segmentId))
                {
                    string selectedSide = DrawWallHelper.GetWallSideMarker(selectedLine);

                    foreach (Line candidate in wallLines)
                    {
                        if (candidate.ObjectId == selectedLine.ObjectId) continue;

                        string candidateSegId = DrawWallHelper.GetWallSegmentId(candidate);
                        string candidateSide = DrawWallHelper.GetWallSideMarker(candidate);

                        if (candidateSegId == segmentId && candidateSide != selectedSide)
                        {
                            if (AreParallelAndOverlapping(selectedLine, candidate))
                            {
                                ProcessPairedLine(tr, db, modelSpace, selectedLine, candidate, selectedIds);
                            }
                        }
                    }
                    continue;
                }

                Line pairedLine = FindLegacyPairedLine(selectedLine, wallLines);
                if (pairedLine != null) ProcessPairedLine(tr, db, modelSpace, selectedLine, pairedLine, selectedIds);
            }
        }

        private void ProcessPairedLine(Transaction tr, Database db, BlockTableRecord modelSpace, Line selected, Line paired, HashSet<ObjectId> selectedIds)
        {
            if (selectedIds.Contains(paired.ObjectId)) return;

            Vector3d pairDir = (paired.EndPoint - paired.StartPoint).GetNormal();
            double pairLen = paired.Length;

            double proj1 = (selected.StartPoint - paired.StartPoint).DotProduct(pairDir);
            double proj2 = (selected.EndPoint - paired.StartPoint).DotProduct(pairDir);

            double minProj = Math.Min(proj1, proj2);
            double maxProj = Math.Max(proj1, proj2);

            double start = Math.Max(0.0, minProj);
            double end = Math.Min(pairLen, maxProj);
            if (end <= start + Tolerance) return;

            double breakThreshold = 100.0;

            bool breakStart = start > breakThreshold;
            bool breakEnd = end < pairLen - breakThreshold;

            if (breakStart || breakEnd)
            {
                paired.UpgradeOpen();

                if (breakStart)
                {
                    Point3d pt = paired.StartPoint + pairDir * start;
                    Line l1 = new Line(paired.StartPoint, pt) { LayerId = paired.LayerId, Color = paired.Color, LineWeight = paired.LineWeight, Linetype = paired.Linetype };
                    modelSpace.AppendEntity(l1);
                    tr.AddNewlyCreatedDBObject(l1, true);
                    DrawWallHelper.CopyWallMetadata(tr, db, paired, l1);
                }
                if (breakEnd)
                {
                    Point3d pt = paired.StartPoint + pairDir * end;
                    Line l2 = new Line(pt, paired.EndPoint) { LayerId = paired.LayerId, Color = paired.Color, LineWeight = paired.LineWeight, Linetype = paired.Linetype };
                    modelSpace.AppendEntity(l2);
                    tr.AddNewlyCreatedDBObject(l2, true);
                    DrawWallHelper.CopyWallMetadata(tr, db, paired, l2);
                }

                Point3d midStart = paired.StartPoint + pairDir * start;
                Point3d midEnd = paired.StartPoint + pairDir * end;
                Line midLine = new Line(midStart, midEnd) { LayerId = paired.LayerId, Color = paired.Color, LineWeight = paired.LineWeight, Linetype = paired.Linetype };

                modelSpace.AppendEntity(midLine);
                tr.AddNewlyCreatedDBObject(midLine, true);
                DrawWallHelper.CopyWallMetadata(tr, db, paired, midLine);

                CreateCutCaps(tr, db, modelSpace, selected, paired, pairDir, start, end);

                paired.Erase(true);
                selectedIds.Add(midLine.ObjectId);
            }
            else
            {
                CreateCutCaps(tr, db, modelSpace, selected, paired, pairDir, start, end);
                selectedIds.Add(paired.ObjectId);
            }
        }

        private void CreateCutCaps(Transaction tr, Database db, BlockTableRecord modelSpace, Line selected, Line paired, Vector3d pairDir, double start, double end)
        {
            TryCreateCutCap(tr, db, modelSpace, selected.StartPoint, paired.StartPoint + pairDir * start, selected.LayerId, selected.Color, selected.Linetype, selected.LineWeight);
            TryCreateCutCap(tr, db, modelSpace, selected.EndPoint, paired.StartPoint + pairDir * end, selected.LayerId, selected.Color, selected.Linetype, selected.LineWeight);
        }

        private void TryCreateCutCap(Transaction tr, Database db, BlockTableRecord modelSpace, Point3d first, Point3d second, ObjectId layerId, Teigha.Colors.Color color, string linetype, LineWeight lineWeight)
        {
            if (first.DistanceTo(second) <= Tolerance) return;

            foreach (ObjectId id in modelSpace)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not Line existing || existing.IsErased ||
                    DrawWallHelper.IsWallCap(existing)) continue;

                if ((existing.StartPoint.DistanceTo(first) <= Tolerance && existing.EndPoint.DistanceTo(second) <= Tolerance) ||
                    (existing.StartPoint.DistanceTo(second) <= Tolerance && existing.EndPoint.DistanceTo(first) <= Tolerance)) return;
            }

            Line cap = new Line(first, second)
            {
                LayerId = layerId,
                Color = color,
                Linetype = linetype,
                LineWeight = lineWeight
            };
            modelSpace.AppendEntity(cap);
            tr.AddNewlyCreatedDBObject(cap, true);
            DrawWallHelper.TagAsCap(tr, db, cap);
        }

        private Line FindLegacyPairedLine(Line selected, List<Line> candidates)
        {
            Vector3d selectedVector = selected.EndPoint - selected.StartPoint;
            double selectedLength = selectedVector.Length;
            if (selectedLength <= Tolerance) return null;

            Vector3d direction = selectedVector.GetNormal();
            string selectedSide = DrawWallHelper.GetWallSideMarker(selected);
            Line best = null;
            double bestDistance = double.MaxValue;

            foreach (Line candidate in candidates)
            {
                if (candidate.ObjectId == selected.ObjectId || candidate.LayerId != selected.LayerId) continue;

                Vector3d candidateVector = candidate.EndPoint - candidate.StartPoint;
                if (candidateVector.Length <= Tolerance ||
                    Math.Abs(direction.DotProduct(candidateVector.GetNormal())) < 1.0 - CollinearAngleTolerance) continue;

                string candidateSide = DrawWallHelper.GetWallSideMarker(candidate);
                if (!string.IsNullOrEmpty(selectedSide) && selectedSide == candidateSide) continue;

                if (!AreParallelAndOverlapping(selected, candidate)) continue;

                double distance = DistanceToInfiniteLine(candidate.StartPoint, selected.StartPoint, direction);
                if (distance <= Tolerance || distance >= bestDistance) continue;

                bestDistance = distance;
                best = candidate;
            }

            return best;
        }

        private static double DistanceToInfiniteLine(Point3d point, Point3d linePoint, Vector3d direction)
        {
            Vector3d offset = point - linePoint;
            return Math.Abs(offset.X * direction.Y - offset.Y * direction.X);
        }

        private bool AreParallelAndOverlapping(Line first, Line second)
        {
            Vector3d firstVector = first.EndPoint - first.StartPoint;
            Vector3d secondVector = second.EndPoint - second.StartPoint;
            if (firstVector.Length <= Tolerance || secondVector.Length <= Tolerance) return false;

            Vector3d direction = firstVector.GetNormal();
            if (Math.Abs(direction.DotProduct(secondVector.GetNormal())) < 1.0 - CollinearAngleTolerance) return false;

            double dist = DistanceToInfiniteLine(second.StartPoint, first.StartPoint, direction);
            if (dist > _zonePadding) return false;

            double firstLength = firstVector.Length;
            double startProjection = (second.StartPoint - first.StartPoint).DotProduct(direction);
            double endProjection = (second.EndPoint - first.StartPoint).DotProduct(direction);

            double minP = Math.Min(startProjection, endProjection);
            double maxP = Math.Max(startProjection, endProjection);

            return maxP > Tolerance && minP < firstLength - Tolerance;
        }

        private void HealBrokenWalls(Transaction tr, Database db, BlockTableRecord modelSpace, Extents3d totalExtents, List<ErasedZoneInfo> erasedZones, HashSet<Point3d> erasedEndpoints, List<ObjectId> erasedIds)
        {
            Point3d minPt = new Point3d(totalExtents.MinPoint.X - _healExpansion, totalExtents.MinPoint.Y - _healExpansion, 0);
            Point3d maxPt = new Point3d(totalExtents.MaxPoint.X + _healExpansion, totalExtents.MaxPoint.Y + _healExpansion, 0);

            List<LineRecord> linesToHeal = new List<LineRecord>();
            List<Line> validWallLines = new List<Line>();

            foreach (ObjectId id in modelSpace)
            {
                if (erasedIds.Contains(id)) continue;

                DBObject obj = tr.GetObject(id, OpenMode.ForRead);
                if (obj.IsErased || !(obj is Entity ent)) continue;

                if (!IsEntityInBounds(ent, minPt, maxPt)) continue;

                if (ent is Line line)
                {
                    if (DrawWallHelper.IsWallCap(line))
                    {
                        bool touchesErased = erasedEndpoints.Any(pt =>
                            pt.DistanceTo(line.StartPoint) <= Tolerance || pt.DistanceTo(line.EndPoint) <= Tolerance);

                        Point3d mid = new Point3d((line.StartPoint.X + line.EndPoint.X) / 2.0, (line.StartPoint.Y + line.EndPoint.Y) / 2.0, 0);

                        if (touchesErased || IsInsideAnyZoneBounds(mid, erasedZones))
                        {
                            line.UpgradeOpen();
                            line.Erase(true);
                        }
                        continue;
                    }
                    linesToHeal.Add(new LineRecord { Start = line.StartPoint, End = line.EndPoint, Entity = line });
                    validWallLines.Add(line);
                }
            }

            var groups = GroupCollinearLines(linesToHeal, erasedZones, validWallLines);
            foreach (var group in groups)
            {
                if (group.Count > 1) JoinLines(tr, db, modelSpace, group, validWallLines);
            }

            List<Line> Alive() => validWallLines.Where(l => !l.IsErased && !DrawWallHelper.IsWallCap(l)).ToList();

            HealCorners(Alive(), erasedEndpoints);

            SquareAndCapDanglingEnds(tr, db, modelSpace, Alive(), erasedZones);
        }

        private void SquareAndCapDanglingEnds(Transaction tr, Database db, BlockTableRecord modelSpace, List<Line> lines, List<ErasedZoneInfo> zones)
        {
            double maxPair = _wallThickness * 1.3;

            for (int i = 0; i < lines.Count; i++)
            {
                Line a = lines[i];
                if (a.IsErased || a.Length <= Tolerance) continue;
                Vector3d dir = (a.EndPoint - a.StartPoint).GetNormal();
                double lenA = a.Length;

                for (int j = i + 1; j < lines.Count; j++)
                {
                    Line b = lines[j];
                    if (b.IsErased || b.LayerId != a.LayerId || b.Length <= Tolerance) continue;
                    if (Math.Abs(dir.DotProduct((b.EndPoint - b.StartPoint).GetNormal())) < 1.0 - CollinearAngleTolerance) continue;

                    double perp = DistanceToInfiniteLine(b.StartPoint, a.StartPoint, dir);
                    if (perp <= Tolerance || perp > maxPair) continue;

                    double pbS = (b.StartPoint - a.StartPoint).DotProduct(dir);
                    double pbE = (b.EndPoint - a.StartPoint).DotProduct(dir);
                    double bMin = Math.Min(pbS, pbE), bMax = Math.Max(pbS, pbE);
                    if (bMax <= Tolerance || bMin >= lenA - Tolerance) continue; // không chồng lấn

                    foreach (bool atMax in new[] { false, true })
                    {
                        double aPos = atMax ? lenA : 0.0;
                        double bPos = atMax ? bMax : bMin;
                        if (Math.Abs(aPos - bPos) > _maxSquareOffset) continue;

                        double target = atMax ? Math.Max(aPos, bPos) : Math.Min(aPos, bPos);
                        Point3d pA = a.StartPoint + dir * target;
                        Point3d pB = b.StartPoint + dir * ((pA - b.StartPoint).DotProduct(dir));

                        Point3d mid = new Point3d((pA.X + pB.X) / 2, (pA.Y + pB.Y) / 2, 0);
                        if (!IsInsideAnyZoneBounds(mid, zones)) continue;

                        if (HasWallTouchingSegment(pA, pB, dir, a, b, lines)) continue;

                        if (Math.Abs(aPos - target) > Tolerance)
                        {
                            a.UpgradeOpen();
                            if (atMax) a.EndPoint = pA; else a.StartPoint = pA;
                        }
                        if (Math.Abs(bPos - target) > Tolerance)
                        {
                            b.UpgradeOpen();
                            bool bStartIsThisEnd = atMax ? (pbS >= pbE) : (pbS <= pbE);
                            if (bStartIsThisEnd) b.StartPoint = pB; else b.EndPoint = pB;
                        }

                        CreateCapIfMissing(tr, db, modelSpace, pA, pB, a);
                    }
                }
            }
        }

        private bool HasWallTouchingSegment(Point3d s, Point3d e, Vector3d wallDir, Line ex1, Line ex2, List<Line> lines)
        {
            foreach (Line l in lines)
            {
                if (l.IsErased || l.ObjectId == ex1.ObjectId || l.ObjectId == ex2.ObjectId) continue;
                Vector3d v = l.EndPoint - l.StartPoint;
                if (v.Length <= Tolerance) continue;
                if (Math.Abs(wallDir.DotProduct(v.GetNormal())) >= 1.0 - CollinearAngleTolerance) continue;
                if (SegmentsTouchOrCross(s, e, l.StartPoint, l.EndPoint, 2.0)) return true;
            }
            return false;
        }

        private void CreateCapIfMissing(Transaction tr, Database db, BlockTableRecord modelSpace, Point3d p1, Point3d p2, Line src)
        {
            if (p1.DistanceTo(p2) <= Tolerance) return;

            foreach (ObjectId id in modelSpace)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not Line ex || ex.IsErased) continue;
                if ((ex.StartPoint.DistanceTo(p1) <= Tolerance && ex.EndPoint.DistanceTo(p2) <= Tolerance) ||
                    (ex.StartPoint.DistanceTo(p2) <= Tolerance && ex.EndPoint.DistanceTo(p1) <= Tolerance)) return;
            }

            Line cap = new Line(p1, p2)
            {
                LayerId = src.LayerId,
                Color = src.Color,
                Linetype = src.Linetype,
                LineWeight = src.LineWeight
            };
            modelSpace.AppendEntity(cap);
            tr.AddNewlyCreatedDBObject(cap, true);
            DrawWallHelper.TagAsCap(tr, db, cap);
        }

        private List<Line> GetThickWallLines(List<Line> group, Point3d center)
        {
            if (group.Count == 0) return new List<Line>();

            var sorted = group.OrderBy(l => Math.Min(l.StartPoint.DistanceTo(center), l.EndPoint.DistanceTo(center))).ToList();

            var first = sorted.First();
            Vector3d dir = (first.EndPoint - first.StartPoint).GetNormal();

            var second = sorted.FirstOrDefault(l => DistanceToInfiniteLine(l.StartPoint, first.StartPoint, dir) > 50.0);

            if (second != null) return new List<Line> { first, second };
            return new List<Line> { first };
        }

        private void HealCorners(List<Line> validWallLines, HashSet<Point3d> erasedEndpoints)
        {
            foreach (var ep in erasedEndpoints)
            {
                var localLines = validWallLines.Where(l =>
                    l.StartPoint.DistanceTo(ep) < _cornerSearchRadius || l.EndPoint.DistanceTo(ep) < _cornerSearchRadius
                ).ToList();

                if (localLines.Count < 2) continue;

                var dirGroups = new List<List<Line>>();
                foreach (var l in localLines)
                {
                    if (l.Length < Tolerance) continue;

                    Vector3d dir = (l.EndPoint - l.StartPoint).GetNormal();
                    bool found = false;
                    foreach (var g in dirGroups)
                    {
                        Vector3d gDir = (g[0].EndPoint - g[0].StartPoint).GetNormal();
                        if (Math.Abs(dir.DotProduct(gDir)) > 0.866)
                        {
                            g.Add(l);
                            found = true;
                            break;
                        }
                    }
                    if (!found) dirGroups.Add(new List<Line> { l });
                }

                dirGroups = dirGroups.OrderByDescending(g => g.Sum(l => l.Length)).ToList();

                if (dirGroups.Count >= 2)
                {
                    var g1 = GetThickWallLines(dirGroups[0], ep);
                    var g2 = GetThickWallLines(dirGroups[1], ep);

                    Point3d? testIntersect = IntersectInfiniteLines(g1[0], g2[0]);
                    if (testIntersect != null)
                    {
                        bool isTOrX = false;
                        foreach (var l in g1.Concat(g2))
                        {
                            Vector3d v = l.EndPoint - l.StartPoint;
                            double len = v.Length;
                            if (len < Tolerance) continue;

                            Vector3d w = testIntersect.Value - l.StartPoint;
                            double t = w.DotProduct(v) / (len * len);

                            double distFromStart = t * len;
                            double distFromEnd = (1.0 - t) * len;

                            if (distFromStart > 50.0 && distFromEnd > 50.0)
                            {
                                isTOrX = true;
                                break;
                            }
                        }
                        if (isTOrX) continue;
                    }

                    if (g1.Count == 2 && g2.Count == 2)
                    {
                        Point3d? i00 = IntersectInfiniteLines(g1[0], g2[0]);
                        Point3d? i01 = IntersectInfiniteLines(g1[0], g2[1]);
                        Point3d? i10 = IntersectInfiniteLines(g1[1], g2[0]);
                        Point3d? i11 = IntersectInfiniteLines(g1[1], g2[1]);

                        if (i00 != null && i01 != null && i10 != null && i11 != null)
                        {
                            Point3d J = new Point3d(
                                (i00.Value.X + i01.Value.X + i10.Value.X + i11.Value.X) / 4.0,
                                (i00.Value.Y + i01.Value.Y + i10.Value.Y + i11.Value.Y) / 4.0, 0);

                            Point3d far1_0 = g1[0].StartPoint.DistanceTo(J) > g1[0].EndPoint.DistanceTo(J) ? g1[0].StartPoint : g1[0].EndPoint;
                            Point3d far1_1 = g1[1].StartPoint.DistanceTo(J) > g1[1].EndPoint.DistanceTo(J) ? g1[1].StartPoint : g1[1].EndPoint;
                            Vector3d v1 = (new Vector3d((far1_0.X + far1_1.X) / 2.0 - J.X, (far1_0.Y + far1_1.Y) / 2.0 - J.Y, 0)).GetNormal();

                            Point3d far2_0 = g2[0].StartPoint.DistanceTo(J) > g2[0].EndPoint.DistanceTo(J) ? g2[0].StartPoint : g2[0].EndPoint;
                            Point3d far2_1 = g2[1].StartPoint.DistanceTo(J) > g2[1].EndPoint.DistanceTo(J) ? g2[1].StartPoint : g2[1].EndPoint;
                            Vector3d v2 = (new Vector3d((far2_0.X + far2_1.X) / 2.0 - J.X, (far2_0.Y + far2_1.Y) / 2.0 - J.Y, 0)).GetNormal();

                            Vector3d innerDir = v1 + v2;

                            if (innerDir.Length > 0.1)
                            {
                                double val1 = Math.Abs((i00.Value - J).DotProduct(innerDir)) + Math.Abs((i11.Value - J).DotProduct(innerDir));
                                double val2 = Math.Abs((i01.Value - J).DotProduct(innerDir)) + Math.Abs((i10.Value - J).DotProduct(innerDir));

                                if (val1 > val2)
                                {
                                    ExtendOrTrimToPoint(g1[0], i00.Value); ExtendOrTrimToPoint(g2[0], i00.Value);
                                    ExtendOrTrimToPoint(g1[1], i11.Value); ExtendOrTrimToPoint(g2[1], i11.Value);
                                }
                                else
                                {
                                    ExtendOrTrimToPoint(g1[0], i01.Value); ExtendOrTrimToPoint(g2[1], i01.Value);
                                    ExtendOrTrimToPoint(g1[1], i10.Value); ExtendOrTrimToPoint(g2[0], i10.Value);
                                }
                            }
                        }
                    }
                    else if (g1.Count >= 1 && g2.Count >= 1)
                    {
                        Point3d? intersect = IntersectInfiniteLines(g1[0], g2[0]);
                        if (intersect != null)
                        {
                            ExtendOrTrimToPoint(g1[0], intersect.Value);
                            ExtendOrTrimToPoint(g2[0], intersect.Value);
                        }
                    }
                }
            }
        }

        private Point3d? IntersectInfiniteLines(Line l1, Line l2)
        {
            Vector3d p = l1.StartPoint.GetAsVector();
            Vector3d r = l1.EndPoint - l1.StartPoint;
            Vector3d q = l2.StartPoint.GetAsVector();
            Vector3d s = l2.EndPoint - l2.StartPoint;

            double rs = Cross2d(r, s);
            if (Math.Abs(rs) < Tolerance) return null;

            double t = Cross2d(q - p, s) / rs;
            return l1.StartPoint + r * t;
        }

        private double Cross2d(Vector3d a, Vector3d b)
        {
            return a.X * b.Y - a.Y * b.X;
        }

        private void ExtendOrTrimToPoint(Line l, Point3d pt)
        {
            if (Math.Min(l.StartPoint.DistanceTo(pt), l.EndPoint.DistanceTo(pt)) > _cornerSearchRadius)
                return;
            l.UpgradeOpen();
            if (l.StartPoint.DistanceTo(pt) < l.EndPoint.DistanceTo(pt))
                l.StartPoint = pt;
            else
                l.EndPoint = pt;
        }

        private void SquareOffWallEnds(List<Line> survivingLines, List<ErasedZoneInfo> erasedZones, HashSet<Point3d> erasedEndpoints)
        {
            var bySegment = survivingLines
                .Where(l => !string.IsNullOrEmpty(DrawWallHelper.GetWallSegmentId(l)))
                .GroupBy(l => DrawWallHelper.GetWallSegmentId(l));

            foreach (var group in bySegment)
            {
                var linesA = group.Where(l => DrawWallHelper.GetWallSideMarker(l) == "A").ToList();
                var linesB = group.Where(l => DrawWallHelper.GetWallSideMarker(l) == "B").ToList();

                foreach (Line lineA in linesA)
                {
                    foreach (Line lineB in linesB)
                    {
                        TrySquareOff(lineA, lineB, true, erasedZones, erasedEndpoints, survivingLines);
                        TrySquareOff(lineA, lineB, false, erasedZones, erasedEndpoints, survivingLines);
                    }
                }
            }
        }

        private void TrySquareOff(Line lineA, Line lineB, bool aIsStart, List<ErasedZoneInfo> erasedZones, HashSet<Point3d> erasedEndpoints, List<Line> survivingLines)
        {
            Point3d ptA = aIsStart ? lineA.StartPoint : lineA.EndPoint;

            double distToBStart = ptA.DistanceTo(lineB.StartPoint);
            double distToBEnd = ptA.DistanceTo(lineB.EndPoint);

            bool bIsStart;
            Point3d ptB;

            if (distToBStart < distToBEnd)
            {
                if (distToBStart > _cornerSearchRadius) return;
                bIsStart = true;
                ptB = lineB.StartPoint;
            }
            else
            {
                if (distToBEnd > _cornerSearchRadius) return;
                bIsStart = false;
                ptB = lineB.EndPoint;
            }

            Point3d mid = new Point3d((ptA.X + ptB.X) / 2, (ptA.Y + ptB.Y) / 2, 0);
            if (!IsInsideAnyZoneBounds(mid, erasedZones)) return;
            if (!erasedEndpoints.Any(ep => ep.DistanceTo(ptA) <= _cornerSearchRadius || ep.DistanceTo(ptB) <= _cornerSearchRadius)) return;

            Vector3d wallDirection = lineA.EndPoint - lineA.StartPoint;
            if (wallDirection.Length <= Tolerance ||
                Math.Abs((ptB - ptA).DotProduct(wallDirection.GetNormal())) > _maxSquareOffset) return;   // CHANGED

            if (HasPerpendicularWallOnSegment(ptA, ptB, lineA, lineB, survivingLines)) return;

            MakeEndsSquare(lineA, lineB, aIsStart, bIsStart);
        }

        private void MakeEndsSquare(Line lineA, Line lineB, bool aIsStart, bool bIsStart)
        {
            Vector3d dirA = (lineA.EndPoint - lineA.StartPoint).GetNormal();
            Point3d ptA = aIsStart ? lineA.StartPoint : lineA.EndPoint;
            Point3d ptB = bIsStart ? lineB.StartPoint : lineB.EndPoint;

            Vector3d vecA = ptA - Point3d.Origin;
            Vector3d vecB = ptB - Point3d.Origin;

            double projA = vecA.DotProduct(dirA);
            double projB = vecB.DotProduct(dirA);

            if (Math.Abs(projA - projB) < Tolerance) return;

            lineA.UpgradeOpen();
            lineB.UpgradeOpen();

            Point3d targetPtA, targetPtB;
            bool aIsFurther = aIsStart ? (projA < projB) : (projA > projB);

            if (aIsFurther)
            {
                targetPtA = ptA;
                targetPtB = ptB + dirA * (projA - projB);
            }
            else
            {
                targetPtB = ptB;
                targetPtA = ptA + dirA * (projB - projA);
            }

            if (aIsStart) lineA.StartPoint = targetPtA; else lineA.EndPoint = targetPtA;
            if (bIsStart) lineB.StartPoint = targetPtB; else lineB.EndPoint = targetPtB;
        }

        private void LocalCapExposedEnds(Transaction tr, Database db, BlockTableRecord modelSpace, List<Line> allLines, List<ErasedZoneInfo> erasedZones, HashSet<Point3d> erasedEndpoints)
        {
            var bySegment = allLines
                .Where(l => !string.IsNullOrEmpty(DrawWallHelper.GetWallSegmentId(l)) && !DrawWallHelper.IsWallCap(l) && !l.IsErased)
                .GroupBy(l => DrawWallHelper.GetWallSegmentId(l));

            foreach (var group in bySegment)
            {
                var linesA = group.Where(l => DrawWallHelper.GetWallSideMarker(l) == "A").ToList();
                var linesB = group.Where(l => DrawWallHelper.GetWallSideMarker(l) == "B").ToList();

                if (linesA.Count == 0 || linesB.Count == 0) continue;

                foreach (Line a in linesA)
                {
                    TryCapEndpoint(tr, db, modelSpace, a, a.StartPoint, linesB, erasedZones, erasedEndpoints, allLines);
                    TryCapEndpoint(tr, db, modelSpace, a, a.EndPoint, linesB, erasedZones, erasedEndpoints, allLines);
                }
            }
        }

        private void TryCapEndpoint(Transaction tr, Database db, BlockTableRecord modelSpace, Line lineA, Point3d ptA, List<Line> linesB, List<ErasedZoneInfo> zones, HashSet<Point3d> erasedEndpoints, List<Line> allLines)
        {
            Point3d bestPtB = Point3d.Origin;
            double minDist = double.MaxValue;
            Line bestLineB = null;

            foreach (Line b in linesB)
            {
                if (ptA.DistanceTo(b.StartPoint) < minDist)
                {
                    minDist = ptA.DistanceTo(b.StartPoint);
                    bestPtB = b.StartPoint;
                    bestLineB = b;
                }
                if (ptA.DistanceTo(b.EndPoint) < minDist)
                {
                    minDist = ptA.DistanceTo(b.EndPoint);
                    bestPtB = b.EndPoint;
                    bestLineB = b;
                }
            }

            if (minDist > _cornerSearchRadius || bestLineB == null) return;
            if (!erasedEndpoints.Any(ep => ep.DistanceTo(ptA) <= _cornerSearchRadius || ep.DistanceTo(bestPtB) <= _cornerSearchRadius)) return;

            Vector3d wallDirection = lineA.EndPoint - lineA.StartPoint;
            if (wallDirection.Length <= Tolerance ||
                Math.Abs((bestPtB - ptA).DotProduct(wallDirection.GetNormal())) > _maxSquareOffset) return;   // CHANGED

            Point3d mid = new Point3d((ptA.X + bestPtB.X) / 2, (ptA.Y + bestPtB.Y) / 2, 0);

            if (!IsInsideAnyZoneBounds(mid, zones)) return;

            if (HasPerpendicularWallOnSegment(ptA, bestPtB, lineA, bestLineB, allLines)) return;

            double checkRadius = 5.0;
            Vector3d wallDir = (lineA.EndPoint - lineA.StartPoint).GetNormal();

            foreach (var l in allLines)
            {
                if (l.IsErased || l.ObjectId == lineA.ObjectId || l.ObjectId == bestLineB.ObjectId || DrawWallHelper.IsWallCap(l)) continue;

                Vector3d lDir = (l.EndPoint - l.StartPoint).GetNormal();

                if (Math.Abs(wallDir.DotProduct(lDir)) < 1.0 - CollinearAngleTolerance)
                {
                    double distToMid = DistanceToSegment(mid, l.StartPoint, l.EndPoint);
                    double distToA = DistanceToSegment(ptA, l.StartPoint, l.EndPoint);
                    double distToB = DistanceToSegment(bestPtB, l.StartPoint, l.EndPoint);

                    if (distToMid <= checkRadius || distToA <= checkRadius || distToB <= checkRadius)
                    {
                        return;
                    }
                }
            }

            if (ptA.DistanceTo(bestPtB) > Tolerance)
            {
                Line cap = new Line(ptA, bestPtB)
                {
                    LayerId = lineA.LayerId,
                    Color = lineA.Color,
                    Linetype = lineA.Linetype,
                    LineWeight = lineA.LineWeight
                };
                modelSpace.AppendEntity(cap);
                tr.AddNewlyCreatedDBObject(cap, true);
                DrawWallHelper.TagAsCap(tr, db, cap);

                allLines.Add(cap);
            }
        }

        private double DistanceToSegment(Point3d pt, Point3d start, Point3d end)
        {
            Vector3d v = end - start;
            Vector3d w = pt - start;

            double c1 = w.DotProduct(v);
            if (c1 <= 0) return pt.DistanceTo(start);

            double c2 = v.DotProduct(v);
            if (c2 <= c1) return pt.DistanceTo(end);

            double b = c1 / c2;
            Point3d pb = start + v * b;
            return pt.DistanceTo(pb);
        }

        private bool HasPerpendicularWallOnSegment(Point3d start, Point3d end, Line excludedA, Line excludedB, IEnumerable<Line> candidates)
        {
            if (candidates == null) return false;

            Vector3d segmentDirection = end - start;
            if (segmentDirection.Length <= Tolerance) return false;
            segmentDirection = segmentDirection.GetNormal();

            foreach (Line candidate in candidates)
            {
                if (candidate == null || candidate.IsErased ||
                    candidate.ObjectId == excludedA.ObjectId || candidate.ObjectId == excludedB.ObjectId ||
                    DrawWallHelper.IsWallCap(candidate))
                    continue;

                Vector3d candidateDirection = candidate.EndPoint - candidate.StartPoint;
                if (candidateDirection.Length <= Tolerance ||
                    Math.Abs(segmentDirection.DotProduct(candidateDirection.GetNormal())) >= 1.0 - CollinearAngleTolerance)
                    continue;

                if (SegmentsTouchOrCross(start, end, candidate.StartPoint, candidate.EndPoint, 5.0))
                    return true;
            }

            return false;
        }

        private bool SegmentsTouchOrCross(Point3d a, Point3d b, Point3d c, Point3d d, double tolerance)
        {
            if (DistanceToSegment(a, c, d) <= tolerance || DistanceToSegment(b, c, d) <= tolerance ||
                DistanceToSegment(c, a, b) <= tolerance || DistanceToSegment(d, a, b) <= tolerance)
                return true;

            double ab = Cross2d(b - a, c - a);
            double ab2 = Cross2d(b - a, d - a);
            double cd = Cross2d(d - c, a - c);
            double cd2 = Cross2d(d - c, b - c);
            return ((ab > 0 && ab2 < 0) || (ab < 0 && ab2 > 0)) &&
                   ((cd > 0 && cd2 < 0) || (cd < 0 && cd2 > 0));
        }

        private bool IsEntityInBounds(Entity ent, Point3d minPt, Point3d maxPt)
        {
            try
            {
                Extents3d ext = ent.GeometricExtents;
                if (ext.MaxPoint.X < minPt.X || ext.MinPoint.X > maxPt.X) return false;
                if (ext.MaxPoint.Y < minPt.Y || ext.MinPoint.Y > maxPt.Y) return false;
                return true;
            }
            catch { return false; }
        }

        private bool IsInsideAnyZoneBounds(Point3d pt, List<ErasedZoneInfo> zones)
        {
            foreach (var zone in zones)
            {
                if (pt.X >= zone.Bounds.MinPoint.X && pt.X <= zone.Bounds.MaxPoint.X &&
                    pt.Y >= zone.Bounds.MinPoint.Y && pt.Y <= zone.Bounds.MaxPoint.Y)
                    return true;
            }
            return false;
        }

        private bool IsValidGapForHealing(Point3d gapMid, Vector3d lineDir, List<ErasedZoneInfo> zones)
        {
            foreach (var zone in zones)
            {
                if (gapMid.X >= zone.Bounds.MinPoint.X && gapMid.X <= zone.Bounds.MaxPoint.X &&
                    gapMid.Y >= zone.Bounds.MinPoint.Y && gapMid.Y <= zone.Bounds.MaxPoint.Y)
                {
                    return Math.Abs(lineDir.DotProduct(zone.Direction)) > 1.0 - CollinearAngleTolerance;
                }
            }
            return false;
        }

        private List<List<LineRecord>> GroupCollinearLines(List<LineRecord> lines, List<ErasedZoneInfo> erasedZones, List<Line> validWallLines)
        {
            var groups = new List<List<LineRecord>>();
            bool[] used = new bool[lines.Count];

            for (int i = 0; i < lines.Count; i++)
            {
                if (used[i]) continue;

                var currentGroup = new List<LineRecord> { lines[i] };
                used[i] = true;

                bool added;
                do
                {
                    added = false;
                    for (int j = 0; j < lines.Count; j++)
                    {
                        if (used[j]) continue;

                        if (CanGroup(currentGroup[0], lines[j], erasedZones, validWallLines))
                        {
                            currentGroup.Add(lines[j]);
                            used[j] = true;
                            added = true;
                        }
                    }
                } while (added);

                groups.Add(currentGroup);
            }
            return groups;
        }

        private bool CanGroup(LineRecord l1, LineRecord l2, List<ErasedZoneInfo> erasedZones, List<Line> validWallLines)
        {
            if (l1.Entity.LayerId != l2.Entity.LayerId) return false;
            if (!AreCollinearHeal(l1, l2)) return false;

            string seg1 = DrawWallHelper.GetWallSegmentId(l1.Entity as Line);
            string side1 = DrawWallHelper.GetWallSideMarker(l1.Entity as Line);

            string seg2 = DrawWallHelper.GetWallSegmentId(l2.Entity as Line);
            string side2 = DrawWallHelper.GetWallSideMarker(l2.Entity as Line);

            double gap = CalculateGap(l1, l2, out Point3d gapMid);

            if (gap > Tolerance && gap <= _maxBridgeGap)
            {
                Vector3d d = (l1.End - l1.Start).GetNormal();
                Point3d gA = gapMid - d * (gap / 2.0);
                Point3d gB = gapMid + d * (gap / 2.0);
                if (BridgesErasedWall(gA, gB, d, erasedZones) &&
                    !IsGapOccupiedByPerpendicularWall(l1, l2, validWallLines))
                    return true;
            }

            double maxGap = GetHealGapLimit(l1, l2, seg1, seg2, erasedZones);

            if (gap >= 0 && gap <= maxGap)
            {
                bool hasMetadata = !string.IsNullOrEmpty(seg1) || !string.IsNullOrEmpty(seg2);
                bool shouldGroup;
                if (hasMetadata)
                {
                    shouldGroup = !string.IsNullOrEmpty(seg1) &&
                                  seg1 == seg2 && side1 == side2 &&
                                  IsInsideAnyZoneBounds(gapMid, erasedZones);
                }
                else
                {
                    Vector3d lineDir = (l1.End - l1.Start).GetNormal();
                    shouldGroup = IsValidGapForHealing(gapMid, lineDir, erasedZones);
                }

                if (shouldGroup)
                {
                    if (IsGapOccupiedByPerpendicularWall(l1, l2, validWallLines))
                    {
                        return false;
                    }
                    return true;
                }
            }

            return false;
        }

        private bool BridgesErasedWall(Point3d a, Point3d b, Vector3d dir, List<ErasedZoneInfo> zones)
        {
            foreach (var z in zones)
            {
                Vector3d zd = z.End - z.Start;
                if (zd.Length <= Tolerance) continue;
                if (Math.Abs(dir.DotProduct(zd.GetNormal())) > 1.0 - CollinearAngleTolerance) continue; // bỏ đường song song
                if (SegmentsTouchOrCross(a, b, z.Start, z.End, 5.0)) return true;
            }
            return false;
        }

        private double GetHealGapLimit(LineRecord l1, LineRecord l2, string seg1, string seg2, List<ErasedZoneInfo> erasedZones)
        {
            if (string.IsNullOrEmpty(seg1) || seg1 != seg2) return _maxHealGap;

            Vector3d direction = l1.End - l1.Start;
            if (direction.Length <= Tolerance) return _maxHealGap;
            direction = direction.GetNormal();

            double limit = _maxHealGap;
            foreach (ErasedZoneInfo zone in erasedZones)
            {
                if (zone.SegmentId != seg1 ||
                    Math.Abs(direction.DotProduct(zone.Direction)) < 1.0 - CollinearAngleTolerance)
                    continue;

                Point3d min = zone.Bounds.MinPoint;
                Point3d max = zone.Bounds.MaxPoint;
                Point3d[] corners =
                {
                    new Point3d(min.X, min.Y, 0), new Point3d(min.X, max.Y, 0),
                    new Point3d(max.X, min.Y, 0), new Point3d(max.X, max.Y, 0)
                };

                double minProjection = corners.Min(p => (p - Point3d.Origin).DotProduct(direction));
                double maxProjection = corners.Max(p => (p - Point3d.Origin).DotProduct(direction));
                limit = Math.Max(limit, maxProjection - minProjection + _maxHealGap);
            }

            return limit;
        }

        private bool IsGapOccupiedByPerpendicularWall(LineRecord l1, LineRecord l2, List<Line> validWallLines)
        {
            Point3d[] pts1 = { l1.Start, l1.End };
            Point3d[] pts2 = { l2.Start, l2.End };
            Point3d p1 = pts1[0], p2 = pts2[0];
            double minDist = double.MaxValue;

            foreach (var pt1 in pts1)
            {
                foreach (var pt2 in pts2)
                {
                    double d = pt1.DistanceTo(pt2);
                    if (d < minDist)
                    {
                        minDist = d;
                        p1 = pt1;
                        p2 = pt2;
                    }
                }
            }

            Vector3d l1Dir = (l1.End - l1.Start).GetNormal();
            double checkRadius = 5.0;

            foreach (Line vLine in validWallLines)
            {
                if (vLine.IsErased) continue;
                if (vLine.ObjectId == l1.Entity.ObjectId || vLine.ObjectId == l2.Entity.ObjectId) continue;

                Vector3d vDir = (vLine.EndPoint - vLine.StartPoint).GetNormal();

                if (Math.Abs(l1Dir.DotProduct(vDir)) > 1.0 - CollinearAngleTolerance) continue;

                if (SegmentsTouchOrCross(p1, p2, vLine.StartPoint, vLine.EndPoint, checkRadius))
                {
                    return true;
                }
            }
            return false;
        }

        private double CalculateGap(LineRecord l1, LineRecord l2, out Point3d gapMid)
        {
            gapMid = Point3d.Origin;

            Vector3d dir = (l1.End - l1.Start).GetNormal();
            double t1Start = 0;
            double t1End = (l1.End - l1.Start).DotProduct(dir);

            double t2Start = (l2.Start - l1.Start).DotProduct(dir);
            double t2End = (l2.End - l1.Start).DotProduct(dir);

            double min1 = Math.Min(t1Start, t1End);
            double max1 = Math.Max(t1Start, t1End);
            double min2 = Math.Min(t2Start, t2End);
            double max2 = Math.Max(t2Start, t2End);

            if (max1 >= min2 - Tolerance && max2 >= min1 - Tolerance)
            {
                double oMin = Math.Max(min1, min2);
                double oMax = Math.Min(max1, max2);
                gapMid = l1.Start + dir * ((oMin + oMax) / 2.0);
                return 0.0;
            }

            double gapStart = max1 < min2 ? max1 : max2;
            double gapEnd = max1 < min2 ? min2 : min1;

            gapMid = l1.Start + dir * ((gapStart + gapEnd) / 2.0);
            return Math.Abs(gapEnd - gapStart);
        }

        private bool AreCollinearHeal(LineRecord l1, LineRecord l2)
        {
            if (l1.Start.DistanceTo(l1.End) < Tolerance || l2.Start.DistanceTo(l2.End) < Tolerance) return false;

            Vector3d dir1 = (l1.End - l1.Start).GetNormal();
            Vector3d dir2 = (l2.End - l2.Start).GetNormal();

            if (Math.Abs(dir1.DotProduct(dir2)) < (1.0 - CollinearAngleTolerance)) return false;

            double distStart = DistanceToInfiniteLine(l2.Start, l1.Start, dir1);
            double distEnd = DistanceToInfiniteLine(l2.End, l1.Start, dir1);

            if (distStart > 5.0 || distEnd > 5.0) return false;

            return true;
        }

        private void JoinLines(Transaction tr, Database db, BlockTableRecord modelSpace, List<LineRecord> group, List<Line> allLines)
        {
            Point3d newStart = group[0].Start;
            Point3d newEnd = group[0].End;
            double maxDist = -1;

            List<Point3d> allPoints = new List<Point3d>();
            foreach (var line in group)
            {
                allPoints.Add(line.Start);
                allPoints.Add(line.End);
            }

            for (int i = 0; i < allPoints.Count; i++)
            {
                for (int j = i + 1; j < allPoints.Count; j++)
                {
                    double dist = allPoints[i].DistanceTo(allPoints[j]);
                    if (dist > maxDist)
                    {
                        maxDist = dist;
                        newStart = allPoints[i];
                        newEnd = allPoints[j];
                    }
                }
            }

            Entity sourceEntity = group[0].Entity;
            Line newLine = new Line(newStart, newEnd)
            {
                LayerId = sourceEntity.LayerId,
                Color = sourceEntity.Color,
                LineWeight = sourceEntity.LineWeight,
                Linetype = sourceEntity.Linetype
            };

            modelSpace.AppendEntity(newLine);
            tr.AddNewlyCreatedDBObject(newLine, true);

            if (sourceEntity is Line sourceLine)
                DrawWallHelper.CopyWallMetadata(tr, db, sourceLine, newLine);

            allLines.Add(newLine);

            foreach (var item in group)
            {
                allLines.Remove(item.Entity as Line);
                item.Entity.UpgradeOpen();
                item.Entity.Erase(true);
            }
        }
    }

    public class LineRecord
    {
        public Point3d Start { get; set; }
        public Point3d End { get; set; }
        public Entity Entity { get; set; }
    }
}