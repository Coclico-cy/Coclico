using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Coclico.Services
{
    public static class ServiceContainer
    {
        private static IServiceProvider? _provider;
        private static ServiceCollection? _services;

        public static IServiceProvider Provider =>
            _provider ?? throw new InvalidOperationException(
                "Service container not built. Call Build() at startup.");

        public static void Build(Action<IServiceCollection> configure)
        {
            _services = new ServiceCollection();

            var logDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Coclico", "logs");
            System.IO.Directory.CreateDirectory(logDir);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(
                    System.IO.Path.Combine(logDir, "coclico-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
                .Enrich.FromLogContext()
                .CreateLogger();

            _services.AddLogging(builder =>
            {
                builder.ClearProviders();
                builder.AddSerilog(Log.Logger, dispose: true);
            });

            _services.AddMemoryCache();

            _services.AddHttpClient();

            configure?.Invoke(_services);
            _provider = _services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = false
            });
        }

        public static T GetRequired<T>() where T : notnull =>
            Provider.GetRequiredService<T>();

        public static T? GetOptional<T>() where T : class =>
            Provider.GetService<T>();

        public static IServiceScope CreateScope() =>
            Provider.CreateScope();

        public static void Shutdown()
        {
            try
            {
                if (_provider is IDisposable disposable)
                    disposable.Dispose();
            }
            catch { }
            finally
            {
                Log.CloseAndFlush();
                _provider = null;
                _services = null;
            }
        }
    }
}
