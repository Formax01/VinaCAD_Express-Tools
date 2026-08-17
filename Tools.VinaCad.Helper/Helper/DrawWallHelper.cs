using Prima.VinaCAD.EditorInput;
using System;
using System.Collections.Generic;
using System.Linq;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.VinaCad.Modeling;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCad.Helper.Helper
{
    public class DrawWallHelper
    {
        private const double Tolerance = 0.001;

        private const string WallSideAppName = "YQARCH_WALL_SIDE";
        private const string SideA = "A";
        private const string SideB = "B";

        private const string CapAppName = "YQARCH_WALL_CAP";
        private const string CapMarker = "1";

        private static void EnsureRegApp(Transaction tr, Database db, string appName)
        {
            RegAppTable regTable = tr.GetObject(db.RegAppTableId, OpenMode.ForRead) as RegAppTable;
            if (regTable != null && !regTable.Has(appName))
            {
                regTable.UpgradeOpen();
                RegAppTableRecord app = new RegAppTableRecord { Name = appName };
                regTable.Add(app);
                tr.AddNewlyCreatedDBObject(app, true);
            }
        }

        private static void TagWallSide(Transaction tr, Database db, Line line, string side)
        {
            EnsureRegApp(tr, db, WallSideAppName);
            ResultBuffer rb = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, WallSideAppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, side));
            line.XData = rb;
        }

        private static string GetWallSide(Line line)
        {
            try
            {
                ResultBuffer rb = line.GetXDataForApplication(WallSideAppName);
                if (rb == null) return null;
                foreach (TypedValue tv in rb)
                {
                    if (tv.TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                        return tv.Value as string;
                }
            }
            catch { /* line chưa từng có XData của app này */ }
            return null;
        }

        private static void TagAsCap(Transaction tr, Database db, Line line)
        {
            EnsureRegApp(tr, db, CapAppName);
            ResultBuffer rb = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, CapAppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, CapMarker));
            line.XData = rb;
        }

        private static bool IsCapLine(Line line)
        {
            try
            {
                ResultBuffer rb = line.GetXDataForApplication(CapAppName);
                return rb != null;
            }
            catch { return false; }
        }

        #region 1. TÍNH TOÁN & TẠO LINE
        public static void CalculateWallLines(
            Point3d startPoint, Point3d endPoint, double thickness, WallAlignment alignment,
            out Point3d line1Start, out Point3d line1End, out Point3d line2Start, out Point3d line2End)
        {
            Vector3d direction = endPoint - startPoint;
            direction = direction.GetNormal();
            Vector3d perpendicular = new Vector3d(-direction.Y, direction.X, 0);

            switch (alignment)
            {
                case WallAlignment.Center:
                    Vector3d offsetHalf = perpendicular * (thickness / 2.0);
                    line1Start = startPoint + offsetHalf; line1End = endPoint + offsetHalf;
                    line2Start = startPoint - offsetHalf; line2End = endPoint - offsetHalf;
                    break;
                case WallAlignment.Left:
                    Vector3d offsetRight = perpendicular * thickness;
                    line1Start = startPoint; line1End = endPoint;
                    line2Start = startPoint + offsetRight; line2End = endPoint + offsetRight;
                    break;
                case WallAlignment.Right:
                    Vector3d offsetLeft = perpendicular * (-thickness);
                    line1Start = startPoint; line1End = endPoint;
                    line2Start = startPoint + offsetLeft; line2End = endPoint + offsetLeft;
                    break;
                default:
                    line1Start = startPoint; line1End = endPoint;
                    line2Start = startPoint; line2End = endPoint;
                    break;
            }
        }

        public static List<ObjectId> CreateWallLines(
            Database db, Point3d line1Start, Point3d line1End, Point3d line2Start, Point3d line2End, string layerName)
        {
            List<ObjectId> lineIds = new List<ObjectId>();
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    BlockTable blockTable = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                    BlockTableRecord modelSpace = tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;
                    ObjectId layerId = GetOrCreateLayer(db, tr, layerName);

                    Line line1 = new Line(line1Start, line1End) { LayerId = layerId };
                    modelSpace.AppendEntity(line1); tr.AddNewlyCreatedDBObject(line1, true); lineIds.Add(line1.ObjectId);
                    TagWallSide(tr, db, line1, SideA);

                    Line line2 = new Line(line2Start, line2End) { LayerId = layerId };
                    modelSpace.AppendEntity(line2); tr.AddNewlyCreatedDBObject(line2, true); lineIds.Add(line2.ObjectId);
                    TagWallSide(tr, db, line2, SideB);

                    tr.Commit();
                }
                catch (Exception ex) { tr.Abort(); throw new Exception($"Error creating wall lines: {ex.Message}", ex); }
            }
            return lineIds;
        }

        private static ObjectId GetOrCreateLayer(Database db, Transaction tr, string layerName)
        {
            LayerTable layerTable = tr.GetObject(db.LayerTableId, OpenMode.ForRead) as LayerTable;
            if (layerTable.Has(layerName)) return layerTable[layerName];

            LayerTableRecord layerRecord = new LayerTableRecord { Name = layerName, Color = Teigha.Colors.Color.FromColorIndex(Teigha.Colors.ColorMethod.ByAci, 7) };
            LayerTable layerTableWrite = tr.GetObject(db.LayerTableId, OpenMode.ForWrite) as LayerTable;
            ObjectId layerId = layerTableWrite.Add(layerRecord);
            tr.AddNewlyCreatedDBObject(layerRecord, true);
            return layerId;
        }
        #endregion

        #region 2. CSG CLEANUP (THE UNIVERSAL ALGORITHM)
        public static List<IntersectionInfo> FindWallIntersections(
            Database db, Point3d line1Start, Point3d line1End, Point3d line2Start, Point3d line2End, string wallLayerName)
        {
            List<IntersectionInfo> intersections = new List<IntersectionInfo>();
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    BlockTable blockTable = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                    BlockTableRecord modelSpace = tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead) as BlockTableRecord;
                    LayerTable layerTable = tr.GetObject(db.LayerTableId, OpenMode.ForRead) as LayerTable;

                    if (!layerTable.Has(wallLayerName)) return intersections;
                    ObjectId targetLayerId = layerTable[wallLayerName];

                    foreach (ObjectId objId in modelSpace)
                    {
                        DBObject obj = tr.GetObject(objId, OpenMode.ForRead);
                        if (obj is Line existingLine && existingLine.LayerId == targetLayerId)
                        {
                            if (existingLine.StartPoint.DistanceTo(existingLine.EndPoint) <= Tolerance) continue;
                            intersections.Add(new IntersectionInfo { ExistingLineId = objId });
                        }
                    }
                    tr.Commit();
                }
                catch (Exception ex) { tr.Abort(); throw new Exception($"Error finding intersections: {ex.Message}", ex); }
            }
            return intersections;
        }

        private class MatchData
        {
            public Point3d Intersection;
            public ObjectId TargetId;
            public bool IsCorner;
            public double Score;
        }

        public static void CleanupIntersections(
            Database db, List<ObjectId> newWallLineIds, List<IntersectionInfo> intersections, string wallLayerName, double thickness, WallAlignment alignment)
        {
            if (intersections == null || intersections.Count == 0 || newWallLineIds.Count < 2) return;

            // FIX (lỗi "căn Trái không kéo dài đường bên ngoài"):
            // Với căn lề Trái/Phải, đường offset (line2) lệch NGUYÊN chiều dày (thickness)
            // so với tim tường, trong khi căn Giữa chỉ lệch thickness/2. Ở góc tường dày,
            // khoảng cách cần "vươn" tới điểm giao thực tế của Phase 1 (SMART SNAP) dễ
            // vượt quá bán kính cố định 400 => bị bỏ qua, đường ngoài không được kéo dài
            // ra góc. Đặt maxRadius co giãn theo thickness, có sàn tối thiểu để không đổi
            // hành vi với tường mỏng.
            double maxRadius = Math.Max(400.0, thickness * 6.0);

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    BlockTable blockTable = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                    BlockTableRecord modelSpace = tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;

                    List<Line> newLines = newWallLineIds.Select(id => tr.GetObject(id, OpenMode.ForWrite) as Line).Where(l => l != null).ToList();
                    List<Line> exLines = intersections.Select(i => tr.GetObject(i.ExistingLineId, OpenMode.ForWrite) as Line).Distinct().Where(l => l != null).ToList();

                    List<Line> allLines = new List<Line>();
                    allLines.AddRange(newLines);
                    allLines.AddRange(exLines);

                    // Đọc XData 1 lần cho mỗi line, tránh đọc lặp lại trong vòng lặp lồng nhau bên dưới
                    Dictionary<ObjectId, string> sideMap = allLines.ToDictionary(l => l.ObjectId, l => GetWallSide(l));

                    // ==============================================================================
                    // PHASE 1: SMART SNAP (Chữa lành Góc chữ L và điểm dừng của Ngã 3)
                    // Tách riêng theo từng căn lề — logic "giao điểm/ngã ba" (Phase 2&3) và
                    // "bo đầu" (CapFreeEnd) hoàn toàn KHÔNG đổi, chỉ Phase 1 này được chia 3.
                    // ==============================================================================
                    switch (alignment)
                    {
                        case WallAlignment.Center:
                            SnapCornersCenter(newLines, allLines, sideMap, maxRadius);
                            break;
                        case WallAlignment.Left:
                            SnapCornersLeft(newLines, allLines, sideMap, maxRadius);
                            break;
                        case WallAlignment.Right:
                            SnapCornersRight(newLines, allLines, sideMap, maxRadius);
                            break;
                        default:
                            SnapCornersCenter(newLines, allLines, sideMap, maxRadius);
                            break;
                    }

                    // ==============================================================================
                    // PHASE 2 & 3: TÌM VẾT CẮT THỰC TẾ VÀ ĐỤC LỖ RỖNG BẰNG TOÁN CSG
                    // ==============================================================================
                    Dictionary<ObjectId, List<Point3d>> lineCuts = new Dictionary<ObjectId, List<Point3d>>();
                    foreach (var l in allLines) lineCuts[l.ObjectId] = new List<Point3d>();

                    for (int i = 0; i < allLines.Count; i++)
                    {
                        for (int j = i + 1; j < allLines.Count; j++)
                        {
                            Line L1 = allLines[i]; Line L2 = allLines[j];
                            Vector3d dir1 = (L1.EndPoint - L1.StartPoint).GetNormal();
                            Vector3d dir2 = (L2.EndPoint - L2.StartPoint).GetNormal();
                            if (Math.Abs(dir1.DotProduct(dir2)) > 0.99) continue;

                            if (GetTrueIntersection(L1.StartPoint, L1.EndPoint, L2.StartPoint, L2.EndPoint, out Point3d I))
                            {
                                if (IsPointOnSegment(I, L1.StartPoint, L1.EndPoint, 1.0) && IsPointOnSegment(I, L2.StartPoint, L2.EndPoint, 1.0))
                                {
                                    lineCuts[L1.ObjectId].Add(I);
                                    lineCuts[L2.ObjectId].Add(I);
                                }
                            }
                        }
                    }

                    List<ObjectId> toErase = new List<ObjectId>();

                    foreach (var line in allLines)
                    {
                        var cuts = lineCuts[line.ObjectId];
                        cuts.Add(line.StartPoint);
                        cuts.Add(line.EndPoint);

                        var sortedCuts = cuts.OrderBy(p => p.DistanceTo(line.StartPoint)).ToList();
                        var uniqueCuts = new List<Point3d>();
                        foreach (var c in sortedCuts)
                        {
                            if (uniqueCuts.Count == 0 || uniqueCuts.Last().DistanceTo(c) > Tolerance)
                                uniqueCuts.Add(c);
                        }

                        for (int i = 0; i < uniqueCuts.Count - 1; i++)
                        {
                            Point3d p1 = uniqueCuts[i];
                            Point3d p2 = uniqueCuts[i + 1];
                            if (p1.DistanceTo(p2) < Tolerance) continue;

                            Point3d mid = new Point3d((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2, (p1.Z + p2.Z) / 2);

                            // BỘ LỌC ĐỤC LỖ: CSG Boolean
                            if (!IsInsideAnyWall(mid, allLines, line.ObjectId, thickness))
                            {
                                Line newSeg = new Line(p1, p2) { LayerId = line.LayerId };
                                modelSpace.AppendEntity(newSeg);
                                tr.AddNewlyCreatedDBObject(newSeg, true);

                                // Giữ nguyên tag Side của line gốc cho đoạn mới, nếu không
                                // đoạn này sẽ thành "vô danh" và Phase 1 của lần vẽ tiếp theo
                                // lại có thể ghép nhầm mặt như trước khi fix.
                                string originalSide = sideMap.ContainsKey(line.ObjectId) ? sideMap[line.ObjectId] : null;
                                if (originalSide != null)
                                    TagWallSide(tr, db, newSeg, originalSide);
                            }
                        }
                        toErase.Add(line.ObjectId);
                    }

                    foreach (var id in toErase)
                    {
                        DBObject obj = tr.GetObject(id, OpenMode.ForWrite);
                        if (obj != null && !obj.IsErased) obj.Erase();
                    }

                    tr.Commit();
                }
                catch (Exception ex) { tr.Abort(); throw new Exception($"Error cleaning up intersections: {ex.Message}", ex); }
            }
        }

        // ------------------------------------------------------------------------------------
        // CĂN GIỮA: giữ NGUYÊN VĂN thuật toán gốc (không lọc theo mặt A/B) — vì cả line1 và
        // line2 đều lệch tim đối xứng (thickness/2 mỗi bên), không có đường "tim trần" nào
        // gây nhập nhằng nên thuật toán gốc đã chọn đúng láng giềng gần nhất một cách tự
        // nhiên. Đã xác nhận hoạt động tốt — KHÔNG đụng vào logic bên trong hàm này.
        // ------------------------------------------------------------------------------------
        private static void SnapCornersCenter(List<Line> newLines, List<Line> allLines, Dictionary<ObjectId, string> sideMap, double maxRadius)
        {
            foreach (Line newLine in newLines)
                ProcessLineEndpoints(newLine, allLines, sideMap, maxRadius, restrictCornerToSameSide: false);
        }

        // ------------------------------------------------------------------------------------
        // CĂN TRÁI: newLines[0] = line "tim" (mặt A, trùng khít điểm click — không lệch),
        // newLines[1] = line "biên" (mặt B, lệch nguyên thickness). Cả 2 line đều cần chạy
        // qua Phase 1 như bình thường (kể cả line tim, vì nó vẫn có thể cần bo Ngã 3 khi
        // đâm vào tường khác) — điểm khác biệt DUY NHẤT so với bản gốc là: ở nhánh GÓC L
        // (corner), chỉ chấp nhận mục tiêu CÙNG MẶT (A với A, B với B) để không bị hút nhầm
        // sang đường tim/biên của tường kề như lỗi cũ. Nhánh NGÃ BA (T-junction) KHÔNG lọc
        // theo mặt, vì mục tiêu ở đó luôn là một bức tường KHÁC — mặt A/B của nó không liên
        // quan gì đến mặt A/B của tường đang vẽ.
        // ------------------------------------------------------------------------------------
        private static void SnapCornersLeft(List<Line> newLines, List<Line> allLines, Dictionary<ObjectId, string> sideMap, double maxRadius)
        {
            foreach (Line newLine in newLines)
                ProcessLineEndpoints(newLine, allLines, sideMap, maxRadius, restrictCornerToSameSide: true);
        }

        // CĂN PHẢI: hình học là ảnh gương của Trái (dấu offset ngược lại ở CalculateWallLines),
        // nhưng cách xử lý Phase 1 giống hệt Trái — vẫn tách hàm riêng để khớp 1-1 với switch
        // của CalculateWallLines và dễ chỉnh sau này nếu Phải cần một quy tắc riêng.
        private static void SnapCornersRight(List<Line> newLines, List<Line> allLines, Dictionary<ObjectId, string> sideMap, double maxRadius)
        {
            foreach (Line newLine in newLines)
                ProcessLineEndpoints(newLine, allLines, sideMap, maxRadius, restrictCornerToSameSide: true);
        }

        // Hàm dùng chung: xử lý bo góc/ngã ba cho CẢ 2 đầu mút của 1 line, thân thuật toán
        // y hệt bản gốc — chỉ thêm 1 điều kiện lọc mặt A/B, và CHỈ áp dụng cho nhánh Góc L.
        private static void ProcessLineEndpoints(Line newLine, List<Line> allLines, Dictionary<ObjectId, string> sideMap, double maxRadius, bool restrictCornerToSameSide)
        {
            string sideN = sideMap.ContainsKey(newLine.ObjectId) ? sideMap[newLine.ObjectId] : null;
            Point3d[] endpoints = { newLine.StartPoint, newLine.EndPoint };

            for (int e = 0; e < 2; e++)
            {
                Point3d P = endpoints[e];
                Point3d otherPt = (e == 0) ? newLine.EndPoint : newLine.StartPoint;
                MatchData bestCorner = null;
                MatchData bestT = null;

                foreach (Line target in allLines)
                {
                    if (target.ObjectId == newLine.ObjectId) continue;

                    Vector3d dirN = (newLine.EndPoint - newLine.StartPoint).GetNormal();
                    Vector3d dirT = (target.EndPoint - target.StartPoint).GetNormal();
                    if (Math.Abs(dirN.DotProduct(dirT)) > 0.99) continue;

                    if (GetTrueIntersection(newLine.StartPoint, newLine.EndPoint, target.StartPoint, target.EndPoint, out Point3d I))
                    {
                        double dA = P.DistanceTo(I);
                        if (dA > maxRadius) continue;

                        double dB_start = I.DistanceTo(target.StartPoint);
                        double dB_end = I.DistanceTo(target.EndPoint);
                        double dB = Math.Min(dB_start, dB_end);

                        bool onSegmentB = IsPointOnSegment(I, target.StartPoint, target.EndPoint, 1.0);

                        if (dB < maxRadius)
                        {
                            // FIX: lọc mặt A/B CHỈ ở nhánh Góc L — vì đây là trường hợp duy
                            // nhất mà "mặt" có ý nghĩa (2 đoạn tường nối tiếp CÙNG 1 chuỗi
                            // đang vẽ hoặc nối trực tiếp). Không áp dụng cho T-junction bên
                            // dưới vì mục tiêu T-junction là tường khác, không cùng quy ước.
                            if (restrictCornerToSameSide)
                            {
                                string sideT = sideMap.ContainsKey(target.ObjectId) ? sideMap[target.ObjectId] : null;
                                if (sideN != null && sideT != null && sideN != sideT) continue;
                            }

                            // Góc L: Ép khép góc Ngoài-Ngoài, Trong-Trong bằng tổng khoảng cách ngắn nhất
                            double score = dA + dB;
                            if (bestCorner == null || score < bestCorner.Score)
                                bestCorner = new MatchData { Intersection = I, TargetId = target.ObjectId, IsCorner = true, Score = score };
                        }
                        else if (onSegmentB)
                        {
                            // DEEP FIX T-JUNCTION: Bắt buộc đâm tới Mép Xa (Far Face)
                            // Bằng cách chọn giao điểm làm cho độ dài đoạn thẳng là DÀI NHẤT
                            double score = -I.DistanceTo(otherPt);

                            if (bestT == null || score < bestT.Score)
                                bestT = new MatchData { Intersection = I, TargetId = target.ObjectId, IsCorner = false, Score = score };
                        }
                    }
                }

                MatchData bestMatch = bestCorner ?? bestT; // Ưu tiên Góc L hơn Ngã 3

                if (bestMatch != null)
                {
                    if (e == 0) newLine.StartPoint = bestMatch.Intersection;
                    else newLine.EndPoint = bestMatch.Intersection;

                    if (bestMatch.IsCorner)
                    {
                        Line targetLine = allLines.First(l => l.ObjectId == bestMatch.TargetId);
                        if (bestMatch.Intersection.DistanceTo(targetLine.StartPoint) < bestMatch.Intersection.DistanceTo(targetLine.EndPoint))
                            targetLine.StartPoint = bestMatch.Intersection;
                        else
                            targetLine.EndPoint = bestMatch.Intersection;
                    }
                }
            }
        }
        #endregion

        #region 3. HÀM TOÁN HỌC HỖ TRỢ (MATH UTILS)
        private static bool IsInsideAnyWall(Point3d pt, List<Line> allLines, ObjectId selfId, double thickness)
        {
            // FIX (lỗi "căn Phải/Giữa không xóa các đường bên trong giao nhau"):
            // Khoảng "bề rộng hợp lệ" trước đây cố định cứng 5–600 đơn vị vẽ. Nếu tường
            // được vẽ với chiều dày nằm ngoài khoảng này (đơn vị khác, hoặc tường dày),
            // điều kiện w > 5.0 && w < 600.0 KHÔNG BAO GIỜ đúng => đoạn line nằm lọt bên
            // trong một bức tường khác sẽ không được nhận diện là "bên trong" và do đó
            // không bị xóa. Đặt ngưỡng co giãn theo thickness thực tế của lệnh đang chạy.
            double minW = Math.Min(5.0, thickness * 0.2);
            double maxW = Math.Max(600.0, thickness * 4.0);

            for (int i = 0; i < allLines.Count; i++)
            {
                for (int j = i + 1; j < allLines.Count; j++)
                {
                    Line e1 = allLines[i]; Line e2 = allLines[j];
                    if (e1.ObjectId == selfId || e2.ObjectId == selfId) continue;

                    Vector3d dir1 = (e1.EndPoint - e1.StartPoint).GetNormal();
                    Vector3d dir2 = (e2.EndPoint - e2.StartPoint).GetNormal();

                    if (Math.Abs(dir1.DotProduct(dir2)) > 0.99)
                    {
                        double w = DistanceLineToLine(e1, e2);
                        if (w > minW && w < maxW)
                        {
                            double d1 = DistancePointToLine(pt, e1);
                            double d2 = DistancePointToLine(pt, e2);

                            if (Math.Abs((d1 + d2) - w) < 2.0)
                            {
                                if (IsPointStrictlyOnSegment(GetProjectedPoint(pt, e1), e1.StartPoint, e1.EndPoint, 5.0) &&
                                    IsPointStrictlyOnSegment(GetProjectedPoint(pt, e2), e2.StartPoint, e2.EndPoint, 5.0))
                                {
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
            return false;
        }

        private static bool IsPointStrictlyOnSegment(Point3d pt, Point3d start, Point3d end, double padding)
        {
            if (!IsPointOnSegment(pt, start, end, 1.0)) return false;
            if (pt.DistanceTo(start) < padding) return false;
            if (pt.DistanceTo(end) < padding) return false;
            return true;
        }

        private static bool GetTrueIntersection(Point3d p1, Point3d p2, Point3d p3, Point3d p4, out Point3d intersection)
        {
            intersection = Point3d.Origin;
            double x1 = p1.X, y1 = p1.Y; double x2 = p2.X, y2 = p2.Y;
            double x3 = p3.X, y3 = p3.Y; double x4 = p4.X, y4 = p4.Y;

            double denom = (y4 - y3) * (x2 - x1) - (x4 - x3) * (y2 - y1);
            if (Math.Abs(denom) < Tolerance) return false;

            double ua = ((x4 - x3) * (y1 - y3) - (y4 - y3) * (x1 - x3)) / denom;
            intersection = new Point3d(x1 + ua * (x2 - x1), y1 + ua * (y2 - y1), p1.Z);
            return true;
        }

        private static bool IsPointOnSegment(Point3d pt, Point3d start, Point3d end, double tolerance)
        {
            return Math.Abs((pt.DistanceTo(start) + pt.DistanceTo(end)) - start.DistanceTo(end)) <= tolerance;
        }

        private static double DistancePointToLine(Point3d pt, Line line)
        {
            Vector3d dir = (line.EndPoint - line.StartPoint).GetNormal();
            Vector3d v = pt - line.StartPoint;
            return (v - dir * v.DotProduct(dir)).Length;
        }

        private static Point3d GetProjectedPoint(Point3d pt, Line line)
        {
            Vector3d dir = (line.EndPoint - line.StartPoint).GetNormal();
            Vector3d v = pt - line.StartPoint;
            double projLen = v.DotProduct(dir);
            return line.StartPoint + dir * projLen;
        }

        private static double DistanceLineToLine(Line l1, Line l2) { return DistancePointToLine(l1.StartPoint, l2); }
        public static Point2d ToPoint2d(Point3d pt) { return new Point2d(pt.X, pt.Y); }
        public static Point3d ToPoint3d(Point2d pt) { return new Point3d(pt.X, pt.Y, 0); }
        #endregion

        #region 4. BO ĐẦU TƯỜNG (END CAP)
        // FIX (lỗi "tất cả các căn lề đều cần bo đầu nếu không giao nhau với tường khác"):
        // Thuật toán gốc chỉ xử lý 2 trường hợp trong Phase 1 (góc chữ L và Ngã 3 chữ T),
        // hoàn toàn không có bước nào đóng kín tiết diện tường tại đầu mút TỰ DO (không
        // chạm tường nào khác). Vì vị trí thực của đầu mút 2 đường biên phụ thuộc alignment
        // (Trái/Phải: 1 đường nằm đúng tại điểm click, đường kia lệch nguyên "thickness";
        // Giữa: cả 2 đường lệch thickness/2 về 2 phía) nên không thể chỉ tìm đúng-tại-đỉnh-
        // click. Thay vào đó, hàm này quét quanh đỉnh (bán kính ~thickness) để tìm 2 đầu
        // mút "mồ côi" (không có tường/đầu mút nào khác hội tụ) rồi nối chúng lại bằng một
        // đường bo đầu. Nếu tại đó có từ 3 đầu mút trở lên (góc/ngã ba thật) thì bỏ qua vì
        // đã được Phase 1/2/3 xử lý đúng rồi.
        //
        // CÁCH DÙNG: gọi 1 lần cho điểm ĐẦU TIÊN và 1 lần cho điểm CUỐI CÙNG của cả chuỗi
        // polyline tường, SAU KHI đã vẽ xong toàn bộ các đoạn (không gọi cho từng đoạn/điểm
        // giữa chừng, vì các điểm giữa luôn được nối bởi đoạn tường tiếp theo).
        public static void CapFreeEnd(Database db, Point3d vertex, double thickness, string wallLayerName)
        {
            double searchRadius = Math.Max(thickness, 1.0) + 5.0;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    BlockTable blockTable = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                    BlockTableRecord modelSpace = tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;
                    LayerTable layerTable = tr.GetObject(db.LayerTableId, OpenMode.ForRead) as LayerTable;

                    if (!layerTable.Has(wallLayerName)) { tr.Commit(); return; }
                    ObjectId targetLayerId = layerTable[wallLayerName];

                    // Thu thập các đầu mút (kèm line sở hữu) nằm trong bán kính quanh vertex
                    List<EndPointInfo> nearEnds = new List<EndPointInfo>();

                    foreach (ObjectId objId in modelSpace)
                    {
                        DBObject obj = tr.GetObject(objId, OpenMode.ForRead);
                        if (obj is Line ln && ln.LayerId == targetLayerId)
                        {
                            if (ln.StartPoint.DistanceTo(ln.EndPoint) <= Tolerance) continue;
                            Vector3d dir = (ln.EndPoint - ln.StartPoint).GetNormal();

                            if (ln.StartPoint.DistanceTo(vertex) <= searchRadius)
                                nearEnds.Add(new EndPointInfo { Pt = ln.StartPoint, LineId = objId, Dir = dir });
                            if (ln.EndPoint.DistanceTo(vertex) <= searchRadius)
                                nearEnds.Add(new EndPointInfo { Pt = ln.EndPoint, LineId = objId, Dir = dir });
                        }
                    }

                    // Gom theo line (mỗi line chỉ tính 1 đầu mút gần vertex nhất, phòng khi
                    // line rất ngắn và cả 2 đầu đều lọt vào bán kính tìm kiếm)
                    var byLine = nearEnds
                        .GroupBy(x => x.LineId)
                        .Select(g => g.OrderBy(x => x.Pt.DistanceTo(vertex)).First())
                        .ToList();

                    // Đúng 2 đường hội tụ tại đây => đầu tường tự do => bo đầu.
                    // Khác 2 (1, 3, 4...) => không phải đầu tự do đơn lẻ, bỏ qua.
                    if (byLine.Count == 2)
                    {
                        var a = byLine[0];
                        var b = byLine[1];

                        // Chỉ bo khi 2 đường này gần như song song (đúng là cặp biên của
                        // CÙNG một bức tường), tránh bo nhầm ở góc/ngã ba lệch tâm.
                        bool roughlyParallel = Math.Abs(a.Dir.DotProduct(b.Dir)) > 0.9;
                        double gap = a.Pt.DistanceTo(b.Pt);

                        if (roughlyParallel && gap > Tolerance)
                        {
                            // Tránh tạo trùng nếu đã có sẵn 1 đường nối y hệt (gọi lại nhiều lần)
                            bool alreadyCapped = false;
                            foreach (var e in byLine)
                            {
                                DBObject o = tr.GetObject(e.LineId, OpenMode.ForRead);
                                if (o is Line l &&
                                    ((l.StartPoint.DistanceTo(a.Pt) <= Tolerance && l.EndPoint.DistanceTo(b.Pt) <= Tolerance) ||
                                     (l.StartPoint.DistanceTo(b.Pt) <= Tolerance && l.EndPoint.DistanceTo(a.Pt) <= Tolerance)))
                                {
                                    alreadyCapped = true;
                                }
                            }

                            if (!alreadyCapped)
                            {
                                Line cap = new Line(a.Pt, b.Pt) { LayerId = targetLayerId };
                                modelSpace.AppendEntity(cap);
                                tr.AddNewlyCreatedDBObject(cap, true);
                                TagAsCap(tr, db, cap);
                            }
                        }
                    }

                    tr.Commit();
                }
                catch (Exception ex) { tr.Abort(); throw new Exception($"Error capping wall end: {ex.Message}", ex); }
            }
        }

        private class EndPointInfo
        {
            public Point3d Pt;
            public ObjectId LineId;
            public Vector3d Dir;
        }

        // FIX (lỗi "tiếp tục vẽ ở đầu đã bo bị sót đường thừa"):
        // Gọi hàm này TRƯỚC khi vẽ đoạn tường mới, với điểm người dùng vừa click. Nếu tại
        // gần điểm đó có sẵn 1 đường đã được đánh dấu là cap (do CapFreeEnd tạo ở lần vẽ
        // trước), xoá nó đi — vì đầu mút đó sắp có tường nối vào, không còn "tự do" nữa.
        public static void RemoveCapAt(Database db, Point3d vertex, double thickness, string wallLayerName)
        {
            double searchRadius = Math.Max(thickness, 1.0) + 5.0;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    LayerTable layerTable = tr.GetObject(db.LayerTableId, OpenMode.ForRead) as LayerTable;
                    if (!layerTable.Has(wallLayerName)) { tr.Commit(); return; }
                    ObjectId targetLayerId = layerTable[wallLayerName];

                    BlockTable blockTable = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                    BlockTableRecord modelSpace = tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead) as BlockTableRecord;

                    List<ObjectId> toErase = new List<ObjectId>();
                    foreach (ObjectId objId in modelSpace)
                    {
                        DBObject obj = tr.GetObject(objId, OpenMode.ForRead);
                        if (obj is Line ln && ln.LayerId == targetLayerId && IsCapLine(ln))
                        {
                            if (ln.StartPoint.DistanceTo(vertex) <= searchRadius || ln.EndPoint.DistanceTo(vertex) <= searchRadius)
                                toErase.Add(objId);
                        }
                    }

                    foreach (ObjectId id in toErase)
                    {
                        DBObject obj = tr.GetObject(id, OpenMode.ForWrite);
                        if (obj != null && !obj.IsErased) obj.Erase();
                    }

                    tr.Commit();
                }
                catch (Exception ex) { tr.Abort(); throw new Exception($"Error removing old cap: {ex.Message}", ex); }
            }
        }
        #endregion
    }

    public class IntersectionInfo
    {
        public ObjectId ExistingLineId { get; set; }
    }
}