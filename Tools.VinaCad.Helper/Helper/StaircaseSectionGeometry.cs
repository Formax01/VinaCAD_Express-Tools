using PrLogTrackingSystem;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Text.Json;
using Tools.Model;
using Tools.VinaCad.Modeling;

namespace Tools.VinaCad.Helper.Helper
{
    public static class StaircaseSectionGeometry
    {
        private const double CoordinateTolerance = 1e-9;
        private const double SelfCheckTolerance = 1e-7;
        private const int SelfCheckStoreyNumber = 2;
        private const string InvalidStepHeightMessage = "Sai công thức tính chiều cao cổ bậc LTP.";
        private const string InvalidSettingsMappingMessage =
            "StaircaseSectionSetting chưa đồng bộ đầy đủ với StaircaseSectionModel.";
        private const string EmptyGeometryMessageFormat = "Kiểu cầu thang {0} không sinh hình học.";
        private const string InvalidCoordinateMessageFormat = "Kiểu cầu thang {0} sinh tọa độ không hợp lệ.";

        private delegate void SectionBuilder(
            List<StaircaseSegment> segments,
            StaircaseSectionModel settings,
            HorizontalDirection direction);

        private static readonly IReadOnlyDictionary<StaircaseSectionType, SectionBuilder> SectionBuilders =
            new Dictionary<StaircaseSectionType, SectionBuilder>
            {
                [StaircaseSectionType.DoubleFlight] = AddDoubleFlightSection,
                [StaircaseSectionType.SingleFlight] = AddSingleFlightSection,
                [StaircaseSectionType.Scissor] = AddScissorSection
            };

        private enum HorizontalDirection
        {
            Left = -1,
            Right = 1
        }

        public static IReadOnlyList<StaircaseSegment> Generate(StaircaseSectionModel settings)
        {
            // B1: Kiểm tra đầu vào trước khi sinh hình học.
            ArgumentNullException.ThrowIfNull(settings);
            if (!settings.TryValidate(out string message))
                throw new ArgumentException(message, nameof(settings));

            // B2: Chọn builder theo enum để phần điều phối không đổi khi logic từng loại được mở rộng.
            var segments = new List<StaircaseSegment>();
            HorizontalDirection direction = settings.FirstRunRightward
                ? HorizontalDirection.Right
                : HorizontalDirection.Left;
            SectionBuilders[settings.Type](segments, settings, direction);

            // B3: Trả danh sách đoạn độc lập nền tảng CAD.
            return segments;
        }

        public static void SelfCheck()
        {
            // B1: Chuẩn bị bộ dữ liệu nhỏ, không phụ thuộc framework kiểm thử.
            StaircaseSectionModel settings = StaircaseSectionSetting.CreateInitial();
            settings.StoreyNumber = SelfCheckStoreyNumber;
            EnsureStepHeightIsValid(settings);
            EnsureSettingsMappingIsValid(settings);

            // B2: Kiểm tra mọi builder sinh ít nhất một đoạn với tọa độ hữu hạn.
            foreach (StaircaseSectionType type in Enum.GetValues<StaircaseSectionType>())
            {
                settings.Type = type;
                IReadOnlyList<StaircaseSegment> segments = Generate(settings);
                EnsureSegmentsExist(type, segments);
                EnsureCoordinatesAreFinite(type, segments);
            }
        }

        private static void EnsureStepHeightIsValid(StaircaseSectionModel settings)
        {
            double calculatedHeight = settings.CurrentStepHeight * settings.StepNumber;
            if (Math.Abs(calculatedHeight - settings.StoreyHeight) > SelfCheckTolerance)
                throw new InvalidOperationException(InvalidStepHeightMessage);
        }

        private static void EnsureSettingsMappingIsValid(StaircaseSectionModel settings)
        {
            StaircaseSectionSetting.LoadFrom(settings);
            StaircaseSectionModel mappedSettings = StaircaseSectionSetting.ToModel();
            string source = JsonSerializer.Serialize(settings);
            string mapped = JsonSerializer.Serialize(mappedSettings);
            if (!string.Equals(source, mapped, StringComparison.Ordinal))
                throw new InvalidOperationException(InvalidSettingsMappingMessage);
        }

        private static void EnsureSegmentsExist(
            StaircaseSectionType type,
            IReadOnlyList<StaircaseSegment> segments)
        {
            if (segments.Count == 0)
                throw new InvalidOperationException(string.Format(EmptyGeometryMessageFormat, type));
        }

        private static void EnsureCoordinatesAreFinite(
            StaircaseSectionType type,
            IReadOnlyList<StaircaseSegment> segments)
        {
            foreach (StaircaseSegment segment in segments)
            {
                bool isFinite = double.IsFinite(segment.Start.X)
                    && double.IsFinite(segment.Start.Y)
                    && double.IsFinite(segment.End.X)
                    && double.IsFinite(segment.End.Y);
                if (!isFinite)
                    throw new InvalidOperationException(string.Format(InvalidCoordinateMessageFormat, type));
            }
        }

        private static void AddSingleFlightSection(
            List<StaircaseSegment> segments,
            StaircaseSectionModel settings,
            HorizontalDirection direction)
        {
            double runLength = (settings.StepNumber - 1) * settings.TreadRun;
            int directionFactor = (int)direction;

            foreach (int floor in GetFloorIndexes(settings.StoreyNumber))
            {
                var start = new StaircasePoint(0.0, floor * settings.StoreyHeight);
                var top = new StaircasePoint(
                    directionFactor * runLength,
                    start.Y + settings.StoreyHeight);

                AddLanding(segments, start, Reverse(direction), settings.Landing1Width, settings.BoardThickness);
                AddFlight(segments, start, direction, settings.StepNumber, settings);
                AddLanding(segments, top, direction, settings.Landing2Width, settings.BoardThickness);
                AddSupports(segments, settings, start, top, direction);
            }
        }

        private static void AddDoubleFlightSection(
            List<StaircaseSegment> segments,
            StaircaseSectionModel settings,
            HorizontalDirection direction)
        {
            int secondFlightSteps = settings.StepNumber - settings.FirstFlightStepNumber;
            double anchorX = 0.0;
            int directionFactor = (int)direction;

            foreach (int floor in GetFloorIndexes(settings.StoreyNumber))
            {
                var start = new StaircasePoint(anchorX, floor * settings.StoreyHeight);
                var middle = new StaircasePoint(
                    anchorX + directionFactor * (settings.FirstFlightStepNumber - 1) * settings.TreadRun,
                    start.Y + settings.FirstFlightStepNumber * settings.CurrentStepHeight);
                var top = new StaircasePoint(
                    middle.X - directionFactor * (secondFlightSteps - 1) * settings.TreadRun,
                    start.Y + settings.StoreyHeight);

                HorizontalDirection reverse = Reverse(direction);
                AddLanding(segments, start, reverse, settings.Landing1Width, settings.BoardThickness);
                AddFlight(segments, start, direction, settings.FirstFlightStepNumber, settings);
                AddLanding(segments, middle, direction, settings.Landing2Width, settings.BoardThickness);
                AddFlight(segments, middle, reverse, secondFlightSteps, settings);
                AddLanding(segments, top, reverse, settings.Landing1Width, settings.BoardThickness);
                AddSupports(segments, settings, start, middle, direction);
                AddSupports(segments, settings, top, middle, reverse);
                anchorX = top.X;
            }
        }

        private static void AddScissorSection(
            List<StaircaseSegment> segments,
            StaircaseSectionModel settings,
            HorizontalDirection direction)
        {
            int firstSteps = settings.FirstFlightStepNumber;
            int secondSteps = settings.StepNumber - firstSteps;
            double firstSpan = (firstSteps - 1) * settings.TreadRun;
            int directionFactor = (int)direction;

            foreach (int floor in GetFloorIndexes(settings.StoreyNumber))
            {
                double baseY = floor * settings.StoreyHeight;
                double middleY = baseY + firstSteps * settings.CurrentStepHeight;
                double rightX = directionFactor * firstSpan;
                var lowerLeft = new StaircasePoint(0.0, baseY);
                var lowerRight = new StaircasePoint(rightX, baseY);
                var middleLeft = new StaircasePoint(0.0, middleY);
                var middleRight = new StaircasePoint(rightX, middleY);
                double secondSpan = (secondSteps - 1) * settings.TreadRun;
                double topY = baseY + settings.StoreyHeight;
                var topRight = new StaircasePoint(directionFactor * secondSpan, topY);
                var topLeft = new StaircasePoint(rightX - directionFactor * secondSpan, topY);

                AddScissorLowerFlights(segments, settings, lowerLeft, lowerRight, direction);
                AddScissorUpperFlights(segments, settings, middleLeft, middleRight, direction);
                AddScissorTopLandings(segments, settings, topLeft, topRight, direction);
                AddSupports(segments, settings, lowerLeft, middleRight, direction);
                AddSupports(segments, settings, middleLeft,
                    new StaircasePoint(rightX, baseY + settings.StoreyHeight), direction);
            }
        }

        private static void AddScissorLowerFlights(
            List<StaircaseSegment> segments,
            StaircaseSectionModel settings,
            StaircasePoint left,
            StaircasePoint right,
            HorizontalDirection direction)
        {
            AddLanding(segments, left, Reverse(direction), settings.Landing1Width, settings.BoardThickness);
            AddLanding(segments, right, direction, settings.Landing2Width, settings.BoardThickness);
            AddFlight(segments, left, direction, settings.FirstFlightStepNumber, settings);
            AddFlight(segments, right, Reverse(direction), settings.FirstFlightStepNumber, settings);
        }

        private static void AddScissorUpperFlights(
            List<StaircaseSegment> segments,
            StaircaseSectionModel settings,
            StaircasePoint left,
            StaircasePoint right,
            HorizontalDirection direction)
        {
            int secondFlightSteps = settings.StepNumber - settings.FirstFlightStepNumber;
            AddLanding(segments, left, Reverse(direction), settings.Landing1Width, settings.BoardThickness);
            AddLanding(segments, right, direction, settings.Landing2Width, settings.BoardThickness);
            AddFlight(segments, left, direction, secondFlightSteps, settings);
            AddFlight(segments, right, Reverse(direction), secondFlightSteps, settings);
        }

        private static void AddScissorTopLandings(
            List<StaircaseSegment> segments,
            StaircaseSectionModel settings,
            StaircasePoint leftLanding,
            StaircasePoint rightLanding,
            HorizontalDirection direction)
        {
            AddLanding(segments, rightLanding, direction, settings.Landing2Width, settings.BoardThickness);
            AddLanding(segments, leftLanding, Reverse(direction), settings.Landing1Width, settings.BoardThickness);
        }

        private static IEnumerable<int> GetFloorIndexes(int storeyNumber)
        {
            for (int floor = 0; floor < storeyNumber; floor++) yield return floor;
        }

        private static void AddFlight(
            List<StaircaseSegment> segments,
            StaircasePoint start,
            HorizontalDirection direction,
            int riserCount,
            StaircaseSectionModel settings)
        {
            AddSteps(segments, start, direction, riserCount, settings);
            StaircasePoint end = GetFlightEnd(start, direction, riserCount, settings);
            AddFlightBoard(segments, start, end, settings.BoardThickness);
            AddRailing(segments, start, end, settings.RailingHeight);
        }

        private static void AddSteps(
            List<StaircaseSegment> segments,
            StaircasePoint start,
            HorizontalDirection direction,
            int riserCount,
            StaircaseSectionModel settings)
        {
            int directionFactor = (int)direction;
            for (int step = 0; step < riserCount; step++)
            {
                double x = start.X + directionFactor * step * settings.TreadRun;
                double y = start.Y + step * settings.CurrentStepHeight;
                var stepStart = new StaircasePoint(x, y);
                var stepTop = new StaircasePoint(x, y + settings.CurrentStepHeight);
                AddSegment(segments, stepStart, stepTop);

                if (step < riserCount - 1)
                {
                    var treadEnd = new StaircasePoint(
                        x + directionFactor * settings.TreadRun,
                        stepTop.Y);
                    AddSegment(segments, stepTop, treadEnd);
                }
            }
        }

        private static StaircasePoint GetFlightEnd(
            StaircasePoint start,
            HorizontalDirection direction,
            int riserCount,
            StaircaseSectionModel settings)
        {
            int directionFactor = (int)direction;
            return new StaircasePoint(
                start.X + directionFactor * (riserCount - 1) * settings.TreadRun,
                start.Y + riserCount * settings.CurrentStepHeight);
        }

        private static void AddFlightBoard(
            List<StaircaseSegment> segments,
            StaircasePoint start,
            StaircasePoint end,
            double thickness)
        {
            var lowerStart = new StaircasePoint(start.X, start.Y - thickness);
            var lowerEnd = new StaircasePoint(end.X, end.Y - thickness);
            AddSegment(segments, lowerStart, lowerEnd);
            AddSegment(segments, start, lowerStart);
            AddSegment(segments, end, lowerEnd);
        }

        private static void AddRailing(
            List<StaircaseSegment> segments,
            StaircasePoint start,
            StaircasePoint end,
            double railingHeight)
        {
            if (railingHeight <= 0) return;

            var railingStart = new StaircasePoint(start.X, start.Y + railingHeight);
            var railingEnd = new StaircasePoint(end.X, end.Y + railingHeight);
            AddSegment(segments, start, railingStart);
            AddSegment(segments, railingStart, railingEnd);
            AddSegment(segments, end, railingEnd);
        }

        private static void AddLanding(
            List<StaircaseSegment> segments,
            StaircasePoint start,
            HorizontalDirection direction,
            double width,
            double thickness)
        {
            if (width <= 0) return;

            double endX = start.X + (int)direction * width;
            var end = new StaircasePoint(endX, start.Y);
            var lowerStart = new StaircasePoint(start.X, start.Y - thickness);
            var lowerEnd = new StaircasePoint(endX, start.Y - thickness);
            AddSegment(segments, start, end);
            AddSegment(segments, lowerStart, lowerEnd);
            AddSegment(segments, end, lowerEnd);
        }

        private static void AddSupports(
            List<StaircaseSegment> segments,
            StaircaseSectionModel settings,
            StaircasePoint first,
            StaircasePoint second,
            HorizontalDirection direction)
        {
            int directionFactor = (int)direction;

            if (settings.GirderHeight > 0 && settings.GirderWidth > 0)
            {
                double outerX = first.X - directionFactor * settings.Landing1Width;
                var corner1 = new StaircasePoint(outerX, first.Y - settings.GirderHeight);
                var corner2 = new StaircasePoint(
                    outerX + directionFactor * settings.GirderWidth,
                    first.Y);
                AddRectangle(segments, corner1, corner2);
            }

            if (settings.HasBeam1)
            {
                var corner = new StaircasePoint(
                    first.X - directionFactor * settings.BeamWidth,
                    first.Y - settings.BeamHeight);
                AddRectangle(segments, corner, first);
            }

            if (settings.HasBeam2)
            {
                var corner = new StaircasePoint(
                    second.X + directionFactor * settings.BeamWidth,
                    second.Y - settings.BeamHeight);
                AddRectangle(segments, second, corner);
            }
        }

        private static void AddRectangle(
            List<StaircaseSegment> segments,
            StaircasePoint first,
            StaircasePoint second)
        {
            double left = Math.Min(first.X, second.X);
            double right = Math.Max(first.X, second.X);
            double bottom = Math.Min(first.Y, second.Y);
            double top = Math.Max(first.Y, second.Y);
            var bottomLeft = new StaircasePoint(left, bottom);
            var bottomRight = new StaircasePoint(right, bottom);
            var topRight = new StaircasePoint(right, top);
            var topLeft = new StaircasePoint(left, top);

            AddSegment(segments, bottomLeft, bottomRight);
            AddSegment(segments, bottomRight, topRight);
            AddSegment(segments, topRight, topLeft);
            AddSegment(segments, topLeft, bottomLeft);
        }

        private static void AddSegment(
            List<StaircaseSegment> segments,
            StaircasePoint start,
            StaircasePoint end)
        {
            bool isZeroLength = Math.Abs(start.X - end.X) < CoordinateTolerance
                && Math.Abs(start.Y - end.Y) < CoordinateTolerance;
            if (!isZeroLength) segments.Add(new StaircaseSegment(start, end));
        }

        private static HorizontalDirection Reverse(HorizontalDirection direction)
        {
            return direction == HorizontalDirection.Right
                ? HorizontalDirection.Left
                : HorizontalDirection.Right;
        }
    }

    /// <summary>
    /// Đọc và ghi cấu hình LTP theo milimét.
    /// </summary>
    public sealed class StaircaseSectionSettingsStore
    {
        private const string SettingsDirectoryName = "VinaCAD";
        private const string ToolDirectoryName = "ExpressTools";
        private const string SettingsFileName = "ltp-settings.json";

        public StaircaseSectionModel LoadOrDefault(StaircaseSectionModel defaults)
        {
            // B1: Kiểm tra file cấu hình trước khi đọc.
            string settingsPath = GetSettingsPath();
            if (!File.Exists(settingsPath)) return defaults.Copy();

            try
            {
                // B2: Bỏ cấu hình cũ có quy đổi đơn vị; dữ liệu mới luôn là milimét.
                string json = File.ReadAllText(settingsPath);
                using JsonDocument document = JsonDocument.Parse(json);
                if (document.RootElement.TryGetProperty("UnitsPerMillimetre", out _))
                    return defaults.Copy();

                SettingsEnvelope? envelope = JsonSerializer.Deserialize<SettingsEnvelope>(json);
                if (!IsValid(envelope)) return defaults.Copy();

                StaircaseSectionModel settings = envelope!.Settings!;

                // B3: Chỉ trả cấu hình hợp lệ; nếu không dùng giá trị mặc định an toàn.
                return settings.TryValidate(out _) ? settings : defaults.Copy();
            }
            catch (Exception exception) when (IsSettingsException(exception))
            {
                Logger.Info(nameof(LoadOrDefault), exception);
                return defaults.Copy();
            }
        }

        public void Save(StaircaseSectionModel settings)
        {
            try
            {
                // B1: Chuẩn bị đường dẫn và dữ liệu cần ghi.
                string settingsPath = GetSettingsPath();
                string? directory = Path.GetDirectoryName(settingsPath);
                var envelope = new SettingsEnvelope
                {
                    Settings = settings
                };

                // B2: Tạo thư mục và tuần tự hóa cấu hình.
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(settingsPath, JsonSerializer.Serialize(envelope));
            }
            catch (Exception exception) when (IsSettingsException(exception))
            {
                // B3: Lỗi lưu cấu hình không được làm hỏng kết quả vẽ.
                Logger.Info(nameof(Save), exception);
            }
        }

        private static bool IsValid(SettingsEnvelope? envelope)
        {
            return envelope?.Settings != null;
        }

        private static bool IsSettingsException(Exception exception)
        {
            return exception is IOException
                or UnauthorizedAccessException
                or SecurityException
                or JsonException
                or NotSupportedException;
        }

        private static string GetSettingsPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                SettingsDirectoryName,
                ToolDirectoryName,
                SettingsFileName);
        }

        private sealed class SettingsEnvelope
        {
            public StaircaseSectionModel? Settings { get; set; }
        }
    }
}
