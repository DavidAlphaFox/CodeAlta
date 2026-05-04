using CodeAlta.Plugins.Abstractions;

namespace CodeAlta.Plugins.Tests;

[TestClass]
public sealed class PluginChangeNotificationServiceTests
{
    [TestMethod]
    public void NotifyCoalescesToastsFooterStatusAndActionDispatch()
    {
        var toastMessages = new List<string>();
        var service = new PluginChangeNotificationService(new PluginChangeNotificationOptions { Interactive = true }, toastMessages.Add);
        var root = new PluginRoot { RootPath = "plugins", Scope = PluginScope.Global };
        PluginChangeNotification? opened = null;
        service.OpenManagementRequested += (_, notification) => opened = notification;

        var notification = service.Notify([
            new PluginSourceChange { Root = root, PackageId = "b", Kind = PluginSourceChangeKind.Changed },
            new PluginSourceChange { Root = root, PackageId = "a", Kind = PluginSourceChangeKind.Added },
            new PluginSourceChange { Root = root, PackageId = "a", Kind = PluginSourceChangeKind.Changed },
        ]);

        Assert.IsNotNull(notification);
        CollectionAssert.AreEqual(new[] { "a", "b" }, notification.PackageIds.ToArray());
        Assert.AreEqual("Plugins: 2 changed", service.FooterStatus);
        CollectionAssert.AreEqual(new[] { "2 plugins changed" }, toastMessages.ToArray());

        service.OpenManagementForChangedPlugins();
        Assert.AreSame(notification, opened);
    }

    [TestMethod]
    public void NotifyUsesHeadlessFallbackAndClearRemovesFooterStatus()
    {
        var service = new PluginChangeNotificationService(new PluginChangeNotificationOptions { Interactive = false });
        var root = new PluginRoot { RootPath = "plugins", Scope = PluginScope.Global };

        service.Notify([
            new PluginSourceChange { Root = root, Kind = PluginSourceChangeKind.UnknownRescanRequired },
        ]);

        Assert.AreEqual("Plugins: 1 changed", service.FooterStatus);
        CollectionAssert.AreEqual(new[] { "1 plugin changed" }, service.HeadlessMessages.ToArray());

        service.Clear();

        Assert.IsNull(service.FooterStatus);
    }
}
