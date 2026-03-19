using CodeAlta.Catalog;
using XenoAtom.Terminal.UI;

namespace CodeAlta.Tests;

[TestClass]
public sealed class CodeAltaAppTabStripTests
{
    [TestMethod]
    public void ResolveOpenTabIndicatorKind_PrefersRunningAndMapsTone()
    {
        Assert.AreEqual(
            CodeAltaApp.OpenTabIndicatorKind.Running,
            CodeAltaApp.ResolveOpenTabIndicatorKind(isBusy: true, CodeAltaApp.StatusTone.Ready));
        Assert.AreEqual(
            CodeAltaApp.OpenTabIndicatorKind.Ready,
            CodeAltaApp.ResolveOpenTabIndicatorKind(isBusy: false, CodeAltaApp.StatusTone.Ready));
        Assert.AreEqual(
            CodeAltaApp.OpenTabIndicatorKind.Warning,
            CodeAltaApp.ResolveOpenTabIndicatorKind(isBusy: false, CodeAltaApp.StatusTone.Warning));
        Assert.AreEqual(
            CodeAltaApp.OpenTabIndicatorKind.Error,
            CodeAltaApp.ResolveOpenTabIndicatorKind(isBusy: false, CodeAltaApp.StatusTone.Error));
    }

    [TestMethod]
    public void CompactTabTitle_DoesNotChangeForSelectionState()
    {
        Assert.AreEqual("Review startup", CodeAltaApp.CompactTabTitle("Review startup"));
    }

    [TestMethod]
    public void CreateThreadTabPageContentPlaceholder_ReturnsHiddenDetachedVisual()
    {
        var method = typeof(CodeAltaApp).GetMethod("CreateThreadTabPageContentPlaceholder", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(method);

        var first = (Visual?)method.Invoke(null, null);
        var second = (Visual?)method.Invoke(null, null);

        Assert.IsNotNull(first);
        Assert.IsNotNull(second);
        Assert.IsFalse(first.IsVisible);
        Assert.IsFalse(second.IsVisible);
        Assert.IsNull(first.Parent);
        Assert.IsNull(second.Parent);
        Assert.AreNotSame(first, second);
    }

    [TestMethod]
    public void BuildDraftTabTitle_ReflectsScope()
    {
        Assert.AreEqual("Global draft", CodeAltaApp.BuildDraftTabTitle(selectedProject: null, globalScopeSelected: true));
        Assert.AreEqual(
            "CodeAlta draft",
            CodeAltaApp.BuildDraftTabTitle(
                new ProjectDescriptor { DisplayName = "CodeAlta" },
                globalScopeSelected: false));
    }

    [TestMethod]
    public void Build_IncludesOpenThreadTabsAndSelectedDraftTab()
    {
        var projection = ThreadTabStripProjectionBuilder.Build(
            openThreadIds: ["thread-1", "missing-thread"],
            availableThreadIds: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "thread-1" },
            draftTabOpen: true,
            draftTabId: CodeAltaApp.DraftTabId,
            selectedThreadId: null);

        CollectionAssert.AreEqual(
            new[] { "thread-1", CodeAltaApp.DraftTabId },
            projection.Tabs.Select(static tab => tab.TabId).ToArray());
        Assert.AreEqual(CodeAltaApp.DraftTabId, projection.SelectedTabId);
        Assert.IsTrue(projection.Tabs[1].IsDraft);
    }

    [TestMethod]
    public void Build_SelectsOpenThreadWhenAvailable()
    {
        var projection = ThreadTabStripProjectionBuilder.Build(
            openThreadIds: ["thread-1", "thread-2"],
            availableThreadIds: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "thread-1", "thread-2" },
            draftTabOpen: true,
            draftTabId: CodeAltaApp.DraftTabId,
            selectedThreadId: "thread-2");

        Assert.AreEqual("thread-2", projection.SelectedTabId);
        Assert.IsFalse(projection.Tabs[1].IsDraft);
        Assert.IsTrue(projection.Tabs[2].IsDraft);
    }
}
