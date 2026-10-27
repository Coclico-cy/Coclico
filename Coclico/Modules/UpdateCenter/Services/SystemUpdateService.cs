using Coclico.Modules.UpdateCenter.Models;

namespace Coclico.Modules.UpdateCenter.Services;

public class SystemUpdateService(SecureProcessRunner runner)
{
    public virtual async Task<UpdateItem> GetWindowsUpdateStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            runner.Timeout = TimeSpan.FromMinutes(2);
            var (exitCode, _) = await runner.RunAsync("usoclient.exe", new[] { "ScanInstallWait" }, cancellationToken).ConfigureAwait(false);
            if (exitCode != 0)
            {
                return UnknownStatus();
            }

            return UpdateItem.UpToDate("windows", "Windows Update", UpdateKind.Windows, UpdateSource.WindowsUpdate);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return UnknownStatus();
        }
    }

    private static UpdateItem UnknownStatus()
    {
        return new UpdateItem
        {
            Id = "windows",
            Name = "Windows Update",
            Kind = UpdateKind.Windows,
            Source = UpdateSource.WindowsUpdate,
            Status = UpdateItemStatus.UnknownVersion
        };
    }
}
