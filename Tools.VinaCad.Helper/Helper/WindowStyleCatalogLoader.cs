using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Teigha.DatabaseServices;
using Tools.Model;

namespace Tools.VinaCad.Helper.Helper
{
    public static class WindowStyleCatalogLoader
    {
        private const string AssetsFolderName = "Window Assets";
        private static WindowStyleCatalog? _cachedCatalog;

        public static WindowStyleCatalog LoadDefault()
        {
            if (_cachedCatalog != null) return _cachedCatalog;

            string assetsDirectory = GetAssetsDirectory();
            string[] files = Directory.GetFiles(assetsDirectory, "*.dwg")
                .OrderBy(NaturalSortKey)
                .ToArray();
            if (files.Length == 0)
                throw new FileNotFoundException("Không tìm thấy bản vẽ cửa sổ *.dwg.", assetsDirectory);

            WindowStyleCatalog catalog = new WindowStyleCatalog();
            foreach (string file in files)
            {
                string name = Path.GetFileNameWithoutExtension(file);
                WindowStyleModel style = new WindowStyleModel
                {
                    Id = name,
                    DisplayName = name,
                    AssetPath = file,
                    PreviewGeometry = ReadPreviewGeometry(file)
                };
                SetOpeningBounds(style);
                catalog.Styles.Add(style);
            }
            _cachedCatalog = catalog;
            return catalog;
        }

        public static string GetAssetsDirectory()
        {
            string assemblyDirectory = Path.GetDirectoryName(typeof(WindowStyleCatalogLoader).Assembly.Location)
                ?? AppContext.BaseDirectory;
            string[] directCandidates =
            {
                Path.Combine(assemblyDirectory, "Templates", AssetsFolderName),
                Path.Combine(assemblyDirectory, "Resources", "Templates", AssetsFolderName),
                Path.Combine(AppContext.BaseDirectory, "Templates", AssetsFolderName),
                Path.Combine(AppContext.BaseDirectory, "Resources", "Templates", AssetsFolderName)
            };
            string? directMatch = directCandidates.FirstOrDefault(Directory.Exists);
            if (directMatch != null) return directMatch;

            DirectoryInfo? directory = new DirectoryInfo(assemblyDirectory);
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, "Resources", "Templates", AssetsFolderName);
                if (Directory.Exists(candidate)) return candidate;
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException($"Không tìm thấy thư mục Templates/{AssetsFolderName}.");
        }

        private static List<WindowPreviewPrimitive> ReadPreviewGeometry(string path)
        {
            List<WindowPreviewPrimitive> primitives = new List<WindowPreviewPrimitive>();
            try
            {
                using Database database = new Database(false, true);
                database.ReadDwgFile(path, FileShare.Read, true, null);
                using Transaction transaction = database.TransactionManager.StartTransaction();
                BlockTable blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
                BlockTableRecord modelSpace = (BlockTableRecord)transaction.GetObject(
                    blockTable[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                foreach (ObjectId objectId in modelSpace)
                {
                    if (transaction.GetObject(objectId, OpenMode.ForRead) is Entity entity)
                        CollectPreviewGeometry(entity, primitives, 0);
                }
            }
            catch
            {
                primitives.Clear();
            }
            return primitives;
        }

        private static void CollectPreviewGeometry(Entity entity, List<WindowPreviewPrimitive> primitives, int depth)
        {
            if (entity is Line line)
            {
                primitives.Add(CreatePrimitive(entity, WindowPreviewPrimitiveKind.Line,
                    line.StartPoint.X, line.StartPoint.Y, line.EndPoint.X, line.EndPoint.Y));
                return;
            }
            if (entity is Arc arc)
            {
                WindowPreviewPrimitive primitive = CreatePrimitive(entity, WindowPreviewPrimitiveKind.Arc,
                    arc.StartPoint.X, arc.StartPoint.Y, arc.EndPoint.X, arc.EndPoint.Y);
                primitive.CenterX = arc.Center.X;
                primitive.CenterY = arc.Center.Y;
                primitive.Radius = arc.Radius;
                primitive.StartAngle = arc.StartAngle;
                primitive.EndAngle = arc.EndAngle;
                primitives.Add(primitive);
                return;
            }
            if (entity is Circle circle)
            {
                WindowPreviewPrimitive primitive = CreatePrimitive(entity, WindowPreviewPrimitiveKind.Circle, 0, 0, 0, 0);
                primitive.CenterX = circle.Center.X;
                primitive.CenterY = circle.Center.Y;
                primitive.Radius = circle.Radius;
                primitives.Add(primitive);
                return;
            }
            if (depth >= 4) return;

            DBObjectCollection exploded = new DBObjectCollection();
            try
            {
                entity.Explode(exploded);
            }
            catch
            {
                return;
            }

            foreach (DBObject item in exploded)
            {
                try
                {
                    if (item is Entity child) CollectPreviewGeometry(child, primitives, depth + 1);
                }
                finally
                {
                    item.Dispose();
                }
            }
        }

        private static WindowPreviewPrimitive CreatePrimitive(
            Entity entity,
            WindowPreviewPrimitiveKind kind,
            double startX,
            double startY,
            double endX,
            double endY)
        {
            WindowPreviewPrimitive primitive = new WindowPreviewPrimitive
            {
                Kind = kind,
                StartX = startX,
                StartY = startY,
                EndX = endX,
                EndY = endY,
                MinX = Math.Min(startX, endX),
                MinY = Math.Min(startY, endY),
                MaxX = Math.Max(startX, endX),
                MaxY = Math.Max(startY, endY)
            };
            try
            {
                Extents3d extents = entity.GeometricExtents;
                primitive.MinX = extents.MinPoint.X;
                primitive.MinY = extents.MinPoint.Y;
                primitive.MaxX = extents.MaxPoint.X;
                primitive.MaxY = extents.MaxPoint.Y;
            }
            catch
            {
                if (kind == WindowPreviewPrimitiveKind.Circle)
                {
                    Circle circle = (Circle)entity;
                    primitive.MinX = circle.Center.X - circle.Radius;
                    primitive.MinY = circle.Center.Y - circle.Radius;
                    primitive.MaxX = circle.Center.X + circle.Radius;
                    primitive.MaxY = circle.Center.Y + circle.Radius;
                }
            }
            return primitive;
        }

        private static void SetOpeningBounds(WindowStyleModel style)
        {
            List<WindowPreviewPrimitive> curves = style.PreviewGeometry
                .Where(item => item.Kind != WindowPreviewPrimitiveKind.Line)
                .ToList();
            IEnumerable<WindowPreviewPrimitive> candidates = curves.Count > 0
                ? curves
                : style.PreviewGeometry.Where(item =>
                    Math.Abs(item.EndY - item.StartY) > Math.Abs(item.EndX - item.StartX) * 0.05);
            List<WindowPreviewPrimitive> detail = candidates.ToList();
            if (detail.Count == 0) detail = style.PreviewGeometry;
            if (detail.Count == 0) return;

            double minimumX = detail.Min(item => item.MinX);
            double maximumX = detail.Max(item => item.MaxX);
            if (maximumX - minimumX <= 1e-6) return;
            style.OpeningMinimumX = minimumX;
            style.OpeningMaximumX = maximumX;
        }

        private static string NaturalSortKey(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            string digits = new string(name.Where(char.IsDigit).ToArray());
            return int.TryParse(digits, out int number) ? number.ToString("D8") : name;
        }
    }
}
