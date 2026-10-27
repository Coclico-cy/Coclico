using Coclico.Services.AI;
using Xunit;

namespace Coclico.Tests;

public sealed class AiToolExecutionServiceSanitizerTests
{
    [Fact]
    public void SanitizeStreamingTextCutsAtVerificationMarker()
    {
        var service = new AiToolExecutionService();

        string result = service.SanitizeStreamingText("Je lance le nettoyage. [Vérification en cours...] 40%");

        Assert.Equal("Je lance le nettoyage.", result);
    }

    [Fact]
    public void SanitizeStreamingTextCutsAtSimulatedStep()
    {
        var service = new AiToolExecutionService();

        string result = service.SanitizeStreamingText("Nettoyage démarré.\nÉtape 1 sur 4 : analyse...");

        Assert.Equal("Nettoyage démarré.", result);
    }

    [Fact]
    public void SanitizeStreamingTextCutsAtFakeDoneMarker()
    {
        var service = new AiToolExecutionService();

        string result = service.SanitizeStreamingText("C'est fait ! Nettoyage terminé, 12 Go libérés.");

        Assert.Equal("C'est fait !", result);
    }

    [Fact]
    public void SanitizeStreamingTextLeavesCleanTextUnchanged()
    {
        var service = new AiToolExecutionService();
        const string clean = "Je peux nettoyer vos fichiers temporaires si vous le souhaitez.";

        Assert.Equal(clean, service.SanitizeStreamingText(clean));
    }

    [Fact]
    public void SanitizeStreamingTextHandlesEmptyInput()
    {
        var service = new AiToolExecutionService();

        Assert.Equal(string.Empty, service.SanitizeStreamingText(string.Empty));
        Assert.Equal(string.Empty, service.SanitizeStreamingText("   "));
    }

    [Theory]
    [InlineData("Progression : étape 2 sur 3 en cours", true)]
    [InlineData("[Vérification des fichiers système...]", true)]
    [InlineData("Nettoyage terminé, 12 Go libérés.", true)]
    [InlineData("Vous devez redémarrer votre PC pour appliquer les changements.", true)]
    [InlineData("processus 1, processus 2 et processus 3 ont été fermés", true)]
    [InlineData("Bonjour ! Comment puis-je vous aider ?", false)]
    [InlineData("", false)]
    public void ContainsSimulatedProgressOrFakeCompletionDetectsHallucinations(string text, bool expected)
    {
        Assert.Equal(expected, AiToolExecutionService.ContainsSimulatedProgressOrFakeCompletion(text));
    }

    [Fact]
    public void SanitizeActionResponseReturnsRawTextWhenNoActions()
    {
        var service = new AiToolExecutionService();
        const string raw = "Voici un texte brut sans action détectée.";

        Assert.Equal(raw, service.SanitizeActionResponse(raw, []));
    }

    [Fact]
    public void SanitizeActionResponseFallsBackToDefaultConfirmationWhenTextIsFullyHallucinated()
    {
        var service = new AiToolExecutionService();
        var action = new AiActionRequest("CLEAN_RAM", null, "[ACTION:CLEAN_RAM]");
        const string hallucinated = """
            Étape 1 sur 2 : analyse des fichiers...
            Étape 2 sur 2 : suppression en cours...
            Nettoyage terminé ! 12 Go libérés.
            """;

        string result = service.SanitizeActionResponse(hallucinated, [action]);

        Assert.Equal("Parfait, je lance l'optimisation de la mémoire vive pour vous.", result);
    }

    [Fact]
    public void SanitizeActionResponseKeepsValidAcknowledgementSentence()
    {
        var service = new AiToolExecutionService();
        var action = new AiActionRequest("CLEAN_TEMP", null, "[ACTION:CLEAN_TEMP]");
        const string mixed = """
            Je lance le nettoyage des fichiers temporaires.
            Étape 1 : analyse en cours...
            Nettoyage terminé ! 2 Go libérés.
            """;

        string result = service.SanitizeActionResponse(mixed, [action]);

        Assert.Contains("Je lance", result);
        Assert.DoesNotContain("Étape", result);
        Assert.DoesNotContain("terminé", result);
    }
}
