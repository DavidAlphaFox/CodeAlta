using CodeAlta.Orchestration.Runtime.Prompts;

namespace CodeAlta.Orchestration.Tests;

[TestClass]
public sealed class SessionPromptTextTests
{
    [TestMethod]
    public void NormalizeForDisplay_CollapsesWhitespaceAndNewLines()
    {
        var normalized = SessionPromptText.NormalizeForDisplay("  Write\r\n  tests\nnow  ");

        Assert.AreEqual("Write tests now", normalized);
    }

    [TestMethod]
    public void CreateInitialSessionTitle_UsesFirstSentence()
    {
        var title = SessionPromptText.CreateInitialSessionTitle("Implement this. Then do that.");

        Assert.AreEqual("Implement this.", title);
    }

    [TestMethod]
    public void CreateInitialSessionTitle_TruncatesLongPrompt()
    {
        var title = SessionPromptText.CreateInitialSessionTitle("0123456789 0123456789 0123456789", maxLength: 16);

        Assert.AreEqual("0123456789 01...", title);
        Assert.IsTrue(title.Length <= 16);
    }

    [TestMethod]
    public void CreateInitialSessionTitle_RejectsTooSmallMaxLength()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SessionPromptText.CreateInitialSessionTitle("prompt", maxLength: 3));
    }
}
