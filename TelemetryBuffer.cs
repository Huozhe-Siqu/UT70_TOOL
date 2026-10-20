using System;
using System.Collections.Generic;

namespace UT70Dashboard
{
    internal sealed class ChartSample
    {
        public readonly DateTime TimeUtc;
        public readonly float Voltage, Current, Power, Wh, Ah, Resistance, Dp, Dm;

        public ChartSample(DateTime timeUtc, float voltage, float current, float power,
            float wh, float ah, float resistance, float dp, float dm)
        {
            TimeUtc = timeUtc;
            Voltage = voltage; Current = current; Power = power;
            Wh = wh; Ah = ah; Resistance = resistance; Dp = dp; Dm = dm;
        }

        public float Value(ChartMetric metric)
        {
            return metric == ChartMetric.Current ? Current : metric == ChartMetric.Power ? Power : Voltage;
        }
    }

    internal sealed class TelemetrySnapshot
    {
        public ChartSample Latest;
        public ChartSample[] Points;
        public DateTime StartUtc, EndUtc;
        public float PeakV, PeakI, PeakP;
        public long Total, Rejected, Version;
    }

    internal sealed class RangeStatistics
    {
        public int Count;
        public double Average;
        public float Minimum, Maximum;
        public DateTime StartUtc, EndUtc;
    }

    internal sealed class TelemetryBuffer
    {
        // 固定容量覆盖最高采样率下最近 60 秒，界面缩放不会增加占用。
        internal const int Capacity = 65536;
        private readonly ChartSample[] _ring = new ChartSample[Capacity];
        private readonly object _gate = new object();
        private int _start, _count;
        private long _total, _rejected, _version;
        private float _peakV, _peakI, _peakP;
        private ChartSample _latest;

        public long Version { get { lock (_gate) return _version; } }
        public long Total { get { lock (_gate) return _total; } }

        public void Append(ChartSample[] samples)
        {
            lock (_gate)
            {
                foreach (ChartSample sample in samples)
                {
                    if (sample == null || !Finite(sample.Voltage) || !Finite(sample.Current) || !Finite(sample.Power))
                    {
                        _rejected++; _version++;
                        continue;
                    }
                    if (_total == 0)
                    {
                        _peakV = sample.Voltage; _peakI = sample.Current; _peakP = sample.Power;
                    }
                    _peakV = Math.Max(_peakV, sample.Voltage);
                    _peakI = Math.Max(_peakI, sample.Current);
                    _peakP = Math.Max(_peakP, sample.Power);
                    if (_count == Capacity)
                    {
                        _ring[_start] = null;
                        _start = (_start + 1) % Capacity;
                        _count--;
                    }
                    _ring[(_start + _count) % Capacity] = sample;
                    _count++;
                    _latest = sample;
                    _total++; _version++;
                    DateTime cutoff = sample.TimeUtc.AddSeconds(-60);
                    while (_count > 1 && At(0).TimeUtc < cutoff)
                    {
                        _ring[_start] = null;
                        _start = (_start + 1) % Capacity;
                        _count--;
                    }
                }
            }
        }

        public void ResetPeaks()
        {
            lock (_gate)
            {
                _peakV = _latest == null ? 0 : _latest.Voltage;
                _peakI = _latest == null ? 0 : _latest.Current;
                _peakP = _latest == null ? 0 : _latest.Power;
                _version++;
            }
        }

        public RangeStatistics CalculateRangeStatistics(DateTime startUtc, DateTime endUtc, ChartMetric metric)
        {
            if (endUtc < startUtc)
            {
                DateTime swap = startUtc;
                startUtc = endUtc;
                endUtc = swap;
            }

            lock (_gate)
            {
                RangeStatistics result = new RangeStatistics
                {
                    StartUtc = startUtc,
                    EndUtc = endUtc,
                    Minimum = Single.PositiveInfinity,
                    Maximum = Single.NegativeInfinity
                };
                double sum = 0;
                for (int i = 0; i < _count; i++)
                {
                    ChartSample sample = At(i);
                    if (sample.TimeUtc < startUtc || sample.TimeUtc > endUtc) continue;
                    float value = sample.Value(metric);
                    sum += value;
                    result.Minimum = Math.Min(result.Minimum, value);
                    result.Maximum = Math.Max(result.Maximum, value);
                    result.Count++;
                }
                if (result.Count > 0) result.Average = sum / result.Count;
                else result.Minimum = result.Maximum = 0;
                return result;
            }
        }

        public TelemetrySnapshot Capture(DateTime endUtc, long windowTicks, ChartMetric metric, int budget)
        {
            lock (_gate)
            {
            long startTicks = windowTicks >= endUtc.Ticks ? 0 : endUtc.Ticks - Math.Max(1, windowTicks);
            DateTime startUtc = new DateTime(startTicks, endUtc.Kind);
                TelemetrySnapshot snapshot = new TelemetrySnapshot
                {
                    Latest = _latest, PeakV = _peakV, PeakI = _peakI, PeakP = _peakP,
                    Total = _total, Rejected = _rejected, Version = _version,
                    StartUtc = startUtc, EndUtc = endUtc
                };
                int first = 0;
                while (first < _count && At(first).TimeUtc < startUtc) first++;
                budget = Math.Max(8, budget);
                List<ChartSample> points = new List<ChartSample>(Math.Min(budget, _count - first));
                if (_count - first <= budget)
                {
                    for (int i = first; i < _count; i++) points.Add(At(i));
                }
                else
                {
                    // 每个时间分桶保留最小、最大值，防止等间隔抽点遗漏尖峰。
                    int bins = (budget - 2) / 2;
                    int currentBin = -1, minIndex = -1, maxIndex = -1;
                    double ticksPerBin = (endUtc - startUtc).Ticks / (double)bins;
                    AddUnique(points, At(first));
                    for (int i = first; i < _count; i++)
                    {
                        ChartSample sample = At(i);
                        int bin = Math.Min(bins - 1, Math.Max(0, (int)((sample.TimeUtc - startUtc).Ticks / ticksPerBin)));
                        if (bin != currentBin)
                        {
                            FlushExtrema(points, minIndex, maxIndex);
                            currentBin = bin; minIndex = maxIndex = i;
                        }
                        else
                        {
                            if (sample.Value(metric) < At(minIndex).Value(metric)) minIndex = i;
                            if (sample.Value(metric) > At(maxIndex).Value(metric)) maxIndex = i;
                        }
                    }
                    FlushExtrema(points, minIndex, maxIndex);
                    AddUnique(points, At(_count - 1));
                }
                snapshot.Points = points.ToArray();
                return snapshot;
            }
        }

        private ChartSample At(int index) { return _ring[(_start + index) % Capacity]; }

        private void FlushExtrema(List<ChartSample> points, int minIndex, int maxIndex)
        {
            if (minIndex < 0) return;
            AddUnique(points, At(Math.Min(minIndex, maxIndex)));
            AddUnique(points, At(Math.Max(minIndex, maxIndex)));
        }

        private static void AddUnique(List<ChartSample> points, ChartSample sample)
        {
            if (points.Count == 0 || !Object.ReferenceEquals(points[points.Count - 1], sample)) points.Add(sample);
        }

        private static bool Finite(float value) { return !Single.IsNaN(value) && !Single.IsInfinity(value); }
    }
}
