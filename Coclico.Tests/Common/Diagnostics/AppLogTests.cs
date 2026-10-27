using Coclico.Services;
using Xunit;

namespace Coclico.Tests;

[Collection("ServiceContainerSerial")]
public sealed class AppLogTests
{
    [Fact]
    public void Info_Warn_Error_NeverThrow()
    {
        AppLog.Info("ut-info");
        AppLog.Warn("ut-warn");
        AppLog.Error("ut-error");
        AppLog.Error("ut-error", new InvalidOperationException("ut"));
    }

    [Fact]
    public void Exception_NeverThrows()
    {
        AppLog.Exception(new InvalidOperationException("ut-exception"), "ut-context");
        AppLog.Exception(new InvalidOperationException("ut-exception"));
    }

    [Fact]
    public void Action_NeverThrowsWhetherContainerIsBuiltOrNot()
    {
        AppLog.Action("Nettoyage", "C:\\Temp");
        AppLog.Action("Analyse", "Disque C:", success: false, details: "simulation", actor: "ut-actor");
    }

    [Fact]
    public void Action_WithBuiltContainer_CompletesWithoutThrowing()
    {
        try
        {
            ServiceContainer.Build(services => { });
            AppLog.Action("ut-action", "ut-cible", actor: "ut");
        }
        finally
        {
            ServiceContainer.Shutdown();
        }
    }
}
