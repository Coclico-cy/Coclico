#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Coclico.Services;

public sealed class SeverityClassifier
{
    private static readonly string[] Classes = { "Low", "Medium", "High", "Critical" };

    private readonly Dictionary<string, (double Mean, double Variance)[]> _params = new();
    private readonly Dictionary<string, double> _logPrior = new();

    private bool _trained;
    private int _sampleCount;

    private static readonly string ModelPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Coclico", "severity_model.json");

    public bool IsTrained => _trained;
    public int SampleCount => _sampleCount;

    public SeverityClassifier()
    {
        TryLoadFromDisk();
    }

    public void TrainFromHistory(IReadOnlyList<CodeHotspot> hotspots)
    {
        if (hotspots.Count < 4) return;

        var byClass = new Dictionary<string, List<double[]>>();
        foreach (var cls in Classes) byClass[cls] = new List<double[]>();

        foreach (var h in hotspots)
        {
            if (!byClass.ContainsKey(h.Severity)) continue;
            byClass[h.Severity].Add(ExtractFeatures(h.CyclomaticComplexity, h.LineCount));
        }

        _params.Clear();
        _logPrior.Clear();

        int total = hotspots.Count;
        foreach (var cls in Classes)
        {
            var samples = byClass[cls];
            int n = Math.Max(1, samples.Count);

            _logPrior[cls] = Math.Log((double)(samples.Count + 1) / (total + Classes.Length));

            int featureCount = 3;
            var classParams = new (double Mean, double Variance)[featureCount];

            for (int f = 0; f < featureCount; f++)
            {
                double mean = 0;
                foreach (var s in samples) mean += s[f];
                mean /= n;

                double variance = 1e-6;
                foreach (var s in samples) variance += (s[f] - mean) * (s[f] - mean);
                variance = variance / n + 1e-6;

                classParams[f] = (mean, variance);
            }
            _params[cls] = classParams;
        }

        _trained = true;
        _sampleCount = hotspots.Count;
        PersistToDisk();

        LoggingService.LogInfo($"[SeverityClassifier] Modèle entraîné sur {hotspots.Count} hotspots.");
    }

    public string Classify(int cyclomaticComplexity, int lineCount)
    {
        if (!_trained)
            return FallbackClassify(cyclomaticComplexity, lineCount);

        var features = ExtractFeatures(cyclomaticComplexity, lineCount);
        string bestClass = "Low";
        double bestLogPost = double.NegativeInfinity;

        foreach (var cls in Classes)
        {
            double logPost = _logPrior[cls];
            var clsParams = _params[cls];

            for (int f = 0; f < features.Length; f++)
            {
                var (mean, variance) = clsParams[f];

                logPost += -0.5 * Math.Log(2 * Math.PI * variance)
                           - (features[f] - mean) * (features[f] - mean) / (2 * variance);
            }

            if (logPost > bestLogPost)
            {
                bestLogPost = logPost;
                bestClass = cls;
            }
        }

        return bestClass;
    }

    private static double[] ExtractFeatures(int cc, int lineCount)
    {
        return new double[]
        {
            cc,
            lineCount,
            cc / 2.0,
        };
    }

    private static string FallbackClassify(int cc, int lineCount)
    {
        var sev = cc switch
        {
            <= 5 => "Low",
            <= 10 => "Medium",
            <= 20 => "High",
            _ => "Critical",
        };

        if (lineCount > 80 && sev != "Critical")
            sev = ElevateOnce(sev);
        return sev;
    }

    private static string ElevateOnce(string sev) => sev switch
    {
        "Low" => "Medium",
        "Medium" => "High",
        "High" => "Critical",
        _ => "Critical",
    };

    private void PersistToDisk()
    {
        try
        {
            var envelope = new ModelEnvelope
            {
                SampleCount = _sampleCount,
                LogPrior = _logPrior,
                Params = new Dictionary<string, double[][]>(),
            };
            foreach (var kv in _params)
            {
                var arr = new double[kv.Value.Length][];
                for (int i = 0; i < kv.Value.Length; i++)
                    arr[i] = new[] { kv.Value[i].Mean, kv.Value[i].Variance };
                envelope.Params[kv.Key] = arr;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(ModelPath)!);
            File.WriteAllText(ModelPath, JsonSerializer.Serialize(envelope));
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "SeverityClassifier.PersistToDisk");
        }
    }

    private void TryLoadFromDisk()
    {
        try
        {
            if (!File.Exists(ModelPath)) return;
            var envelope = JsonSerializer.Deserialize<ModelEnvelope>(File.ReadAllText(ModelPath));
            if (envelope?.Params == null || envelope.LogPrior == null) return;

            _sampleCount = envelope.SampleCount;
            foreach (var kv in envelope.LogPrior) _logPrior[kv.Key] = kv.Value;
            foreach (var kv in envelope.Params)
            {
                var arr = new (double Mean, double Variance)[kv.Value.Length];
                for (int i = 0; i < kv.Value.Length; i++)
                    arr[i] = (kv.Value[i][0], kv.Value[i][1]);
                _params[kv.Key] = arr;
            }
            _trained = _params.Count == Classes.Length;
            if (_trained)
                LoggingService.LogInfo($"[SeverityClassifier] Modèle chargé ({_sampleCount} échantillons).");
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, "SeverityClassifier.TryLoadFromDisk");
        }
    }

    private sealed class ModelEnvelope
    {
        public int SampleCount { get; set; }
        public Dictionary<string, double> LogPrior { get; set; } = new();
        public Dictionary<string, double[][]> Params { get; set; } = new();
    }
}
