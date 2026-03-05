using XenoAtom.Terminal.UI.Controls;

namespace CodeAlta.Tests;

[TestClass]
public sealed class CodeAltaTerminalUiTests
{
    [TestMethod]
    public void CreatePendingChatMessage_CreatesStreamingMarkdownSynchronously()
    {
        var pending = CodeAltaTerminalUi.CreatePendingChatMessage("hello, who are you?");

        Assert.IsInstanceOfType<MarkdownControl>(pending.StreamingMarkdown);
        Assert.AreEqual(string.Empty, pending.StreamingMarkdown.Markdown);
    }
}
