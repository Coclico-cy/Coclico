using System.Globalization;
using System.Text;

namespace Coclico.Installer;

public static class TextFormatService
{
    public static string Format(string? pattern, params object?[] args)
        => Format(null, pattern, args);

    public static string Format(IFormatProvider? provider, string? pattern, params object?[] args)
    {
        if (string.IsNullOrEmpty(pattern) || args is not { Length: > 0 })
        {
            return pattern ?? string.Empty;
        }

        var builder = new StringBuilder(pattern.Length + 64);
        int i = 0;
        while (i < pattern.Length)
        {
            char c = pattern[i];
            if (c == '{' && i + 1 < pattern.Length && pattern[i + 1] == '{')
            {
                builder.Append('{');
                i += 2;
                continue;
            }

            if (c == '{')
            {
                int close = pattern.IndexOf('}', i + 1);
                if (close > i)
                {
                    string token = pattern.Substring(i + 1, close - i - 1);
                    int colon = token.IndexOf(':');
                    string indexText = colon < 0 ? token : token[..colon];
                    string spec = colon < 0 ? string.Empty : token[(colon + 1)..];
                    if (int.TryParse(indexText, out int index) && index >= 0 && index < args.Length)
                    {
                        object? arg = args[index];
                        builder.Append(arg is IFormattable formattable
                            ? formattable.ToString(spec.Length > 0 ? spec : null, provider ?? CultureInfo.CurrentCulture)
                            : arg?.ToString() ?? string.Empty);
                        i = close + 1;
                        continue;
                    }
                }

                builder.Append('{');
                i++;
                continue;
            }

            builder.Append(c);
            i++;
        }

        return builder.ToString();
    }
}
