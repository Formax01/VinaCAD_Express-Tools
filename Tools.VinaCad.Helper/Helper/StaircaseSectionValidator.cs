using System;
using Tools.Model;
using Tools.Resources.Language;

namespace Tools.VinaCad.Helper.Helper
{
    public static class StaircaseSectionValidator
    {
        private static string InvalidPlanDimensionsMessage => Lang.LTP_Validation_ChieuCaoTangChieuRongBacPhaiLonHon0ChieuRong;
        private static string InvalidCountsMessage => Lang.LTP_Validation_SoTangPhaiTu1VaTongSoBacMoiTangPhai;
        private static string InvalidFirstFlightMessage => Lang.LTP_Validation_SoBacVeDauPhaiNamTrongKhoangTu1DenNho;
        private static string InvalidStructureDimensionsMessage => Lang.LTP_Validation_KichThuocKetCauKhongDuocAmVaChieuDayBanPhai;
        private static string InvalidBeamDimensionsMessage => Lang.LTP_Validation_ChieuCaoVaChieuRongDamPhuPhaiLonHon0Khi;
        private static string InvalidStaircaseTypeMessage => Lang.LTP_Validation_KieuCauThangKhongHopLe;

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