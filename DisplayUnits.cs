using System;
using System.Globalization;

namespace UT70Dashboard
{
    internal struct MeasurementText
    {
        public readonly string Number, Unit;
        public MeasurementText(string number, string unit) { Number = number; Unit = unit; }
        public string Text { get { return Number + " " + Unit; } }
    }

    internal struct MeasurementUnit
    {
        public readonly double Factor;
        public readonly string Symbol;
        public MeasurementUnit(double factor, string symbol) { Factor = factor; Symbol = symbol; }
        public string FormatNumber(double baseValue, bool trimZeros)
        {
            return DisplayUnits.Number(baseValue / Factor, trimZeros);
        }
    }

    internal static class DisplayUnits
    {
        private static readonly double[] Factors = { 1e-6, 1e-3, 1, 1e3, 1e6, 1e9 };
        private static readonly string[] Prefixes = { "μ", "m", "", "k", "M", "G" };

        public static MeasurementText Format(double baseValue, string baseUnit)
        {
            MeasurementUnit unit = Select(baseValue, baseUnit);
            return new MeasurementText(unit.FormatNumber(baseValue, false), unit.Symbol);
        }

        public static MeasurementUnit Select(double referenceValue, string baseUnit)
        {
            double magnitude = Math.Abs(referenceValue);
            int index = 2;
            // 零值、无效值与无穷大沿用基本单位，避免无数据时单位跳动。
            if (magnitude > 0 && !Double.IsNaN(magnitude) && !Double.IsInfinity(magnitude))
            {
                while (index > 0 && magnitude < Factors[index]) index--;
                // 提前处理舍入进位，避免出现 1000.000 mA 等显示。
                while (index < Factors.Length - 1 &&
                    Math.Round(magnitude / Factors[index], 3, MidpointRounding.AwayFromZero) >= 1000) index++;
            }
            return new MeasurementUnit(Factors[index], Prefixes[index] + baseUnit);
        }

        internal static string Number(double value, bool trimZeros)
        {
            if (Double.IsNaN(value)) return "—";
            if (Double.IsPositiveInfinity(value)) return "∞";
            if (Double.IsNegativeInfinity(value)) return "−∞";
            if (value == 0) value = 0;
            double magnitude = Math.Abs(value);
            string format = magnitude >= 1000 || (magnitude > 0 && magnitude < 0.001)
                ? "0.###E+0" : trimZeros ? "0.###" : "0.000";
            return value.ToString(format, CultureInfo.InvariantCulture);
        }
    }
}
