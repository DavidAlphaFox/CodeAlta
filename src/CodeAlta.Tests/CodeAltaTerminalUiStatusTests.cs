using CodeAlta.Agent;

namespace CodeAlta.Tests;

[TestClass]
public sealed class CodeAltaTerminalUiStatusTests
{
    [TestMethod]
    public void ResolveSelectionStatus_PrefersThreadSpecificStatus()
    {
        var snapshot = CodeAltaTerminalUi.ResolveSelectionStatus(
            readyMessage: "Prompt ready · Review startup",
            hasThreadStatus: true,
            threadStatusMessage: "Running 'Review startup'...",
            threadStatusBusy: true,
            threadStatusTone: CodeAltaTerminalUi.StatusTone.Info,
            promptUnavailable: true,
            promptUnavailableMessage: "Codex is unavailable.",
            promptUnavailableTone: CodeAltaTerminalUi.StatusTone.Warning);

        Assert.AreEqual("Running 'Review startup'...", snapshot.Message);
        Assert.IsTrue(snapshot.Busy);
        Assert.AreEqual(CodeAltaTerminalUi.StatusTone.Info, snapshot.Tone);
    }

    [TestMethod]
    public void ResolveSelectionStatus_FallsBackToPromptUnavailableWhenThreadHasNoCustomStatus()
    {
        var snapshot = CodeAltaTerminalUi.ResolveSelectionStatus(
            readyMessage: "Prompt ready · Review startup",
            hasThreadStatus: false,
            threadStatusMessage: "Stopped · Review startup",
            threadStatusBusy: false,
            threadStatusTone: CodeAltaTerminalUi.StatusTone.Warning,
            promptUnavailable: true,
            promptUnavailableMessage: "Codex is unavailable.",
            promptUnavailableTone: CodeAltaTerminalUi.StatusTone.Warning);

        Assert.AreEqual("Codex is unavailable.", snapshot.Message);
        Assert.IsFalse(snapshot.Busy);
        Assert.AreEqual(CodeAltaTerminalUi.StatusTone.Warning, snapshot.Tone);
    }

    [TestMethod]
    public void ResolveSelectionStatus_UsesReadyMessageWhenNothingOverridesIt()
    {
        var snapshot = CodeAltaTerminalUi.ResolveSelectionStatus(
            readyMessage: "Prompt ready · Review startup",
            hasThreadStatus: false,
            threadStatusMessage: null,
            threadStatusBusy: false,
            threadStatusTone: CodeAltaTerminalUi.StatusTone.Info,
            promptUnavailable: false,
            promptUnavailableMessage: null,
            promptUnavailableTone: CodeAltaTerminalUi.StatusTone.Warning);

        Assert.AreEqual("Prompt ready · Review startup", snapshot.Message);
        Assert.IsFalse(snapshot.Busy);
        Assert.AreEqual(CodeAltaTerminalUi.StatusTone.Ready, snapshot.Tone);
    }
}
