namespace CodeAlta.Tests;

[TestClass]
public sealed class CodeAltaLoggingTests
{
    [TestMethod]
    public void GetLogFilePath_UsesCodeAltaLogsDirectory()
    {
        var path = CodeAltaLogging.GetLogFilePath(@"C:\Users\alex\.codealta");

        Assert.AreEqual(@"C:\Users\alex\.codealta\logs\codealta.log", path);
    }
}
