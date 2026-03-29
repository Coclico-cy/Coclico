#nullable enable
using System;
using System.IO;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Coclico.Services;

public static class ServiceContainer
{
    private static volatile bool _isBuilt = false;

    private static Lazy<IServiceProvider> _lazy =
        new(() => throw new InvalidOperationException(
            "Service container not built. Call Build() at startup."),
            LazyThreadSafetyMode.ExecutionAndPublication);

    public static IServiceProvider Provider => _lazy.Value;

    public static void Build(Action<IServiceCollection> configure)
    {
        _lazy = new Lazy<IServiceProvider>(() =>
        {
            var services = new ServiceCollection();

            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Coclico", "logs");
            Directory.CreateDirectory(logDir);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(
                    Path.Combine(logDir, "coclico-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
                .Enrich.FromLogContext()
                .CreateLogger();

            services.AddLogging(builder =>
            {
                builder.ClearProviders();
                builder.AddSerilog(Log.Logger, dispose: true);
            });

            services.AddMemoryCache();
            services.AddSingleton<IAuditLog, AuditLogService>();
            services.AddHttpClient();

            configure?.Invoke(services);

            return services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = false
            });
        }, LazyThreadSafetyMode.ExecutionAndPublication);
        _isBuilt = true;
    }

    public static T GetRequired<T>() where T : notnull =>
        Provider.GetRequiredService<T>();

    public static T? GetOptional<T>() where T : class =>
        _isBuilt ? Provider.GetService<T>() : null;

    public static IServiceScope CreateScope() =>
        Provider.CreateScope();

    public static void Shutdown()
    {
        try
        {
            if (_lazy.IsValueCreated && _lazy.Value is IDisposable disposable)
                disposable.Dispose();
        }
        catch { }
        finally
        {
            Log.CloseAndFlush();
            _lazy = new Lazy<IServiceProvider>(
                () => throw new InvalidOperationException("Service container has been shut down."),
                LazyThreadSafetyMode.ExecutionAndPublication);
        }
    }
}
