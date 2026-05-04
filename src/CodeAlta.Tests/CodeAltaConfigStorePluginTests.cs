using CodeAlta.Catalog;

namespace CodeAlta.Tests;

[TestClass]
public sealed class CodeAltaConfigStorePluginTests
{
    [TestMethod]
    public void SaveGlobalDefaultProviderPreservesUnknownPluginEntries()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "CodeAltaConfigStorePluginTests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempPath);
            var options = new CatalogOptions { GlobalRoot = tempPath };
            File.WriteAllText(options.ConfigPath, """
[plugins.unknown_plugin]
enabled = true

[chat]
default_provider = "codex"
""");
            var store = new CodeAltaConfigStore(options);

            store.SaveGlobalDefaultProvider("copilot");
            var document = store.LoadGlobal();

            Assert.IsNotNull(document.Plugins);
            Assert.IsTrue(document.Plugins.TryGetValue("unknown_plugin", out var settings));
            Assert.AreEqual(true, settings.Enabled);
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
