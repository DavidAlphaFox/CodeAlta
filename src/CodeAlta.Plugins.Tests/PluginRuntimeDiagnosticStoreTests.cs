using CodeAlta.Plugins.Abstractions;

namespace CodeAlta.Plugins.Tests;

[TestClass]
public sealed class PluginRuntimeDiagnosticStoreTests
{
    [TestMethod]
    public void StoreKeepsPluginDiagnosticsOutsideConversationHistoryModels()
    {
        var store = new PluginRuntimeDiagnosticStore();
        var exception = new InvalidOperationException("Boom");

        store.Add(PluginRuntimeDiagnostic.Error(
            PluginRuntimeDiagnosticSource.Callback,
            "Callback failed.",
            "hello",
            "plugin.cs",
            exception));
        store.Add(PluginRuntimeDiagnostic.Warning(
            PluginRuntimeDiagnosticSource.SourceChange,
            "Source changed.",
            "hello",
            "plugin.cs"));

        Assert.AreEqual(2, store.GetSnapshot().Count);
        Assert.AreEqual(2, store.GetByPackage("hello").Count);
        Assert.AreEqual(1, store.GetBySource(PluginRuntimeDiagnosticSource.Callback).Count);
        var errors = store.GetByMinimumSeverity(PluginDiagnosticSeverity.Error);
        Assert.AreEqual(1, errors.Count);
        Assert.IsNotNull(errors[0].Exception);
        Assert.AreEqual("Boom", errors[0].Exception!.Message);

        store.Clear();

        Assert.AreEqual(0, store.GetSnapshot().Count);
    }
}
