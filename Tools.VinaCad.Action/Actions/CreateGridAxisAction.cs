using Prima.VinaCAD.ApplicationServices;
using Prima.VinaCAD.EditorInput;
using System;
using System.Collections.Generic;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.GraphicsInterface;
using Tools.Model;
using Tools.VinaCad.Helper.Helper;
using Tools.VinaCAD.UI;
using Application = Prima.VinaCAD.ApplicationServices.Application;

namespace Tools.VinaCad.Action.Actions
{
    public class CreateGridAxisAction
    {
        public void Execute()
        {
            Document? document = Application.DocumentManager.MdiActiveDocument;
            if (document == null)
                throw new InvalidOperationException("Không có bản vẽ đang hoạt động.");

            Editor editor = document.Editor;
            Database database = document.Database;

            var window = new GridAxisWindow();
            Application.ShowModalWindow(window);

            if (window.DialogResult != true || window.Input == null)
                return;

            GridAxisInput input = window.Input;
            var jig = new GridAxisPlacementJig(database, input);
            PromptResult placementResult = editor.Drag(jig);

            if (placementResult.Status != PromptStatus.OK)
            {
                editor.WriteMessage("\nZXW đã hủy; bản vẽ không thay đổi.");
                return;
            }

            int entityCount = GridAxisHelper.CreateGrid(database, input, jig.Origin);
            editor.Regen();
            editor.WriteMessage($"\nZXW đã tạo lưới {input.Breadths.Count + 1} x {input.Depths.Count + 1} trục ({entityCount} đối tượng). ");
        }

        private sealed class GridAxisPlacementJig : DrawJig
        {
            private readonly Database _database;
            private readonly GridAxisInput _input;
            private readonly IReadOnlyList<double> _xStations;
            private readonly IReadOnlyList<double> _yStations;
            private readonly GridAxisHelper.AnnotationMetrics _metrics;
            private Point3d _origin;
            private bool _hasOrigin;

            public Point3d Origin => _origin;

            public GridAxisPlacementJig(Database database, GridAxisInput input)
            {
                _database = database;
                _input = input;
                _xStations = GridAxisDataHelper.BuildStations(input.Breadths);
                _yStations = GridAxisDataHelper.BuildStations(input.Depths);
                _metrics = GridAxisHelper.GetPreviewMetrics(input);
                _origin = Point3d.Origin;
            }

            protected override SamplerStatus Sampler(JigPrompts prompts)
            {
                var options = new JigPromptPointOptions
                {
                    Message = "\nChọn điểm góc trái-dưới để đặt lưới trục: ",
                    UserInputControls = UserInputControls.Accept3dCoordinates
                };

                PromptPointResult result = prompts.AcquirePoint(options);
                if (result.Status == PromptStatus.Cancel)
                    return SamplerStatus.Cancel;

                if (result.Status != PromptStatus.OK)
                    return SamplerStatus.NoChange;

                if (_hasOrigin && result.Value.DistanceTo(_origin) <= 1e-6)
                    return SamplerStatus.NoChange;

                _origin = result.Value;
                _hasOrigin = true;
                return SamplerStatus.OK;
            }

            protected override bool WorldDraw(WorldDraw draw)
            {
                Vector3d axisX = _database.Ucsxdir.GetNormal();
                Vector3d axisY = _database.Ucsydir.GetNormal();
                Vector3d normal = axisX.CrossProduct(axisY).GetNormal();
                double maxX = _input.TotalWidth;
                double maxY = _input.TotalDepth;
                double extension = _input.DrawAnnotations ? _metrics.BubbleOffset : 0;
                double dashLength = Math.Max(0.5, Math.Min(maxX, maxY) * 0.08);

                Point3d PointAt(double x, double y) => _origin + axisX * x + axisY * y;

                foreach (double x in _xStations)
                    DrawDashedLine(draw, PointAt(x, -extension),
                        PointAt(x, maxY + extension), 1, dashLength);

                foreach (double y in _yStations)
                    DrawDashedLine(draw, PointAt(-extension, y),
                        PointAt(maxX + extension, y), 1, dashLength);

                if (_input.DrawAnnotations)
                {
                    foreach (double x in _xStations)
                    {
                        DrawCircle(draw, PointAt(x, -extension), normal, _metrics.BubbleRadius, 3);
                        DrawCircle(draw, PointAt(x, maxY + extension), normal, _metrics.BubbleRadius, 3);
                    }

                    foreach (double y in _yStations)
                    {
                        DrawCircle(draw, PointAt(-extension, y), normal, _metrics.BubbleRadius, 3);
                        DrawCircle(draw, PointAt(maxX + extension, y), normal, _metrics.BubbleRadius, 3);
                    }

                    DrawDimensionRails(draw, PointAt, maxX, maxY);
                }

                return true;
            }

            private void DrawDimensionRails(
                WorldDraw draw,
                Func<double, double, Point3d> pointAt,
                double maxX,
                double maxY)
            {
                DrawLine(draw, pointAt(0, -_metrics.InnerDimensionOffset),
                    pointAt(maxX, -_metrics.InnerDimensionOffset), 3);
                DrawLine(draw, pointAt(0, -_metrics.OuterDimensionOffset),
                    pointAt(maxX, -_metrics.OuterDimensionOffset), 3);
                DrawLine(draw, pointAt(0, maxY + _metrics.InnerDimensionOffset),
                    pointAt(maxX, maxY + _metrics.InnerDimensionOffset), 3);
                DrawLine(draw, pointAt(0, maxY + _metrics.OuterDimensionOffset),
                    pointAt(maxX, maxY + _metrics.OuterDimensionOffset), 3);
                DrawLine(draw, pointAt(-_metrics.InnerDimensionOffset, 0),
                    pointAt(-_metrics.InnerDimensionOffset, maxY), 3);
                DrawLine(draw, pointAt(-_metrics.OuterDimensionOffset, 0),
                    pointAt(-_metrics.OuterDimensionOffset, maxY), 3);
                DrawLine(draw, pointAt(maxX + _metrics.InnerDimensionOffset, 0),
                    pointAt(maxX + _metrics.InnerDimensionOffset, maxY), 3);
                DrawLine(draw, pointAt(maxX + _metrics.OuterDimensionOffset, 0),
                    pointAt(maxX + _metrics.OuterDimensionOffset, maxY), 3);
            }

            private static void DrawLine(WorldDraw draw, Point3d start, Point3d end, short colorIndex)
            {
                using var line = new Line(start, end) { ColorIndex = colorIndex };
                draw.Geometry.Draw(line);
            }

            private static void DrawDashedLine(
                WorldDraw draw,
                Point3d start,
                Point3d end,
                short colorIndex,
                double dashLength)
            {
                Vector3d direction = end - start;
                double totalLength = direction.Length;
                if (totalLength <= 1e-9)
                    return;

                direction = direction.GetNormal();
                double gapLength = dashLength * 0.5;
                double patternLength = dashLength + gapLength;

                for (double distance = 0; distance < totalLength; distance += patternLength)
                {
                    double dashEnd = Math.Min(distance + dashLength, totalLength);
                    DrawLine(draw, start + direction * distance,
                        start + direction * dashEnd, colorIndex);
                }
            }

            private static void DrawCircle(
                WorldDraw draw,
                Point3d center,
                Vector3d normal,
                double radius,
                short colorIndex)
            {
                using var circle = new Circle(center, normal, radius) { ColorIndex = colorIndex };
                draw.Geometry.Draw(circle);
            }
        }
    }
}
