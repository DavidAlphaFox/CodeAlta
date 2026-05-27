using CodeAlta.Catalog;

namespace CodeAlta.Models;

internal sealed class ShellSelectionState
{
    public SessionViewViewState ViewState { get; set; } = new();

    public ShellSelection Selection { get; set; } = ShellSelection.GlobalDraft();

    public string? PendingStartupSessionRestoreId { get; set; }
}
