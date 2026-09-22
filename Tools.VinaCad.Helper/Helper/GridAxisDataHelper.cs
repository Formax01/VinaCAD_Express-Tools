using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

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
