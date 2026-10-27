using System.IO;
using System.Linq;

namespace Coclico.Services;

public sealed record ParsedUninstallCommand(
    string ExecutableOrUri,
    string Arguments,
    bool IsUri,
    bool IsMsi);

public static class UninstallCommandParser
{
    public static ParsedUninstallCommand? Parse(string? rawCommand)
    {
        if (string.IsNullOrWhiteSpace(rawCommand))
        {
            return null;
        }

        string trimmed = rawCommand.Trim();

        if (trimmed.StartsWith("steam://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("com.epicgames.launcher://", StringComparison.OrdinalIgnoreCase))
        {
            return new ParsedUninstallCommand(trimmed, string.Empty, IsUri: true, IsMsi: false);
        }

        if (trimmed.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase))
        {
            string page = trimmed["ms-settings:".Length..];
            if (page.Length > 0 && page.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
            {
                return new ParsedUninstallCommand(trimmed, string.Empty, IsUri: true, IsMsi: false);
            }

            return null;
        }

        if (trimmed.StartsWith("msiexec", StringComparison.OrdinalIgnoreCase))
        {
            int exeLen = trimmed.StartsWith("msiexec.exe", StringComparison.OrdinalIgnoreCase) ? 11 : 7;
            string msiArgs = trimmed[exeLen..].Trim();

            if (!IsMsiUninstallInvocation(msiArgs))
            {
                return null;
            }

            string msiExe = Path.Combine(Environment.SystemDirectory, "msiexec.exe");
            return new ParsedUninstallCommand(File.Exists(msiExe) ? msiExe : "msiexec.exe", msiArgs, IsUri: false, IsMsi: true);
        }

        if (trimmed.StartsWith('"'))
        {
            int closeQuote = trimmed.IndexOf('"', 1);
            if (closeQuote > 1)
            {
                string exe = trimmed[1..closeQuote].Trim();
                string args = trimmed[(closeQuote + 1)..].Trim();
                return new ParsedUninstallCommand(exe, args, IsUri: false, IsMsi: false);
            }
        }

        int exeExtIndex = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        while (exeExtIndex > 0)
        {
            int candidateEnd = exeExtIndex + 4;
            string candidateExe = trimmed[..candidateEnd].Trim();
            if (File.Exists(candidateExe))
            {
                string args = trimmed[candidateEnd..].Trim();
                return new ParsedUninstallCommand(candidateExe, args, IsUri: false, IsMsi: false);
            }

            exeExtIndex = trimmed.IndexOf(".exe", exeExtIndex + 4, StringComparison.OrdinalIgnoreCase);
        }

        int firstSpace = trimmed.IndexOf(' ');
        if (firstSpace > 0)
        {
            string exe = trimmed[..firstSpace].Trim();
            string args = trimmed[(firstSpace + 1)..].Trim();
            return new ParsedUninstallCommand(exe, args, IsUri: false, IsMsi: false);
        }

        return new ParsedUninstallCommand(trimmed, string.Empty, IsUri: false, IsMsi: false);
    }

    private static bool IsMsiUninstallInvocation(string args)
    {
        return args.StartsWith("/x", StringComparison.OrdinalIgnoreCase)
            || args.StartsWith("/i", StringComparison.OrdinalIgnoreCase)
            || args.StartsWith("/uninstall", StringComparison.OrdinalIgnoreCase);
    }
}
