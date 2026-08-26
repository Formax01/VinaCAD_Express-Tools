using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Teigha.DatabaseServices;
using Tools.Model;
using Tools.Resources.Definitions;

namespace Tools.VinaCad.Helper.Helper
{
    public sealed class EntityPropertyExportResult
    {
        private int _errorCount;

        public int EntityCount { get; internal set; }
        public long EntityPropertyCount { get; internal set; }
        public long PropertyCount { get; internal set; }
        public int ErrorCount => _errorCount;

        internal void AddError(
            string scope,
            string layerName,
            string entityName,
            string id,
            string propertyName,
            Exception exception)
        {
            _errorCount++;
        }
    }

    public sealed class EntityPropertyCsvExporter
    {
        private const string UnreadableValue = "<Unreadable>";

        private static readonly string[] PropertiesPaletteOrder =
        {
            "Color",
            "Layer",
            "Linetype",
            "LinetypeScale",
            "PlotStyleName",
            "LineWeight",
            "Transparency",
            "Hyperlinks",
            "Thickness",
        };

        private static readonly IEntityPropertyProvider DefaultEntityPropertyProvider = new DxfEntityPropertyProvider(Array.Empty<string>());

        private static readonly IReadOnlyList<IEntityPropertyProvider> EntityPropertyProviders =
            new IEntityPropertyProvider[]
            {
                new DxfEntityPropertyProvider(new[] { "LINE" }, "StartPoint", "EndPoint", "Delta", "Length", "Angle"),
                new DxfEntityPropertyProvider(new[] { "INSERT" }, "Name", "Position", "ScaleFactors", "Rotation", "BlockUnit", "UnitFactor", "Annotative"),
                new DxfEntityPropertyProvider(new[] { "LWPOLYLINE", "POLYLINE" }, "Closed", "ConstantWidth", "Elevation", "Area", "Length", "Plinegen", "NumberOfVertices", "Annotative"),
                new DxfEntityPropertyProvider(new[] { "LEADER" }, "FirstVertex", "LastVertex", "NumVertices", "IsSplined", "HasArrowHead", "DimensionStyleName", "AnnoType", "Annotation", "AnnotationOffset", "Dimasz", "Dimgap", "Dimtxt", "Annotative"),
                new DxfEntityPropertyProvider(new[] { "TEXT" }, "TextString", "TextStyleName", "Position", "AlignmentPoint", "Height", "Rotation", "WidthFactor", "Oblique", "Justify", "HorizontalMode", "VerticalMode", "IsMirroredInX", "IsMirroredInY", "Annotative"),
                new DxfEntityPropertyProvider(new[] { "MTEXT" }, "Contents", "TextStyleName", "Location", "TextHeight", "Rotation", "Attachment", "Width", "Height", "ActualWidth", "ActualHeight", "LineSpacingStyle", "LineSpacingFactor", "ColumnType", "ColumnCount", "ColumnWidth", "ColumnGutterWidth", "BackgroundFill", "BackgroundFillColor", "BackgroundTransparency", "Annotative"),
                new DxfEntityPropertyProvider(new[] { "HATCH" }, "PatternName", "PatternType", "PatternScale", "PatternAngle", "PatternDouble", "Associative", "HatchStyle", "Origin", "Elevation", "Area", "BackgroundColor", "GradientName", "GradientAngle", "GradientOneColorMode", "GradientShift", "Annotative"),
                new DxfEntityPropertyProvider(new[] { "CIRCLE" }, "Center", "Radius", "Diameter", "Circumference", "Area", "Annotative"),
                new DxfEntityPropertyProvider(new[] { "ARC" }, "Center", "Radius", "StartAngle", "EndAngle", "TotalAngle", "Length", "Area", "Annotative"),
                new DxfEntityPropertyProvider(new[] { "SPLINE" }, "Degree", "NumControlPoints", "NumFitPoints", "FitTolerance", "StartFitTangent", "EndFitTangent", "StartPoint", "EndPoint", "Closed", "Area", "IsRational", "Annotative"),
                new DxfEntityPropertyProvider(new[] { "IMAGE" }, "Name", "Path", "Position", "Scale", "Rotation", "Width", "Height", "Brightness", "Contrast", "Fade", "ShowImage", "ImageTransparency", "IsClipped", "ClipBoundaryType", "Annotative"),
                new DxfEntityPropertyProvider(new[] { "DIMENSION" }, "DimensionStyleName", "DimensionText", "Measurement", "TextPosition", "TextRotation", "HorizontalRotation", "Rotation", "Oblique", "XLine1Point", "XLine2Point", "DimLinePoint", "ArcPoint", "Elevation", "Prefix", "Suffix", "AlternatePrefix", "AlternateSuffix", "SuppressLeadingZeros", "SuppressTrailingZeros", "ToleranceSuppressLeadingZeros", "ToleranceSuppressTrailingZeros", "CenterMarkType", "CenterMarkSize", "Annotative"),
                new DxfEntityPropertyProvider(new[] { "POINT" }, "Position", "Annotative"),
                new DxfEntityPropertyProvider(new[] { "ELLIPSE" }, "Center", "MajorAxis", "MinorAxis", "RadiusRatio", "StartAngle", "EndAngle", "Area", "Annotative"),
                new DxfEntityPropertyProvider(new[] { "RAY", "XLINE" }, "BasePoint", "UnitDir", "Annotative"),
                new DxfEntityPropertyProvider(new[] { "ACAD_TABLE" }, "Position", "Rows", "Columns", "Width", "Height", "Direction", "TableStyleName", "Annotative"),
                new DxfEntityPropertyProvider(new[] { "VIEWPORT" }, "CenterPoint", "Width", "Height", "ViewCenter", "ViewHeight", "CustomScale", "TwistAngle", "Locked", "ShadePlot", "Annotative"),
                new DxfEntityPropertyProvider(new[] { "ATTDEF", "ATTRIB" }, "Tag", "Prompt", "TextString", "Position", "AlignmentPoint", "Height", "Rotation", "TextStyleName", "Invisible", "Constant", "Verifiable", "Preset", "LockPositionInBlock", "Annotative"),
            };

        public int CountEntities(Database database)
        {
            ArgumentNullException.ThrowIfNull(database);

            using Transaction transaction = database.TransactionManager.StartOpenCloseTransaction();
            BlockTable blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
            EntityPropertyExportResult result = new();
            return GetSortedEntities(transaction, blockTable, result).Count;
        }

        public EntityPropertyExportResult Export(
            Database database,
            string filePath,
            Action<EntityPropertyExportProgress>? progressCallback = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(database);
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

            EntityPropertyExportResult result = new();

            ReportProgress(progressCallback, "Scanning entities...", string.Empty, 0, 0, 0);
            cancellationToken.ThrowIfCancellationRequested();

            using Transaction transaction = database.TransactionManager.StartOpenCloseTransaction();
            BlockTable blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
            List<EntityIdentity> exportObjects = GetSortedExportObjects(transaction, blockTable, result, progressCallback, cancellationToken);
            ReportProgress(progressCallback, "Preparing CSV file...", string.Empty, 0, exportObjects.Count, 0);

            string fullFilePath = Path.GetFullPath(filePath);
            bool isReplacingExistingFile = File.Exists(fullFilePath);
            string workingFilePath = isReplacingExistingFile ? CreateTemporaryFilePath(fullFilePath) : fullFilePath;

            try
            {
                using (StreamWriter writer = new(workingFilePath,false,new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
                {
                    if (isReplacingExistingFile)
                    {
                        TryHideFile(workingFilePath);
                    }

                    WriteCsvRow(writer, StringDefinition.EXCSV_HEADER_LAYER_NAME, StringDefinition.EXCSV_HEADER_ENTITY, StringDefinition.EXCSV_HEADER_ID, StringDefinition.EXCSV_HEADER_PROPERTY_NAME, StringDefinition.EXCSV_HEADER_PROPERTY_VALUE);
                    writer.Flush();

                    long propertyCountAtLastFlush = 0;
                    int processedObjectCount = 0;
                    Stopwatch progressStopwatch = Stopwatch.StartNew();
                    foreach (EntityIdentity identity in exportObjects)
                    {
                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            if (!identity.IsReadable)
                            {
                                continue;
                            }

                            Entity? entity;
                            try
                            {
                                entity = transaction.GetObject(identity.ObjectId, OpenMode.ForRead, false) as Entity;
                                if (entity is null)
                                {
                                    throw new InvalidOperationException("The object is not an entity.");
                                }
                            }
                            catch (Exception ex)
                            {
                                result.AddError("Entity", identity.LayerName, identity.EntityName, identity.Handle, string.Empty, ex);
                                continue;
                            }

                            WriteEntityProperties(writer, transaction, entity, identity, result);

                            FlushIfNeeded(writer, result, ref propertyCountAtLastFlush);
                        }
                        finally
                        {
                            processedObjectCount++;
                            if (progressStopwatch.ElapsedMilliseconds >= 100 ||
                                processedObjectCount == exportObjects.Count)
                            {
                                ReportProgress(progressCallback, "Exporting CSV...", $"{identity.LayerName} — {identity.EntityName} ({identity.Handle})", processedObjectCount, exportObjects.Count, result.PropertyCount);
                                progressStopwatch.Restart();
                            }
                        }
                    }
                    writer.Flush();
                }

                ReportProgress(progressCallback, "Finalizing CSV file...", string.Empty, exportObjects.Count, exportObjects.Count, result.PropertyCount);
                cancellationToken.ThrowIfCancellationRequested();

                if (isReplacingExistingFile)
                {
                    CommitTemporaryFile(workingFilePath, fullFilePath);
                }
                workingFilePath = string.Empty;

                ReportProgress(progressCallback, "Completed.", string.Empty, exportObjects.Count, exportObjects.Count, result.PropertyCount);
            }
            catch (OperationCanceledException)
            {
                if (!string.IsNullOrEmpty(workingFilePath) && File.Exists(workingFilePath))
                {
                    TryDeleteFile(workingFilePath);
                }
                workingFilePath = string.Empty;
                throw;
            }
            catch (Exception ex)
            {
                string? partialFilePath = PreservePartialFile(workingFilePath, fullFilePath);
                if (partialFilePath is not null)
                {
                    workingFilePath = string.Empty;
                }
                string partialMessage = partialFilePath is null
                    ? string.Empty
                    : $" Partial data was saved to: {partialFilePath}";
                throw new IOException($"The CSV file could not be completed.{partialMessage}", ex);
            }
            finally
            {
                if (!string.IsNullOrEmpty(workingFilePath) && File.Exists(workingFilePath))
                {
                    TryDeleteFile(workingFilePath);
                }
            }

            return result;
        }

        private static List<EntityIdentity> GetSortedExportObjects(
            Transaction transaction,
            BlockTable blockTable,
            EntityPropertyExportResult result,
            Action<EntityPropertyExportProgress>? progressCallback = null,
            CancellationToken cancellationToken = default)
        {
            List<EntityIdentity> exportObjects = GetSortedEntities(transaction, blockTable, result, progressCallback, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            exportObjects.Sort(EntityIdentityComparer.Instance);
            return exportObjects;
        }

        private static List<EntityIdentity> GetSortedEntities(
            Transaction transaction,
            BlockTable blockTable,
            EntityPropertyExportResult result,
            Action<EntityPropertyExportProgress>? progressCallback = null,
            CancellationToken cancellationToken = default)
        {
            List<EntityIdentity> entities = new();
            Stopwatch scanProgressStopwatch = Stopwatch.StartNew();

            foreach (ObjectId blockTableRecordId in blockTable)
            {
                cancellationToken.ThrowIfCancellationRequested();

                BlockTableRecord blockTableRecord;
                try
                {
                    blockTableRecord = (BlockTableRecord)transaction.GetObject(blockTableRecordId, OpenMode.ForRead, false);
                }
                catch (Exception ex)
                {
                    result.AddError("BlockTableRecord", string.Empty, string.Empty, TryGetHandle(blockTableRecordId), string.Empty, ex);
                    continue;
                }

                // Only layout-owned entities are visible drawing objects. Block definition
                // contents are represented by their BlockReference and must not be exported
                // as additional hidden entities.
                if (!blockTableRecord.IsLayout)
                {
                    continue;
                }

                foreach (ObjectId objectId in blockTableRecord)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (scanProgressStopwatch.ElapsedMilliseconds >= 100)
                    {
                        ReportProgress(progressCallback, "Scanning entities...", string.Empty, 0, 0, 0);
                        scanProgressStopwatch.Restart();
                    }

                    if (!objectId.IsValid || objectId.IsErased)
                    {
                        continue;
                    }

                    try
                    {
                        if (transaction.GetObject(objectId, OpenMode.ForRead, false) is not Entity entity)
                        {
                            continue;
                        }
                        // TryGetHandle handles conversion failures internally. If the handle cannot be read,
                        // only the ID column is left blank so the remaining properties can still be exported.
                        entities.Add(new EntityIdentity(objectId, entity.Layer ?? string.Empty, GetEntityName(entity), TryGetHandle(objectId), true));
                    }
                    catch (Exception ex)
                    {
                        string handle = TryGetHandle(objectId);
                        result.AddError("Entity", string.Empty, "<Cannot open>", handle, string.Empty, ex);
                        entities.Add(new EntityIdentity(objectId, string.Empty, "<Cannot open>", handle, false));
                    }
                }
            }

            entities.Sort(EntityIdentityComparer.Instance);
            result.EntityCount = entities.Count;
            return entities;
        }

        private static void WriteEntityProperties(
            StreamWriter writer,
            Transaction transaction,
            Entity entity,
            EntityIdentity identity,
            EntityPropertyExportResult result)
        {
            List<ExportProperty> exportedProperties;
            try
            {
                exportedProperties = GetEntityProperties(transaction, entity, identity, result);
            }
            catch (Exception ex)
            {
                result.AddError("EntityProperties", identity.LayerName, identity.EntityName, identity.Handle, string.Empty, ex);
                return;
            }

            foreach (ExportProperty property in exportedProperties)
            {
                WriteProperty(writer, identity, property.Name, property.Value);
                result.EntityPropertyCount++;
                result.PropertyCount++;
            }
        }

        private static List<ExportProperty> GetEntityProperties(
            Transaction transaction,
            Entity entity,
            EntityIdentity identity,
            EntityPropertyExportResult result)
        {
            List<ExportProperty> exportedProperties = new();
            IEntityPropertyProvider propertyProvider = ResolveEntityPropertyProvider(entity);
            AddDisplayProperties(transaction, entity, identity, propertyProvider.PropertyOrder, exportedProperties, result);

            if (entity is BlockReference blockReference)
            {
                AddBlockAttributes(transaction, blockReference, identity, exportedProperties, result);
                AddDynamicBlockProperties(blockReference, identity, exportedProperties, result);
            }

            return exportedProperties;
        }

        private static void AddDisplayProperties(
            Transaction transaction,
            object source,
            EntityIdentity identity,
            IReadOnlyList<string> preferredOrder,
            ICollection<ExportProperty> exportedProperties,
            EntityPropertyExportResult result)
        {
            PropertyDescriptorCollection descriptors;
            try
            {
                descriptors = TypeDescriptor.GetProperties(source, new Attribute[] { BrowsableAttribute.Yes });
            }
            catch (Exception ex)
            {
                result.AddError("PropertyList", identity.LayerName, identity.EntityName, identity.Handle, string.Empty, ex);
                return;
            }

            foreach (PropertyDescriptor descriptor in OrderPropertyDescriptors(descriptors, preferredOrder))
            {
                if (!descriptor.IsBrowsable)
                {
                    continue;
                }

                if (source is Spline spline && IsSplineFitTangent(descriptor.Name) && !HasSplineFitData(spline))
                {
                    continue;
                }

                try
                {
                    object? value = source is Dimension dimension &&
                        string.Equals(descriptor.Name, "DimensionStyleName", StringComparison.OrdinalIgnoreCase)
                            ? GetDimensionStyleName(transaction, dimension)
                            : descriptor.GetValue(source);
                    exportedProperties.Add(new ExportProperty(descriptor.DisplayName, FormatDisplayValue(descriptor, value)));
                }
                catch (Exception ex)
                {
                    if (source is Hatch && string.Equals(descriptor.Name, "Area", StringComparison.OrdinalIgnoreCase))
                    {
                        exportedProperties.Add(new ExportProperty(descriptor.DisplayName, string.Empty));
                        continue;
                    }

                    result.AddError("Property", identity.LayerName, identity.EntityName, identity.Handle, descriptor.DisplayName, ex);
                }
            }

        }

        private static IEnumerable<PropertyDescriptor> OrderPropertyDescriptors(
            PropertyDescriptorCollection descriptors,
            IReadOnlyList<string> preferredOrder)
        {
            HashSet<string> emittedNames = new(StringComparer.OrdinalIgnoreCase);

            foreach (string propertyName in preferredOrder)
            {
                PropertyDescriptor? descriptor = descriptors.Find(propertyName, ignoreCase: true);
                if (descriptor is not null && emittedNames.Add(descriptor.Name))
                {
                    yield return descriptor;
                }
            }

        }

        private static IEntityPropertyProvider ResolveEntityPropertyProvider(Entity entity)
        {
            string dxfName = TryGetDxfName(entity);
            return EntityPropertyProviders.FirstOrDefault(provider => provider.CanHandle(dxfName)) ?? DefaultEntityPropertyProvider;
        }

        private static string[] CreatePropertyOrder(IEnumerable<string> entityPropertyNames)
        {
            return PropertiesPaletteOrder.Concat(entityPropertyNames).Append("Material").Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static string GetDimensionStyleName(Transaction transaction, Dimension dimension)
        {
            ObjectId dimensionStyleId = dimension.DimensionStyle;
            if (dimensionStyleId.IsNull || !dimensionStyleId.IsValid || dimensionStyleId.IsErased)
            {
                return string.Empty;
            }

            return transaction.GetObject(dimensionStyleId, OpenMode.ForRead, false) is DimStyleTableRecord dimensionStyle
                ? dimensionStyle.Name ?? string.Empty
                : string.Empty;
        }

        private static bool IsSplineFitTangent(string propertyName)
        {
            return string.Equals(propertyName, "StartFitTangent", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(propertyName, "EndFitTangent", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasSplineFitData(Spline spline)
        {
            try
            {
                return spline.HasFitData;
            }
            catch
            {
                return false;
            }
        }

        private static string FormatDisplayValue(PropertyDescriptor descriptor, object? value)
        {
            if (value is null)
            {
                return string.Empty;
            }

            try
            {
                TypeConverter converter = descriptor.Converter;
                if (converter.CanConvertTo(typeof(string)))
                {
                    string? convertedValue = converter.ConvertToString(context: null, CultureInfo.CurrentCulture, value);
                    if (convertedValue is not null)
                    {
                        return convertedValue;
                    }
                }
            }
            catch
            {
                // Fall back to the CAD-specific invariant formatter below.
            }

            return FormatValue(value);
        }

        private static void AddBlockAttributes(
            Transaction transaction,
            BlockReference blockReference,
            EntityIdentity identity,
            ICollection<ExportProperty> exportedProperties,
            EntityPropertyExportResult result)
        {
            int unnamedAttributeIndex = 0;

            foreach (ObjectId attributeId in blockReference.AttributeCollection)
            {
                try
                {
                    if (transaction.GetObject(attributeId, OpenMode.ForRead, false) is not AttributeReference attribute)
                    {
                        continue;
                    }

                    if (attribute.Invisible)
                    {
                        continue;
                    }

                    string tag = string.IsNullOrWhiteSpace(attribute.Tag)
                        ? (++unnamedAttributeIndex).ToString(CultureInfo.InvariantCulture)
                        : attribute.Tag;

                    exportedProperties.Add(new ExportProperty($"Attribute.{tag}", attribute.TextString ?? string.Empty));
                }
                catch (Exception ex)
                {
                    result.AddError("BlockAttribute", identity.LayerName, identity.EntityName, identity.Handle, TryGetHandle(attributeId), ex);
                }
            }
        }

        private static void AddDynamicBlockProperties(
            BlockReference blockReference,
            EntityIdentity identity,
            ICollection<ExportProperty> exportedProperties,
            EntityPropertyExportResult result)
        {
            try
            {
                if (!blockReference.IsDynamicBlock)
                {
                    return;
                }

                foreach (DynamicBlockReferenceProperty property in blockReference.DynamicBlockReferencePropertyCollection)
                {
                    try
                    {
                        if (!property.Show || !property.VisibleInCurrentVisibilityState)
                        {
                            continue;
                        }

                        string propertyName = string.IsNullOrWhiteSpace(property.PropertyName)
                            ? "Unnamed"
                            : property.PropertyName;

                        exportedProperties.Add(new ExportProperty($"Dynamic.{propertyName}", FormatValue(property.Value)));
                    }
                    catch (Exception ex)
                    {
                        result.AddError("DynamicProperty", identity.LayerName, identity.EntityName, identity.Handle, string.Empty, ex);
                    }
                }
            }
            catch (Exception ex)
            {
                result.AddError("DynamicPropertyList", identity.LayerName, identity.EntityName, identity.Handle, string.Empty, ex);
            }
        }

        private static void WriteProperty(
            StreamWriter writer,
            EntityIdentity identity,
            string propertyName,
            string propertyValue)
        {
            WriteCsvRow(writer, identity.LayerName, identity.EntityName, identity.Handle, propertyName, propertyValue);
        }

        private static void WriteCsvRow(StreamWriter writer, params string[] values)
        {
            writer.WriteLine(string.Join(',', values.Select(EscapeCsv)));
        }

        private static void FlushIfNeeded(
            StreamWriter writer,
            EntityPropertyExportResult result,
            ref long propertyCountAtLastFlush)
        {
            if (result.PropertyCount - propertyCountAtLastFlush < 500)
            {
                return;
            }

            writer.Flush();
            propertyCountAtLastFlush = result.PropertyCount;
        }

        private static void ReportProgress(
            Action<EntityPropertyExportProgress>? progressCallback,
            string stage,
            string currentObject,
            int processedObjectCount,
            int totalObjectCount,
            long propertyCount)
        {
            progressCallback?.Invoke(new EntityPropertyExportProgress
            {
                Stage = stage,
                CurrentObject = currentObject,
                ProcessedObjectCount = processedObjectCount,
                TotalObjectCount = totalObjectCount,
                PropertyCount = propertyCount,
            });
        }

        private static string EscapeCsv(string? value)
        {
            string safeValue = value ?? string.Empty;
            if (!safeValue.Contains(',') && !safeValue.Contains('"') &&
                !safeValue.Contains('\r') && !safeValue.Contains('\n'))
            {
                return safeValue;
            }

            return $"\"{safeValue.Replace("\"", "\"\"")}\"";
        }

        private static string FormatValue(object? value)
        {
            return FormatValue(value, new HashSet<object>(ReferenceEqualityComparer.Instance));
        }

        private static string FormatValue(object? value, ISet<object> visitedCollections)
        {
            if (value is null)
            {
                return string.Empty;
            }

            if (value is string text)
            {
                return text;
            }

            if (value is ObjectId objectId)
            {
                return TryGetHandle(objectId);
            }

            Type valueType = value.GetType();
            if (valueType.Namespace?.StartsWith("Teigha.Colors", StringComparison.Ordinal) == true)
            {
                string? color = TryFormatColor(value, valueType);
                if (color is not null)
                {
                    return color;
                }
            }

            if (valueType.Namespace?.StartsWith("Teigha.Geometry", StringComparison.Ordinal) == true)
            {
                string? coordinates = TryFormatCoordinates(value, valueType);
                if (coordinates is not null)
                {
                    return coordinates;
                }
            }

            if (value is IFormattable formattable)
            {
                return formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty;
            }

            if (value is IEnumerable enumerable)
            {
                return FormatEnumerable(enumerable, visitedCollections);
            }

            return value.ToString() ?? string.Empty;
        }

        private static string FormatEnumerable(
            IEnumerable enumerable,
            ISet<object> visitedCollections)
        {
            if (!visitedCollections.Add(enumerable))
            {
                return "<Circular reference>";
            }

            List<string> items = new();
            IEnumerator enumerator;
            try
            {
                enumerator = enumerable.GetEnumerator();
            }
            catch
            {
                visitedCollections.Remove(enumerable);
                return UnreadableValue;
            }

            try
            {
                while (true)
                {
                    bool hasNext;
                    try
                    {
                        hasNext = enumerator.MoveNext();
                    }
                    catch
                    {
                        items.Add(UnreadableValue);
                        break;
                    }

                    if (!hasNext)
                    {
                        break;
                    }

                    try
                    {
                        items.Add(FormatValue(enumerator.Current, visitedCollections));
                    }
                    catch
                    {
                        items.Add(UnreadableValue);
                    }
                }
            }
            finally
            {
                visitedCollections.Remove(enumerable);
                (enumerator as IDisposable)?.Dispose();
            }

            return string.Join("; ", items);
        }

        private static string? TryFormatColor(object value, Type valueType)
        {
            PropertyInfo? methodProperty = valueType.GetProperty("ColorMethod", BindingFlags.Instance | BindingFlags.Public);
            string? colorMethod = methodProperty?.GetValue(value)?.ToString();

            if (string.Equals(colorMethod, "ByLayer", StringComparison.OrdinalIgnoreCase))
            {
                return "ByLayer";
            }

            if (string.Equals(colorMethod, "ByBlock", StringComparison.OrdinalIgnoreCase))
            {
                return "ByBlock";
            }

            if (string.Equals(colorMethod, "ByAci", StringComparison.OrdinalIgnoreCase))
            {
                PropertyInfo? indexProperty = valueType.GetProperty("ColorIndex", BindingFlags.Instance | BindingFlags.Public);
                object? colorIndex = indexProperty?.GetValue(value);
                if (colorIndex is not null)
                {
                    return $"Color {FormatValue(colorIndex)}";
                }
            }

            return null;
        }

        private static string? TryFormatCoordinates(object value, Type valueType)
        {
            PropertyInfo? xProperty = valueType.GetProperty("X", BindingFlags.Instance | BindingFlags.Public);
            PropertyInfo? yProperty = valueType.GetProperty("Y", BindingFlags.Instance | BindingFlags.Public);
            if (xProperty is null || yProperty is null)
            {
                return null;
            }

            List<string> coordinates = new()
            {
                FormatValue(xProperty.GetValue(value)),
                FormatValue(yProperty.GetValue(value)),
            };

            PropertyInfo? zProperty = valueType.GetProperty("Z", BindingFlags.Instance | BindingFlags.Public);
            if (zProperty is not null)
            {
                coordinates.Add(FormatValue(zProperty.GetValue(value)));
            }

            return string.Join(", ", coordinates);
        }

        private static string CreateTemporaryFilePath(string targetFilePath)
        {
            string directory = Path.GetDirectoryName(targetFilePath)
                ?? throw new InvalidOperationException("The CSV output folder could not be determined.");
            return Path.Combine(directory, $".{Path.GetFileName(targetFilePath)}.{Guid.NewGuid():N}.tmp");
        }

        private static void TryHideFile(string filePath)
        {
            try
            {
                File.SetAttributes(filePath, File.GetAttributes(filePath) | FileAttributes.Hidden);
            }
            catch
            {
                // Hiding the working file is cosmetic and must not interrupt the export.
            }
        }

        private static void CommitTemporaryFile(string temporaryFilePath, string targetFilePath)
        {
            File.SetAttributes(temporaryFilePath, FileAttributes.Normal);
            File.Move(temporaryFilePath, targetFilePath, overwrite: true);
        }

        private static string? PreservePartialFile(string temporaryFilePath, string targetFilePath)
        {
            if (string.IsNullOrEmpty(temporaryFilePath) || !File.Exists(temporaryFilePath))
            {
                return null;
            }

            try
            {
                File.SetAttributes(temporaryFilePath, FileAttributes.Normal);
                string directory = Path.GetDirectoryName(targetFilePath) ?? string.Empty;
                string fileName = Path.GetFileNameWithoutExtension(targetFilePath);
                string partialFilePath = Path.Combine(directory, $"{fileName}_partial_{DateTime.Now:yyyyMMdd_HHmmssfff}.csv");
                File.Move(temporaryFilePath, partialFilePath);
                return partialFilePath;
            }
            catch
            {
                return temporaryFilePath;
            }
        }

        private static void TryDeleteFile(string filePath)
        {
            try
            {
                File.SetAttributes(filePath, FileAttributes.Normal);
                File.Delete(filePath);
            }
            catch
            {
                // Cleanup must not hide the original export failure.
            }
        }

        private static string TryGetHandle(ObjectId objectId)
        {
            try
            {
                if (objectId.IsNull)
                {
                    return string.Empty;
                }

                string hexadecimalHandle = objectId.Handle.ToString();
                return ulong.TryParse(hexadecimalHandle, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong decimalHandle)
                    ? decimalHandle.ToString(CultureInfo.InvariantCulture)
                    : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetEntityName(Entity entity)
        {
            try
            {
                string dxfName = TryGetDxfName(entity);
                if (!string.IsNullOrWhiteSpace(dxfName) && StringDefinition.EXCSV_ENTITY_DISPLAY_NAMES.TryGetValue(dxfName, out string? displayName))
                {
                    return displayName;
                }

                return entity.GetType().Name;
            }
            catch
            {
                return entity.GetType().Name;
            }
        }

        private static string TryGetDxfName(Entity entity)
        {
            try
            {
                return entity.GetRXClass().DxfName ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private interface IEntityPropertyProvider
        {
            IReadOnlyList<string> PropertyOrder { get; }

            bool CanHandle(string dxfName);
        }

        private sealed class DxfEntityPropertyProvider : IEntityPropertyProvider
        {
            private readonly HashSet<string> _dxfNames;

            public DxfEntityPropertyProvider(IEnumerable<string> dxfNames, params string[] entityPropertyNames)
            {
                _dxfNames = new HashSet<string>(dxfNames, StringComparer.OrdinalIgnoreCase);
                PropertyOrder = CreatePropertyOrder(entityPropertyNames);
            }

            public IReadOnlyList<string> PropertyOrder { get; }

            public bool CanHandle(string dxfName)
            {
                return _dxfNames.Contains(dxfName);
            }
        }

        private sealed record EntityIdentity(
            ObjectId ObjectId,
            string LayerName,
            string EntityName,
            string Handle,
            bool IsReadable);

        private sealed record ExportProperty(string Name, string Value);

        private sealed class EntityIdentityComparer : IComparer<EntityIdentity>
        {
            public static EntityIdentityComparer Instance { get; } = new();

            public int Compare(EntityIdentity? x, EntityIdentity? y)
            {
                if (ReferenceEquals(x, y))
                {
                    return 0;
                }

                if (x is null)
                {
                    return -1;
                }

                if (y is null)
                {
                    return 1;
                }

                int layerComparison = StringComparer.OrdinalIgnoreCase.Compare(x.LayerName, y.LayerName);
                if (layerComparison != 0)
                {
                    return layerComparison;
                }

                int entityComparison = StringComparer.OrdinalIgnoreCase.Compare(x.EntityName, y.EntityName);
                if (entityComparison != 0)
                {
                    return entityComparison;
                }

                return StringComparer.OrdinalIgnoreCase.Compare(x.Handle, y.Handle);
            }
        }
    }
}
