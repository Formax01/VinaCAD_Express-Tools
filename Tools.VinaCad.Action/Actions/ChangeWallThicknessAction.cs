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

        private bool ChangeWall(
            Database database,
            ObjectId clickedId,
            bool keepClickedSide,
            out string message)
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
                    RepairConnections(transaction, allLines, movedIds, endpointMoves);

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
                if (transaction.GetObject(id, OpenMode.ForRead) is Line line && !line.IsErased)
                    lines.Add(line);
            }
            return lines;
        }

        private static bool TryFindWallFaces(
            Line clicked,
            List<Line> allLines,
            out List<Line> clickedSide,
            out List<Line> otherSide)
        {
            clickedSide = new List<Line>();
            otherSide = new List<Line>();
            string segmentId = DrawWallHelper.GetWallSegmentId(clicked);
            string clickedMarker = DrawWallHelper.GetWallSideMarker(clicked);

            if (!string.IsNullOrEmpty(segmentId))
            {
                List<Line> segmentLines = allLines.Where(line =>
                    !DrawWallHelper.IsWallCap(line) &&
                    DrawWallHelper.GetWallSegmentId(line) == segmentId).ToList();

                clickedSide = segmentLines.Where(line =>
                    DrawWallHelper.GetWallSideMarker(line) == clickedMarker).ToList();
                List<Line> oppositeCandidates = segmentLines.Where(line =>
                    DrawWallHelper.GetWallSideMarker(line) != clickedMarker).ToList();
                if (oppositeCandidates.Any(line => AreParallelAndOverlapping(clicked, line)))
                    otherSide = oppositeCandidates;

                if (clickedSide.Count > 0 && otherSide.Count > 0) return true;
            }

            // Legacy wall: the clicked entity is one face and the closest parallel,
            // overlapping line on the same layer is the opposite face.
            Line pair = allLines
                .Where(line => line.ObjectId != clicked.ObjectId &&
                               line.LayerId == clicked.LayerId &&
                               !DrawWallHelper.IsWallCap(line) &&
                               AreParallelAndOverlapping(clicked, line))
                .OrderBy(line => DistanceToInfiniteLine(line.StartPoint, clicked))
                .FirstOrDefault();

            if (pair == null) return false;
            clickedSide.Add(clicked);
            otherSide.Add(pair);
            return true;
        }

        private static void MoveFace(
            IEnumerable<Line> lines,
            Vector3d move,
            List<EndpointMove> endpointMoves)
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

        private static void RepairConnections(
            Transaction transaction,
            List<Line> allLines,
            HashSet<ObjectId> movedIds,
            List<EndpointMove> endpointMoves)
        {
            foreach (Line line in allLines)
            {
                if (movedIds.Contains(line.ObjectId) || line.IsErased) continue;

                bool startChanged = false;
                bool endChanged = false;
                Point3d newStart = line.StartPoint;
                Point3d newEnd = line.EndPoint;

                foreach (EndpointMove endpointMove in endpointMoves)
                {
                    if (newStart.DistanceTo(endpointMove.OldPoint) <= Tolerance)
                    {
                        newStart = endpointMove.NewPoint;
                        startChanged = true;
                    }
                    if (newEnd.DistanceTo(endpointMove.OldPoint) <= Tolerance)
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

        private static double DistanceToInfiniteLine(Point3d point, Line line)
        {
            Vector3d direction = (line.EndPoint - line.StartPoint).GetNormal();
            Vector3d offset = point - line.StartPoint;
            return Math.Abs(offset.X * direction.Y - offset.Y * direction.X);
        }

        private static bool AreParallelAndOverlapping(Line first, Line second)
        {
            Vector3d firstVector = first.EndPoint - first.StartPoint;
            Vector3d secondVector = second.EndPoint - second.StartPoint;
            if (firstVector.Length <= Tolerance || secondVector.Length <= Tolerance)
                return false;

            Vector3d direction = firstVector.GetNormal();
            if (Math.Abs(direction.DotProduct(secondVector.GetNormal())) < 1.0 - ParallelTolerance)
                return false;

            double firstLength = firstVector.Length;
            double startProjection = (second.StartPoint - first.StartPoint).DotProduct(direction);
            double endProjection = (second.EndPoint - first.StartPoint).DotProduct(direction);
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
