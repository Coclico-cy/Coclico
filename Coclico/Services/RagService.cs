using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Coclico.Services
{
    public sealed class RagService
    {
        private sealed record Chunk(string Source, string Section, string Text);

        private List<Chunk> _chunks = [];
        private Dictionary<string, double> _idf = [];
        private double _avgLength;

        private List<Dictionary<string, double>> _chunkTf = [];
        private double[] _chunkTfIdfNorms = [];

        private List<Dictionary<string, int>> _chunkRawCounts = [];
        private int[] _chunkLengths = [];

        private static readonly Regex HeaderRegex     = new(@"^#{1,3}\s+(.+)$", RegexOptions.Multiline | RegexOptions.Compiled);
        private static readonly Regex TokenizerRegex  = new(@"[^\wàâäéèêëîïôùûüçœæ]+", RegexOptions.Compiled);

        public int ChunkCount => _chunks.Count;

        public void BuildIndex(string docsPath)
        {
            if (!Directory.Exists(docsPath)) return;

            _chunks.Clear();
            foreach (var file in Directory.GetFiles(docsPath, "*.md", SearchOption.TopDirectoryOnly))
                _chunks.AddRange(SplitIntoChunks(
                    Path.GetFileNameWithoutExtension(file),
                    File.ReadAllText(file)));

            BuildIdf();
            BuildCosineVectors();

            _avgLength = _chunkLengths.Length > 0
                ? _chunkLengths.Average()
                : 1.0;
        }

        private static List<Chunk> SplitIntoChunks(string source, string content)
        {
            var result = new List<Chunk>();
            var matches = HeaderRegex.Matches(content).Cast<Match>().ToList();

            for (int i = 0; i < matches.Count; i++)
            {
                int start = matches[i].Index + matches[i].Length;
                int end   = i + 1 < matches.Count ? matches[i + 1].Index : content.Length;

                var section = matches[i].Groups[1].Value.Trim();
                var text    = content[start..end].Trim();

                if (text.Length > 40)
                    result.Add(new Chunk(source, section, $"[{section}]:\n{text}"));
            }

            if (result.Count == 0 && !string.IsNullOrWhiteSpace(content))
                result.Add(new Chunk(source, source, content.Trim()));

            return result;
        }

        private void BuildIdf()
        {
            var df = new Dictionary<string, int>();
            int n = _chunks.Count;

            foreach (var chunk in _chunks)
                foreach (var token in Tokenize(chunk.Text).Distinct())
                    df[token] = df.GetValueOrDefault(token) + 1;

            _idf = df.ToDictionary(
                kv => kv.Key,
                kv => Math.Log((n - kv.Value + 0.5) / (kv.Value + 0.5) + 1));
        }

        private void BuildCosineVectors()
        {
            _chunkTf = new List<Dictionary<string, double>>(_chunks.Count);
            _chunkTfIdfNorms = new double[_chunks.Count];
            _chunkRawCounts = new List<Dictionary<string, int>>(_chunks.Count);
            _chunkLengths = new int[_chunks.Count];

            for (int i = 0; i < _chunks.Count; i++)
            {
                var tokens = Tokenize(_chunks[i].Text);
                var count  = Math.Max(1, tokens.Count);

                var rawCounts = new Dictionary<string, int>(tokens.Count);
                foreach (var t in tokens)
                    rawCounts[t] = rawCounts.TryGetValue(t, out var c) ? c + 1 : 1;
                _chunkRawCounts.Add(rawCounts);
                _chunkLengths[i] = tokens.Count;

                var tf = new Dictionary<string, double>(rawCounts.Count);
                foreach (var (term, cnt) in rawCounts)
                    tf[term] = (double)cnt / count;
                _chunkTf.Add(tf);

                double norm = 0;
                foreach (var (term, tfVal) in tf)
                {
                    if (_idf.TryGetValue(term, out var idfVal))
                    {
                        double v = tfVal * idfVal;
                        norm += v * v;
                    }
                }
                _chunkTfIdfNorms[i] = Math.Sqrt(norm);
            }
        }

        private double ComputeCosineSimilarity(IReadOnlyList<string> queryTokens, int chunkIdx)
        {
            double chunkNorm = _chunkTfIdfNorms[chunkIdx];
            if (chunkNorm < 1e-10) return 0;

            var qCount = Math.Max(1, queryTokens.Count);
            var queryTf = queryTokens.GroupBy(t => t)
                                     .ToDictionary(g => g.Key, g => (double)g.Count() / qCount);

            double dot = 0, queryNorm = 0;
            foreach (var (term, qTf) in queryTf)
            {
                if (!_idf.TryGetValue(term, out var idfVal)) continue;
                double qTfIdf = qTf * idfVal;
                queryNorm += qTfIdf * qTfIdf;

                if (_chunkTf[chunkIdx].TryGetValue(term, out var cTf))
                    dot += qTfIdf * (cTf * idfVal);
            }

            double denom = chunkNorm * Math.Sqrt(queryNorm);
            return denom < 1e-10 ? 0 : dot / denom;
        }

        public string Search(string query, int topK = 3, int maxChars = 1800)
        {
            if (_chunks.Count == 0) return string.Empty;

            var queryTokens = Tokenize(query);
            if (queryTokens.Count == 0) return string.Empty;

            const double k1     = 1.5;
            const double b      = 0.75;
            var          avgLen = Math.Max(1.0, _avgLength);

            var allScores = _chunks
                .Select((chunk, idx) =>
                {
                    var rawCounts = _chunkRawCounts[idx];
                    var len       = _chunkLengths[idx];

                    double bm25 = queryTokens
                        .Distinct()
                        .Where(qt => rawCounts.ContainsKey(qt))
                        .Sum(qt =>
                        {
                            var freq   = (double)rawCounts[qt];
                            var idfVal = _idf.GetValueOrDefault(qt, 0.0);
                            return idfVal * freq * (k1 + 1) / (freq + k1 * (1 - b + b * len / avgLen));
                        });

                    double cosine = ComputeCosineSimilarity(queryTokens, idx);
                    return (idx, bm25, cosine);
                })
                .ToList();

            double maxBm25 = allScores.Max(x => x.bm25);
            if (maxBm25 < 1e-10) return string.Empty;

            var scored = allScores
                .Select(x => (x.idx, score: 0.6 * x.bm25 / maxBm25 + 0.4 * x.cosine))
                .Where(x => x.score > 0.01)
                .OrderByDescending(x => x.score)
                .Take(topK)
                .ToList();

            if (scored.Count == 0) return string.Empty;

            var parts = new List<string>();
            int used  = 0;

            foreach (var (idx, _) in scored)
            {
                var text   = _chunks[idx].Text;
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
                .Where(t => t.Length > 2)
                .ToList();
    }
}
