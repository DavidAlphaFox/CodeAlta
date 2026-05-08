using XenoAtom.Terminal.UI;
using CodeAlta.Models;
using CodeAlta.Presentation.Prompting;

namespace CodeAlta.ViewModels;

public sealed partial class ThreadWorkspaceViewModel
{
    public ThreadWorkspaceViewModel()
    {
        ModelProviderStatusMarkup = string.Empty;
        ProviderSummaryMarkup = string.Empty;
        SelectedModelProviderIndex = -1;
        SelectedModelIndex = -1;
        SelectedReasoningIndex = -1;
        ModelProviderOptions = [];
        ModelOptions = [];
        ReasoningOptions = [];
        PromptStripItems = [];
    }

    [Bindable]
    public partial string ModelProviderStatusMarkup { get; set; }

    [Bindable]
    public partial string ProviderSummaryMarkup { get; set; }

    [Bindable]
    public partial bool CanSelectModelProvider { get; set; }

    [Bindable]
    public partial bool CanSelectModel { get; set; }

    [Bindable]
    public partial bool CanSelectReasoning { get; set; }

    [Bindable]
    public partial IReadOnlyList<ChatBackendOption> ModelProviderOptions { get; set; }

    [Bindable]
    public partial int SelectedModelProviderIndex { get; set; }

    [Bindable]
    public partial IReadOnlyList<ChatModelOption> ModelOptions { get; set; }

    [Bindable]
    public partial int SelectedModelIndex { get; set; }

    [Bindable]
    public partial IReadOnlyList<ChatReasoningOption> ReasoningOptions { get; set; }

    [Bindable]
    public partial int SelectedReasoningIndex { get; set; }

    [Bindable]
    public partial bool HasQueuedPrompts { get; set; }

    [Bindable]
    public partial bool CanShowThreadInfo { get; set; }

    [Bindable]
    public partial IReadOnlyList<PromptStripItem> PromptStripItems { get; set; }

    internal void SetPromptStripItems(IReadOnlyList<PromptStripItem> promptStripItems, bool hasQueuedPrompts)
    {
        ArgumentNullException.ThrowIfNull(promptStripItems);

        PromptStripItems = promptStripItems;
        HasQueuedPrompts = hasQueuedPrompts;
    }
}
