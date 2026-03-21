using System.Text.RegularExpressions;

namespace Coclico.Services
{
    public static partial class AiActionParser
    {
        [GeneratedRegex(@"<think>.*?</think>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
        private static partial Regex ThinkBlockRegex();

        public static string Clean(string rawText) =>
            ThinkBlockRegex().Replace(rawText, string.Empty).Trim();
    }
}
