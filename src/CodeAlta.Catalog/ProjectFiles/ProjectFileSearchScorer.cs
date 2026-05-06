namespace CodeAlta.Catalog;

/// <summary>
/// Ranks lexical file and directory matches for project-file search.
/// </summary>
public sealed class ProjectFileSearchScorer
{
    private const double ExactRelativePathScore = 2000;
    private const double ExactBasenameScore = 1700;
    private const double BasenamePrefixScore = 1300;
    private const double RelativePathPrefixScore = 1100;
    private const double SegmentExactScore = 950;
    private const double SegmentPrefixScore = 820;
    private const double ExtensionExactScore = 700;
    private const double ExtensionPrefixScore = 520;
    private const double FileBiasScore = 25;
    private const double DirectoryPreferenceScore = 150;
    private const double MaximumRecentBoost = 80;

    /// <summary>
    /// Produces ranked results for the specified query.
    /// </summary>
    /// <param name="query">Query text.</param>
    /// <param name="candidates">Candidate items.</param>
    /// <param name="limit">Maximum number of results to return.</param>
    /// <returns>The ranked results.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="candidates"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="limit"/> is not positive.</exception>
    public IReadOnlyList<ProjectFileSearchResult> Rank(
        string? query,
        IReadOnlyList<ProjectFileSearchItem> candidates,
        int limit)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be positive.");
        }

        var normalizedQuery = NormalizeQuery(query, out var prefersDirectories);
        if (normalizedQuery.Length == 0)
        {
            return candidates
                .Select(
                    candidate => new ProjectFileSearchResult(
                        candidate,
                        ScoreForEmptyQuery(candidate, prefersDirectories),
                        IsRecent: candidate.Usage is not null))
                .OrderByDescending(static result => result.Score)
                .ThenBy(static result => result.Item.Kind == ProjectFileSearchItemKind.File ? 0 : 1)
                .ThenBy(static result => result.Item.RelativePath.Length)
                .ThenBy(static result => result.Item.RelativePath, StringComparer.OrdinalIgnoreCase)
                .Take(limit)
                .ToArray();
        }

        var applyPrefilter = candidates.Count >= 2000;
        var results = new List<ProjectFileSearchResult>();
        foreach (var candidate in candidates)
        {
            if (applyPrefilter && !PassesPrefilter(normalizedQuery, candidate.SearchFields))
            {
                continue;
            }

            if (!TryScoreCandidate(normalizedQuery, prefersDirectories, candidate, out var score))
            {
                continue;
            }

            results.Add(new ProjectFileSearchResult(candidate, score, candidate.Usage is not null));
        }

        return results
            .OrderByDescending(static result => result.Score)
            .ThenBy(static result => result.Item.Kind == ProjectFileSearchItemKind.File ? 0 : 1)
            .ThenBy(static result => result.Item.RelativePath.Length)
            .ThenBy(static result => result.Item.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToArray();
    }

    private static bool TryScoreCandidate(
        string query,
        bool prefersDirectories,
        ProjectFileSearchItem candidate,
        out double score)
    {
        score = 0;
        var fields = candidate.SearchFields;
        var extensionQuery = query.TrimStart('.');
        var basenameScore = ScoreSubsequence(query, fields.Basename);
        var pathScore = ScoreSubsequence(query, fields.RelativePath);

        if (fields.RelativePath.Equals(query, StringComparison.Ordinal))
        {
            score += ExactRelativePathScore;
        }

        if (fields.Basename.Equals(query, StringComparison.Ordinal))
        {
            score += ExactBasenameScore;
        }

        if (fields.Basename.StartsWith(query, StringComparison.Ordinal))
        {
            score += BasenamePrefixScore;
        }

        if (fields.RelativePath.StartsWith(query, StringComparison.Ordinal))
        {
            score += RelativePathPrefixScore;
        }

        if (fields.PathSegments.Any(segment => segment.Equals(query, StringComparison.Ordinal)))
        {
            score += SegmentExactScore;
        }

        if (fields.PathSegments.Any(segment => segment.StartsWith(query, StringComparison.Ordinal)))
        {
            score += SegmentPrefixScore;
        }

        if (extensionQuery.Length > 0 &&
            fields.Extension.TrimStart('.').Equals(extensionQuery, StringComparison.Ordinal))
        {
            score += ExtensionExactScore;
        }

        if (extensionQuery.Length > 0 &&
            fields.Extension.TrimStart('.').StartsWith(extensionQuery, StringComparison.Ordinal))
        {
            score += ExtensionPrefixScore;
        }

        if (basenameScore < 0 && pathScore < 0 && score <= 0)
        {
            return false;
        }

        if (basenameScore >= 0)
        {
            score += 280 + (basenameScore * 3.5);
        }

        if (pathScore >= 0)
        {
            score += 140 + (pathScore * 2.2);
        }

        if (candidate.Kind == ProjectFileSearchItemKind.File)
        {
            score += FileBiasScore;
        }
        else if (prefersDirectories)
        {
            score += DirectoryPreferenceScore;
        }

        score += ComputeRecentBoost(candidate.Usage);
        score -= candidate.RelativePath.Length * 0.45;
        score -= candidate.SearchFields.PathSegments.Count * 3.0;
        return true;
    }

    private static double ScoreForEmptyQuery(ProjectFileSearchItem candidate, bool prefersDirectories)
    {
        var score = ComputeRecentBoost(candidate.Usage);
        if (candidate.Kind == ProjectFileSearchItemKind.File)
        {
            score += FileBiasScore;
        }
        else if (prefersDirectories)
        {
            score += DirectoryPreferenceScore;
        }

        score -= candidate.RelativePath.Length * 0.2;
        return score;
    }

    private static double ComputeRecentBoost(ProjectFileUsageEntry? usage)
    {
        if (usage is null)
        {
            return 0;
        }

        var hours = Math.Max(0, (DateTimeOffset.UtcNow - usage.LastAccessedAt).TotalHours);
        var accessComponent = Math.Min(40, Math.Log2(usage.AccessCount + 1) * 12);
        var recencyComponent = Math.Max(0, 50 - (hours * 0.75));
        return Math.Min(MaximumRecentBoost, accessComponent + recencyComponent);
    }

    private static bool PassesPrefilter(string query, ProjectFileSearchFields fields)
    {
        if (fields.Basename.Contains(query, StringComparison.Ordinal) ||
            fields.RelativePath.Contains(query, StringComparison.Ordinal) ||
            fields.Extension.TrimStart('.').Contains(query.TrimStart('.'), StringComparison.Ordinal))
        {
            return true;
        }

        return ContainsDistinctQueryCharacters(query, fields.Basename) ||
               ContainsDistinctQueryCharacters(query, fields.RelativePath);
    }

    private static bool ContainsDistinctQueryCharacters(string query, string candidate)
    {
        foreach (var ch in query.Distinct())
        {
            if (candidate.IndexOf(ch) < 0)
            {
                return false;
            }
        }

        return true;
    }

    private static double ScoreSubsequence(string query, string candidate)
    {
        var candidateIndex = 0;
        var previousMatch = -1;
        var contiguousRuns = 0;
        var matched = 0;
        double penalty = 0;

        for (var queryIndex = 0; queryIndex < query.Length; queryIndex++)
        {
            var nextIndex = candidate.IndexOf(query[queryIndex], candidateIndex);
            if (nextIndex < 0)
            {
                return -1;
            }

            matched++;
            if (previousMatch >= 0)
            {
                var gap = nextIndex - previousMatch - 1;
                if (gap == 0)
                {
                    contiguousRuns++;
                }
                else
                {
                    penalty += gap * 1.5;
                }
            }

            candidateIndex = nextIndex + 1;
            previousMatch = nextIndex;
        }

        return (matched * 20) + (contiguousRuns * 12) - penalty;
    }

    private static string NormalizeQuery(string? query, out bool prefersDirectories)
    {
        var raw = (query ?? string.Empty).Trim().Replace('\\', '/');
        prefersDirectories = raw.EndsWith("/", StringComparison.Ordinal);
        raw = raw.TrimStart('@');
        raw = raw.TrimEnd('/');
        return raw.ToLowerInvariant();
    }
}
