using CodeAlta.Catalog;

namespace CodeAlta.Tests;

[TestClass]
public sealed class CodeAltaConfigStoreCompatibilityTests
{
    [TestMethod]
    public void SaveGlobalDefaultProvider_PreservesIgnoredAcpConfigBlocks()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), nameof(CodeAltaConfigStoreCompatibilityTests), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempPath);
            var options = new CatalogOptions { GlobalRoot = tempPath };
            File.WriteAllText(options.ConfigPath, """
[acp]
legacy = true

[acp.agents.sample]
command = "sample-agent"
args = ["--stdio"]

[chat]
default_provider = "codex"
""");
            var store = new CodeAltaConfigStore(options);

            store.SaveGlobalDefaultProvider("copilot");
            var content = File.ReadAllText(options.ConfigPath);

            StringAssert.Contains(content, "[acp]");
            StringAssert.Contains(content, "legacy = true");
            StringAssert.Contains(content, "[acp.agents.sample]");
            StringAssert.Contains(content, "command = \"sample-agent\"");
            Assert.AreEqual("copilot", store.LoadGlobal().Chat?.DefaultProvider);
        }
        finally
        {
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, recursive: true);
            }
        }
    }
}
