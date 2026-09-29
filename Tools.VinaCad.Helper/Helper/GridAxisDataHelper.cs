using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Tools.Model;

namespace Tools.VinaCad.Helper.Helper
{
    public static class GridAxisDataHelper
    {
        private const int MaximumBayCount = 200;
        private const double MinimumSpacing = 1e-6;

        public static bool TryParseSpacings(
            string? text,
            string fieldName,
            out IReadOnlyList<double> spacings,
            out string error)
        {
            spacings = Array.Empty<double>();
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(text))
            {
                error = $"{fieldName} không được để trống.";
                return false;
            }

            string normalized = Regex.Replace(text.Trim(), @"\s*([xX×*])\s*", "$1");
            string[] tokens = Regex.Split(normalized, @"[\s;]+")
                .Where(token => !string.IsNullOrWhiteSpace(token))
                .ToArray();

            var values = new List<double>();

            for (int tokenIndex = 0; tokenIndex < tokens.Length; tokenIndex++)
            {
                string token = tokens[tokenIndex];
                Match repeated = Regex.Match(token, @"^(\d+)[xX×*](.+)$");
                int repeatCount = 1;
                string valueToken = token;

                if (repeated.Success)
                {
                    if (!int.TryParse(repeated.Groups[1].Value, out repeatCount) || repeatCount <= 0)
                    {
                        error = $"{fieldName}: hệ số lặp tại mục {tokenIndex + 1} không hợp lệ.";
                        return false;
                    }

                    valueToken = repeated.Groups[2].Value;
                }

                if (!TryParsePositiveNumber(valueToken, out double value))
                {
                    error = $"{fieldName}: '{token}' không phải khoảng cách dương hợp lệ.";
                    return false;
                }

                if (values.Count + repeatCount > MaximumBayCount)
                {
                    error = $"{fieldName} chỉ hỗ trợ tối đa {MaximumBayCount} nhịp.";
                    return false;
                }

                for (int i = 0; i < repeatCount; i++)
                    values.Add(value);
            }

            if (values.Count == 0)
            {
                error = $"{fieldName} phải có ít nhất một nhịp.";
                return false;
            }

            spacings = new ReadOnlyCollection<double>(values);
            return true;
        }

        public static IReadOnlyList<double> BuildStations(IEnumerable<double> spacings)
        {
            var stations = new List<double> { 0 };
            double current = 0;

            foreach (double spacing in spacings)
            {
                current += spacing;
                stations.Add(current);
            }

            return stations;
        }

        public static string ToAlphabeticLabel(int zeroBasedIndex)
        {
            if (zeroBasedIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(zeroBasedIndex));

            int value = zeroBasedIndex + 1;
            string label = string.Empty;

            while (value > 0)
            {
                value--;
                label = (char)('A' + value % 26) + label;
                value /= 26;
            }

            return label;
        }

        public static bool TryBuildLabel(
            GridAxisLabelType type,
            string start,
            int offset,
            out string label,
            out string error)
        {
            label = string.Empty;
            error = string.Empty;

            if (offset < 0)
            {
                error = "Vị trí nhãn không hợp lệ.";
                return false;
            }

            if (type == GridAxisLabelType.Number)
            {
                if (!int.TryParse(start, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)
                    || number < 0
                    || number > int.MaxValue - offset)
                {
                    error = "Ký tự bắt đầu của trục số phải là số nguyên không âm.";
                    return false;
                }

                label = (number + offset).ToString(CultureInfo.InvariantCulture);
                return true;
            }

            string letters = (start ?? string.Empty).Trim().ToUpperInvariant();
            if (letters.Length == 0 || letters.Any(c => c < 'A' || c > 'Z'))
            {
                error = "Ký tự bắt đầu của trục chữ phải gồm A-Z.";
                return false;
            }

            long value = 0;
            foreach (char letter in letters)
            {
                value = value * 26 + (letter - 'A' + 1);
                if (value > int.MaxValue)
                {
                    error = "Ký tự bắt đầu của trục chữ quá lớn.";
                    return false;
                }
            }

            value += offset;
            if (value > int.MaxValue)
            {
                error = "Số lượng trục vượt quá giới hạn nhãn chữ.";
                return false;
            }

            label = ToAlphabeticLabel((int)value - 1);
            return true;
        }

        private static bool TryParsePositiveNumber(string token, out double value)
        {
            NumberStyles styles = NumberStyles.Float | NumberStyles.AllowThousands;
            bool parsed = double.TryParse(token, styles, CultureInfo.CurrentCulture, out value)
                || double.TryParse(token, styles, CultureInfo.InvariantCulture, out value)
                || double.TryParse(token.Replace(',', '.'), styles, CultureInfo.InvariantCulture, out value);

            return parsed
                && !double.IsNaN(value)
                && !double.IsInfinity(value)
                && value > MinimumSpacing;
        }
    }
}
