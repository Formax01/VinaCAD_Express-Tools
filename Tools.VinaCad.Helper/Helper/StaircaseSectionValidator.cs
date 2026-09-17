using System;
using Tools.Model;

namespace Tools.VinaCad.Helper.Helper
{
    public static class StaircaseSectionValidator
    {
        private const string InvalidPlanDimensionsMessage =
            "Chiều cao tầng, chiều rộng bậc phải lớn hơn 0; chiều rộng chiếu nghỉ không được âm.";

        private const string InvalidCountsMessage =
            "Số tầng phải từ 1 và tổng số bậc mỗi tầng phải từ 2.";

        private const string InvalidFirstFlightMessage =
            "Số bậc vế đầu phải nằm trong khoảng từ 1 đến nhỏ hơn tổng số bậc.";

        private const string InvalidStructureDimensionsMessage =
            "Kích thước kết cấu không được âm và chiều dày bản phải lớn hơn 0.";

        private const string InvalidBeamDimensionsMessage =
            "Chiều cao và chiều rộng dầm phụ phải lớn hơn 0 khi bật Dầm 1 hoặc Dầm 2.";

        private const string InvalidStaircaseTypeMessage =
            "Kiểu cầu thang không hợp lệ.";

        public static bool TryValidate( StaircaseSectionModel settings, out string message)
        {
            ArgumentNullException.ThrowIfNull(settings);

            if (!Enum.IsDefined(typeof(StaircaseSectionType), settings.Type))
            {
                message = InvalidStaircaseTypeMessage;
                return false;
            }

            if (!HasValidPlanDimensions(settings))
            {
                message = InvalidPlanDimensionsMessage;
                return false;
            }

            if (!HasValidCounts(settings))
            {
                message = InvalidCountsMessage;
                return false;
            }

            if (!HasValidFirstFlight(settings))
            {
                message = InvalidFirstFlightMessage;
                return false;
            }

            if (!HasValidStructureDimensions(settings))
            {
                message = InvalidStructureDimensionsMessage;
                return false;
            }

            if (!HasValidEnabledBeams(settings))
            {
                message = InvalidBeamDimensionsMessage;
                return false;
            }

            message = string.Empty;
            return true;
        }

        private static bool HasValidPlanDimensions( StaircaseSectionModel settings)
        {
            return settings.StoreyHeight > 0
                && settings.TreadRun > 0
                && settings.Landing1Width >= 0
                && settings.Landing2Width >= 0;
        }

        private static bool HasValidCounts( StaircaseSectionModel settings)
        {
            return settings.StoreyNumber >= 1
                && settings.StepNumber >= 2;
        }

        private static bool HasValidFirstFlight( StaircaseSectionModel settings)
        {
            return settings.Type != StaircaseSectionType.DoubleFlight
                || settings.FirstFlightStepNumber >= 1
                && settings.FirstFlightStepNumber < settings.StepNumber;
        }

        private static bool HasValidStructureDimensions( StaircaseSectionModel settings)
        {
            return settings.BoardThickness > 0
                && settings.RailingHeight >= 0
                && settings.GirderHeight >= 0
                && settings.GirderWidth >= 0
                && settings.BeamHeight >= 0
                && settings.BeamWidth >= 0;
        }

        private static bool HasValidEnabledBeams( StaircaseSectionModel settings)
        {
            return (!settings.HasBeam1 && !settings.HasBeam2)
                || settings.BeamHeight > 0
                && settings.BeamWidth > 0;
        }

        private static bool Fail( string validationMessage, out string message)
        {
            message = validationMessage;
            return false;
        }
    }
}