using CodeAlta.App.State;
using CodeAlta.Models;
using CodeAlta.ViewModels;

namespace CodeAlta.Tests;

[TestClass]
public sealed class ShellViewModelProjectionTests
{
    [TestMethod]
    public void ApplyStatus_ProjectsSnapshotIntoViewModel()
    {
        var viewModel = new CodeAltaShellViewModel();

        ShellViewModelProjection.ApplyStatus(viewModel, new ShellStatusSnapshot("Ready", Busy: false, StatusTone.Ready));

        Assert.AreEqual("Ready", viewModel.StatusText);
        Assert.IsFalse(viewModel.StatusBusy);
        Assert.AreEqual(StatusTone.Ready, viewModel.StatusTone);
        Assert.IsFalse(string.IsNullOrWhiteSpace(viewModel.StatusIconMarkup));
    }
}
