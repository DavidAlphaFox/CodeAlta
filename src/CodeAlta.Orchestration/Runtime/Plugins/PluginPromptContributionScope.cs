using CodeAlta.Plugins.Abstractions;

namespace CodeAlta.Orchestration.Runtime.Plugins;

/// <summary>
/// Tracks temporary plugin prompt contributions by orchestration thread/run scope.
/// </summary>
public sealed class PluginPromptContributionScope
{
    private readonly object _gate = new();
    private readonly Dictionary<PluginPromptContributionScopeKey, List<PluginSystemPromptContribution>> _pending = new();

    /// <summary>Adds temporary prompt contributions for a scoped prompt/run.</summary>
    public void Add(PluginPromptContributionScopeKey key, IReadOnlyList<PluginSystemPromptContribution> contributions)
    {
        ArgumentNullException.ThrowIfNull(contributions);
        key.Validate();
        if (contributions.Count == 0)
        {
            return;
        }

        lock (_gate)
        {
            if (!_pending.TryGetValue(key, out var list))
            {
                list = [];
                _pending[key] = list;
            }

            list.AddRange(contributions);
        }
    }

    /// <summary>Takes and removes temporary prompt contributions for a scoped prompt/run.</summary>
    public IReadOnlyList<PluginSystemPromptContribution> Take(PluginPromptContributionScopeKey key)
    {
        key.Validate();
        lock (_gate)
        {
            if (!_pending.Remove(key, out var list))
            {
                return [];
            }

            return list.ToArray();
        }
    }
}

/// <summary>Identifies temporary plugin prompt contributions for a thread and optional run.</summary>
public readonly record struct PluginPromptContributionScopeKey(string ThreadId, string? RunId = null)
{
    /// <summary>Validates that the scoped key can safely index pending prompt contributions.</summary>
    /// <exception cref="ArgumentException">Thrown when the thread id is empty.</exception>
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ThreadId);
    }
}
