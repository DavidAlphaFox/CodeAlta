namespace CodeAlta.Plugin.Mcp;

internal sealed class McpActivationState
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, McpActivationScopeState> _scopes = new(StringComparer.Ordinal);

    public IReadOnlyList<string> GetActiveServers(string scopeKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeKey);
        lock (_syncRoot)
        {
            return _scopes.TryGetValue(scopeKey, out var state)
                ? state.ActiveServers.OrderBy(static server => server, StringComparer.Ordinal).ToArray()
                : [];
        }
    }

    public IReadOnlyDictionary<string, int> GetToolCounts(string scopeKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeKey);
        lock (_syncRoot)
        {
            return _scopes.TryGetValue(scopeKey, out var state)
                ? new Dictionary<string, int>(state.ToolCounts, StringComparer.Ordinal)
                : new Dictionary<string, int>(StringComparer.Ordinal);
        }
    }

    public IReadOnlyList<string> ActivateServers(string scopeKey, IEnumerable<string> serverKeys)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeKey);
        ArgumentNullException.ThrowIfNull(serverKeys);
        lock (_syncRoot)
        {
            var state = GetOrCreateScope(scopeKey);
            foreach (var serverKey in serverKeys)
            {
                if (!string.IsNullOrWhiteSpace(serverKey))
                {
                    state.ActiveServers.Add(serverKey.Trim());
                }
            }

            return state.ActiveServers.OrderBy(static server => server, StringComparer.Ordinal).ToArray();
        }
    }

    public void ReplaceActiveServers(string scopeKey, IEnumerable<string> serverKeys)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeKey);
        ArgumentNullException.ThrowIfNull(serverKeys);
        lock (_syncRoot)
        {
            var state = GetOrCreateScope(scopeKey);
            state.ActiveServers.Clear();
            foreach (var serverKey in serverKeys)
            {
                if (!string.IsNullOrWhiteSpace(serverKey))
                {
                    state.ActiveServers.Add(serverKey.Trim());
                }
            }

            foreach (var serverKey in state.ToolCounts.Keys.Except(state.ActiveServers, StringComparer.Ordinal).ToArray())
            {
                state.ToolCounts.Remove(serverKey);
            }
        }
    }

    public void UpdateToolCounts(string scopeKey, IReadOnlyDictionary<string, int> counts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeKey);
        ArgumentNullException.ThrowIfNull(counts);
        lock (_syncRoot)
        {
            var state = GetOrCreateScope(scopeKey);
            foreach (var serverKey in state.ActiveServers)
            {
                state.ToolCounts[serverKey] = counts.TryGetValue(serverKey, out var count) ? count : 0;
            }
        }
    }

    public static string ResolveScopeKey(string? sessionId, string? projectPath)
    {
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            return "session:" + sessionId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(projectPath))
        {
            return "project:" + Path.GetFullPath(projectPath.Trim());
        }

        return "global";
    }

    private McpActivationScopeState GetOrCreateScope(string scopeKey)
    {
        if (!_scopes.TryGetValue(scopeKey, out var state))
        {
            state = new McpActivationScopeState();
            _scopes.Add(scopeKey, state);
        }

        return state;
    }

    private sealed class McpActivationScopeState
    {
        public HashSet<string> ActiveServers { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, int> ToolCounts { get; } = new(StringComparer.Ordinal);
    }
}
