#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Coclico.Services.Algorithms;

/// <summary>
/// T-Digest implementation for streaming quantile estimation.
/// Algorithm: Dunning &amp; Ertl 2019.
/// </summary>
public sealed class TDigest
{
    private readonly double _compression;
    private readonly List<Centroid> _centroids = [];
    private double _totalWeight;
    private double _min = double.PositiveInfinity;
    private double _max = double.NegativeInfinity;

    private readonly List<(double Value, double Weight)> _buffer = [];
    private const int BufferCapacity = 200;

    public TDigest(double compression = 100) => _compression = compression;

    public long Count => (long)_totalWeight;

    public void Add(double value, double weight = 1.0)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return;
        _buffer.Add((value, weight));
        if (_buffer.Count >= BufferCapacity) FlushBuffer();
    }

    public double Quantile(double q)
    {
        if (_buffer.Count > 0) FlushBuffer();
        if (_centroids.Count == 0) return 0;
        if (_centroids.Count == 1) return _centroids[0].Mean;
        if (q <= 0) return _min;
        if (q >= 1) return _max;

        double target = q * _totalWeight;
        double cumulative = 0;

        for (int i = 0; i < _centroids.Count; i++)
        {
            double halfW = _centroids[i].Weight / 2.0;
            if (cumulative + halfW >= target)
            {
                if (i == 0)
                    return _min + (target / halfW) * (_centroids[0].Mean - _min);
                var prev = _centroids[i - 1];
                double delta = _centroids[i].Mean - prev.Mean;
                double t = (target - cumulative) / halfW;
                return prev.Mean + delta * t;
            }
            cumulative += _centroids[i].Weight;
        }
        return _max;
    }

    public (double P50, double P95, double P99) GetQuantiles() =>
        (Quantile(0.50), Quantile(0.95), Quantile(0.99));

    private void FlushBuffer()
    {
        foreach (var (value, weight) in _buffer)
        {
            _min = Math.Min(_min, value);
            _max = Math.Max(_max, value);
            MergeOne(value, weight);
        }
        _buffer.Clear();
        Compress();
    }

    private void MergeOne(double value, double weight)
    {
        int idx = FindClosestCentroid(value);
        if (idx < 0 || _centroids.Count == 0)
        {
            _centroids.Add(new Centroid(value, weight));
            _totalWeight += weight;
            _centroids.Sort((a, b) => a.Mean.CompareTo(b.Mean));
            return;
        }

        double qLeft = CumulativeWeight(idx) / _totalWeight;
        double maxW = 4.0 * _compression * qLeft * (1 - qLeft) / _centroids.Count;

        if (_centroids[idx].Weight + weight <= Math.Max(1, maxW))
        {
            var c = _centroids[idx];
            double newWeight = c.Weight + weight;
            double newMean = (c.Mean * c.Weight + value * weight) / newWeight;
            _centroids[idx] = new Centroid(newMean, newWeight);
            _totalWeight += weight;
        }
        else
        {
            _centroids.Add(new Centroid(value, weight));
            _totalWeight += weight;
            _centroids.Sort((a, b) => a.Mean.CompareTo(b.Mean));
        }
    }

    private void Compress()
    {
        if (_centroids.Count < 2) return;
        _centroids.Sort((a, b) => a.Mean.CompareTo(b.Mean));
        var compressed = new List<Centroid> { _centroids[0] };

        for (int i = 1; i < _centroids.Count; i++)
        {
            var last = compressed[^1];
            double qLeft = compressed.Sum(c => c.Weight) / _totalWeight;
            double maxW = 4.0 * _compression * qLeft * (1 - qLeft) / _centroids.Count;

            if (last.Weight + _centroids[i].Weight <= Math.Max(1, maxW))
            {
                double newWeight = last.Weight + _centroids[i].Weight;
                double newMean = (last.Mean * last.Weight + _centroids[i].Mean * _centroids[i].Weight) / newWeight;
                compressed[^1] = new Centroid(newMean, newWeight);
            }
            else
            {
                compressed.Add(_centroids[i]);
            }
        }

        _centroids.Clear();
        _centroids.AddRange(compressed);
    }

    private int FindClosestCentroid(double value)
    {
        if (_centroids.Count == 0) return -1;
        int best = 0;
        double bestDist = Math.Abs(_centroids[0].Mean - value);
        for (int i = 1; i < _centroids.Count; i++)
        {
            double d = Math.Abs(_centroids[i].Mean - value);
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return best;
    }

    private double CumulativeWeight(int idx)
    {
        double sum = 0;
        for (int i = 0; i < idx; i++) sum += _centroids[i].Weight;
        return sum + _centroids[idx].Weight / 2.0;
    }

    private readonly record struct Centroid(double Mean, double Weight);
}
