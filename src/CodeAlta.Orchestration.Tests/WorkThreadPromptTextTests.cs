using CodeAlta.Orchestration.Runtime.Prompts;

namespace CodeAlta.Orchestration.Tests;

[TestClass]
public sealed class WorkThreadPromptTextTests
{
    [TestMethod]
    public void NormalizeForDisplay_CollapsesWhitespaceAndNewLines()
    {
        var normalized = WorkThreadPromptText.NormalizeForDisplay("  Write\r\n  tests\nnow  ");

        Assert.AreEqual("Write tests now", normalized);
    }

    [TestMethod]
    public void CreateInitialThreadTitle_UsesFirstSentence()
    {
        var title = WorkThreadPromptText.CreateInitialThreadTitle("Implement this. Then do that.");

        Assert.AreEqual("Implement this.", title);
    }

    [TestMethod]
    public void CreateInitialThreadTitle_TruncatesLongPrompt()
    {
        var title = WorkThreadPromptText.CreateInitialThreadTitle("0123456789 0123456789 0123456789", maxLength: 16);

        Assert.AreEqual("0123456789 01...", title);
        Assert.IsTrue(title.Length <= 16);
    }

    [TestMethod]
    public void CreateInitialThreadTitle_RejectsTooSmallMaxLength()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => WorkThreadPromptText.CreateInitialThreadTitle("prompt", maxLength: 3));
    }
}
