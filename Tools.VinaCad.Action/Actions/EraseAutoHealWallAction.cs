using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using System;
using System.Collections.Generic;
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
            pso.MessageForAdding = "\n[VinaCAD] - Quét chọn đối tượng cần xóa (cửa/nét thừa) -> Nhấn Enter/Space: ";

            PromptSelectionResult psr = ed.GetSelection(pso);
            if (psr.Status != PromptStatus.OK) return;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    Extents3d? totalExtents = null;
                    List<ObjectId> erasedIds = new List<ObjectId>();

                    // BƯỚC 2: XÓA ĐỐI TƯỢNG VÀ TÍNH TOÁN VÙNG HEAL
                    foreach (SelectedObject selObj in psr.Value)
                    {
                        DBObject obj = tr.GetObject(selObj.ObjectId, OpenMode.ForWrite);

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
                        erasedIds.Add(selObj.ObjectId);
                    }

                    // BƯỚC 3: TÌM VÀ NỐI CÁC ĐƯỜNG TƯỜNG BỊ ĐỨT (HEAL)
                    if (totalExtents != null)
                    {
                        HealBrokenWalls(ed, tr, totalExtents.Value, erasedIds);
                    }

                    tr.Commit();
                    ed.WriteMessage("\n[VinaCAD] - Xóa và nối tường thành công!");
                }
                catch (Exception ex)
                {
                    tr.Abort();
                    Logger.Info(nameof(EraseAutoHealWallAction), ex);
                    MessageBox.Show($"Lỗi xử lý: {ex.Message}", StringDefinition.TITLE_ERROR);
                }
            }
        }

        private void HealBrokenWalls(Editor ed, Transaction tr, Extents3d extents, List<ObjectId> erasedIds)
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
                if (group.Count > 1) JoinLines(tr, group);
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

                    if (AreCollinear(lines[i], lines[j]))
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

        private void JoinLines(Transaction tr, List<LineRecord> group)
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