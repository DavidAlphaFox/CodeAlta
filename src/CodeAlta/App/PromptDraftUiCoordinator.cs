using CodeAlta.Models;
using CodeAlta.Catalog;
using CodeAlta.ViewModels;
using XenoAtom.Terminal.UI;

namespace CodeAlta.App;

internal sealed class PromptDraftUiCoordinator : IAsyncDisposable
{
    private readonly PromptDraftCoordinator _promptDrafts;
    private readonly ThreadPromptDraftPersistenceCoordinator _promptDraftPersistence;
    private readonly Func<string?> _getSelectedThreadId;
    private readonly Action _onThreadPromptEditedStateChanged;
    private readonly PromptDraftViewModel _viewModel;
    private ThreadSessionState? _selectedSession;
    private bool _syncingPromptText;

    public PromptDraftUiCoordinator(
        PromptDraftCoordinator promptDrafts,
        CatalogOptions catalogOptions,
        Func<string?> getSelectedThreadId,
        Action onThreadPromptEditedStateChanged)
    {
        ArgumentNullException.ThrowIfNull(promptDrafts);
        ArgumentNullException.ThrowIfNull(catalogOptions);
        ArgumentNullException.ThrowIfNull(getSelectedThreadId);
        ArgumentNullException.ThrowIfNull(onThreadPromptEditedStateChanged);

        _promptDrafts = promptDrafts;
        _promptDraftPersistence = new ThreadPromptDraftPersistenceCoordinator(catalogOptions);
        _getSelectedThreadId = getSelectedThreadId;
        _onThreadPromptEditedStateChanged = onThreadPromptEditedStateChanged;
        _viewModel = new PromptDraftViewModel(OnPromptTextChanged);
    }

    public Binding<string?> PromptTextBinding => _viewModel.Bind.PromptText;

    public string? PromptText
    {
        get => _viewModel.PromptText;
        set => _viewModel.PromptText = value ?? string.Empty;
    }

    public void SyncPromptText(ThreadSessionState? session)
    {
        _selectedSession = session;

        var promptText = _promptDrafts.GetPrompt(session);
        if (!string.Equals(PromptText, promptText, StringComparison.Ordinal))
        {
            _syncingPromptText = true;
            try
            {
                PromptText = promptText;
            }
            finally
            {
                _syncingPromptText = false;
            }
        }
    }

    public void ClearPromptText()
        => PromptText = string.Empty;

    public void ClearDraftPromptText()
    {
        _promptDrafts.RememberPrompt(null, string.Empty);
        if (_selectedSession is null)
        {
            PromptText = string.Empty;
        }
    }

    public string? LoadPromptDraft(string threadId)
        => _promptDraftPersistence.LoadPromptDraft(threadId);

    public bool HasPersistedPromptDraft(string threadId)
        => _promptDraftPersistence.HasPromptDraft(threadId);

    public void DeletePersistedPromptDraft(string threadId)
        => _promptDraftPersistence.DeletePromptDraft(threadId);

    public ValueTask DisposeAsync()
        => _promptDraftPersistence.DisposeAsync();

    private void OnPromptTextChanged(string? value)
    {
        if (_syncingPromptText)
        {
            return;
        }

        var change = _promptDrafts.RememberPrompt(_selectedSession, value);
        if (_selectedSession is not null && _getSelectedThreadId() is { } selectedThreadId)
        {
            _promptDraftPersistence.ObservePromptDraft(selectedThreadId, _selectedSession.PromptDraftText);
            if (change.EditedStateChanged)
            {
                _onThreadPromptEditedStateChanged();
            }
        }
    }
}
