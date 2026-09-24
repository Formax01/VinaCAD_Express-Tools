using PrLogTrackingSystem;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Text.Json;
using Teigha.DatabaseServices;
using Tools.Model;
using Tools.VinaCad.Modeling;

namespace Tools.VinaCad.Helper.Helper
{
    public static class StaircaseSectionGeometry
    {
        private const double CoordinateTolerance = 1e-9;
        private const double SelfCheckTolerance = 1e-7;

        // Cập nhật Delegate: Phân luồng rõ ràng Tiền Cảnh (primSegs/primZones) và Hậu Cảnh (secSegs/secZones)
        private delegate void SectionBuilder(
            List<StaircaseSegment> primSegs, List<StaircaseSegment> secSegs,
            List<StaircasePoint[]> primZones, List<StaircasePoint[]> secZones,
            StaircaseSectionModel settings, HorizontalDirection direction);

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
            ArgumentNullException.ThrowIfNull(settings);
            if (!StaircaseSectionValidator.TryValidate(settings, out string message))
            {
                throw new ArgumentException(message, nameof(settings));
            }

            var primSegs = new List<StaircaseSegment>();
            var secSegs = new List<StaircaseSegment>();
            var primZones = new List<StaircasePoint[]>();
            var secZones = new List<StaircasePoint[]>();

            HorizontalDirection direction = settings.FirstRunRightward
                ? HorizontalDirection.Right
                : HorizontalDirection.Left;

            SectionBuilders[settings.Type](primSegs, secSegs, primZones, secZones, settings, direction);

            // B1: Gọt nét thừa thông qua bộ Trim phân lớp không gian
            var resultSegments = MergeCollinearSegments(TrimExcessSegments(primSegs, secSegs, primZones, secZones));

            // B2: Draw Order - Nét Secondary xuống dưới, Primary đè lên trên
            var sortedSegments = new List<StaircaseSegment>(resultSegments);
            sortedSegments.Sort((a, b) =>
            {
                int orderA = a.Style == StaircaseSegmentStyle.Secondary ? 0 : 1;
                int orderB = b.Style == StaircaseSegmentStyle.Secondary ? 0 : 1;
                return orderA.CompareTo(orderB);
            });

            return sortedSegments;
        }

        private static IReadOnlyList<StaircaseSegment> TrimExcessSegments(
            List<StaircaseSegment> primSegs,
            List<StaircaseSegment> secSegs,
            List<StaircasePoint[]> primaryZones,
            List<StaircasePoint[]> secondaryZones)
        {
            var result = new List<StaircaseSegment>();

            // 1. Xử lý nhóm Tiền cảnh (Gồm Bê tông Primary + Lan can Secondary ở phía trước)
            foreach (StaircaseSegment segment in primSegs)
            {
                var blocked = new List<(double Start, double End)>();

                // Tiền cảnh CHỈ bị gọt bởi chính khối bê tông Tiền cảnh
                foreach (var poly in primaryZones)
                {
                    if (TryGetPolygonInteriorOverlap(segment, poly, out double start, out double end))
                        blocked.Add((start, end));
                }

                ProcessBlocked(result, segment, blocked);
            }

            // Trích xuất các nét Bê tông Tiền cảnh để chống vế sau vẽ đè lên viền
            var foregroundConcrete = new List<StaircaseSegment>();
            foreach (var seg in result)
                if (seg.Style == StaircaseSegmentStyle.Primary) foregroundConcrete.Add(seg);

            // 2. Xử lý nhóm Hậu cảnh (Gồm Bê tông Secondary + Lan can Secondary ở phía sau)
            foreach (StaircaseSegment segment in secSegs)
            {
                var blocked = new List<(double Start, double End)>();

                // Hậu cảnh bị che khuất bởi khối Tiền cảnh
                foreach (var poly in primaryZones)
                {
                    if (TryGetPolygonInteriorOverlap(segment, poly, out double start, out double end))
                        blocked.Add((start, end));
                }

                // Hậu cảnh tự gọt dọn nét nội bộ của chính nó
                foreach (var poly in secondaryZones)
                {
                    if (TryGetPolygonInteriorOverlap(segment, poly, out double start, out double end))
                        blocked.Add((start, end));
                }

                // Hậu cảnh không được phép vẽ đè lên biên của Tiền cảnh
                foreach (var obstacle in foregroundConcrete)
                {
                    if (TryGetCollinearOverlap(segment, obstacle, out double start, out double end))
                        blocked.Add((start, end));
                }

                ProcessBlocked(result, segment, blocked);
            }

            return result;
        }

        private static void ProcessBlocked(List<StaircaseSegment> result, StaircaseSegment segment, List<(double Start, double End)> blocked)
        {
            if (blocked.Count == 0)
            {
                AddSegment(result, segment.Start, segment.End, segment.Style);
                return;
            }

            blocked.Sort((first, second) => first.Start.CompareTo(second.Start));
            double current = 0.0;
            double finish = 1.0;
            foreach ((double start, double end) in MergeIntervals(blocked))
            {
                AddSegmentPart(result, segment, current, start);
                current = Math.Max(current, end);
            }
            AddSegmentPart(result, segment, current, finish);
        }

        private static bool TryGetPolygonInteriorOverlap(StaircaseSegment segment, StaircasePoint[] poly, out double start, out double end)
        {
            start = 0.0;
            end = 1.0;
            double dx = segment.End.X - segment.Start.X;
            double dy = segment.End.Y - segment.Start.Y;
            double segLen = Math.Sqrt(dx * dx + dy * dy);

            if (segLen <= CoordinateTolerance) return false;

            for (int i = 0; i < poly.Length; i++)
            {
                StaircasePoint v1 = poly[i];
                StaircasePoint v2 = poly[(i + 1) % poly.Length];

                double nx = -(v2.Y - v1.Y);
                double ny = (v2.X - v1.X);
                double len = Math.Sqrt(nx * nx + ny * ny);
                if (len <= CoordinateTolerance) continue;

                double den = dx * nx + dy * ny;
                double num = (v1.X - segment.Start.X) * nx + (v1.Y - segment.Start.Y) * ny + SelfCheckTolerance * len;

                if (Math.Abs(den) <= CoordinateTolerance * len * segLen)
                {
                    if (num >= -SelfCheckTolerance * len) return false;
                }
                else
                {
                    double t = num / den;
                    if (den > 0) start = Math.Max(start, t);
                    else end = Math.Min(end, t);
                }
            }
            return start <= end - CoordinateTolerance;
        }

        private static void AddConvexZone(List<StaircasePoint[]> zones, params StaircasePoint[] pts)
        {
            double area = 0;
            for (int i = 0; i < pts.Length; i++)
            {
                var p1 = pts[i]; var p2 = pts[(i + 1) % pts.Length];
                area += p1.X * p2.Y - p2.X * p1.Y;
            }
            if (area < 0) Array.Reverse(pts);
            zones.Add(pts);
        }

        private static IEnumerable<(double Start, double End)> MergeIntervals(List<(double Start, double End)> intervals)
        {
            double start = intervals[0].Start;
            double end = intervals[0].End;
            for (int index = 1; index < intervals.Count; index++)
            {
                (double nextStart, double nextEnd) = intervals[index];
                if (nextStart <= end + CoordinateTolerance)
                {
                    end = Math.Max(end, nextEnd);
                    continue;
                }
                yield return (start, end);
                start = nextStart;
                end = nextEnd;
            }
            yield return (start, end);
        }

        private static bool TryGetCollinearOverlap(StaircaseSegment segment, StaircaseSegment obstacle, out double start, out double end)
        {
            double dx = segment.End.X - segment.Start.X;
            double dy = segment.End.Y - segment.Start.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            start = end = 0.0;
            if (length <= CoordinateTolerance) return false;

            double obstacleDx = obstacle.End.X - obstacle.Start.X;
            double obstacleDy = obstacle.End.Y - obstacle.Start.Y;
            double cross = dx * obstacleDy - dy * obstacleDx;
            if (Math.Abs(cross) > SelfCheckTolerance * length * Math.Sqrt(obstacleDx * obstacleDx + obstacleDy * obstacleDy)) return false;

            double offsetX = obstacle.Start.X - segment.Start.X;
            double offsetY = obstacle.Start.Y - segment.Start.Y;
            if (Math.Abs(dx * offsetY - dy * offsetX) > SelfCheckTolerance * length) return false;

            double projectionStart = (offsetX * dx + offsetY * dy) / (length * length);
            double endOffsetX = obstacle.End.X - segment.Start.X;
            double endOffsetY = obstacle.End.Y - segment.Start.Y;
            double projectionEnd = (endOffsetX * dx + endOffsetY * dy) / (length * length);
            start = Math.Max(0.0, Math.Min(projectionStart, projectionEnd));
            end = Math.Min(1.0, Math.Max(projectionStart, projectionEnd));
            return end - start > CoordinateTolerance;
        }

        private static void AddSegmentPart(List<StaircaseSegment> result, StaircaseSegment source, double start, double end)
        {
            if (end - start <= CoordinateTolerance) return;
            double x = source.End.X - source.Start.X;
            double y = source.End.Y - source.Start.Y;
            AddSegment(result,
                new StaircasePoint(source.Start.X + x * start, source.Start.Y + y * start),
                new StaircasePoint(source.Start.X + x * end, source.Start.Y + y * end),
                source.Style);
        }

        private static IReadOnlyList<StaircaseSegment> MergeCollinearSegments(IReadOnlyList<StaircaseSegment> source)
        {
            var result = new List<StaircaseSegment>();
            foreach (StaircaseSegment sourceSegment in source)
            {
                StaircaseSegment candidate = sourceSegment;
                for (int index = 0; index < result.Count;)
                {
                    if (!TryMergeCollinear(candidate, result[index], out StaircaseSegment merged))
                    {
                        index++;
                        continue;
                    }
                    candidate = merged;
                    result.RemoveAt(index);
                    index = 0;
                }
                result.Add(candidate);
            }
            return result;
        }

        private static bool TryMergeCollinear(StaircaseSegment first, StaircaseSegment second, out StaircaseSegment merged)
        {
            double firstX = first.End.X - first.Start.X;
            double firstY = first.End.Y - first.Start.Y;
            double secondX = second.End.X - second.Start.X;
            double secondY = second.End.Y - second.Start.Y;
            double firstLength = Math.Sqrt(firstX * firstX + firstY * firstY);
            double secondLength = Math.Sqrt(secondX * secondX + secondY * secondY);
            merged = first;
            if (first.Style != second.Style) return false;
            if (firstLength <= CoordinateTolerance || secondLength <= CoordinateTolerance) return false;

            double parallelCross = firstX * secondY - firstY * secondX;
            if (Math.Abs(parallelCross) > SelfCheckTolerance * firstLength * secondLength) return false;

            double offsetX = second.Start.X - first.Start.X;
            double offsetY = second.Start.Y - first.Start.Y;
            double lineDistance = Math.Abs(offsetX * firstY - offsetY * firstX) / firstLength;
            if (lineDistance > SelfCheckTolerance) return false;

            double unitX = firstX / firstLength;
            double unitY = firstY / firstLength;
            double secondStart = offsetX * unitX + offsetY * unitY;
            double secondEnd = (second.End.X - first.Start.X) * unitX + (second.End.Y - first.Start.Y) * unitY;

            double intervalMin = Math.Min(secondStart, secondEnd);
            double intervalMax = Math.Max(secondStart, secondEnd);
            if (intervalMin > firstLength + SelfCheckTolerance || intervalMax < -SelfCheckTolerance) return false;

            double mergedMin = Math.Min(0.0, intervalMin);
            double mergedMax = Math.Max(firstLength, intervalMax);
            merged = new StaircaseSegment(
                new StaircasePoint(first.Start.X + unitX * mergedMin, first.Start.Y + unitY * mergedMin),
                new StaircasePoint(first.Start.X + unitX * mergedMax, first.Start.Y + unitY * mergedMax),
                first.Style);
            return true;
        }

        #region Vẽ hình học cầu thang 

        private static HorizontalDirection Reverse(HorizontalDirection direction)
        {
            return direction == HorizontalDirection.Right ? HorizontalDirection.Left : HorizontalDirection.Right;
        }

        private static void AddSegment(List<StaircaseSegment> segments, StaircasePoint start, StaircasePoint end, StaircaseSegmentStyle style = StaircaseSegmentStyle.Primary)
        {
            bool isZeroLength = Math.Abs(start.X - end.X) < CoordinateTolerance && Math.Abs(start.Y - end.Y) < CoordinateTolerance;
            if (!isZeroLength) segments.Add(new StaircaseSegment(start, end, style));
        }

        private static IEnumerable<int> GetFloorIndexes(int storeyNumber)
        {
            for (int floor = 0; floor < storeyNumber; floor++) yield return floor;
        }

        private static void AddSingleFlightSection(List<StaircaseSegment> primSegs, List<StaircaseSegment> secSegs, List<StaircasePoint[]> primZones, List<StaircasePoint[]> secZones, StaircaseSectionModel settings, HorizontalDirection direction)
        {
            foreach (int floor in GetFloorIndexes(settings.StoreyNumber))
            {
                var start = new StaircasePoint(0.0, floor * settings.StoreyHeight);
                StaircasePoint top = GetFlightEnd(start, direction, settings.StepNumber, settings);

                AddLanding(primSegs, primZones, start, Reverse(direction), settings.Landing1Width, settings.BoardThickness, settings, true);
                AddFlight(primSegs, primZones, start, direction, settings.StepNumber, settings, false);
                AddLanding(primSegs, primZones, top, direction, settings.Landing2Width, settings.BoardThickness, settings, false);

                var floorStart = new StaircasePoint(start.X, top.Y);
                double floorWidth = Math.Abs(top.X - floorStart.X);

                // Sàn ngang chạy phía sau vế thang
                AddSecondaryLanding(secSegs, secZones, floorStart, direction, floorWidth, settings.BoardThickness);
                AddLandingRailing(secSegs, floorStart, direction, floorWidth, settings);

                if (floor == settings.StoreyNumber - 1)
                {
                    AddLanding(primSegs, primZones, floorStart, Reverse(direction), settings.Landing1Width, settings.BoardThickness, settings, true);
                    AddLanding1Supports(primSegs, primZones, settings, floorStart, direction);
                }
                AddSupports(primSegs, primZones, settings, start, top, direction);
            }
        }

        private static void AddDoubleFlightSection(List<StaircaseSegment> primSegs, List<StaircaseSegment> secSegs, List<StaircasePoint[]> primZones, List<StaircasePoint[]> secZones, StaircaseSectionModel settings, HorizontalDirection direction)
        {
            int secondFlightSteps = settings.StepNumber - settings.FirstFlightStepNumber;
            double anchorX = 0.0;
            StaircasePoint finalTop = default;

            foreach (int floor in GetFloorIndexes(settings.StoreyNumber))
            {
                var start = new StaircasePoint(anchorX, floor * settings.StoreyHeight);
                HorizontalDirection reverse = Reverse(direction);
                StaircasePoint middle = GetFlightEnd(start, direction, settings.FirstFlightStepNumber, settings);
                StaircasePoint top = GetFlightEnd(middle, reverse, secondFlightSteps, settings);

                AddLanding(primSegs, primZones, start, reverse, settings.Landing1Width, settings.BoardThickness, settings, true);
                AddFlight(primSegs, primZones, start, direction, settings.FirstFlightStepNumber, settings, true);
                AddLanding(primSegs, primZones, middle, direction, settings.Landing2Width, settings.BoardThickness, settings, false);

                bool isContinuingTop = floor < settings.StoreyNumber - 1;
                AddHighlightedFlight(secSegs, secZones, middle, reverse, secondFlightSteps, settings, isContinuingTop);

                AddLanding(primSegs, primZones, top, reverse, settings.Landing1Width, settings.BoardThickness, settings, true);
                AddSupports(primSegs, primZones, settings, start, middle, direction);
                anchorX = top.X;
                finalTop = top;
            }
            AddLanding1Supports(primSegs, primZones, settings, finalTop, direction);
        }

        private static void AddScissorSection(List<StaircaseSegment> primSegs, List<StaircaseSegment> secSegs, List<StaircasePoint[]> primZones, List<StaircasePoint[]> secZones, StaircaseSectionModel settings, HorizontalDirection direction)
        {
            double flightSpan = (settings.StepNumber - 1) * settings.TreadRun;
            int directionFactor = (int)direction;

            foreach (int floor in GetFloorIndexes(settings.StoreyNumber))
            {
                double baseY = floor * settings.StoreyHeight;
                double rightX = directionFactor * flightSpan;
                var lowerLeft = new StaircasePoint(0.0, baseY);
                var lowerRight = new StaircasePoint(rightX, baseY);
                StaircasePoint topRight = GetFlightEnd(lowerLeft, direction, settings.StepNumber, settings);
                StaircasePoint topLeft = GetFlightEnd(lowerRight, Reverse(direction), settings.StepNumber, settings);

                bool isContinuing = floor < settings.StoreyNumber - 1;

                // Vế Trái (Đi lên) là Tiền cảnh, Vế Phải (Đi ngược) là Hậu cảnh
                AddScissorFlights(primSegs, secSegs, primZones, secZones, settings, lowerLeft, lowerRight, direction, isContinuing);
                AddScissorTopLandings(primSegs, secSegs, primZones, secZones, settings, topLeft, topRight, direction);

                bool isGroundRight = floor == 0;

                AddLanding1Supports(primSegs, primZones, settings, lowerLeft, direction); // Trái trước
                AddLanding2Support(secSegs, secZones, settings, lowerRight, direction, !isGroundRight); // Phải sau

                AddLanding1Supports(primSegs, primZones, settings, topLeft, direction); // Trái trên trước
                AddLanding2Support(secSegs, secZones, settings, topRight, direction, true); // Phải trên sau
            }
            AddScissorSideRailLines(primSegs, secSegs, settings, direction, flightSpan);
        }

        private static void AddScissorSideRailLines(List<StaircaseSegment> primSegs, List<StaircaseSegment> secSegs, StaircaseSectionModel settings, HorizontalDirection direction, double flightSpan)
        {
            if (settings.RailingHeight <= 0) return;

            int directionFactor = (int)direction;
            double bottomY = 0.0;
            double topY = settings.StoreyNumber * settings.StoreyHeight + settings.RailingHeight;

            double beam1Offset = settings.HasBeam1 ? settings.BeamWidth / 2.0 : 0.0;
            double beam2Offset = settings.HasBeam2 ? settings.BeamWidth / 2.0 : 0.0;

            double leftRailX = -directionFactor * beam1Offset;
            double rightRailX = directionFactor * flightSpan + directionFactor * beam2Offset;

            // Trục lan can trái ở Tiền cảnh, phải ở Hậu cảnh
            AddSegment(primSegs, new StaircasePoint(leftRailX, bottomY), new StaircasePoint(leftRailX, topY), StaircaseSegmentStyle.Secondary);
            AddSegment(secSegs, new StaircasePoint(rightRailX, bottomY), new StaircasePoint(rightRailX, topY), StaircaseSegmentStyle.Secondary);
        }

        private static void AddScissorFlights(List<StaircaseSegment> primSegs, List<StaircaseSegment> secSegs, List<StaircasePoint[]> primZones, List<StaircasePoint[]> secZones, StaircaseSectionModel settings, StaircasePoint left, StaircasePoint right, HorizontalDirection direction, bool isContinuing)
        {
            AddLanding(primSegs, primZones, left, Reverse(direction), settings.Landing1Width, settings.BoardThickness, settings, true);
            AddLanding(secSegs, secZones, right, direction, settings.Landing2Width, settings.BoardThickness, settings, false);

            AddFlight(primSegs, primZones, left, direction, settings.StepNumber, settings, isContinuing);
            AddHighlightedFlight(secSegs, secZones, right, Reverse(direction), settings.StepNumber, settings, isContinuing);
        }

        private static void AddScissorTopLandings(List<StaircaseSegment> primSegs, List<StaircaseSegment> secSegs, List<StaircasePoint[]> primZones, List<StaircasePoint[]> secZones, StaircaseSectionModel settings, StaircasePoint leftLanding, StaircasePoint rightLanding, HorizontalDirection direction)
        {
            AddLanding(secSegs, secZones, rightLanding, direction, settings.Landing2Width, settings.BoardThickness, settings, false);
            AddLanding(primSegs, primZones, leftLanding, Reverse(direction), settings.Landing1Width, settings.BoardThickness, settings, true);
        }

        private static void AddFlight(List<StaircaseSegment> segments, List<StaircasePoint[]> zones, StaircasePoint start, HorizontalDirection direction, int riserCount, StaircaseSectionModel settings, bool isContinuing = false)
        {
            AddSteps(segments, zones, start, direction, riserCount, settings, isContinuing);
            StaircasePoint end = GetFlightEnd(start, direction, riserCount, settings);
            AddFlightBoard(segments, zones, start, end, direction, settings);
            AddRailing(segments, start, end, direction, settings);
        }

        private static void AddHighlightedFlight(List<StaircaseSegment> secSegs, List<StaircasePoint[]> secZones, StaircasePoint start, HorizontalDirection direction, int riserCount, StaircaseSectionModel settings, bool isContinuing = false)
        {
            int firstSegment = secSegs.Count;
            AddFlight(secSegs, secZones, start, direction, riserCount, settings, isContinuing);
            for (int index = firstSegment; index < secSegs.Count; index++)
            {
                StaircaseSegment segment = secSegs[index];
                secSegs[index] = new StaircaseSegment(segment.Start, segment.End, StaircaseSegmentStyle.Secondary);
            }
        }

        private static void AddSteps(List<StaircaseSegment> segments, List<StaircasePoint[]> zones, StaircasePoint start, HorizontalDirection direction, int riserCount, StaircaseSectionModel settings, bool isContinuing = false)
        {
            int directionFactor = (int)direction;
            for (int step = 0; step < riserCount; step++)
            {
                double x = start.X + directionFactor * step * settings.TreadRun;
                double y = start.Y + step * settings.CurrentStepHeight;
                var stepStart = new StaircasePoint(x, y);
                var stepTop = new StaircasePoint(x, start.Y + (step + 1) * settings.CurrentStepHeight);
                var treadEnd = new StaircasePoint(start.X + directionFactor * (step + 1) * settings.TreadRun, stepTop.Y);

                if (zones != null) AddConvexZone(zones, stepStart, stepTop, treadEnd);

                if (step == riserCount - 1 && isContinuing)
                {
                    // Trống để hợp nhất khối với thang tiếp theo
                }
                else
                {
                    AddSegment(segments, stepStart, stepTop);
                }

                if (step < riserCount - 1)
                {
                    AddSegment(segments, stepTop, treadEnd);
                }
            }
        }

        private static StaircasePoint GetFlightEnd(StaircasePoint start, HorizontalDirection direction, int riserCount, StaircaseSectionModel settings)
        {
            int directionFactor = (int)direction;
            return new StaircasePoint(
                start.X + directionFactor * (riserCount - 1) * settings.TreadRun,
                start.Y + riserCount * settings.CurrentStepHeight);
        }

        private static void AddFlightBoard(List<StaircaseSegment> segments, List<StaircasePoint[]> zones, StaircasePoint start, StaircasePoint end, HorizontalDirection direction, StaircaseSectionModel settings)
        {
            double connectionY = end.Y - settings.BeamHeight * 2.0 / 3.0;
            var beamConnection = new StaircasePoint(end.X, connectionY);
            var lowerStart = new StaircasePoint(start.X, start.Y - settings.BoardThickness);

            AddSegment(segments, lowerStart, beamConnection);

            // Bịt mép nối bằng đường gióng dọc để không bị hở khối
            AddSegment(segments, start, lowerStart);
            AddSegment(segments, new StaircasePoint(end.X, end.Y), beamConnection);

            if (zones != null)
            {
                var p3 = new StaircasePoint(end.X, end.Y - settings.CurrentStepHeight);
                var p4 = start;
                AddConvexZone(zones, lowerStart, beamConnection, p3, p4);
            }
        }

        private static void AddRailing(List<StaircaseSegment> segments, StaircasePoint start, StaircasePoint end, HorizontalDirection direction, StaircaseSectionModel settings)
        {
            if (settings.RailingHeight <= 0) return;

            bool startHasBeam = direction == HorizontalDirection.Right ? settings.HasBeam1 : settings.HasBeam2;
            bool endHasBeam = direction == HorizontalDirection.Right ? settings.HasBeam2 : settings.HasBeam1;

            double beamCenterOffset = endHasBeam ? (int)direction * settings.BeamWidth / 2.0 : 0.0;
            double startBeamCenterOffset = startHasBeam ? -(int)direction * settings.BeamWidth / 2.0 : 0.0;

            var railingStartBase = new StaircasePoint(start.X + startBeamCenterOffset, start.Y);
            var railingEndBase = new StaircasePoint(end.X + beamCenterOffset, end.Y);

            var railingStart = new StaircasePoint(railingStartBase.X, railingStartBase.Y + settings.RailingHeight);
            var railingEnd = new StaircasePoint(railingEndBase.X, railingEndBase.Y + settings.RailingHeight);

            AddSegment(segments, railingStartBase, railingStart, StaircaseSegmentStyle.Secondary);
            AddSegment(segments, railingStart, railingEnd, StaircaseSegmentStyle.Secondary);
            AddSegment(segments, railingEndBase, railingEnd, StaircaseSegmentStyle.Secondary);
        }

        private static void AddLanding(List<StaircaseSegment> segments, List<StaircasePoint[]> zones, StaircasePoint start, HorizontalDirection direction, double width, double thickness, StaircaseSectionModel settings = null, bool isLanding1 = true)
        {
            if (width <= 0) return;

            double endX = start.X + (int)direction * width;
            var end = new StaircasePoint(endX, start.Y);
            var lowerStart = new StaircasePoint(start.X, start.Y - thickness);
            var lowerEnd = new StaircasePoint(endX, start.Y - thickness);

            AddSegment(segments, start, end);
            AddSegment(segments, end, lowerEnd);

            if (zones != null)
            {
                double minX = Math.Min(start.X, endX);
                double maxX = Math.Max(start.X, endX);
                double minY = start.Y - thickness;
                double maxY = start.Y;
                AddConvexZone(zones, new StaircasePoint(minX, minY), new StaircasePoint(maxX, minY), new StaircasePoint(maxX, maxY), new StaircasePoint(minX, maxY));
            }

            if (settings == null)
            {
                AddSegment(segments, lowerStart, lowerEnd);
                AddSegment(segments, start, lowerStart);
            }
            else
            {
                int dir = (int)direction;
                bool hasInnerBeam = isLanding1 ? settings.HasBeam1 : settings.HasBeam2;
                double innerBeamWidth = settings.BeamWidth;
                bool hasOuterBeam = settings.GirderHeight > 0 && settings.GirderWidth > 0;
                double outerBeamWidth = settings.GirderWidth;

                double lineStartX = start.X + (hasInnerBeam ? dir * innerBeamWidth : 0);
                double lineEndX = endX - (hasOuterBeam ? dir * outerBeamWidth : 0);

                if ((dir == 1 && lineStartX < lineEndX - CoordinateTolerance) ||
                    (dir == -1 && lineStartX > lineEndX + CoordinateTolerance))
                {
                    AddSegment(segments, new StaircasePoint(lineStartX, start.Y - thickness), new StaircasePoint(lineEndX, start.Y - thickness));
                }
            }
        }

        private static void AddSecondaryLanding(List<StaircaseSegment> secSegs, List<StaircasePoint[]> secZones, StaircasePoint start, HorizontalDirection direction, double width, double thickness)
        {
            int firstSegment = secSegs.Count;
            AddLanding(secSegs, secZones, start, direction, width, thickness);
            for (int index = firstSegment; index < secSegs.Count; index++)
            {
                StaircaseSegment segment = secSegs[index];
                secSegs[index] = new StaircaseSegment(segment.Start, segment.End, StaircaseSegmentStyle.Secondary);
            }
        }

        private static void AddLandingRailing(List<StaircaseSegment> segments, StaircasePoint start, HorizontalDirection direction, double width, StaircaseSectionModel settings)
        {
            if (width <= 0 || settings.RailingHeight <= 0) return;

            int directionFactor = (int)direction;
            bool startHasBeam = direction == HorizontalDirection.Right ? settings.HasBeam1 : settings.HasBeam2;
            bool endHasBeam = direction == HorizontalDirection.Right ? settings.HasBeam2 : settings.HasBeam1;

            double startX = start.X - (startHasBeam ? directionFactor * settings.BeamWidth / 2.0 : 0.0);
            double endX = start.X + directionFactor * width + (endHasBeam ? directionFactor * settings.BeamWidth / 2.0 : 0.0);

            var railingStartBase = new StaircasePoint(startX, start.Y);
            var railingEndBase = new StaircasePoint(endX, start.Y);
            var railingStart = new StaircasePoint(startX, start.Y + settings.RailingHeight);
            var railingEnd = new StaircasePoint(endX, start.Y + settings.RailingHeight);

            AddSegment(segments, railingStartBase, railingStart, StaircaseSegmentStyle.Secondary);
            AddSegment(segments, railingStart, railingEnd, StaircaseSegmentStyle.Secondary);
            AddSegment(segments, railingEndBase, railingEnd, StaircaseSegmentStyle.Secondary);
        }

        private static void AddSupports(List<StaircaseSegment> segments, List<StaircasePoint[]> zones, StaircaseSectionModel settings, StaircasePoint first, StaircasePoint second, HorizontalDirection direction, bool secondHasIncomingFlight = true)
        {
            AddLanding1Supports(segments, zones, settings, first, direction);
            AddLanding2Support(segments, zones, settings, second, direction, secondHasIncomingFlight);
        }

        private static void AddLanding1Supports(List<StaircaseSegment> segments, List<StaircasePoint[]> zones, StaircaseSectionModel settings, StaircasePoint anchor, HorizontalDirection direction)
        {
            int directionFactor = (int)direction;

            if (settings.GirderHeight > 0 && settings.GirderWidth > 0)
            {
                double outerX = anchor.X - directionFactor * settings.Landing1Width;
                var underside1 = new StaircasePoint(outerX, anchor.Y - settings.BoardThickness);
                var underside2 = new StaircasePoint(outerX + directionFactor * settings.GirderWidth, anchor.Y - settings.BoardThickness);
                AddDownstand(segments, underside1, underside2, anchor.Y - settings.GirderHeight);

                if (zones != null)
                {
                    double b1 = outerX; double b2 = outerX + directionFactor * settings.GirderWidth;
                    AddConvexZone(zones, new StaircasePoint(Math.Min(b1, b2), anchor.Y - settings.GirderHeight), new StaircasePoint(Math.Max(b1, b2), anchor.Y - settings.GirderHeight), new StaircasePoint(Math.Max(b1, b2), anchor.Y - settings.BoardThickness), new StaircasePoint(Math.Min(b1, b2), anchor.Y - settings.BoardThickness));
                }
            }

            if (settings.HasBeam1)
            {
                var underside1 = new StaircasePoint(anchor.X - directionFactor * settings.BeamWidth, anchor.Y - settings.BoardThickness);
                var underside2 = new StaircasePoint(anchor.X, anchor.Y - settings.BoardThickness);
                AddDownstand(segments, underside1, underside2, anchor.Y - settings.BeamHeight);

                if (zones != null)
                {
                    double b1 = anchor.X; double b2 = anchor.X - directionFactor * settings.BeamWidth;
                    AddConvexZone(zones, new StaircasePoint(Math.Min(b1, b2), anchor.Y - settings.BeamHeight), new StaircasePoint(Math.Max(b1, b2), anchor.Y - settings.BeamHeight), new StaircasePoint(Math.Max(b1, b2), anchor.Y - settings.BoardThickness), new StaircasePoint(Math.Min(b1, b2), anchor.Y - settings.BoardThickness));
                }
            }
        }

        private static void AddLanding2Support(List<StaircaseSegment> segments, List<StaircasePoint[]> zones, StaircaseSectionModel settings, StaircasePoint anchor, HorizontalDirection direction, bool hasIncomingFlight = true)
        {
            int directionFactor = (int)direction;
            if (settings.HasBeam2)
            {
                double connectionY = hasIncomingFlight ? anchor.Y - settings.BeamHeight * 2.0 / 3.0 : anchor.Y - settings.BoardThickness;
                var innerTop = new StaircasePoint(anchor.X, connectionY);
                var innerBottom = new StaircasePoint(anchor.X, anchor.Y - settings.BeamHeight);
                var outerBottom = new StaircasePoint(anchor.X + directionFactor * settings.BeamWidth, anchor.Y - settings.BeamHeight);
                var outerTop = new StaircasePoint(anchor.X + directionFactor * settings.BeamWidth, anchor.Y - settings.BoardThickness);

                if (connectionY > anchor.Y - settings.BeamHeight + CoordinateTolerance)
                {
                    AddSegment(segments, innerTop, innerBottom);
                }
                AddSegment(segments, innerBottom, outerBottom);
                AddSegment(segments, outerBottom, outerTop);

                if (zones != null)
                {
                    double b1 = anchor.X; double b2 = anchor.X + directionFactor * settings.BeamWidth;
                    AddConvexZone(zones, new StaircasePoint(Math.Min(b1, b2), anchor.Y - settings.BeamHeight), new StaircasePoint(Math.Max(b1, b2), anchor.Y - settings.BeamHeight), new StaircasePoint(Math.Max(b1, b2), anchor.Y - settings.BoardThickness), new StaircasePoint(Math.Min(b1, b2), anchor.Y - settings.BoardThickness));
                }
            }

            if (settings.GirderHeight <= 0 || settings.GirderWidth <= 0) return;
            double outerX = anchor.X + directionFactor * settings.Landing2Width;
            var outerUnderside1 = new StaircasePoint(outerX - directionFactor * settings.GirderWidth, anchor.Y - settings.BoardThickness);
            var outerUnderside2 = new StaircasePoint(outerX, anchor.Y - settings.BoardThickness);
            AddDownstand(segments, outerUnderside1, outerUnderside2, anchor.Y - settings.GirderHeight);

            if (zones != null)
            {
                double b3 = outerX; double b4 = outerX - directionFactor * settings.GirderWidth;
                AddConvexZone(zones, new StaircasePoint(Math.Min(b3, b4), anchor.Y - settings.GirderHeight), new StaircasePoint(Math.Max(b3, b4), anchor.Y - settings.GirderHeight), new StaircasePoint(Math.Max(b3, b4), anchor.Y - settings.BoardThickness), new StaircasePoint(Math.Min(b3, b4), anchor.Y - settings.BoardThickness));
            }
        }

        private static void AddDownstand(List<StaircaseSegment> segments, StaircasePoint firstUnderside, StaircasePoint secondUnderside, double bottomY)
        {
            if (bottomY >= firstUnderside.Y - CoordinateTolerance) return;

            var firstBottom = new StaircasePoint(firstUnderside.X, bottomY);
            var secondBottom = new StaircasePoint(secondUnderside.X, bottomY);
            AddSegment(segments, firstUnderside, firstBottom);
            AddSegment(segments, firstBottom, secondBottom);
            AddSegment(segments, secondBottom, secondUnderside);
        }

        #endregion
    }

    public sealed class StaircaseSectionSettingsStore
    {
        private const string SettingsDirectoryName = "VinaCAD";
        private const string ToolDirectoryName = "ExpressTools";
        private const string SettingsFileName = "ltp-settings.json";

        public StaircaseSectionModel LoadOrDefault(StaircaseSectionModel defaults)
        {
            string settingsPath = GetSettingsPath();
            if (!File.Exists(settingsPath)) return defaults.Copy();

            try
            {
                string json = File.ReadAllText(settingsPath);
                using JsonDocument document = JsonDocument.Parse(json);
                if (document.RootElement.TryGetProperty("UnitsPerMillimetre", out _))
                    return defaults.Copy();

                SettingsEnvelope? envelope = JsonSerializer.Deserialize<SettingsEnvelope>(json);
                if (envelope?.Settings == null) return defaults.Copy();

                StaircaseSectionModel settings = envelope.Settings;
                return StaircaseSectionValidator.TryValidate(settings, out _)
                    ? settings
                    : defaults.Copy();
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
                string settingsPath = GetSettingsPath();
                string? directory = Path.GetDirectoryName(settingsPath);
                var envelope = new SettingsEnvelope { Settings = settings };

                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(settingsPath, JsonSerializer.Serialize(envelope));
            }
            catch (Exception exception) when (IsSettingsException(exception))
            {
                Logger.Info(nameof(Save), exception);
            }
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