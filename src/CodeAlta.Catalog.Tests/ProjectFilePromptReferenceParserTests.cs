using CodeAlta.Catalog;

namespace CodeAlta.Catalog.Tests;

[TestClass]
public sealed class ProjectFilePromptReferenceParserTests
{
    [TestMethod]
    public void Parse_HandlesEscapesQuotedPathsRangesAndEmailFalsePositives()
    {
        var tokens = ProjectFilePromptReferenceParser.Parse(
            "mail name@example.com @@ literal @\"src/My File.cs\":10-12 and @docs/readme.md");

        Assert.AreEqual(3, tokens.Count);
        Assert.AreEqual(ProjectFilePromptTokenKind.EscapedAt, tokens[0].Kind);
        Assert.AreEqual(ProjectFilePromptTokenKind.Reference, tokens[1].Kind);
        Assert.AreEqual("src/My File.cs", tokens[1].LookupText);
        Assert.AreEqual(10, tokens[1].LineRange!.StartLine);
        Assert.AreEqual(12, tokens[1].LineRange!.EndLine);
        Assert.AreEqual(ProjectFilePromptTokenKind.Reference, tokens[2].Kind);
        Assert.AreEqual("docs/readme.md", tokens[2].LookupText);
    }

    [TestMethod]
    public void Parse_RecognizesProjectRelativeMarkdownLinks()
    {
        var tokens = ProjectFilePromptReferenceParser.Parse(
            "See [Program.cs](src/Program.cs:10-12) and [site](https://example.com)");

        Assert.AreEqual(1, tokens.Count);
        Assert.AreEqual(ProjectFilePromptTokenKind.Reference, tokens[0].Kind);
        Assert.AreEqual("Program.cs", tokens[0].DisplayText);
        Assert.AreEqual("src/Program.cs", tokens[0].LookupText);
        Assert.AreEqual(10, tokens[0].LineRange!.StartLine);
        Assert.AreEqual(12, tokens[0].LineRange!.EndLine);
    }
}
