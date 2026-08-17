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

    public class EraseAutoHealWallAction
    {
        private const double Tolerance = 0.001;
        private const double CollinearAngleTolerance = 0.002; // Dung sai góc lệch

        public void Execute()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;


            // BƯỚC 1: CHO PHÉP QUÉT CHỌN ĐỐI TƯỢNG (Kết thúc bằng Enter/Space)            
            PromptSelectionOptions pso = new PromptSelectionOptions();
            pso.MessageForAdding = "\n[VinaCAD] - Chọn một mặt tường cần xóa; EW sẽ tự chọn mặt còn lại -> Enter: ";

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

                    ExpandWallPairs(tr, db, selectedIds);
                    List<ObjectId> erasedIds = new List<ObjectId>();

                    // BƯỚC 2: XÓA ĐỐI TƯỢNG VÀ TÍNH TOÁN VÙNG HEAL
                    foreach (ObjectId objectId in selectedIds)
                    {
                        DBObject obj = tr.GetObject(objectId, OpenMode.ForWrite);
                        if (obj == null || obj.IsErased) continue;

                        if (obj is Entity ent)
                        {
                            try
                            {
                                // Lấy kích thước tổng quát của vật thể bị xóa để làm vùng dò tìm nét đứt
                                Extents3d ext = ent.GeometricExtents;
                                if (totalExtents == null)
                                    totalExtents = ext;
                                else
                                    totalExtents = new Extents3d(
                                        new Point3d(Math.Min(totalExtents.Value.MinPoint.X, ext.MinPoint.X),
                                                    Math.Min(totalExtents.Value.MinPoint.Y, ext.MinPoint.Y),
                                                    Math.Min(totalExtents.Value.MinPoint.Z, ext.MinPoint.Z)),
                                        new Point3d(Math.Max(totalExtents.Value.MaxPoint.X, ext.MaxPoint.X),
                                                    Math.Max(totalExtents.Value.MaxPoint.Y, ext.MaxPoint.Y),
                                                    Math.Max(totalExtents.Value.MaxPoint.Z, ext.MaxPoint.Z))
                                    );
                            }
                            catch {}
                        }

                        obj.Erase(true);
                        erasedIds.Add(objectId);
                    }

                    // BƯỚC 3: TÌM VÀ NỐI CÁC ĐƯỜNG TƯỜNG BỊ ĐỨT (HEAL)
                    if (totalExtents != null)
                    {
                        HealBrokenWalls(ed, tr, db, totalExtents.Value, erasedIds);
                    }

                    tr.Commit();
                    int autoSelected = Math.Max(0, selectedIds.Count - originalCount);
                    ed.WriteMessage($"\n[VinaCAD] - Đã tự chọn thêm {autoSelected} mặt tường và nối tường thành công.");
                }
                catch (Exception ex)
                {
                    tr.Abort();
                    Logger.Info(nameof(EraseAutoHealWallAction), ex);
                    MessageBox.Show($"Lỗi xử lý: {ex.Message}", StringDefinition.TITLE_ERROR);
                }
            }
        }

        private void ExpandWallPairs(Transaction tr, Database db, HashSet<ObjectId> selectedIds)
        {
            BlockTable blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(
                blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            List<Line> wallLines = new List<Line>();
            foreach (ObjectId id in modelSpace)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is Line line && !line.IsErased && !DrawWallHelper.IsWallCap(line))
                    wallLines.Add(line);
            }

            foreach (ObjectId selectedId in selectedIds.ToList())
            {
                if (tr.GetObject(selectedId, OpenMode.ForRead) is not Line selectedLine ||
                    DrawWallHelper.IsWallCap(selectedLine))
                    continue;

                string segmentId = DrawWallHelper.GetWallSegmentId(selectedLine);
                if (!string.IsNullOrEmpty(segmentId))
                {
                    string selectedSide = DrawWallHelper.GetWallSideMarker(selectedLine);
                    foreach (Line line in wallLines)
                    {
                        string candidateSide = DrawWallHelper.GetWallSideMarker(line);
                        bool isOtherSide = string.IsNullOrEmpty(selectedSide) ||
                                           string.IsNullOrEmpty(candidateSide) ||
                                           selectedSide != candidateSide;

                        if (DrawWallHelper.GetWallSegmentId(line) == segmentId &&
                            isOtherSide && AreParallelAndOverlapping(selectedLine, line))
                            selectedIds.Add(line.ObjectId);
                    }
                    continue;
                }

                // Bản vẽ cũ: chưa có ID XData, tìm mặt song song gần nhất trên cùng layer.
                Line pairedLine = FindLegacyPairedLine(selectedLine, wallLines);
                if (pairedLine != null)
                    selectedIds.Add(pairedLine.ObjectId);
            }
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
                if (candidate.ObjectId == selected.ObjectId || candidate.LayerId != selected.LayerId)
                    continue;

                Vector3d candidateVector = candidate.EndPoint - candidate.StartPoint;
                if (candidateVector.Length <= Tolerance ||
                    Math.Abs(direction.DotProduct(candidateVector.GetNormal())) < 1.0 - CollinearAngleTolerance)
                    continue;

                string candidateSide = DrawWallHelper.GetWallSideMarker(candidate);
                if (!string.IsNullOrEmpty(selectedSide) && selectedSide == candidateSide)
                    continue;

                double startProjection = (candidate.StartPoint - selected.StartPoint).DotProduct(direction);
                double endProjection = (candidate.EndPoint - selected.StartPoint).DotProduct(direction);
                double overlap = Math.Min(selectedLength, Math.Max(startProjection, endProjection)) -
                                 Math.Max(0.0, Math.Min(startProjection, endProjection));
                if (overlap <= Tolerance) continue;

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

        private static bool AreParallelAndOverlapping(Line first, Line second)
        {
            Vector3d firstVector = first.EndPoint - first.StartPoint;
            Vector3d secondVector = second.EndPoint - second.StartPoint;
            if (firstVector.Length <= Tolerance || secondVector.Length <= Tolerance)
                return false;

            Vector3d direction = firstVector.GetNormal();
            if (Math.Abs(direction.DotProduct(secondVector.GetNormal())) < 1.0 - CollinearAngleTolerance)
                return false;

            double firstLength = firstVector.Length;
            double startProjection = (second.StartPoint - first.StartPoint).DotProduct(direction);
            double endProjection = (second.EndPoint - first.StartPoint).DotProduct(direction);
            double overlap = Math.Min(firstLength, Math.Max(startProjection, endProjection)) -
                             Math.Max(0.0, Math.Min(startProjection, endProjection));
            return overlap > Tolerance;
        }

        private void HealBrokenWalls(Editor ed, Transaction tr, Database db, Extents3d extents, List<ObjectId> erasedIds)
        {
            // Mở rộng hộp giới hạn ra 5 đơn vị để chắc chắn quét trúng các mép tường bị đứt
            double exp = 5.0;
            Point3d minPt = new Point3d(extents.MinPoint.X - exp, extents.MinPoint.Y - exp, 0);
            Point3d maxPt = new Point3d(extents.MaxPoint.X + exp, extents.MaxPoint.Y + exp, 0);

            // Chỉ dò tìm Line và Polyline xung quanh đó
            TypedValue[] tvs = new TypedValue[] {
                new TypedValue((int)DxfCode.Operator, "<OR"),
                new TypedValue((int)DxfCode.Start, "LINE"),
                new TypedValue((int)DxfCode.Start, "LWPOLYLINE"),
                new TypedValue((int)DxfCode.Operator, "OR>")
            };
            SelectionFilter filter = new SelectionFilter(tvs);

            PromptSelectionResult psr = ed.SelectCrossingWindow(minPt, maxPt, filter);
            if (psr.Status != PromptStatus.OK) return;

            List<LineRecord> linesToHeal = new List<LineRecord>();

            foreach (SelectedObject selObj in psr.Value)
            {
                if (erasedIds.Contains(selObj.ObjectId)) continue; // Bỏ qua đối tượng đã bị xóa ở bước 2

                DBObject obj = tr.GetObject(selObj.ObjectId, OpenMode.ForWrite);
                if (obj.IsErased) continue;

                if (obj is Line line)
                {
                    if (DrawWallHelper.IsWallCap(line)) continue;
                    linesToHeal.Add(new LineRecord { Start = line.StartPoint, End = line.EndPoint, Entity = line });
                }
                else if (obj is Polyline pline && pline.NumberOfVertices == 2)
                {
                    Point2d pt1 = pline.GetPoint2dAt(0);
                    Point2d pt2 = pline.GetPoint2dAt(1);
                    linesToHeal.Add(new LineRecord
                    {
                        Start = new Point3d(pt1.X, pt1.Y, pline.Elevation),
                        End = new Point3d(pt2.X, pt2.Y, pline.Elevation),
                        Entity = pline
                    });
                }
            }

            // Gộp các đường thành từng nhóm (các đường thẳng hàng vào 1 group)
            var groups = GroupCollinearLines(linesToHeal);

            // Nối (Heal) từng nhóm
            foreach (var group in groups)
            {
                if (group.Count > 1) JoinLines(tr, db, group);
            }
        }

        private List<List<LineRecord>> GroupCollinearLines(List<LineRecord> lines)
        {
            var groups = new List<List<LineRecord>>();
            bool[] used = new bool[lines.Count];

            for (int i = 0; i < lines.Count; i++)
            {
                if (used[i]) continue;

                var currentGroup = new List<LineRecord> { lines[i] };
                used[i] = true;

                for (int j = i + 1; j < lines.Count; j++)
                {
                    if (used[j]) continue;

                    if (lines[i].Entity.LayerId == lines[j].Entity.LayerId && AreCollinear(lines[i], lines[j]))
                    {
                        currentGroup.Add(lines[j]);
                        used[j] = true;
                    }
                }
                groups.Add(currentGroup);
            }
            return groups;
        }

        private bool AreCollinear(LineRecord l1, LineRecord l2)
        {
            Vector3d dir1 = (l1.End - l1.Start).GetNormal();
            Vector3d dir2 = (l2.End - l2.Start).GetNormal();

            // Phải song song
            if (Math.Abs(dir1.DotProduct(dir2)) < (1.0 - CollinearAngleTolerance)) return false;

            // Vector nối 2 đường thẳng phải song song với chúng
            Vector3d connect = (l2.Start - l1.Start);
            if (connect.Length < Tolerance) return true;

            if (Math.Abs(dir1.DotProduct(connect.GetNormal())) < (1.0 - CollinearAngleTolerance)) return false;

            return true;
        }

        private void JoinLines(Transaction tr, Database db, List<LineRecord> group)
        {
            // 1. Tìm 2 điểm xa nhất trong tất cả các điểm của nhóm
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

            // 2. Vẽ nét tường liền mạch mới 
            Entity sourceEntity = group[0].Entity;
            BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(sourceEntity.OwnerId, OpenMode.ForWrite);

            if (sourceEntity is Polyline plineSource)
            {
                Polyline newPline = new Polyline();
                newPline.AddVertexAt(0, new Point2d(newStart.X, newStart.Y), 0, 0, 0);
                newPline.AddVertexAt(1, new Point2d(newEnd.X, newEnd.Y), 0, 0, 0);
                newPline.Elevation = plineSource.Elevation;

                newPline.LayerId = plineSource.LayerId;
                newPline.Color = plineSource.Color;
                newPline.LineWeight = plineSource.LineWeight;
                newPline.Linetype = plineSource.Linetype;

                modelSpace.AppendEntity(newPline);
                tr.AddNewlyCreatedDBObject(newPline, true);
            }
            else
            {
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
            }

            // 3. Xóa các đoạn tường vụn cũ đi
            foreach (var item in group)
            {
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
