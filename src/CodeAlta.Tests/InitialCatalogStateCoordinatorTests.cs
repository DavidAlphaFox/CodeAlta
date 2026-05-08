using CodeAlta.App;
using CodeAlta.Catalog;
using CodeAlta.Models;
using CodeAlta.Views;

namespace CodeAlta.Tests;

[TestClass]
public sealed class InitialCatalogStateCoordinatorTests
{
    [TestMethod]
    public void TryResolve_RefreshesCatalogWorkspaceImmediatelyAfterApplyingState()
    {
        var state = new ShellThreadStateCoordinator.InitialCatalogState(
            Array.Empty<ProjectDescriptor>(),
            Array.Empty<WorkThreadDescriptor>(),
            new WorkThreadViewState());
        var sequence = new List<string>();
        var stateApplied = false;
        var coordinator = new InitialCatalogStateCoordinator(
            _ => Task.FromResult(state),
            appliedState =>
            {
                Assert.AreSame(state, appliedState);
                stateApplied = true;
                sequence.Add("apply");
            },
            () =>
            {
                Assert.IsTrue(stateApplied, "Catalog state must be applied before refreshing the sidebar/workspace projection.");
                sequence.Add("refresh");
            },
            () => sequence.Add("focus"),
            static (_, _, _) => Assert.Fail("Resolving a completed catalog state should not report an error."));

        coordinator.EnsureStarted(CancellationToken.None);
        var resolved = coordinator.TryResolve(CancellationToken.None);

        Assert.IsTrue(resolved);
        CollectionAssert.AreEqual(new[] { "apply", "refresh", "focus" }, sequence);
    }
}
