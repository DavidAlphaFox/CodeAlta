using CodeAlta.App.Context;

namespace CodeAlta.Tests;

[TestClass]
public sealed class WorkspaceRefreshContextTests
{
    [TestMethod]
    public void RefreshOperations_PublishTypedReasons()
    {
        var requests = new List<WorkspaceRefreshRequest>();
        var context = new WorkspaceRefreshContext(requests.Add);

        context.InvalidateSelectedSessionUsage();
        context.RefreshHeaderAndThreadWorkspace();

        CollectionAssert.AreEqual(
            new[]
            {
                new WorkspaceRefreshRequest(WorkspaceRefreshReason.SelectedSessionUsageInvalidated),
                new WorkspaceRefreshRequest(WorkspaceRefreshReason.HeaderAndThreadWorkspace),
            },
            requests);
    }
}
