namespace CodeAlta.Tests;

[TestClass]
public sealed class CodeAltaLoggingTests
{
    [TestMethod]
    public void GetLogFilePath_UsesCodeAltaLogsDirectory()
    {
        var homeRoot = Path.Combine(Path.GetTempPath(), ".codealta-test-home");
        var path = CodeAltaLogging.GetLogFilePath(homeRoot);

        Assert.AreEqual(Path.Combine(homeRoot, "logs", CodeAltaLogging.LogFileName), path);
    }
}
