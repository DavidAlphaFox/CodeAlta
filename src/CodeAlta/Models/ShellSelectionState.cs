using CodeAlta.Catalog;

namespace CodeAlta.Models;

internal sealed class ShellSelectionState
{
    public WorkThreadViewState ViewState { get; set; } = new();

    public ShellSelection Selection { get; set; } = ShellSelection.GlobalDraft();

    public string? PendingStartupThreadRestoreId { get; set; }
}
