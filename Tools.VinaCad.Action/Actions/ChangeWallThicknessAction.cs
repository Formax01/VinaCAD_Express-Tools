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
using Application = Prima.VinaCAD.ApplicationServices.Application;
using MessageBox = System.Windows.MessageBox;

namespace Tools.VinaCAD.Action.Actions
{
    public class ChangeWallThicknessAction
    {
        private const double Tolerance = 0.001;
        private const double ParallelTolerance = 0.002;
        private double _thickness = 200.0;

        public void Execute()
        {
            Document document = Application.DocumentManager.MdiActiveDocument;
            if (document == null) return;

            Editor editor = document.Editor;
            Database database = document.Database;

            try
            {
                if (!PromptThickness(editor)) return;
                bool keepClickedSide = PromptAlignment(editor);

                editor.WriteMessage(
                    $"\nWWT: Chiều dày mới = {_thickness:0.##}; " +
                    (keepClickedSide ? "giữ mặt được chọn." : "thay đổi đều qua tim tường."));

                int changedCount = 0;
                while (true)
                {
                    PromptEntityOptions options = new PromptEntityOptions(
                        "\nChọn một mặt tường cần đổi chiều dày <Kết thúc>: ");
                    options.SetRejectMessage("\nWWT chỉ nhận đường thẳng Line của tường.");
                    options.AddAllowedClass(typeof(Line), true);

                    PromptEntityResult result = editor.GetEntity(options);
                    if (result.Status != PromptStatus.OK) break;

                    if (ChangeWall(database, result.ObjectId, keepClickedSide, out string message))
                    {
                        changedCount++;
                        editor.UpdateScreen();
                        editor.WriteMessage($"\n{message}");
                    }
                    else
                    {
                        editor.WriteMessage($"\nKhông thể đổi tường: {message}");
                    }
                }

                editor.WriteMessage($"\nWWT: Đã thay đổi {changedCount} đoạn tường.");
            }
            catch (Exception ex)
            {
                Logger.Info(nameof(ChangeWallThicknessAction), ex);
                MessageBox.Show($"Lỗi WWT: {ex.Message}", StringDefinition.TITLE_ERROR);
            }
        }

        private bool PromptThickness(Editor editor)
        {
            bool showDialog = true;
            while (showDialog)
            {
                var window = new Tools.VinaCAD.UI.WallThicknessWindow(_thickness);
                Application.ShowModalWindow(window);

                if (window.DialogResult != true) return false;

                if (window.IsPickUpRequested)
                {
                    PromptDistanceOptions options = new PromptDistanceOptions(
                        "\nChọn hai điểm xác định chiều dày mới: ");
                    PromptDoubleResult result = editor.GetDistance(options);
                    if (result.Status == PromptStatus.OK && result.Value > Tolerance)
                        _thickness = Math.Round(result.Value, 2);
                }
                else
                {
                    _thickness = window.SelectedThickness;
                    showDialog = false;
                }
            }

            return _thickness > Tolerance;
        }

        private static bool PromptAlignment(Editor editor)
        {
            PromptStringOptions options = new PromptStringOptions(
                "\nChế độ căn [C=Tâm/S=Giữ mặt được chọn] <C>: ")
            {
                AllowSpaces = false,
                DefaultValue = "C"
            };

            PromptResult result = editor.GetString(options);
            return result.Status == PromptStatus.OK &&
                   string.Equals(result.StringResult, "S", StringComparison.OrdinalIgnoreCase);
        }

        private bool ChangeWall(Database database, ObjectId clickedId, bool keepClickedSide, out string message)
        {
            message = string.Empty;
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                try
                {
                    Line clicked = transaction.GetObject(clickedId, OpenMode.ForRead) as Line;
                    if (clicked == null || clicked.IsErased || DrawWallHelper.IsWallCap(clicked))
                    {
                        message = "đối tượng không phải mặt tường hợp lệ.";
                        return false;
                    }

                    List<Line> allLines = GetModelSpaceLines(transaction, database);
                    List<Line> clickedSide;
                    List<Line> otherSide;
                    if (!TryFindWallFaces(clicked, allLines, out clickedSide, out otherSide))
                    {
                        message = "không tìm thấy mặt tường song song tương ứng.";
                        return false;
                    }

                    Vector3d direction = GetStableDirection(clicked);
                    Vector3d normal = new Vector3d(-direction.Y, direction.X, 0.0);
                    double signedDistance = SignedDistance(otherSide[0].StartPoint, clicked.StartPoint, normal);
                    if (Math.Abs(signedDistance) <= Tolerance)
                    {
                        message = "hai mặt tường đang trùng nhau.";
                        return false;
                    }

                    double oldThickness = Math.Abs(signedDistance);
                    double sign = Math.Sign(signedDistance);
                    Vector3d clickedMove;
                    Vector3d otherMove;
                    if (keepClickedSide)
                    {
                        clickedMove = new Vector3d(0.0, 0.0, 0.0);
                        otherMove = normal * (sign * (_thickness - oldThickness));
                    }
                    else
                    {
                        double halfChange = (_thickness - oldThickness) / 2.0;
                        clickedMove = normal * (-sign * halfChange);
                        otherMove = normal * (sign * halfChange);
                    }

                    var movedIds = new HashSet<ObjectId>(clickedSide.Select(x => x.ObjectId));
                    movedIds.UnionWith(otherSide.Select(x => x.ObjectId));
                    var endpointMoves = new List<EndpointMove>();

                    MoveFace(clickedSide, clickedMove, endpointMoves);
                    MoveFace(otherSide, otherMove, endpointMoves);
                    RepairConnections(allLines, movedIds, endpointMoves);

                    transaction.Commit();
                    message = $"Đã đổi từ {oldThickness:0.##} thành {_thickness:0.##}.";
                    return true;
                }
                catch (Exception ex)
                {
                    transaction.Abort();
                    Logger.Info(nameof(ChangeWall), ex);
                    message = ex.Message;
                    return false;
                }
            }
        }

        private static List<Line> GetModelSpaceLines(Transaction transaction, Database database)
        {
            BlockTable table = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
            BlockTableRecord modelSpace = (BlockTableRecord)transaction.GetObject(
                table[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            var lines = new List<Line>();
            foreach (ObjectId id in modelSpace)
            {
                // kiểm tra toàn bộ các đối tượng trong ModelSpace, nhưng chỉ lấy các đường thẳng Line không null có sẵn ,k bị xóa và có lớp "LINE"
                if (id.IsNull || !id.IsValid || id.IsErased ||
                    !string.Equals(id.ObjectClass?.DxfName, "LINE", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (transaction.GetObject(id, OpenMode.ForRead) is Line line)
                    lines.Add(line);
            }
            return lines;
        }

        private static bool TryFindWallFaces(Line clicked, List<Line> allLines, out List<Line> clickedSide, out List<Line> otherSide)
        {
            clickedSide = new List<Line>();
            otherSide = new List<Line>();

            Vector3d clickedVector = clicked.EndPoint - clicked.StartPoint;
            double clickedLength = clickedVector.Length;
            if (clickedLength <= Tolerance) return false;

            Point3d clickedStart = clicked.StartPoint;
            Vector3d clickedDirection = clickedVector / clickedLength;
            string segmentId = DrawWallHelper.GetWallSegmentId(clicked);
            string clickedMarker = DrawWallHelper.GetWallSideMarker(clicked);

            if (!string.IsNullOrEmpty(segmentId))
            {
                var oppositeCandidates = new List<Line>();
                bool hasOverlappingOpposite = false;

                // Chỉ quét một lần và chỉ đọc marker của các line cùng segment.
                foreach (Line line in allLines)
                {
                    // Kiểm tra segment trước để đa số line chỉ cần một lần đọc XData.
                    if (!string.Equals(DrawWallHelper.GetWallSegmentId(line), segmentId, StringComparison.Ordinal) ||
                        DrawWallHelper.IsWallCap(line))
                        continue;

                    string marker = DrawWallHelper.GetWallSideMarker(line);
                    if (string.Equals(marker, clickedMarker, StringComparison.Ordinal))
                    {
                        clickedSide.Add(line);
                        continue;
                    }

                    oppositeCandidates.Add(line);
                    if (!hasOverlappingOpposite &&
                        AreParallelAndOverlapping(clickedStart, clickedDirection, clickedLength, line))
                        hasOverlappingOpposite = true;
                }

                if (hasOverlappingOpposite)
                    otherSide = oppositeCandidates;

                if (clickedSide.Count > 0 && otherSide.Count > 0) return true;
            }

            // tìm đường Line gần nhất được xem là mặt đối diện của bức tường so với đường người dùng vừa chọn
            Line pair = null;
            double pairDistance = double.MaxValue;
            foreach (Line line in allLines)
            {
                if (line.ObjectId == clicked.ObjectId ||line.LayerId != clicked.LayerId || DrawWallHelper.IsWallCap(line) || !AreParallelAndOverlapping(clickedStart, clickedDirection, clickedLength, line))
                    continue;

                double distance = DistanceToInfiniteLine(line.StartPoint, clickedStart, clickedDirection);
                if (distance >= pairDistance) continue;

                pair = line;
                pairDistance = distance;
            }

            if (pair == null) return false;
            clickedSide.Add(clicked);
            otherSide.Add(pair);
            return true;
        }

        private static void MoveFace(IEnumerable<Line> lines, Vector3d move, List<EndpointMove> endpointMoves)
        {
            foreach (Line line in lines)
            {
                Point3d oldStart = line.StartPoint;
                Point3d oldEnd = line.EndPoint;
                line.UpgradeOpen();
                line.StartPoint = oldStart + move;
                line.EndPoint = oldEnd + move;
                endpointMoves.Add(new EndpointMove(oldStart, line.StartPoint));
                endpointMoves.Add(new EndpointMove(oldEnd, line.EndPoint));
            }
        }

        private static void RepairConnections(List<Line> allLines, HashSet<ObjectId> movedIds, List<EndpointMove> endpointMoves)
        {
            if (endpointMoves.Count == 0) return;

            // Loại các phép dịch chuyển bằng 0 để không UpgradeOpen và ghi lại
            // những line thực tế không thay đổi.
            var effectiveMoves = new List<EndpointMove>(endpointMoves.Count);
            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double minZ = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;
            double maxZ = double.MinValue;

            foreach (EndpointMove endpointMove in endpointMoves)
            {
                if (SquaredDistance(endpointMove.OldPoint, endpointMove.NewPoint) <= Tolerance * Tolerance)
                    continue;

                effectiveMoves.Add(endpointMove);
                Point3d point = endpointMove.OldPoint;
                minX = Math.Min(minX, point.X);
                minY = Math.Min(minY, point.Y);
                minZ = Math.Min(minZ, point.Z);
                maxX = Math.Max(maxX, point.X);
                maxY = Math.Max(maxY, point.Y);
                maxZ = Math.Max(maxZ, point.Z);
            }

            if (effectiveMoves.Count == 0) return;

            minX -= Tolerance;
            minY -= Tolerance;
            minZ -= Tolerance;
            maxX += Tolerance;
            maxY += Tolerance;
            maxZ += Tolerance;
            double toleranceSquared = Tolerance * Tolerance;

            foreach (Line line in allLines)
            {
                if (movedIds.Contains(line.ObjectId) || line.IsErased) continue;

                bool startChanged = false;
                bool endChanged = false;
                Point3d newStart = line.StartPoint;
                Point3d newEnd = line.EndPoint;

                // Broad phase: phần lớn line trong bản vẽ lớn nằm ngoài vùng các
                // endpoint vừa đổi và được loại bằng vài phép so sánh rẻ.
                bool checkStart = IsInsideBounds(newStart, minX, minY, minZ, maxX, maxY, maxZ);
                bool checkEnd = IsInsideBounds(newEnd, minX, minY, minZ, maxX, maxY, maxZ);
                if (!checkStart && !checkEnd) continue;

                foreach (EndpointMove endpointMove in effectiveMoves)
                {
                    if (checkStart && SquaredDistance(newStart, endpointMove.OldPoint) <= toleranceSquared)
                    {
                        newStart = endpointMove.NewPoint;
                        startChanged = true;
                    }
                    if (checkEnd && SquaredDistance(newEnd, endpointMove.OldPoint) <= toleranceSquared)
                    {
                        newEnd = endpointMove.NewPoint;
                        endChanged = true;
                    }
                }

                if (!startChanged && !endChanged) continue;
                line.UpgradeOpen();
                if (startChanged) line.StartPoint = newStart;
                if (endChanged) line.EndPoint = newEnd;
            }
        }

        private static bool IsInsideBounds(Point3d point, double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
        {
            return point.X >= minX && point.X <= maxX &&
                   point.Y >= minY && point.Y <= maxY &&
                   point.Z >= minZ && point.Z <= maxZ;
        }

        private static double SquaredDistance(Point3d first, Point3d second)
        {
            double dx = first.X - second.X;
            double dy = first.Y - second.Y;
            double dz = first.Z - second.Z;
            return dx * dx + dy * dy + dz * dz;
        }

        private static Vector3d GetStableDirection(Line line)
        {
            Vector3d direction = (line.EndPoint - line.StartPoint).GetNormal();
            if (direction.X < -Tolerance ||
                (Math.Abs(direction.X) <= Tolerance && direction.Y < 0.0))
                direction = -direction;
            return direction;
        }

        private static double SignedDistance(Point3d point, Point3d origin, Vector3d normal)
        {
            return (point - origin).DotProduct(normal);
        }

        private static double DistanceToInfiniteLine(Point3d point, Point3d lineStart, Vector3d lineDirection)
        {
            Vector3d offset = point - lineStart;
            return Math.Abs(offset.X * lineDirection.Y - offset.Y * lineDirection.X);
        }

        private static bool AreParallelAndOverlapping(Point3d firstStart, Vector3d firstDirection, double firstLength, Line second)
        {
            Vector3d secondVector = second.EndPoint - second.StartPoint;
            double secondLength = secondVector.Length;
            if (secondLength <= Tolerance) return false;

            if (Math.Abs(firstDirection.DotProduct(secondVector / secondLength)) < 1.0 - ParallelTolerance)
                return false;

            double startProjection = (second.StartPoint - firstStart).DotProduct(firstDirection);
            double endProjection = (second.EndPoint - firstStart).DotProduct(firstDirection);
            double overlap = Math.Min(firstLength, Math.Max(startProjection, endProjection)) -
                             Math.Max(0.0, Math.Min(startProjection, endProjection));
            return overlap > Tolerance;
        }

        private sealed class EndpointMove
        {
            public EndpointMove(Point3d oldPoint, Point3d newPoint)
            {
                OldPoint = oldPoint;
                NewPoint = newPoint;
            }

            public Point3d OldPoint { get; }
            public Point3d NewPoint { get; }
        }
    }
}
