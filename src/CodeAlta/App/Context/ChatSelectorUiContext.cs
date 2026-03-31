using CodeAlta.Threading;
using CodeAlta.Models;
using CodeAlta.Presentation.Chat;
using XenoAtom.Terminal.UI.Controls;

namespace CodeAlta.App.Context;

internal sealed class ChatSelectorUiContext
{
    private readonly Func<Select<ChatBackendOption>?> _getChatBackendSelect;
    private readonly Func<Select<ChatModelOption>?> _getChatModelSelect;
    private readonly Func<Select<ChatReasoningOption>?> _getChatReasoningSelect;
    private readonly Func<IUiDispatcher> _getUiDispatcher;
    private readonly Action _verifyBindableAccess;

    public ChatSelectorUiContext(
        Func<Select<ChatBackendOption>?> getChatBackendSelect,
        Func<Select<ChatModelOption>?> getChatModelSelect,
        Func<Select<ChatReasoningOption>?> getChatReasoningSelect,
        Func<IUiDispatcher> getUiDispatcher,
        Action verifyBindableAccess)
    {
        ArgumentNullException.ThrowIfNull(getChatBackendSelect);
        ArgumentNullException.ThrowIfNull(getChatModelSelect);
        ArgumentNullException.ThrowIfNull(getChatReasoningSelect);
        ArgumentNullException.ThrowIfNull(getUiDispatcher);
        ArgumentNullException.ThrowIfNull(verifyBindableAccess);

        _getChatBackendSelect = getChatBackendSelect;
        _getChatModelSelect = getChatModelSelect;
        _getChatReasoningSelect = getChatReasoningSelect;
        _getUiDispatcher = getUiDispatcher;
        _verifyBindableAccess = verifyBindableAccess;
    }

    public int? GetSelectedBackendIndex()
        => _getChatBackendSelect()?.SelectedIndex;

    public int? GetSelectedModelIndex()
        => _getChatModelSelect()?.SelectedIndex;

    public int? GetSelectedReasoningIndex()
        => _getChatReasoningSelect()?.SelectedIndex;

    public void UpdateBackendOptions(IReadOnlyList<ChatBackendOption> items, int selectedIndex, bool isEnabled)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (_getChatBackendSelect() is not { } select)
        {
            return;
        }

        ChatBackendPresentation.ReplaceSelectItems(select, items);
        select.SelectedIndex = selectedIndex;
        select.IsEnabled = isEnabled;
    }

    public void UpdateModelOptions(IReadOnlyList<ChatModelOption> items, int selectedIndex, bool isEnabled)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (_getChatModelSelect() is not { } select)
        {
            return;
        }

        ChatBackendPresentation.ReplaceSelectItems(select, items);
        select.SelectedIndex = selectedIndex;
        select.IsEnabled = isEnabled;
    }

    public void UpdateReasoningOptions(IReadOnlyList<ChatReasoningOption> items, int selectedIndex, bool isEnabled)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (_getChatReasoningSelect() is not { } select)
        {
            return;
        }

        ChatBackendPresentation.ReplaceSelectItems(select, items);
        select.SelectedIndex = selectedIndex;
        select.IsEnabled = isEnabled;
    }

    public IUiDispatcher GetUiDispatcher()
        => _getUiDispatcher();

    public void VerifyBindableAccess()
        => _verifyBindableAccess();
}
