using System.Reflection;
using System.Runtime.InteropServices;

namespace Coclico.Installer;

internal static class NativeLibBundle
{
    public static void ExtractAndLoad()
    {
        string dir = Path.Join(Path.GetTempPath(), "CoclicoInstaller");
        Directory.CreateDirectory(dir);

        string[] dlls = { "libSkiaSharp.dll", "libHarfBuzzSharp.dll", "av_libglesv2.dll" };

        foreach (string name in dlls)
        {
            string target = Path.Join(dir, name);
            try
            {
                using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
                if (stream is null) continue;
                using var fs = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.Read);
                stream.CopyTo(fs);
            }
            catch (IOException ex) { _ = ex; }

            try { NativeLibrary.Load(target); } catch (Exception ex) when (ex is System.DllNotFoundException or System.BadImageFormatException or System.EntryPointNotFoundException or System.TypeLoadException) { _ = ex; }
        }
    }
}
