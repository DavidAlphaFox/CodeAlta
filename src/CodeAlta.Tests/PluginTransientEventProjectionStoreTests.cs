using CodeAlta.App;
using CodeAlta.Plugins.Abstractions;

namespace CodeAlta.Tests;

[TestClass]
public sealed class PluginTransientEventProjectionStoreTests
{
    [TestMethod]
    public void Apply_UpsertsStableMarkdownProjection()
    {
        var store = new PluginTransientEventProjectionStore();

        var changed = store.Apply(new PluginDerivedThreadEvent
        {
            EventId = "plugin:event-1",
            Markdown = "### Stats",
        });
        var unchanged = store.Apply(new PluginDerivedThreadEvent
        {
            EventId = "plugin:event-1",
            Markdown = "### Stats",
        });

        Assert.IsTrue(changed);
        Assert.IsFalse(unchanged);
        Assert.AreEqual("### Stats", store.Snapshot.Single().Markdown);
    }

    [TestMethod]
    public void Apply_RemoveDeletesExistingProjection()
    {
        var store = new PluginTransientEventProjectionStore();
        store.Apply(new PluginDerivedThreadEvent { EventId = "plugin:event-1", Markdown = "text" });

        var removed = store.Apply(new PluginDerivedThreadEvent { EventId = "plugin:event-1", Remove = true });

        Assert.IsTrue(removed);
        Assert.AreEqual(0, store.Snapshot.Count);
    }

    [TestMethod]
    public void Apply_UsesDefaultMarkdownWhenPluginDoesNotProvideText()
    {
        var store = new PluginTransientEventProjectionStore();

        store.Apply(new PluginDerivedThreadEvent
        {
            EventId = "plugin:event-1",
            RenderTarget = "stats",
        });

        StringAssert.Contains(store.Snapshot.Single().Markdown, "plugin:event-1");
        StringAssert.Contains(store.Snapshot.Single().Markdown, "stats");
    }
}
