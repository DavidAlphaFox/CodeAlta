using CodeAlta.Catalog;
using XenoAtom.Terminal.UI;

namespace CodeAlta.ViewModels;

public sealed partial class ProjectThreadsDialogRowViewModel
{
    public ProjectThreadsDialogRowViewModel()
    {
        ThreadId = string.Empty;
        Title = string.Empty;
        BackendId = string.Empty;
        BackendDisplayName = string.Empty;
        LastUpdatedRelative = "never";
        LastUpdatedExact = "Never";
    }

    [Bindable]
    public partial bool IsSelected { get; set; }

    [Bindable]
    public partial string ThreadId { get; set; }

    [Bindable]
    public partial string Title { get; set; }

    [Bindable]
    public partial string BackendId { get; set; }

    [Bindable]
    public partial string BackendDisplayName { get; set; }

    [Bindable]
    public partial WorkThreadKind ThreadKind { get; set; }

    [Bindable]
    public partial DateTimeOffset? LastUpdatedAt { get; set; }

    [Bindable]
    public partial string LastUpdatedRelative { get; set; }

    [Bindable]
    public partial string LastUpdatedExact { get; set; }

    [Bindable]
    public partial int? MessageCount { get; set; }

    [Bindable]
    public partial string? ProjectId { get; set; }
}
