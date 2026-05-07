namespace CodeAlta.App.State;

/// <summary>
/// UI-thread-owned immutable state store for shell frontend projections.
/// </summary>
internal class ShellStateStore
{
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private ShellFrontendStateSnapshot _snapshot = ShellFrontendStateSnapshot.Empty;

    /// <summary>Gets the current immutable snapshot.</summary>
    public ShellFrontendStateSnapshot Snapshot
    {
        get
        {
            VerifyOwnerThread();
            return _snapshot;
        }
    }

    /// <summary>
    /// Applies a mutation and publishes the resulting immutable snapshot.
    /// </summary>
    /// <param name="mutation">Mutation function.</param>
    /// <returns>The updated snapshot.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="mutation"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the store is accessed from a non-owner thread.</exception>
    public ShellFrontendStateSnapshot Mutate(Func<ShellFrontendStateSnapshot, ShellFrontendStateSnapshot> mutation)
    {
        VerifyOwnerThread();
        ArgumentNullException.ThrowIfNull(mutation);

        _snapshot = mutation(_snapshot) ?? throw new InvalidOperationException("Frontend state mutation returned null.");
        return _snapshot;
    }

    private void VerifyOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException("Shell frontend state must be accessed from its owning UI thread.");
        }
    }
}

/// <summary>
/// Compatibility name for the frontend shell state store while callers migrate to <see cref="ShellStateStore"/>.
/// </summary>
internal sealed class ShellFrontendStateStore : ShellStateStore;

/// <summary>
/// Immutable shell frontend state snapshot.
/// </summary>
/// <param name="Tabs">The projected shell tabs.</param>
/// <param name="ActiveTabId">The active tab identifier, when one is selected.</param>
/// <param name="StatusText">The shell status text, when available.</param>
internal sealed record ShellFrontendStateSnapshot(
    IReadOnlyList<ShellFrontendTabSnapshot> Tabs,
    string? ActiveTabId,
    string? StatusText)
{
    /// <summary>Gets an empty shell frontend snapshot.</summary>
    public static ShellFrontendStateSnapshot Empty { get; } = new([], ActiveTabId: null, StatusText: null);

    /// <summary>Returns a snapshot with the supplied tab inserted or replaced.</summary>
    public ShellFrontendStateSnapshot UpsertTab(ShellFrontendTabSnapshot tab)
    {
        ArgumentNullException.ThrowIfNull(tab);

        var tabs = Tabs.ToList();
        var index = tabs.FindIndex(candidate => string.Equals(candidate.TabId, tab.TabId, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            tabs[index] = tab;
        }
        else
        {
            tabs.Add(tab);
        }

        return this with { Tabs = tabs, ActiveTabId = ActiveTabId ?? tab.TabId };
    }

    /// <summary>Returns a snapshot with the supplied tab removed.</summary>
    public ShellFrontendStateSnapshot RemoveTab(string tabId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tabId);

        var tabs = Tabs.Where(tab => !string.Equals(tab.TabId, tabId, StringComparison.OrdinalIgnoreCase)).ToList();
        var activeTabId = string.Equals(ActiveTabId, tabId, StringComparison.OrdinalIgnoreCase)
            ? tabs.FirstOrDefault()?.TabId
            : ActiveTabId;
        return this with { Tabs = tabs, ActiveTabId = activeTabId };
    }

    /// <summary>Returns a snapshot with the supplied tab selected.</summary>
    public ShellFrontendStateSnapshot SelectTab(string tabId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tabId);
        if (!Tabs.Any(tab => string.Equals(tab.TabId, tabId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Tab '{tabId}' is not present in the shell frontend state.");
        }

        return this with { ActiveTabId = tabId };
    }

    /// <summary>Returns a snapshot with updated status text.</summary>
    public ShellFrontendStateSnapshot SetStatus(string? statusText)
        => this with { StatusText = statusText };
}

/// <summary>
/// Immutable shell tab projection stored by <see cref="ShellFrontendStateStore"/>.
/// </summary>
/// <param name="TabId">The stable tab identifier.</param>
/// <param name="Title">The tab title.</param>
/// <param name="Kind">The tab kind.</param>
/// <param name="Data">Optional stable view-model/projection data associated with the tab.</param>
internal sealed record ShellFrontendTabSnapshot(
    string TabId,
    string Title,
    string Kind,
    object? Data = null);
