namespace Coclico.Services.Algorithms;

public static class SimpleStemmer
{
    private static readonly string[] FrenchSuffixes = BuildSuffixes(
        "issements", "issement", "ements", "ement", "ations", "ation",
        "ances", "ance", "ences", "ence",
        "assions", "issions", "assent", "issent",
        "erions", "irions", "eront", "iront", "aient", "erait", "irait",
        "iques", "ique", "ables", "able", "ments", "ment",
        "eurs", "eur", "euses", "euse", "ants", "ant",
        "èrent", "erent", "âmes", "îtes", "ames", "ites",
        "tion", "sion", "ages", "age", "ies", "ie",
        "ifs", "if", "ives", "ive", "aux", "al",
        "ions", "iez", "ent", "ont", "ait", "ais",
        "ées", "ée", "és", "é",
        "er", "ir", "ez", "es", "s"
    );

    private static readonly string[] EnglishSuffixes = BuildSuffixes(
        "ements", "ement", "ations", "ation", "nesses", "ness",
        "ances", "ance", "ences", "ence",
        "ments", "ment", "ingly", "ings", "ing", "tion",
        "able", "ible", "ally", "ful", "ous",
        "ive", "ers", "er", "ed", "ly", "es", "s"
    );

    private static readonly HashSet<string> StopWordsFr = new(StringComparer.OrdinalIgnoreCase)
    {
        "le", "la", "les", "un", "une", "des", "du", "de", "au", "aux",
        "et", "ou", "mais", "donc", "car", "ni", "que", "qui", "quoi",
        "ce", "cette", "ces", "mon", "ton", "son", "notre", "votre", "leur",
        "je", "tu", "il", "elle", "nous", "vous", "ils", "elles", "on",
        "est", "sont", "etre", "avoir", "fait", "dans", "par", "pour",
        "sur", "avec", "sans", "sous", "entre", "vers", "chez",
        "pas", "plus", "moins", "tres", "bien", "aussi", "comme"
    };

    private static readonly HashSet<string> StopWordsEn = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "is", "are", "was", "were", "be", "been",
        "being", "have", "has", "had", "do", "does", "did", "will",
        "would", "could", "should", "may", "might", "can", "shall",
        "and", "but", "or", "nor", "not", "no", "so", "if", "then",
        "than", "that", "this", "these", "those", "it", "its",
        "in", "on", "at", "to", "for", "of", "with", "by", "from",
        "as", "into", "about", "between", "through", "after", "before"
    };

    public static bool IsStopWord(string word)
    {
        return word.Length <= 2 || StopWordsFr.Contains(word) || StopWordsEn.Contains(word);
    }

    public static string Stem(string word)
    {
        if (word.Length < 4)
        {
            return word;
        }

        if (word.EndsWith("eaux", StringComparison.OrdinalIgnoreCase) && word.Length >= 5)
        {
            return word[..^1];
        }

        if (word.EndsWith("aux", StringComparison.OrdinalIgnoreCase) && word.Length >= 5)
        {
            return word[..^2] + "l";
        }

        foreach (string suffix in FrenchSuffixes)
        {
            if (MatchesSuffix(word, suffix))
            {
                return word[..^suffix.Length];
            }
        }

        foreach (string suffix in EnglishSuffixes)
        {
            if (MatchesSuffix(word, suffix))
            {
                return word[..^suffix.Length];
            }
        }

        return word;
    }

    private static bool MatchesSuffix(string word, string suffix)
    {
        return word.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && word.Length - suffix.Length >= 3;
    }

    private static string[] BuildSuffixes(params string[] suffixes)
    {
        return suffixes
            .Distinct()
            .OrderByDescending(s => s.Length)
            .ToArray();
    }
}