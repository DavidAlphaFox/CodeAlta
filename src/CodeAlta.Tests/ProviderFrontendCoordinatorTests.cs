using CodeAlta.Agent;
using CodeAlta.App;
using CodeAlta.Catalog;
using CodeAlta.Models;

namespace CodeAlta.Tests;

[TestClass]
public sealed class ProviderFrontendCoordinatorTests
{
    [TestMethod]
    public void TryBuildActiveProviderTestResult_UsesReadyBackendState()
    {
        var definition = new CodeAltaProviderDocument
        {
            ProviderKey = "copilot",
            ProviderType = "copilot",
        };
        var backendState = new ModelProviderState(ModelProviderIds.Copilot, "Copilot")
        {
            Availability = ModelProviderAvailability.Ready,
            StatusMessage = "Ready",
        };
        backendState.Models.Add(new AgentModelInfo("gpt-4.1"));
        backendState.Models.Add(new AgentModelInfo("gpt-4o"));

        var reused = ProviderFrontendCoordinator.TryBuildActiveProviderTestResult(
            definition,
            new Dictionary<string, ModelProviderState>(StringComparer.OrdinalIgnoreCase)
            {
                [definition.ProviderKey] = backendState,
            },
            out var result);

        Assert.IsTrue(reused);
        Assert.IsTrue(result.Success);
        Assert.AreEqual(2, result.ModelCount);
        StringAssert.Contains(result.Message, "Using active model provider");
    }

    [TestMethod]
    public void TryBuildActiveProviderTestResult_UsesFailureStatusForBackend()
    {
        var definition = new CodeAltaProviderDocument
        {
            ProviderKey = "codex",
            ProviderType = "codex",
        };
        var backendState = new ModelProviderState(ModelProviderIds.Codex, "Codex")
        {
            Availability = ModelProviderAvailability.Failed,
            StatusMessage = "Codex startup failed.",
        };

        var reused = ProviderFrontendCoordinator.TryBuildActiveProviderTestResult(
            definition,
            new Dictionary<string, ModelProviderState>(StringComparer.OrdinalIgnoreCase)
            {
                [definition.ProviderKey] = backendState,
            },
            out var result);

        Assert.IsTrue(reused);
        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, result.ModelCount);
        Assert.AreEqual("Codex startup failed.", result.Message);
    }

    [TestMethod]
    public void TryBuildActiveProviderTestResult_DoesNotReuseMissingProviderState()
    {
        var definition = new CodeAltaProviderDocument
        {
            ProviderKey = "openai",
            ProviderType = "openai-chat",
        };
        var reused = ProviderFrontendCoordinator.TryBuildActiveProviderTestResult(
            definition,
            new Dictionary<string, ModelProviderState>(StringComparer.OrdinalIgnoreCase),
            out var result);

        Assert.IsFalse(reused);
        Assert.AreEqual(default, result);
    }

}
