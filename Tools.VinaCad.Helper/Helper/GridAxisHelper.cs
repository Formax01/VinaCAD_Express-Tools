using System;
using System.Collections.Generic;
using System.Linq;
using Teigha.Colors;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Tools.Model;
using Tools.VinaCad.Modeling;

namespace Tools.VinaCad.Helper.Helper
{
    public static class GridAxisHelper
    {
        public const string AxisLayerName = "AXIS";
        public const string SymbolLayerName = "AXIS-SYMBOL";
        public const string DimensionLayerName = "AXIS-DIM";
        public const string AxisLinetypeName = "ZXW-DASHED";
        //public const string AxisLinetypeName = "VCAD_POLAR";

        public sealed class AnnotationMetrics
        {
            public double TextHeight { get; init; }
            public double ArrowSize { get; init; }
            public double ExtensionLineOffset { get; init; }
            public double ExtensionBeyondDimension { get; init; }
            public double BubbleRadius { get; init; }
            public double InnerDimensionOffset { get; init; }
            public double OuterDimensionOffset { get; init; }
            public double BubbleOffset { get; init; }
        }

        public static int CreateGrid(Database database, GridAxisInput input, Point3d origin)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (input == null) throw new ArgumentNullException(nameof(input));

            Vector3d axisX = database.Ucsxdir.GetNormal();
            Vector3d axisY = database.Ucsydir.GetNormal();
            Vector3d normal = axisX.CrossProduct(axisY).GetNormal();
            double rotation = Math.Atan2(axisX.Y, axisX.X);
            IReadOnlyList<double> xStations = GridAxisDataHelper.BuildStations(input.Breadths);
            IReadOnlyList<double> yStations = GridAxisDataHelper.BuildStations(input.Depths);
            int entityCount = 0;

            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                BlockTableRecord owner = (BlockTableRecord)transaction.GetObject(
                    database.CurrentSpaceId, OpenMode.ForWrite);

                /*ObjectId axisLinetypeId = GetAxisLinetype(database, transaction);*/
                ObjectId axisLinetypeId = EnsureAxisLinetype(database, transaction);
                ObjectId axisLayerId = EnsureLayer(
                    database, transaction, AxisLayerName, 1, axisLinetypeId);
                ObjectId symbolLayerId = EnsureLayer(database, transaction, SymbolLayerName, 3);
                ObjectId dimensionLayerId = EnsureLayer(database, transaction, DimensionLayerName, 3);
                AnnotationMetrics metrics = GetAnnotationMetrics(database, transaction, input);

                double minX = 0;
                double maxX = input.TotalWidth;
                double minY = 0;
                double maxY = input.TotalDepth;

                Point3d PointAt(double x, double y) => origin + axisX * x + axisY * y;

                foreach (double x in xStations)
                {
                    Append(owner, transaction, new Line(PointAt(x, minY), PointAt(x, maxY))
                    {
                        LayerId = axisLayerId
                    });
                    entityCount++;
                }

                foreach (double y in yStations)
                {
                    Append(owner, transaction, new Line(PointAt(minX, y), PointAt(maxX, y))
                    {
                        LayerId = axisLayerId
                    });
                    entityCount++;
                }

                if (input.DrawAnnotations)
                {
                    for (int i = 0; i < xStations.Count; i++)
                    {
                        string label = (i + 1).ToString();
                        Append(owner, transaction, new Line(
                            PointAt(xStations[i], minY - metrics.ExtensionLineOffset),
                            PointAt(xStations[i], -metrics.BubbleOffset + metrics.BubbleRadius))
                        {
                            LayerId = dimensionLayerId
                        });
                        Append(owner, transaction, new Line(
                            PointAt(xStations[i], maxY + metrics.ExtensionLineOffset),
                            PointAt(xStations[i], maxY + metrics.BubbleOffset - metrics.BubbleRadius))
                        {
                            LayerId = dimensionLayerId
                        });
                        AddBubble(owner, transaction, PointAt(xStations[i], -metrics.BubbleOffset),
                            label, metrics, normal, rotation, symbolLayerId, database.Textstyle);
                        AddBubble(owner, transaction, PointAt(xStations[i], maxY + metrics.BubbleOffset),
                            label, metrics, normal, rotation, symbolLayerId, database.Textstyle);
                        entityCount += 6;
                    }

                    for (int i = 0; i < yStations.Count; i++)
                    {
                        string label = GridAxisDataHelper.ToAlphabeticLabel(i);
                        Append(owner, transaction, new Line(
                            PointAt(minX - metrics.ExtensionLineOffset, yStations[i]),
                            PointAt(-metrics.BubbleOffset + metrics.BubbleRadius, yStations[i]))
                        {
                            LayerId = dimensionLayerId
                        });
                        Append(owner, transaction, new Line(
                            PointAt(maxX + metrics.ExtensionLineOffset, yStations[i]),
                            PointAt(maxX + metrics.BubbleOffset - metrics.BubbleRadius, yStations[i]))
                        {
                            LayerId = dimensionLayerId
                        });
                        AddBubble(owner, transaction, PointAt(-metrics.BubbleOffset, yStations[i]),
                            label, metrics, normal, rotation, symbolLayerId, database.Textstyle);
                        AddBubble(owner, transaction, PointAt(maxX + metrics.BubbleOffset, yStations[i]),
                            label, metrics, normal, rotation, symbolLayerId, database.Textstyle);
                        entityCount += 6;
                    }

                    entityCount += AddHorizontalDimensions(owner, transaction, database, xStations,
                        minY, maxY, metrics, PointAt, rotation, dimensionLayerId);
                    entityCount += AddVerticalDimensions(owner, transaction, database, yStations,
                        minX, maxX, metrics, PointAt, rotation + Math.PI / 2, dimensionLayerId);
                }

                transaction.Commit();
            }

            return entityCount;
        }

        public static AnnotationMetrics GetPreviewMetrics(GridAxisInput input)
        {
            double typicalBay = GetTypicalBaySize(input);
            return CreateMetrics(GetAdaptiveTextHeight(input, typicalBay), typicalBay);
        }

        private static AnnotationMetrics GetAnnotationMetrics(
            Database database,
            Transaction transaction,
            GridAxisInput input)
        {
            double textHeight = GridAxisSetting.MinimumAnnotationTextHeight;
            DimStyleTableRecord? dimStyle = transaction.GetObject(
                database.Dimstyle, OpenMode.ForRead) as DimStyleTableRecord;

            if (dimStyle != null)
            {
                double dimScale = Math.Abs(dimStyle.Dimscale) > 1e-9 ? Math.Abs(dimStyle.Dimscale) : 1;
                if (dimStyle.Dimtxt > 1e-9)
                    textHeight = dimStyle.Dimtxt * dimScale;
            }

            double typicalBay = GetTypicalBaySize(input);
            double adaptiveTextHeight = GetAdaptiveTextHeight(input, typicalBay);
            double minimumTextHeight = Math.Max(1e-6, GridAxisSetting.MinimumAnnotationTextHeight);
            double maximumRatio = Math.Max(
                GridAxisSetting.AnnotationTextHeightToBayRatio,
                GridAxisSetting.MaximumAnnotationTextHeightToGridRatio);
            double maximumTextHeight = Math.Max(
                minimumTextHeight,
                Math.Min(input.TotalWidth, input.TotalDepth) * maximumRatio);
            textHeight = Math.Max(textHeight, adaptiveTextHeight);
            textHeight = Math.Min(textHeight, maximumTextHeight);
            return CreateMetrics(textHeight, typicalBay);
        }

        private static double GetTypicalBaySize(GridAxisInput input)
        {
            double[] spacings = input.Breadths
                .Concat(input.Depths)
                .Where(value => value > 1e-9)
                .OrderBy(value => value)
                .ToArray();

            double minimumTextHeight = Math.Max(1e-6, GridAxisSetting.MinimumAnnotationTextHeight);
            if (spacings.Length == 0)
                return minimumTextHeight / Math.Max(
                    1e-6, GridAxisSetting.AnnotationTextHeightToBayRatio);

            int middle = spacings.Length / 2;
            return spacings.Length % 2 == 0
                ? (spacings[middle - 1] + spacings[middle]) / 2
                : spacings[middle];
        }

        private static double GetAdaptiveTextHeight(GridAxisInput input, double typicalBay)
        {
            double minimumTextHeight = Math.Max(1e-6, GridAxisSetting.MinimumAnnotationTextHeight);
            double ratio = Math.Max(1e-6, GridAxisSetting.AnnotationTextHeightToBayRatio);
            double maximumRatio = Math.Max(
                ratio,
                GridAxisSetting.MaximumAnnotationTextHeightToGridRatio);
            double maximumTextHeight = Math.Max(
                minimumTextHeight,
                Math.Min(input.TotalWidth, input.TotalDepth) * maximumRatio);

            return Math.Min(
                Math.Max(minimumTextHeight, typicalBay * ratio),
                maximumTextHeight);
        }

        private static AnnotationMetrics CreateMetrics(double textHeight, double typicalBay)
        {
            double bubbleRadius = textHeight * 1.25;
            double innerDimensionOffset = Math.Max(
                bubbleRadius * 2.2,
                typicalBay * Math.Max(0, GridAxisSetting.InnerDimensionOffsetToBayRatio));
            double dimensionLineGap = Math.Max(
                textHeight * 2,
                typicalBay * Math.Max(0, GridAxisSetting.DimensionLineGapToBayRatio));
            double bubbleGap = Math.Max(
                bubbleRadius * 2,
                typicalBay * Math.Max(0, GridAxisSetting.BubbleGapToBayRatio));
            double axisToDimensionGap = Math.Max(
                textHeight * 0.75,
                typicalBay * Math.Max(0, GridAxisSetting.AxisToDimensionGapToBayRatio));

            return new AnnotationMetrics
            {
                TextHeight = textHeight,
                ArrowSize = textHeight * 0.8,
                ExtensionLineOffset = axisToDimensionGap,
                ExtensionBeyondDimension = textHeight * 0.5,
                BubbleRadius = bubbleRadius,
                InnerDimensionOffset = innerDimensionOffset,
                OuterDimensionOffset = innerDimensionOffset + dimensionLineGap,
                BubbleOffset = innerDimensionOffset + dimensionLineGap + bubbleGap
            };
        }

        private static ObjectId EnsureLayer(
            Database database,
            Transaction transaction,
            string layerName,
            short colorIndex,
            ObjectId linetypeId = default)
        {
            LayerTable layerTable = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
            if (layerTable.Has(layerName))
            {
                ObjectId existingId = layerTable[layerName];
                var existingLayer = (LayerTableRecord)transaction.GetObject(existingId, OpenMode.ForWrite);
                existingLayer.Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex);
                if (!linetypeId.IsNull)
                    existingLayer.LinetypeObjectId = linetypeId;
                return existingId;
            }

            layerTable.UpgradeOpen();
            var layer = new LayerTableRecord
            {
                Name = layerName,
                Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex)
            };
            if (!linetypeId.IsNull)
                layer.LinetypeObjectId = linetypeId;

            ObjectId id = layerTable.Add(layer);
            transaction.AddNewlyCreatedDBObject(layer, true);
            return id;
        }

        private static ObjectId EnsureAxisLinetype(Database database, Transaction transaction)
        {
            var linetypeTable = (LinetypeTable)transaction.GetObject(
                database.LinetypeTableId, OpenMode.ForRead);
            if (linetypeTable.Has(AxisLinetypeName))
                return linetypeTable[AxisLinetypeName];

            linetypeTable.UpgradeOpen();
            var linetype = new LinetypeTableRecord
            {
                Name = AxisLinetypeName,
                AsciiDescription = "ZXW dashed axis",
                PatternLength = 6.0,
                NumDashes = 2
            };
            linetype.SetDashLengthAt(0, 4.0);
            linetype.SetDashLengthAt(1, -2.0);

            ObjectId id = linetypeTable.Add(linetype);
            transaction.AddNewlyCreatedDBObject(linetype, true);
            return id;
        }


        /*private static ObjectId GetAxisLinetype(Database database, Transaction transaction)
        {
            var linetypeTable = (LinetypeTable)transaction.GetObject(
                database.LinetypeTableId, OpenMode.ForRead);

            // VinaCAD đã cung cấp sẵn linetype DASH, không tạo LinetypeTableRecord mới.
            if (!linetypeTable.Has(AxisLinetypeName))
                throw new InvalidOperationException("Không tìm thấy linetype DASH trong bản vẽ VinaCAD.");

            return linetypeTable[AxisLinetypeName];
        }*/

        private static void AddBubble(
            BlockTableRecord owner,
            Transaction transaction,
            Point3d center,
            string label,
            AnnotationMetrics metrics,
            Vector3d normal,
            double rotation,
            ObjectId layerId,
            ObjectId textStyleId)
        {
            var circle = new Circle(center, normal, metrics.BubbleRadius)
            {
                LayerId = layerId
            };
            Append(owner, transaction, circle);

            var text = new MText
            {
                Location = center,
                Contents = label,
                TextHeight = metrics.TextHeight,
                Attachment = AttachmentPoint.MiddleCenter,
                Rotation = rotation,
                Normal = normal,
                LayerId = layerId,
                TextStyleId = textStyleId
            };
            Append(owner, transaction, text);
        }

        private static int AddHorizontalDimensions(
            BlockTableRecord owner,
            Transaction transaction,
            Database database,
            IReadOnlyList<double> stations,
            double minY,
            double maxY,
            AnnotationMetrics metrics,
            Func<double, double, Point3d> pointAt,
            double rotation,
            ObjectId layerId)
        {
            int count = 0;
            for (int i = 0; i < stations.Count - 1; i++)
            {
                count += AddDimension(owner, transaction, database,
                    pointAt(stations[i], minY), pointAt(stations[i + 1], minY),
                    pointAt(stations[i], minY - metrics.InnerDimensionOffset), metrics, rotation, layerId);
                count += AddDimension(owner, transaction, database,
                    pointAt(stations[i], maxY), pointAt(stations[i + 1], maxY),
                    pointAt(stations[i], maxY + metrics.InnerDimensionOffset), metrics, rotation, layerId);
            }

            count += AddDimension(owner, transaction, database,
                pointAt(stations[0], minY), pointAt(stations[^1], minY),
                pointAt(stations[0], minY - metrics.OuterDimensionOffset), metrics, rotation, layerId);
            count += AddDimension(owner, transaction, database,
                pointAt(stations[0], maxY), pointAt(stations[^1], maxY),
                pointAt(stations[0], maxY + metrics.OuterDimensionOffset), metrics, rotation, layerId);
            return count;
        }

        private static int AddVerticalDimensions(
            BlockTableRecord owner,
            Transaction transaction,
            Database database,
            IReadOnlyList<double> stations,
            double minX,
            double maxX,
            AnnotationMetrics metrics,
            Func<double, double, Point3d> pointAt,
            double rotation,
            ObjectId layerId)
        {
            int count = 0;
            for (int i = 0; i < stations.Count - 1; i++)
            {
                count += AddDimension(owner, transaction, database,
                    pointAt(minX, stations[i]), pointAt(minX, stations[i + 1]),
                    pointAt(minX - metrics.InnerDimensionOffset, stations[i]), metrics, rotation, layerId);
                count += AddDimension(owner, transaction, database,
                    pointAt(maxX, stations[i]), pointAt(maxX, stations[i + 1]),
                    pointAt(maxX + metrics.InnerDimensionOffset, stations[i]), metrics, rotation, layerId);
            }

            count += AddDimension(owner, transaction, database,
                pointAt(minX, stations[0]), pointAt(minX, stations[^1]),
                pointAt(minX - metrics.OuterDimensionOffset, stations[0]), metrics, rotation, layerId);
            count += AddDimension(owner, transaction, database,
                pointAt(maxX, stations[0]), pointAt(maxX, stations[^1]),
                pointAt(maxX + metrics.OuterDimensionOffset, stations[0]), metrics, rotation, layerId);
            return count;
        }

        private static int AddDimension(
            BlockTableRecord owner,
            Transaction transaction,
            Database database,
            Point3d first,
            Point3d second,
            Point3d dimensionLinePoint,
            AnnotationMetrics metrics,
            double rotation,
            ObjectId layerId)
        {
            var dimension = new RotatedDimension(
                rotation, first, second, dimensionLinePoint, "<>", database.Dimstyle)
            {
                LayerId = layerId,
                Dimscale = 1,
                Dimtxt = metrics.TextHeight,
                Dimasz = metrics.ArrowSize,
                Dimdec = 0,
                Dimexo = metrics.ExtensionLineOffset,
                Dimexe = metrics.ExtensionBeyondDimension
            };
            Append(owner, transaction, dimension);
            return 1;
        }

        private static void Append(
            BlockTableRecord owner,
            Transaction transaction,
            Entity entity)
        {
            owner.AppendEntity(entity);
            transaction.AddNewlyCreatedDBObject(entity, true);
        }

    }
}
