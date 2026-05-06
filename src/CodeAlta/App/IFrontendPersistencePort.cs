using CodeAlta.Catalog;
using CodeAlta.Models;

namespace CodeAlta.App;

internal interface IFrontendPersistencePort
{
    string? LoadPromptDraft(PromptSessionId promptSessionId);

    void DeletePromptDraft(PromptSessionId promptSessionId);

    Task PersistViewStateAsync(CancellationToken cancellationToken = default);

    Task RegisterCreatedThreadAsync(WorkThreadDescriptor thread, CancellationToken cancellationToken = default);
}

internal sealed class FrontendPersistencePort : IFrontendPersistencePort
{
    private readonly Func<string, string?> _loadPromptDraft;
    private readonly Action<string> _deletePromptDraft;
    private readonly Func<CancellationToken, Task> _persistViewStateAsync;
    private readonly Func<WorkThreadDescriptor, CancellationToken, Task> _registerCreatedThreadAsync;

    public FrontendPersistencePort(
        Func<string, string?> loadPromptDraft,
        Action<string> deletePromptDraft,
        Func<CancellationToken, Task> persistViewStateAsync,
        Func<WorkThreadDescriptor, CancellationToken, Task> registerCreatedThreadAsync)
    {
        ArgumentNullException.ThrowIfNull(loadPromptDraft);
        ArgumentNullException.ThrowIfNull(deletePromptDraft);
        ArgumentNullException.ThrowIfNull(persistViewStateAsync);
        ArgumentNullException.ThrowIfNull(registerCreatedThreadAsync);

        _loadPromptDraft = loadPromptDraft;
        _deletePromptDraft = deletePromptDraft;
        _persistViewStateAsync = persistViewStateAsync;
        _registerCreatedThreadAsync = registerCreatedThreadAsync;
    }

    public string? LoadPromptDraft(PromptSessionId promptSessionId)
        => _loadPromptDraft(GetPromptSessionKey(promptSessionId));

    public void DeletePromptDraft(PromptSessionId promptSessionId)
        => _deletePromptDraft(GetPromptSessionKey(promptSessionId));

    public async Task PersistViewStateAsync(CancellationToken cancellationToken = default)
        => await _persistViewStateAsync(cancellationToken);

    public async Task RegisterCreatedThreadAsync(WorkThreadDescriptor thread, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(thread);
        await _registerCreatedThreadAsync(thread, cancellationToken);
    }

    private static string GetPromptSessionKey(PromptSessionId promptSessionId)
    {
        if (promptSessionId.IsEmpty)
        {
            throw new ArgumentException("Prompt session id cannot be empty.", nameof(promptSessionId));
        }

        return promptSessionId.Value;
    }
}
