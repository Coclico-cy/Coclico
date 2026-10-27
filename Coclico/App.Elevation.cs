using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using Coclico.Services;
namespace Coclico;

public partial class App
{
    public static bool IsRunningAsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex) when (ex is not System.OutOfMemoryException)
        {
            return false;
        }
    }

    public static bool RestartAsAdmin(string[]? args = null)
    {
        if (TryRestartAsAdmin(args))
        {
            LoggingService.LogInfo("Coclico relancé en mode administrateur. Arrêt immédiat de l'instance courante.");
            PerformApplicationShutdown();
            System.Threading.Thread.Sleep(200);
            Environment.Exit(0);
            return true;
        }
        return false;
    }

    public static bool TryRestartAsAdmin(string[]? args = null)
    {
        if (!IntegrityGate.IsSensitiveOperationAllowed("restart-as-admin", out string integrityReason))
        {
            LoggingService.LogError($"[App.TryRestartAsAdmin] Elevation refused: {integrityReason}");
            return false;
        }

        try
        {
            string exePath = Environment.ProcessPath ?? string.Empty;
            string? dllPath = null;

            if (string.IsNullOrWhiteSpace(exePath) ||
                Path.GetFileNameWithoutExtension(exePath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            {
                string candidate = Path.Join(AppContext.BaseDirectory, "Coclico.exe");
                if (File.Exists(candidate))
                {
                    exePath = candidate;
                }
                else
                {
                    string? entryLoc = System.Reflection.Assembly.GetEntryAssembly()?.Location;
                    if (!string.IsNullOrWhiteSpace(entryLoc))
                    {
                        string? dir = Path.GetDirectoryName(entryLoc);
                        if (!string.IsNullOrWhiteSpace(dir))
                        {
                            string candidate2 = Path.Join(dir, "Coclico.exe");
                            if (File.Exists(candidate2))
                            {
                                exePath = candidate2;
                            }
                        }
                    }
                }

                if (!File.Exists(exePath))
                {
                    string candidateDll = Path.Join(AppContext.BaseDirectory, "Coclico.dll");
                    if (File.Exists(candidateDll))
                    {
                        dllPath = candidateDll;
                    }
                    else
                    {
                        string? entryLoc = System.Reflection.Assembly.GetEntryAssembly()?.Location;
                        if (!string.IsNullOrWhiteSpace(entryLoc) && File.Exists(entryLoc))
                        {
                            dllPath = entryLoc;
                        }
                    }
                }
            }

            string[] rawArgs = args ?? Environment.GetCommandLineArgs();
            if (args == null && rawArgs.Length > 0)
            {
                rawArgs = rawArgs.Skip(1).ToArray();
            }

            var argList = new System.Collections.Generic.List<string>(rawArgs);
            if (!argList.Any(a => a.Equals("--elevated", StringComparison.OrdinalIgnoreCase)))
            {
                argList.Add("--elevated");
            }

            string workingDirectory = SanitizeWorkingDirectory(AppContext.BaseDirectory);

            var psi = new ProcessStartInfo
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = workingDirectory
            };

            if (!string.IsNullOrWhiteSpace(dllPath))
            {
                psi.FileName = ResolveDotNetHost();
                string dllQuoted = $"\"{dllPath}\"";
                string remaining = FormatCommandLineArgs(argList);
                psi.Arguments = string.IsNullOrWhiteSpace(remaining) ? dllQuoted : $"{dllQuoted} {remaining}";
            }
            else
            {
                if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                {
                    LoggingService.LogError($"[App.TryRestartAsAdmin] Fichier exécutable introuvable : '{exePath}'");
                    return false;
                }
                psi.FileName = exePath;
                psi.Arguments = FormatCommandLineArgs(argList);
            }

            LoggingService.LogInfo($"[App.TryRestartAsAdmin] Lancement de l'exécutable avec élévation runas : {psi.FileName} {psi.Arguments}");

            try
            {
                var process = Process.Start(psi);
                string pidStr;
                try { pidStr = process?.Id.ToString() ?? "détaché"; } catch (Exception ex) when (ex is not System.OutOfMemoryException) { pidStr = "détaché"; }
                LoggingService.LogInfo($"[App.TryRestartAsAdmin] Processus lancé avec succès (PID: {pidStr}).");
                return true;
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                LoggingService.LogInfo("Élévation UAC annulée ou refusée par l'utilisateur (code 1223).");
                return false;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or System.InvalidOperationException)
            {
                LoggingService.LogException(ex, "App.TryRestartAsAdmin.ProcessStart");
                return false;
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException or System.ComponentModel.Win32Exception or System.InvalidOperationException)
        {
            LoggingService.LogException(ex, "App.TryRestartAsAdmin");
            return false;
        }
    }

    internal static string SanitizeWorkingDirectory(string? baseDir)
    {
        if (string.IsNullOrWhiteSpace(baseDir))
        {
            return Environment.CurrentDirectory;
        }

        string? root = Path.GetPathRoot(baseDir);
        return string.Equals(baseDir, root, StringComparison.OrdinalIgnoreCase)
            ? baseDir
            : baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    internal static string ResolveDotNetHost()
    {
        string? procPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(procPath) &&
            Path.GetFileNameWithoutExtension(procPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(procPath))
        {
            return procPath;
        }

        string? dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(dotnetRoot))
        {
            string candidateInRoot = Path.Join(dotnetRoot, "dotnet.exe");
            if (File.Exists(candidateInRoot))
            {
                return candidateInRoot;
            }
        }

        string pfDotNet = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
        if (File.Exists(pfDotNet))
        {
            return pfDotNet;
        }

        string pfX86DotNet = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "dotnet", "dotnet.exe");
        if (File.Exists(pfX86DotNet))
        {
            return pfX86DotNet;
        }

        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathEnv))
        {
            foreach (string pathPart in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                try
                {
                    string candidate = Path.Join(pathPart, "dotnet.exe");
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch (Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException) { LoggingService.LogException(ex, "App.ResolveDotNetHost.PathCandidate"); }
            }
        }

        return "dotnet.exe";
    }

    internal static string FormatCommandLineArgs(System.Collections.Generic.IEnumerable<string> args)
    {
        var formatted = new System.Collections.Generic.List<string>();
        foreach (string arg in args)
        {
            if (string.IsNullOrEmpty(arg))
            {
                formatted.Add("\"\"");
            }
            else if (arg.Contains(' ') || arg.Contains('\"') || arg.Contains('\t') || arg.Contains('\n') || arg.Contains('\r'))
            {
                var sb = new System.Text.StringBuilder();
                _ = sb.Append('"');
                int backslashCount = 0;
                foreach (char c in arg)
                {
                    if (c == '\\')
                    {
                        backslashCount++;
                    }
                    else if (c == '"')
                    {
                        _ = sb.Append('\\', (backslashCount * 2) + 1);
                        _ = sb.Append('"');
                        backslashCount = 0;
                    }
                    else
                    {
                        _ = sb.Append('\\', backslashCount);
                        _ = sb.Append(c);
                        backslashCount = 0;
                    }
                }
                _ = sb.Append('\\', backslashCount * 2);
                _ = sb.Append('"');
                formatted.Add(sb.ToString());
            }
            else
            {
                formatted.Add(arg);
            }
        }
        return string.Join(" ", formatted);
    }
}
