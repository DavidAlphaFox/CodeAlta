using RawCaptureApp;

namespace CodeAlta.Tests;

[TestClass]
public sealed class RawCaptureAppOptionsTests
{
    [TestMethod]
    public void TryParse_InfersTestNameFromFolderAndDefaultsToBothTargets()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"raw-capture-test-{Guid.NewGuid():N}");
        var workingDirectory = Path.Combine(tempRoot, "agent-discovery");
        Directory.CreateDirectory(workingDirectory);

        try {
            var result = CaptureRunOptionsParser.TryParse(
                ["Explain this folder.", workingDirectory],
                outputDirectory: @"C:\captures",
                out var options,
                out var errorMessage);

            Assert.IsTrue(result);
            Assert.IsNull(errorMessage);
            Assert.IsNotNull(options);
            Assert.AreEqual("Explain this folder.", options.Prompt);
            Assert.AreEqual(Path.GetFullPath(workingDirectory), options.SourceWorkingDirectory);
            Assert.AreEqual("agent-discovery", options.TestCaseName);
            Assert.AreEqual(CaptureTargets.Copilot | CaptureTargets.Codex, options.Targets);
            Assert.AreEqual(@"C:\captures\copilot_agent-discovery.jsonl", options.CopilotOutputPath);
            Assert.AreEqual(@"C:\captures\codex_agent-discovery.jsonl", options.CodexOutputPath);
        } finally {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [TestMethod]
    public void TryParse_AcceptsExplicitTestNameAndSingleTarget()
    {
        var workingDirectory = Path.Combine(Path.GetTempPath(), $"raw-capture-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workingDirectory);

        try {
            var result = CaptureRunOptionsParser.TryParse(
                ["--codex", "Prompt", workingDirectory, "Readme Edit"],
                outputDirectory: @"C:\captures",
                out var options,
                out var errorMessage);

            Assert.IsTrue(result);
            Assert.IsNull(errorMessage);
            Assert.IsNotNull(options);
            Assert.AreEqual(CaptureTargets.Codex, options.Targets);
            Assert.AreEqual("readme_edit", options.TestCaseName);
            Assert.AreEqual(@"C:\captures\codex_readme_edit.jsonl", options.CodexOutputPath);
        } finally {
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void TryParse_RejectsMissingFolder()
    {
        var result = CaptureRunOptionsParser.TryParse(
            ["Prompt", @"C:\definitely-missing-folder-for-raw-capture"],
            outputDirectory: @"C:\captures",
            out var options,
            out var errorMessage);

        Assert.IsFalse(result);
        Assert.IsNull(options);
        Assert.AreEqual(
            "Folder 'C:\\definitely-missing-folder-for-raw-capture' does not exist.",
            errorMessage);
    }
}
