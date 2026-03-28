#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Coclico.Services.Algorithms;

namespace Coclico.Services;

public sealed class RagService
{
    private struct ChunkData
    {
        public string Source;
        public string Section;
        public string Text;
        public Dictionary<string, int> RawCounts;
        public Dictionary<string, double> Tf;
        public double TfIdfNorm;
        public int Length;
    }

    private ChunkData[] _data = [];
    private Dictionary<string, double> _idf = [];
    private double _avgLength;

    private static readonly Regex HeaderRegex = new(@"^#{1,3}\s+(.+)$", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex TokenizerRegex = new(@"[^\wàâäéèêëîïôùûüçœæ]+", RegexOptions.Compiled);

    public int ChunkCount => _data.Length;

    public void BuildIndex(string docsPath)
    {
        if (!Directory.Exists(docsPath)) return;

        var rawChunks = new List<(string Source, string Section, string Text)>();
        foreach (var file in Directory.GetFiles(docsPath, "*.md", SearchOption.TopDirectoryOnly))
            rawChunks.AddRange(SplitIntoChunks(
                Path.GetFileNameWithoutExtension(file),
                File.ReadAllText(file)));

        _data = new ChunkData[rawChunks.Count];
        for (int i = 0; i < rawChunks.Count; i++)
        {
            var (src, sec, txt) = rawChunks[i];
            _data[i] = new ChunkData { Source = src, Section = sec, Text = txt };
        }

        BuildIdf();
        BuildChunkVectors();

        long totalLen = 0;
        foreach (ref var d in _data.AsSpan()) totalLen += d.Length;
        _avgLength = _data.Length > 0 ? (double)totalLen / _data.Length : 1.0;
    }

    private static List<(string Source, string Section, string Text)> SplitIntoChunks(string source, string content)
    {
        var result = new List<(string, string, string)>();
        var matches = HeaderRegex.Matches(content).Cast<Match>().ToList();

        for (int i = 0; i < matches.Count; i++)
        {
            int start = matches[i].Index + matches[i].Length;
            int end = i + 1 < matches.Count ? matches[i + 1].Index : content.Length;
            var section = matches[i].Groups[1].Value.Trim();
            var text = content[start..end].Trim();

            if (text.Length > 40)
                result.Add((source, section, $"[{section}]:\n{text}"));
        }

        if (result.Count == 0 && !string.IsNullOrWhiteSpace(content))
            result.Add((source, source, content.Trim()));

        return result;
    }

    private void BuildIdf()
    {
        var df = new Dictionary<string, int>();
        int n = _data.Length;

        foreach (ref var d in _data.AsSpan())
            foreach (var token in Tokenize(d.Text).Distinct())
                df[token] = df.GetValueOrDefault(token) + 1;

        _idf = df.ToDictionary(
            kv => kv.Key,
            kv => Math.Log((n - kv.Value + 0.5) / (kv.Value + 0.5) + 1));
    }

    private void BuildChunkVectors()
    {
        for (int i = 0; i < _data.Length; i++)
        {
            var tokens = Tokenize(_data[i].Text);
            var count = Math.Max(1, tokens.Count);

            var rawCounts = new Dictionary<string, int>(tokens.Count);
            foreach (var t in tokens)
                rawCounts[t] = rawCounts.TryGetValue(t, out var c) ? c + 1 : 1;

            var tf = new Dictionary<string, double>(rawCounts.Count);
            foreach (var (term, cnt) in rawCounts)
                tf[term] = (double)cnt / count;

            double norm = 0;
            foreach (var (term, tfVal) in tf)
            {
                if (_idf.TryGetValue(term, out var idfVal))
                {
                    double v = tfVal * idfVal;
                    norm += v * v;
                }
            }

            _data[i].RawCounts = rawCounts;
            _data[i].Tf = tf;
            _data[i].TfIdfNorm = Math.Sqrt(norm);
            _data[i].Length = tokens.Count;
        }
    }

    private double ComputeCosineSimilarity(Dictionary<string, double> queryTfIdf, double queryNorm, int idx)
    {
        double chunkNorm = _data[idx].TfIdfNorm;
        if (chunkNorm < 1e-10 || queryNorm < 1e-10) return 0;

        var chunkTf = _data[idx].Tf;
        double dot = 0;

        foreach (var (term, qTfIdf) in queryTfIdf)
        {
            if (chunkTf.TryGetValue(term, out var cTf) &&
                _idf.TryGetValue(term, out var idfVal))
                dot += qTfIdf * (cTf * idfVal);
        }

        return dot / (chunkNorm * Math.Sqrt(queryNorm));
    }

    public string Search(string query, int topK = 3, int maxChars = 1800)
    {
        if (_data.Length == 0) return string.Empty;

        var queryTokens = Tokenize(query);
        if (queryTokens.Count == 0) return string.Empty;

        const double k1 = 1.5;
        const double b = 0.75;
        var avgLen = Math.Max(1.0, _avgLength);

        var qCount = Math.Max(1, queryTokens.Count);
        var distinctQueryTerms = new HashSet<string>(queryTokens);

        var queryTfIdf = new Dictionary<string, double>(distinctQueryTerms.Count);
        double queryNorm = 0;
        foreach (var term in distinctQueryTerms)
        {
            if (!_idf.TryGetValue(term, out var idfVal)) continue;
            double qTf = queryTokens.Count(t => t == term) / (double)qCount;
            double weight = qTf * idfVal;
            queryTfIdf[term] = weight;
            queryNorm += weight * weight;
        }

        var allScores = new (int idx, double bm25, double cosine)[_data.Length];
        for (int i = 0; i < _data.Length; i++)
        {
            ref var d = ref _data[i];
            double bm25 = 0;
            foreach (var qt in distinctQueryTerms)
            {
                if (!d.RawCounts.TryGetValue(qt, out var rawFreq)) continue;
                var idfVal = _idf.GetValueOrDefault(qt, 0.0);
                var freq = (double)rawFreq;
                bm25 += idfVal * freq * (k1 + 1) / (freq + k1 * (1 - b + b * d.Length / avgLen));
            }
            allScores[i] = (i, bm25, ComputeCosineSimilarity(queryTfIdf, queryNorm, i));
        }

        double maxBm25 = 0;
        foreach (var s in allScores) if (s.bm25 > maxBm25) maxBm25 = s.bm25;
        if (maxBm25 < 1e-10) return string.Empty;

        var scored = new List<(int idx, double score)>(allScores.Length);
        foreach (var s in allScores)
        {
            double score = 0.6 * s.bm25 / maxBm25 + 0.4 * s.cosine;
            if (score > 0.01) scored.Add((s.idx, score));
        }
        scored.Sort((a, b2) => b2.score.CompareTo(a.score));

        var parts = new List<string>();
        int used = 0;

        for (int i = 0; i < Math.Min(topK, scored.Count); i++)
        {
            var text = _data[scored[i].idx].Text;
            int budget = maxChars - used - (parts.Count > 0 ? 5 : 0);
            if (budget < 80) break;

            var piece = text.Length > budget ? text[..budget] + "..." : text;
            parts.Add(piece);
            used += piece.Length + 5;
        }

        return string.Join("\n---\n", parts);
    }

    private static List<string> Tokenize(string text) =>
        TokenizerRegex
            .Split(text.ToLowerInvariant())
            .Where(t => !SimpleStemmer.IsStopWord(t))
            .Select(SimpleStemmer.Stem)
            .Where(t => t.Length > 2)
            .ToList();
}
