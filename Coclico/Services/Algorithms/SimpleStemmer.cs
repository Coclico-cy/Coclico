#nullable enable
using System;
using System.Collections.Generic;

namespace Coclico.Services.Algorithms;

/// <summary>
/// Lightweight French+English suffix stemmer without external dependencies.
/// </summary>
public static class SimpleStemmer
{
    private static readonly string[] FrenchSuffixes =
    [
        "issements", "issement", "ements", "ement", "ations", "ation",
        "iques", "ique", "ables", "able", "ments", "ment",
        "eurs", "eur", "euses", "euse", "ants", "ant",
        "tion", "sion", "ages", "age", "ies", "ie",
        "ifs", "if", "ives", "ive", "aux", "al",
        "es", "s"
    ];

    private static readonly string[] EnglishSuffixes =
    [
        "ements", "ement", "ations", "ation", "nesses", "ness",
        "ments", "ment", "ingly", "ings", "ing", "tion",
        "able", "ible", "ally", "ful", "ous",
        "ive", "ers", "er", "ed", "ly", "es", "s"
    ];

    private static readonly HashSet<string> StopWordsFr = new(StringComparer.OrdinalIgnoreCase)
    {
        "le", "la", "les", "un", "une", "des", "du", "de", "au", "aux",
        "et", "ou", "mais", "donc", "car", "ni", "que", "qui", "quoi",
        "ce", "cette", "ces", "mon", "ton", "son", "notre", "votre", "leur",
        "je", "tu", "il", "elle", "nous", "vous", "ils", "elles", "on",
        "est", "sont", "etre", "avoir", "fait", "dans", "par", "pour",
        "sur", "avec", "sans", "sous", "entre", "vers", "chez",
        "pas", "plus", "moins", "tres", "bien", "aussi", "comme",
    };

    private static readonly HashSet<string> StopWordsEn = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "is", "are", "was", "were", "be", "been",
        "being", "have", "has", "had", "do", "does", "did", "will",
        "would", "could", "should", "may", "might", "can", "shall",
        "and", "but", "or", "nor", "not", "no", "so", "if", "then",
        "than", "that", "this", "these", "those", "it", "its",
        "in", "on", "at", "to", "for", "of", "with", "by", "from",
        "as", "into", "about", "between", "through", "after", "before",
    };

    public static bool IsStopWord(string word) =>
        word.Length <= 2 || StopWordsFr.Contains(word) || StopWordsEn.Contains(word);

    public static string Stem(string word)
    {
        if (word.Length < 4) return word;

        foreach (var suffix in FrenchSuffixes)
        {
            if (word.EndsWith(suffix, StringComparison.Ordinal) && word.Length - suffix.Length >= 3)
                return word[..^suffix.Length];
        }
        foreach (var suffix in EnglishSuffixes)
        {
            if (word.EndsWith(suffix, StringComparison.Ordinal) && word.Length - suffix.Length >= 3)
                return word[..^suffix.Length];
        }

        return word;
    }
}
