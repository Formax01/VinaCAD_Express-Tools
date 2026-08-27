using Prima.VinaCAD.EditorInput;
using System.Collections.Generic;
using System.Linq;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace Tools.VinaCad.Helper.Helper
{
    /// <summary>
    /// Reads only wall candidates selected by the CAD engine. This avoids opening
    /// every object in a large drawing before checking its type and layer.
    /// </summary>
    public static class WallObjectQueryHelper
    {
        public static List<Line> ReadLines(Editor editor, Transaction transaction, string layerName, bool includeWallCaps = false)
        {
            PromptSelectionResult selection = editor.SelectAll(CreateLineFilter(layerName));
            return ReadSelection(transaction, selection, includeWallCaps);
        }

        public static List<Line> ReadLinesNear(Editor editor, Transaction transaction, string layerName, IEnumerable<Point3d> areaPoints, double padding, bool includeWallCaps = false)
        {
            List<Point3d> points = areaPoints.ToList();
            if (points.Count == 0)
                return new List<Line>();

            double safePadding = System.Math.Max(0.0, padding);
            Point3d minimum = new Point3d(points.Min(point => point.X) - safePadding, points.Min(point => point.Y) - safePadding, 0.0);
            Point3d maximum = new Point3d(points.Max(point => point.X) + safePadding, points.Max(point => point.Y) + safePadding, 0.0);
            PromptSelectionResult selection = editor.SelectCrossingWindow(minimum, maximum, CreateLineFilter(layerName));
            return ReadSelection(transaction, selection, includeWallCaps);
        }

        private static SelectionFilter CreateLineFilter(string layerName)
        {
            return new SelectionFilter(new[]
            {
                new TypedValue((int)DxfCode.Start, "LINE"),
                new TypedValue((int)DxfCode.LayerName, EscapeFilterLiteral(layerName))
            });
        }

        private static List<Line> ReadSelection(Transaction transaction, PromptSelectionResult selection, bool includeWallCaps)
        {
            List<Line> lines = new List<Line>();
            if (selection.Status != PromptStatus.OK)
                return lines;

            foreach (ObjectId id in selection.Value.GetObjectIds())
            {
                Line? line = transaction.GetObject(id, OpenMode.ForRead) as Line;
                if (line != null && !line.IsErased && (includeWallCaps || !DrawWallHelper.IsWallCap(line)))
                    lines.Add(line);
            }

            return lines;
        }

        private static string EscapeFilterLiteral(string value)
        {
            const string wildcardCharacters = "#@.*?~[]-,`";
            string escaped = string.Empty;
            foreach (char character in value)
                escaped += wildcardCharacters.IndexOf(character) >= 0 ? $"`{character}" : character.ToString();
            return escaped;
        }
    }
}
