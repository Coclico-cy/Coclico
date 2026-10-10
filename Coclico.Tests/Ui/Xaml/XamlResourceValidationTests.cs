using System.Text.RegularExpressions;
using Xunit;

namespace Coclico.Tests;

public sealed class XamlResourceValidationTests
{
    private static readonly string[] ProjectNames = ["Coclico", "Coclico.Installer"];

    private static readonly Regex StaticResourceRegex = new(@"\{StaticResource\s+([A-Za-z0-9_.]+)", RegexOptions.Compiled);
    private static readonly Regex MergedDictionaryRegex = new(@"ResourceDictionary\s+Source=""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex PackResourceRegex = new(@"pack://application:,,,/([A-Za-z0-9_.]+);component/(.+?)(?=[""]|$)", RegexOptions.Compiled);
    private static readonly Regex IconSourceRegex = new("""(?:Icon|Source)="([^"{}]+)"""", RegexOptions.Compiled);
    private static readonly Regex KeyRegex = new(@"x:Key=""([^""]+)""", RegexOptions.Compiled);
    private static readonly string[] ImageExtensions = [".ico", ".png", ".jpg", ".jpeg", ".bmp", ".gif"];

    [Fact]
    public void AllXamlResourceReferencesResolve()
    {
        string repoRoot = FindRepoRoot();
        var violations = new List<string>();

        foreach (string projectName in ProjectNames)
        {
            string projectDir = Path.Combine(repoRoot, projectName);
            if (!Directory.Exists(projectDir))
            {
                violations.Add($"Projet introuvable : {projectDir}");
                continue;
            }

            var globalKeys = new HashSet<string>(ExtractKeys(File.ReadAllText(Path.Combine(projectDir, "App.xaml"))));

            var xamlFiles = Directory.EnumerateFiles(projectDir, "*.xaml", SearchOption.AllDirectories)
                .Where(IsSourceFile)
                .Where(f => !string.Equals(Path.GetFileName(f), "App.xaml", StringComparison.OrdinalIgnoreCase));

            foreach (string file in xamlFiles)
            {
                string raw = File.ReadAllText(file);
                var available = new HashSet<string>(globalKeys);
                available.UnionWith(ExtractKeys(raw));
                CollectMergedDictionaryKeys(projectDir, raw, available, [file]);

                foreach (Match match in StaticResourceRegex.Matches(raw))
                {
                    string key = match.Groups[1].Value;
                    if (!available.Contains(key))
                    {
                        violations.Add($"{Rel(repoRoot, file)} : clé StaticResource '{key}' introuvable (ni locale, ni App.xaml, ni dictionnaires fusionnés)");
                    }
                }

                foreach (Match match in MergedDictionaryRegex.Matches(raw))
                {
                    string source = match.Groups[1].Value;
                    if (!TryResolvePackUri(projectDir, source, out string? resolved))
                    {
                        violations.Add($"{Rel(repoRoot, file)} : dictionnaire fusionné introuvable '{source}'");
                    }
                }

                foreach (Match match in IconSourceRegex.Matches(raw))
                {
                    string rawValue = match.Groups[1].Value.Trim();
                    string value = rawValue.Replace('/', '\\');
                    if (!IsImagePath(rawValue))
                    {
                        continue;
                    }

                    if (rawValue.StartsWith("pack://", StringComparison.OrdinalIgnoreCase))
                    {
                        var packMatch = PackResourceRegex.Match(rawValue);
                        if (!packMatch.Success || packMatch.Groups[1].Value != Path.GetFileName(projectName))
                        {
                            continue;
                        }

                        string target = Path.Combine(projectDir, packMatch.Groups[2].Value.Replace('/', '\\'));
                        if (!File.Exists(target))
                        {
                            violations.Add($"{Rel(repoRoot, file)} : ressource pack introuvable '{rawValue}'");
                        }
                    }
                    else
                    {
                        string target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, value));
                        if (!File.Exists(target))
                        {
                            violations.Add($"{Rel(repoRoot, file)} : image/icône introuvable '{rawValue}' (résolue : {target})");
                        }
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Références XAML cassées (" + violations.Count + ") — ces erreurs provoquent des XamlParseException au chargement des vues :" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static bool IsSourceFile(string path)
    {
        string withSeparators = Path.DirectorySeparatorChar + path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        return !withSeparators.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
            && !withSeparators.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar);
    }

    private static bool IsImagePath(string value)
    {
        if (value.StartsWith("http", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("/", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("\\", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("{", StringComparison.Ordinal))
        {
            return false;
        }

        string extension = Path.GetExtension(value);
        return ImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> ExtractKeys(string xaml)
    {
        foreach (Match match in KeyRegex.Matches(xaml))
        {
            yield return match.Groups[1].Value;
        }
    }

    private static void CollectMergedDictionaryKeys(string projectDir, string xaml, ISet<string> keys, IReadOnlyList<string> visitedFiles)
    {
        foreach (Match match in MergedDictionaryRegex.Matches(xaml))
        {
            string source = match.Groups[1].Value;
            if (!TryResolvePackUri(projectDir, source, out string? resolved) || resolved is null)
            {
                continue;
            }

            if (visitedFiles.Contains(resolved, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            string content = File.ReadAllText(resolved);
            foreach (string key in ExtractKeys(content))
            {
                keys.Add(key);
            }

            var newVisited = visitedFiles.ToList();
            newVisited.Add(resolved);
            CollectMergedDictionaryKeys(projectDir, content, keys, newVisited);
        }
    }

    private static bool TryResolvePackUri(string projectDir, string source, out string? resolved)
    {
        resolved = null;
        Match match = Regex.Match(source, @"/([A-Za-z0-9_.]+);component/(.+)");
        if (!match.Success)
        {
            return true;
        }

        if (match.Groups[1].Value != Path.GetFileName(projectDir))
        {
            return true;
        }

        string candidate = Path.Combine(projectDir, match.Groups[2].Value.Replace('/', '\\'));
        if (!File.Exists(candidate))
        {
            return false;
        }

        resolved = candidate;
        return true;
    }

    internal static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Coclico.slnx")) || File.Exists(Path.Combine(dir.FullName, "Coclico.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Racine du dépôt introuvable depuis " + AppContext.BaseDirectory);
    }

    private static string Rel(string root, string path) => Path.GetRelativePath(root, path);
}

public sealed class LanguageParityTests
{
    [Fact]
    public void FrAndEnDictionariesHaveIdenticalKeySets()
    {
        string repoRoot = XamlResourceValidationTests.FindRepoRoot();
        HashSet<string> fr = ExtractLangKeys(Path.Combine(repoRoot, "Coclico", "Resources", "Lang", "fr.xaml"));
        HashSet<string> en = ExtractLangKeys(Path.Combine(repoRoot, "Coclico", "Resources", "Lang", "en.xaml"));

        var missingInEn = fr.Except(en).ToList();
        var missingInFr = en.Except(fr).ToList();

        Assert.True(missingInEn.Count == 0, "Clés absentes de en.xaml : " + string.Join(", ", missingInEn));
        Assert.True(missingInFr.Count == 0, "Clés absentes de fr.xaml : " + string.Join(", ", missingInFr));
    }

    [Fact]
    public void NoDeadLanguageKeys()
    {
        string repoRoot = XamlResourceValidationTests.FindRepoRoot();
        HashSet<string> keys = ExtractLangKeys(Path.Combine(repoRoot, "Coclico", "Resources", "Lang", "fr.xaml"));

        var searched = new List<string> { "Coclico", "Coclico.Tests" };
        var dead = new List<string>();
        foreach (string key in keys)
        {
            if (key.StartsWith("Cleaning_Cat_", StringComparison.Ordinal))
            {
                continue;
            }

            bool found = false;
            foreach (string dir in searched)
            {
                string baseDir = Path.Combine(repoRoot, dir);
                foreach (string ext in new[] { "*.cs", "*.xaml" })
                {
                    foreach (string file in Directory.EnumerateFiles(baseDir, ext, SearchOption.AllDirectories))
                    {
                        string rel = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
                        if (rel.Contains("/obj/") || rel.Contains("/bin/") || rel.Contains("TestResults") ||
                            rel.EndsWith("Resources/Lang/fr.xaml", StringComparison.Ordinal) ||
                            rel.EndsWith("Resources/Lang/en.xaml", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        if (File.ReadAllText(file).Contains(key, StringComparison.Ordinal))
                        {
                            found = true;
                            break;
                        }
                    }

                    if (found) break;
                }

                if (found) break;
            }

            if (!found)
            {
                dead.Add(key);
            }
        }

        Assert.True(dead.Count == 0, "Clés i18n mortes : " + string.Join(", ", dead));
    }

    private static HashSet<string> ExtractLangKeys(string path)
    {
        string content = File.ReadAllText(path);
        var keys = new HashSet<string>();
        foreach (Match m in Regex.Matches(content, "x:Key=\"([^\"]+)\""))
        {
            _ = keys.Add(m.Groups[1].Value);
        }

        return keys;
    }
}