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

        private delegate void SectionBuilder( List<StaircaseSegment> segments, StaircaseSectionModel settings, HorizontalDirection direction);

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
            var segments = new List<StaircaseSegment>();
            HorizontalDirection direction = settings.FirstRunRightward
                ? HorizontalDirection.Right
                : HorizontalDirection.Left;
            SectionBuilders[settings.Type](segments, settings, direction);

            return MergeCollinearSegments(TrimExcessSegments(segments, settings));
        }

        private static IReadOnlyList<StaircaseSegment> TrimExcessSegments(
            IReadOnlyList<StaircaseSegment> source,
            StaircaseSectionModel settings)
        {
            var primary = new List<StaircaseSegment>();
            foreach (StaircaseSegment segment in source)
            {
                if (segment.Style == StaircaseSegmentStyle.Primary)
                    primary.Add(segment);
            }
            IReadOnlyList<LandingBounds> landings = FindLandingBounds(primary, settings.BoardThickness);

            var result = new List<StaircaseSegment>(source.Count);
            foreach (StaircaseSegment segment in source)
            {
                if (segment.Style != StaircaseSegmentStyle.Secondary)
                {
                    AddSegment(result, segment.Start, segment.End, segment.Style);
                    continue;
                }

                var blocked = new List<(double Start, double End)>();
                foreach (StaircaseSegment obstacle in primary)
                {
                    if (TryGetCollinearOverlap(segment, obstacle, out double start, out double end))
                        blocked.Add((start, end));
                }
                foreach (LandingBounds landing in landings)
                {
                    if (TryGetInteriorOverlap(segment, landing, out double start, out double end))
                        blocked.Add((start, end));
                }

                if (blocked.Count == 0)
                {
                    AddSegment(result, segment.Start, segment.End, segment.Style);
                    continue;
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

            return result;
        }

        private static IReadOnlyList<LandingBounds> FindLandingBounds(
            IReadOnlyList<StaircaseSegment> primary,
            double boardThickness)
        {
            var horizontals = new List<StaircaseSegment>();
            foreach (StaircaseSegment segment in primary)
            {
                if (Math.Abs(segment.Start.Y - segment.End.Y) <= CoordinateTolerance)
                    horizontals.Add(segment);
            }

            var result = new List<LandingBounds>();
            for (int index = 0; index < horizontals.Count; index++)
            {
                StaircaseSegment first = horizontals[index];
                double firstMinX = Math.Min(first.Start.X, first.End.X);
                double firstMaxX = Math.Max(first.Start.X, first.End.X);
                for (int next = index + 1; next < horizontals.Count; next++)
                {
                    StaircaseSegment second = horizontals[next];
                    if (Math.Abs(firstMinX - Math.Min(second.Start.X, second.End.X)) > SelfCheckTolerance
                        || Math.Abs(firstMaxX - Math.Max(second.Start.X, second.End.X)) > SelfCheckTolerance)
                        continue;

                    double height = Math.Abs(first.Start.Y - second.Start.Y);
                    if (height <= CoordinateTolerance || height > boardThickness + SelfCheckTolerance)
                        continue;

                    result.Add(new LandingBounds(
                        firstMinX + CoordinateTolerance,
                        firstMaxX - CoordinateTolerance,
                        Math.Min(first.Start.Y, second.Start.Y) + CoordinateTolerance,
                        Math.Max(first.Start.Y, second.Start.Y) - CoordinateTolerance));
                    break;
                }
            }

            return result;
        }

        private static bool TryGetInteriorOverlap(
            StaircaseSegment segment,
            LandingBounds bounds,
            out double start,
            out double end)
        {
            double dx = segment.End.X - segment.Start.X;
            double dy = segment.End.Y - segment.Start.Y;
            start = 0.0;
            end = 1.0;
            if (!ClipParameter(-dx, segment.Start.X - bounds.MinX, ref start, ref end)
                || !ClipParameter(dx, bounds.MaxX - segment.Start.X, ref start, ref end)
                || !ClipParameter(-dy, segment.Start.Y - bounds.MinY, ref start, ref end)
                || !ClipParameter(dy, bounds.MaxY - segment.Start.Y, ref start, ref end))
                return false;

            return end - start > CoordinateTolerance;
        }

        private static bool ClipParameter(double coefficient, double value, ref double start, ref double end)
        {
            if (Math.Abs(coefficient) <= CoordinateTolerance) return value >= 0;
            double parameter = value / coefficient;
            if (coefficient < 0)
                start = Math.Max(start, parameter);
            else
                end = Math.Min(end, parameter);
            return start <= end + CoordinateTolerance;
        }

        private static IEnumerable<(double Start, double End)> MergeIntervals(
            List<(double Start, double End)> intervals)
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

        private static bool TryGetCollinearOverlap(
            StaircaseSegment segment,
            StaircaseSegment obstacle,
            out double start,
            out double end)
        {
            double dx = segment.End.X - segment.Start.X;
            double dy = segment.End.Y - segment.Start.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            start = end = 0.0;
            if (length <= CoordinateTolerance) return false;

            double obstacleDx = obstacle.End.X - obstacle.Start.X;
            double obstacleDy = obstacle.End.Y - obstacle.Start.Y;
            double cross = dx * obstacleDy - dy * obstacleDx;
            if (Math.Abs(cross) > SelfCheckTolerance * length
                * Math.Sqrt(obstacleDx * obstacleDx + obstacleDy * obstacleDy))
                return false;

            double offsetX = obstacle.Start.X - segment.Start.X;
            double offsetY = obstacle.Start.Y - segment.Start.Y;
            if (Math.Abs(dx * offsetY - dy * offsetX) > SelfCheckTolerance * length)
                return false;

            double projectionStart = (offsetX * dx + offsetY * dy) / (length * length);
            double endOffsetX = obstacle.End.X - segment.Start.X;
            double endOffsetY = obstacle.End.Y - segment.Start.Y;
            double projectionEnd = (endOffsetX * dx + endOffsetY * dy) / (length * length);
            start = Math.Max(0.0, Math.Min(projectionStart, projectionEnd));
            end = Math.Min(1.0, Math.Max(projectionStart, projectionEnd));
            return end - start > CoordinateTolerance;
        }

        private static void AddSegmentPart(
            List<StaircaseSegment> result,
            StaircaseSegment source,
            double start,
            double end)
        {
            if (end - start <= CoordinateTolerance) return;
            double x = source.End.X - source.Start.X;
            double y = source.End.Y - source.Start.Y;
            AddSegment(result,
                new StaircasePoint(source.Start.X + x * start, source.Start.Y + y * start),
                new StaircasePoint(source.Start.X + x * end, source.Start.Y + y * end),
                source.Style);
        }

        private readonly record struct LandingBounds(double MinX, double MaxX, double MinY, double MaxY);

        private static IReadOnlyList<StaircaseSegment> MergeCollinearSegments( IReadOnlyList<StaircaseSegment> source)
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

        private static bool TryMergeCollinear( StaircaseSegment first, StaircaseSegment second, out StaircaseSegment merged)
        {
            double firstX = first.End.X - first.Start.X;
            double firstY = first.End.Y - first.Start.Y;
            double secondX = second.End.X - second.Start.X;
            double secondY = second.End.Y - second.Start.Y;
            double firstLength = Math.Sqrt(firstX * firstX + firstY * firstY);
            double secondLength = Math.Sqrt(secondX * secondX + secondY * secondY);
            merged = first;
            if (first.Style != second.Style) return false;
            if (firstLength <= CoordinateTolerance || secondLength <= CoordinateTolerance)
                return false;

            double parallelCross = firstX * secondY - firstY * secondX;
            if (Math.Abs(parallelCross) > SelfCheckTolerance * firstLength * secondLength)
                return false;

            double offsetX = second.Start.X - first.Start.X;
            double offsetY = second.Start.Y - first.Start.Y;
            double lineDistance = Math.Abs(offsetX * firstY - offsetY * firstX) / firstLength;
            if (lineDistance > SelfCheckTolerance) return false;

            double unitX = firstX / firstLength;
            double unitY = firstY / firstLength;
            double secondStart = offsetX * unitX + offsetY * unitY;
            double secondEnd = (second.End.X - first.Start.X) * unitX
                + (second.End.Y - first.Start.Y) * unitY;
            double intervalMin = Math.Min(secondStart, secondEnd);
            double intervalMax = Math.Max(secondStart, secondEnd);
            if (intervalMin > firstLength + SelfCheckTolerance
                || intervalMax < -SelfCheckTolerance) return false;

            double mergedMin = Math.Min(0.0, intervalMin);
            double mergedMax = Math.Max(firstLength, intervalMax);
            merged = new StaircaseSegment(
                new StaircasePoint(first.Start.X + unitX * mergedMin,
                    first.Start.Y + unitY * mergedMin),
                new StaircasePoint(first.Start.X + unitX * mergedMax,
                    first.Start.Y + unitY * mergedMax),
                first.Style);
            return true;
        }

        #region  Vẽ hình học cầu thang 

        private static HorizontalDirection Reverse(HorizontalDirection direction)
        {
            return direction == HorizontalDirection.Right
                ? HorizontalDirection.Left
                : HorizontalDirection.Right;
        }

        private static void AddSegment( List<StaircaseSegment> segments, StaircasePoint start, StaircasePoint end, StaircaseSegmentStyle style = StaircaseSegmentStyle.Primary)
        {
            bool isZeroLength = Math.Abs(start.X - end.X) < CoordinateTolerance
                && Math.Abs(start.Y - end.Y) < CoordinateTolerance;
            if (!isZeroLength) segments.Add(new StaircaseSegment(start, end, style));
        }

        private static IEnumerable<int> GetFloorIndexes(int storeyNumber)
        {
            for (int floor = 0; floor < storeyNumber; floor++) yield return floor;
        }

        private static void AddSingleFlightSection( List<StaircaseSegment> segments, StaircaseSectionModel settings, HorizontalDirection direction)
        {
            foreach (int floor in GetFloorIndexes(settings.StoreyNumber))
            {
                var start = new StaircasePoint(0.0, floor * settings.StoreyHeight);
                StaircasePoint top = GetFlightEnd(start, direction, settings.StepNumber, settings);

                AddLanding(segments, start, Reverse(direction), settings.Landing1Width, settings.BoardThickness, settings, true);
                AddFlight(segments, start, direction, settings.StepNumber, settings);
                AddLanding(segments, top, direction, settings.Landing2Width, settings.BoardThickness, settings, false);

                // Sàn tầng chạy suốt khoảng vế, tô Secondary; không tạo lan can ngang theo sàn.
                var floorStart = new StaircasePoint(start.X, top.Y);
                double floorWidth = Math.Abs(top.X - floorStart.X);
                AddSecondaryLanding(segments, floorStart, direction,
                    floorWidth, settings.BoardThickness);
                AddLandingRailing(segments, floorStart, direction,
                    floorWidth, settings);
                if (floor == settings.StoreyNumber - 1)
                {
                    AddLanding(segments, floorStart, Reverse(direction),
                        settings.Landing1Width, settings.BoardThickness, settings, true);
                    AddLanding1Supports(segments, settings, floorStart, direction);
                }
                AddSupports(segments, settings, start, top, direction);
            }
        }

        private static void AddDoubleFlightSection( List<StaircaseSegment> segments, StaircaseSectionModel settings, HorizontalDirection direction)
        {
            int secondFlightSteps = settings.StepNumber - settings.FirstFlightStepNumber;
            double anchorX = 0.0;
            StaircasePoint finalTop = default;

            foreach (int floor in GetFloorIndexes(settings.StoreyNumber))
            {
                var start = new StaircasePoint(anchorX, floor * settings.StoreyHeight);
                HorizontalDirection reverse = Reverse(direction);
                StaircasePoint middle = GetFlightEnd(
                    start, direction, settings.FirstFlightStepNumber, settings);
                StaircasePoint top = GetFlightEnd(
                    middle, reverse, secondFlightSteps, settings);

                AddLanding(segments, start, reverse, settings.Landing1Width, settings.BoardThickness, settings, true);
                AddFlight(segments, start, direction, settings.FirstFlightStepNumber, settings);
                AddLanding(segments, middle, direction, settings.Landing2Width, settings.BoardThickness, settings, false);
                AddHighlightedFlight(segments, middle, reverse, secondFlightSteps, settings);
                AddLanding(segments, top, reverse, settings.Landing1Width, settings.BoardThickness, settings, true);
                AddSupports(segments, settings, start, middle, direction);
                anchorX = top.X;
                finalTop = top;
            }

            AddLanding1Supports(segments, settings, finalTop, direction);
        }

        private static void AddScissorSection( List<StaircaseSegment> segments, StaircaseSectionModel settings, HorizontalDirection direction)
        {
            double flightSpan = (settings.StepNumber - 1) * settings.TreadRun;
            int directionFactor = (int)direction;

            foreach (int floor in GetFloorIndexes(settings.StoreyNumber))
            {
                double baseY = floor * settings.StoreyHeight;
                double rightX = directionFactor * flightSpan;
                var lowerLeft = new StaircasePoint(0.0, baseY);
                var lowerRight = new StaircasePoint(rightX, baseY);
                StaircasePoint topRight = GetFlightEnd(
                    lowerLeft, direction, settings.StepNumber, settings);
                StaircasePoint topLeft = GetFlightEnd(
                    lowerRight, Reverse(direction), settings.StepNumber, settings);

                AddScissorFlights(segments, settings, lowerLeft, lowerRight, direction);
                AddScissorTopLandings(segments, settings, topLeft, topRight, direction);

                bool isGroundRight = floor == 0;
                AddSupports(segments, settings, lowerLeft, lowerRight, direction, !isGroundRight);
                AddSupports(segments, settings, topLeft, topRight, direction);
            }

            AddScissorSideRailLines(segments, settings, direction, flightSpan);
        }

        private static void AddScissorSideRailLines( List<StaircaseSegment> segments, StaircaseSectionModel settings, HorizontalDirection direction, double flightSpan)
        {
            if (settings.RailingHeight <= 0) return;

            int directionFactor = (int)direction;
            double bottomY = 0.0;
            double topY = settings.StoreyNumber * settings.StoreyHeight
                + settings.RailingHeight;
            double leftRailX = -directionFactor * settings.BeamWidth / 2.0;
            double rightRailX = directionFactor * (flightSpan + settings.BeamWidth / 2.0);
            AddSegment(segments,
                new StaircasePoint(leftRailX, bottomY),
                new StaircasePoint(leftRailX, topY),
                StaircaseSegmentStyle.Secondary);
            AddSegment(segments,
                new StaircasePoint(rightRailX, bottomY),
                new StaircasePoint(rightRailX, topY),
                StaircaseSegmentStyle.Secondary);
        }

        private static void AddScissorFlights( List<StaircaseSegment> segments, StaircaseSectionModel settings, StaircasePoint left, StaircasePoint right, HorizontalDirection direction)
        {
            AddLanding(segments, left, Reverse(direction), settings.Landing1Width, settings.BoardThickness, settings, true);
            AddLanding(segments, right, direction, settings.Landing2Width, settings.BoardThickness, settings, false);
            AddFlight(segments, left, direction, settings.StepNumber, settings);
            AddHighlightedFlight(
                segments, right, Reverse(direction), settings.StepNumber, settings);
        }

        private static void AddScissorTopLandings( List<StaircaseSegment> segments, StaircaseSectionModel settings, StaircasePoint leftLanding, StaircasePoint rightLanding, HorizontalDirection direction)
        {
            AddLanding(segments, rightLanding, direction, settings.Landing2Width, settings.BoardThickness, settings, false);
            AddLanding(segments, leftLanding, Reverse(direction), settings.Landing1Width, settings.BoardThickness, settings, true);
        }

        private static void AddFlight( List<StaircaseSegment> segments, StaircasePoint start, HorizontalDirection direction, int riserCount, StaircaseSectionModel settings)
        {
            AddSteps(segments, start, direction, riserCount, settings);
            StaircasePoint end = GetFlightEnd(start, direction, riserCount, settings);
            AddFlightBoard(segments, start, end, settings);
            AddRailing(segments, start, end, direction, settings);
        }

        private static void AddHighlightedFlight( List<StaircaseSegment> segments, StaircasePoint start, HorizontalDirection direction, int riserCount, StaircaseSectionModel settings)
        {
            int firstSegment = segments.Count;
            AddFlight(segments, start, direction, riserCount, settings);
            for (int index = firstSegment; index < segments.Count; index++)
            {
                StaircaseSegment segment = segments[index];
                segments[index] = new StaircaseSegment(
                    segment.Start,
                    segment.End,
                    StaircaseSegmentStyle.Secondary);
            }
        }

        private static void AddSteps( List<StaircaseSegment> segments, StaircasePoint start, HorizontalDirection direction, int riserCount, StaircaseSectionModel settings)
        {
            int directionFactor = (int)direction;
            for (int step = 0; step < riserCount; step++)
            {
                double x = start.X + directionFactor * step * settings.TreadRun;
                double y = start.Y + step * settings.CurrentStepHeight;
                var stepStart = new StaircasePoint(x, y);
                var stepTop = new StaircasePoint(
                    x,
                    start.Y + (step + 1) * settings.CurrentStepHeight);
                AddSegment(segments, stepStart, stepTop);

                if (step < riserCount - 1)
                {
                    var treadEnd = new StaircasePoint(
                        start.X + directionFactor * (step + 1) * settings.TreadRun,
                        stepTop.Y);
                    AddSegment(segments, stepTop, treadEnd);
                }
            }
        }

        private static StaircasePoint GetFlightEnd( StaircasePoint start, HorizontalDirection direction, int riserCount, StaircaseSectionModel settings)
        {
            int directionFactor = (int)direction;
            return new StaircasePoint(
                start.X + directionFactor * (riserCount - 1) * settings.TreadRun,
                start.Y + riserCount * settings.CurrentStepHeight);
        }

        private static void AddFlightBoard( List<StaircaseSegment> segments, StaircasePoint start, StaircasePoint end, StaircaseSectionModel settings)
        {
            double connectionY = end.Y - settings.BeamHeight * 2.0 / 3.0;
            var beamConnection = new StaircasePoint(end.X, connectionY);
            var lowerStart = new StaircasePoint(
                start.X,
                start.Y - settings.BoardThickness);
            AddSegment(segments, lowerStart, beamConnection);
        }

        private static void AddRailing( List<StaircaseSegment> segments, StaircasePoint start, StaircasePoint end, HorizontalDirection direction, StaircaseSectionModel settings)
        {
            if (settings.RailingHeight <= 0) return;

            double beamCenterOffset = settings.HasBeam2 ? (int)direction * settings.BeamWidth / 2.0 : 0.0;

            double startBeamCenterOffset = settings.HasBeam1 ? -(int)direction * settings.BeamWidth / 2.0 : 0.0;

            var railingStartBase = new StaircasePoint(
                start.X + startBeamCenterOffset,
                start.Y);
            var railingEndBase = new StaircasePoint(end.X + beamCenterOffset, end.Y);

            var railingStart = new StaircasePoint(
                railingStartBase.X,
                railingStartBase.Y + settings.RailingHeight);
            var railingEnd = new StaircasePoint(
                railingEndBase.X,
                railingEndBase.Y + settings.RailingHeight);
            AddSegment(segments, railingStartBase, railingStart, StaircaseSegmentStyle.Secondary);
            AddSegment(segments, railingStart, railingEnd, StaircaseSegmentStyle.Secondary);
            AddSegment(segments, railingEndBase, railingEnd, StaircaseSegmentStyle.Secondary);
        }

        private static void AddLanding( List<StaircaseSegment> segments, StaircasePoint start, HorizontalDirection direction, double width, double thickness, StaircaseSectionModel settings = null, bool isLanding1 = true)
        {
            if (width <= 0) return;

            double endX = start.X + (int)direction * width;
            var end = new StaircasePoint(endX, start.Y);
            var lowerStart = new StaircasePoint(start.X, start.Y - thickness);
            var lowerEnd = new StaircasePoint(endX, start.Y - thickness);

            AddSegment(segments, start, end);
            AddSegment(segments, end, lowerEnd);

            if (settings == null)
            {
                AddSegment(segments, lowerStart, lowerEnd);
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
                    AddSegment(segments,
                        new StaircasePoint(lineStartX, start.Y - thickness),
                        new StaircasePoint(lineEndX, start.Y - thickness));
                }
            }
        }

        private static void AddSecondaryLanding( List<StaircaseSegment> segments, StaircasePoint start, HorizontalDirection direction, double width, double thickness)
        {
            int firstSegment = segments.Count;
            AddLanding(segments, start, direction, width, thickness);
            for (int index = firstSegment; index < segments.Count; index++)
            {
                StaircaseSegment segment = segments[index];
                segments[index] = new StaircaseSegment(
                    segment.Start, segment.End, StaircaseSegmentStyle.Secondary);
            }
        }

        private static void AddLandingRailing( List<StaircaseSegment> segments, StaircasePoint start, HorizontalDirection direction, double width, StaircaseSectionModel settings)
        {
            if (width <= 0 || settings.RailingHeight <= 0) return;

            int directionFactor = (int)direction;
            double startX = start.X - (settings.HasBeam1 ? directionFactor * settings.BeamWidth / 2.0 : 0.0);
            double endX = start.X + directionFactor * width
                + (settings.HasBeam2 ? directionFactor * settings.BeamWidth / 2.0 : 0.0);
            var railingStartBase = new StaircasePoint(startX, start.Y);
            var railingEndBase = new StaircasePoint(endX, start.Y);
            var railingStart = new StaircasePoint(startX, start.Y + settings.RailingHeight);
            var railingEnd = new StaircasePoint(endX, start.Y + settings.RailingHeight);
            AddSegment(segments, railingStartBase, railingStart, StaircaseSegmentStyle.Secondary);
            AddSegment(segments, railingStart, railingEnd, StaircaseSegmentStyle.Secondary);
            AddSegment(segments, railingEndBase, railingEnd, StaircaseSegmentStyle.Secondary);
        }

        private static void AddSupports( List<StaircaseSegment> segments, StaircaseSectionModel settings, StaircasePoint first, StaircasePoint second, HorizontalDirection direction, bool secondHasIncomingFlight = true)
        {
            AddLanding1Supports(segments, settings, first, direction);
            AddLanding2Support(segments, settings, second, direction, secondHasIncomingFlight);
        }

        private static void AddLanding1Supports( List<StaircaseSegment> segments, StaircaseSectionModel settings, StaircasePoint anchor, HorizontalDirection direction)
        {
            int directionFactor = (int)direction;

            if (settings.GirderHeight > 0 && settings.GirderWidth > 0)
            {
                double outerX = anchor.X - directionFactor * settings.Landing1Width;
                var underside1 = new StaircasePoint(
                    outerX,
                    anchor.Y - settings.BoardThickness);
                var underside2 = new StaircasePoint(
                    outerX + directionFactor * settings.GirderWidth,
                    anchor.Y - settings.BoardThickness);
                AddDownstand(segments, underside1, underside2,
                    anchor.Y - settings.GirderHeight);
            }

            if (settings.HasBeam1)
            {
                var underside1 = new StaircasePoint(
                    anchor.X - directionFactor * settings.BeamWidth,
                    anchor.Y - settings.BoardThickness);
                var underside2 = new StaircasePoint(
                    anchor.X,
                    anchor.Y - settings.BoardThickness);
                AddDownstand(segments, underside1, underside2,
                    anchor.Y - settings.BeamHeight);
            }
        }

        private static void AddLanding2Support(List<StaircaseSegment> segments, StaircaseSectionModel settings, StaircasePoint anchor, HorizontalDirection direction, bool hasIncomingFlight = true)
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
            }

            if (settings.GirderHeight <= 0 || settings.GirderWidth <= 0) return;
            double outerX = anchor.X + directionFactor * settings.Landing2Width;
            var outerUnderside1 = new StaircasePoint(
                outerX - directionFactor * settings.GirderWidth,
                anchor.Y - settings.BoardThickness);
            var outerUnderside2 = new StaircasePoint(
                outerX,
                anchor.Y - settings.BoardThickness);
            AddDownstand(segments, outerUnderside1, outerUnderside2,
                anchor.Y - settings.GirderHeight);
        }

        private static void AddDownstand( List<StaircaseSegment> segments, StaircasePoint firstUnderside, StaircasePoint secondUnderside, double bottomY)
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
                if (envelope?.Settings == null) return defaults.Copy();

                StaircaseSectionModel settings = envelope.Settings;

                // B3: Chỉ trả cấu hình hợp lệ; nếu không dùng giá trị mặc định an toàn.
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
