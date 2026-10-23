using System;
using System.Threading.Tasks;

namespace Coclico.Services;

public static class AsyncEventSafe
{
    public static void Run(Func<Task> handler, string operationName)
    {
        _ = RunAsync(handler, operationName);
    }

    private static async Task RunAsync(Func<Task> handler, string operationName)
    {
        try
        {
            if (handler != null)
            {
                await handler().ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex, operationName);
        }
    }
}
