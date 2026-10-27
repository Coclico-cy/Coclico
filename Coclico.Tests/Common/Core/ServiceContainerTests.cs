using Coclico.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Coclico.Tests;

[Collection("ServiceContainerSerial")]
public sealed class ServiceContainerTests
{
    private sealed class Marker;

    [Fact]
    public void Build_ThenGetRequired_ReturnsRegisteredSingleton()
    {
        try
        {
            ServiceContainer.Build(services => services.AddSingleton<Marker>());

            Marker first = ServiceContainer.GetRequired<Marker>();
            Marker second = ServiceContainer.GetRequired<Marker>();

            Assert.Same(first, second);
            Assert.Same(first, ServiceContainer.GetOptional<Marker>());
        }
        finally
        {
            ServiceContainer.Shutdown();
        }
    }

    [Fact]
    public void Build_ThenGetRequired_ReturnsNewInstancesForTransients()
    {
        try
        {
            ServiceContainer.Build(services => services.AddTransient<Marker>());

            Marker first = ServiceContainer.GetRequired<Marker>();
            Marker second = ServiceContainer.GetRequired<Marker>();

            Assert.NotSame(first, second);
        }
        finally
        {
            ServiceContainer.Shutdown();
        }
    }

    [Fact]
    public void GetOptional_ReturnsNullForUnregisteredService()
    {
        try
        {
            ServiceContainer.Build(services => { });

            Assert.Null(ServiceContainer.GetOptional<Marker>());
        }
        finally
        {
            ServiceContainer.Shutdown();
        }
    }

    [Fact]
    public void Shutdown_ReleasesContainerAndGetOptionalFallsBackToNull()
    {
        ServiceContainer.Build(services => services.AddSingleton<Marker>());
        _ = ServiceContainer.GetRequired<Marker>();
        ServiceContainer.Shutdown();

        Assert.False(ServiceContainer.GetOptional<Marker>() is not null);
    }

    [Fact]
    public void Provider_AfterShutdownWithoutRebuild_Throws()
    {
        ServiceContainer.Build(services => services.AddSingleton<Marker>());
        ServiceContainer.Shutdown();

        Assert.Throws<InvalidOperationException>(() => ServiceContainer.Provider);
    }
}
