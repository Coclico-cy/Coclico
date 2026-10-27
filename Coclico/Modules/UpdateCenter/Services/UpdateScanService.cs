using Coclico.Modules.UpdateCenter.Models;

namespace Coclico.Modules.UpdateCenter.Services;

public class UpdateScanService(
    WingetUpdateService winget,
    DriverUpdateService drivers,
    SystemUpdateService system)
{
    public virtual async Task<UpdateScanResult> ScanAllAsync(CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string>();
        var items = new List<UpdateItem>();

        await Task.WhenAll(
            RunList(() => winget.ScanAsync(cancellationToken), UpdateKind.Application, items, errors),
            RunList(() => drivers.SearchWindowsUpdateDriverUpdatesAsync(cancellationToken), UpdateKind.Driver, items, errors),
            RunSingle(() => system.GetWindowsUpdateStatusAsync(cancellationToken), UpdateKind.Windows, items, errors)
        ).ConfigureAwait(false);

        return new UpdateScanResult
        {
            Items = items.OrderBy(i => i.Kind).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            ScannedAt = DateTimeOffset.UtcNow,
            SourceErrors = errors
        };
    }

    private static async Task RunList(Func<Task<IReadOnlyList<UpdateItem>>> factory, UpdateKind kind, List<UpdateItem> items, Dictionary<string, string> errors)
    {
        try
        {
            items.AddRange(await factory().ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            errors[kind.ToString()] = ex.Message;
        }
    }

    private static async Task RunSingle(Func<Task<UpdateItem>> factory, UpdateKind kind, List<UpdateItem> items, Dictionary<string, string> errors)
    {
        try
        {
            var item = await factory().ConfigureAwait(false);
            if (item is not null)
                items.Add(item);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            errors[kind.ToString()] = ex.Message;
        }
    }
}
