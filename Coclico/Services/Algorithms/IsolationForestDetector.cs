#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Coclico.Services.Algorithms;

/// <summary>
/// Incremental Isolation Forest for anomaly detection.
/// Algorithm: Liu et al. 2008 — adapted for streaming.
/// </summary>
public sealed class IsolationForestDetector
{
    private readonly int _numTrees;
    private readonly int _subsampleSize;
    private readonly double _anomalyThreshold;
    private readonly Random _rng = new();

    private readonly List<double[]> _buffer = [];
    private IsolationTree[]? _forest;
    private int _featureCount;

    private const int MinSamplesForFit = 20;
    private const int MaxBufferSize = 500;

    public bool IsReady => _forest is not null;

    public IsolationForestDetector(
        int numTrees = 100,
        int subsampleSize = 64,
        double anomalyThreshold = 0.65)
    {
        _numTrees = numTrees;
        _subsampleSize = subsampleSize;
        _anomalyThreshold = anomalyThreshold;
    }

    public (double Score, bool IsAnomaly, string[] Details) Observe(params double[] features)
    {
        _featureCount = features.Length;
        _buffer.Add((double[])features.Clone());

        if (_buffer.Count > MaxBufferSize)
            _buffer.RemoveRange(0, _buffer.Count - MaxBufferSize);

        if (_buffer.Count >= MinSamplesForFit && (_forest is null || _buffer.Count % 50 == 0))
            Fit();

        if (_forest is null)
            return (-1, false, Array.Empty<string>());

        double score = ComputeAnomalyScore(features);
        bool isAnomaly = score > _anomalyThreshold;

        var details = new List<string>();
        if (isAnomaly)
        {
            for (int i = 0; i < features.Length; i++)
            {
                var mean = _buffer.Average(b => b[i]);
                var stddev = Math.Sqrt(_buffer.Average(b => (b[i] - mean) * (b[i] - mean)));
                if (stddev > 1e-9)
                {
                    var z = (features[i] - mean) / stddev;
                    if (Math.Abs(z) > 2.0)
                        details.Add($"Feature[{i}] z={z:+0.00;-0.00} (val={features[i]:F1}, mu={mean:F1}, sigma={stddev:F1})");
                }
            }
        }

        return (score, isAnomaly, details.ToArray());
    }

    private void Fit()
    {
        _forest = new IsolationTree[_numTrees];
        int maxDepth = (int)Math.Ceiling(Math.Log2(Math.Max(2, _subsampleSize)));

        for (int i = 0; i < _numTrees; i++)
        {
            var subsample = SampleWithReplacement(_subsampleSize);
            _forest[i] = BuildTree(subsample, 0, maxDepth);
        }
    }

    private double ComputeAnomalyScore(double[] point)
    {
        if (_forest is null) return 0;
        double avgPathLength = 0;
        foreach (var tree in _forest)
            avgPathLength += PathLength(point, tree, 0);
        avgPathLength /= _forest.Length;
        double c = AveragePathLengthBST(_subsampleSize);
        return Math.Pow(2, -avgPathLength / c);
    }

    private double PathLength(double[] point, IsolationTree node, int depth)
    {
        if (node.IsLeaf)
            return depth + AveragePathLengthBST(node.Size);
        return point[node.SplitFeature] < node.SplitValue
            ? PathLength(point, node.Left!, depth + 1)
            : PathLength(point, node.Right!, depth + 1);
    }

    private IsolationTree BuildTree(List<double[]> data, int depth, int maxDepth)
    {
        if (depth >= maxDepth || data.Count <= 1)
            return new IsolationTree { IsLeaf = true, Size = data.Count };

        int feature = _rng.Next(_featureCount);
        double min = data.Min(d => d[feature]);
        double max = data.Max(d => d[feature]);

        if (Math.Abs(max - min) < 1e-10)
            return new IsolationTree { IsLeaf = true, Size = data.Count };

        double splitValue = min + _rng.NextDouble() * (max - min);
        var left = data.Where(d => d[feature] < splitValue).ToList();
        var right = data.Where(d => d[feature] >= splitValue).ToList();

        if (left.Count == 0 || right.Count == 0)
            return new IsolationTree { IsLeaf = true, Size = data.Count };

        return new IsolationTree
        {
            SplitFeature = feature,
            SplitValue = splitValue,
            Left = BuildTree(left, depth + 1, maxDepth),
            Right = BuildTree(right, depth + 1, maxDepth),
        };
    }

    private List<double[]> SampleWithReplacement(int count)
    {
        var sample = new List<double[]>(count);
        for (int i = 0; i < count; i++)
            sample.Add(_buffer[_rng.Next(_buffer.Count)]);
        return sample;
    }

    private static double AveragePathLengthBST(int n) =>
        n <= 1 ? 0 : 2.0 * (Math.Log(n - 1) + 0.5772156649) - 2.0 * (n - 1.0) / n;

    private sealed class IsolationTree
    {
        public bool IsLeaf;
        public int Size;
        public int SplitFeature;
        public double SplitValue;
        public IsolationTree? Left;
        public IsolationTree? Right;
    }
}
