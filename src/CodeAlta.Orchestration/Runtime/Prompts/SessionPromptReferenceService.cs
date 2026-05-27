using System.Text;
using CodeAlta.Agent;
using CodeAlta.Catalog;

namespace CodeAlta.Orchestration.Runtime.Prompts;

/// <summary>
/// Resolves project-file references in session-view prompts and records reference usage.
/// </summary>
public interface ISessionPromptReferenceService
{
    /// <summary>
    /// Resolves prompt references into normalized prompt text and backend attachments.
    /// </summary>
    /// <param name="prompt">The prompt text.</param>
    /// <param name="projectRoot">The project root used to resolve references.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resolved prompt input.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="prompt"/> is <see langword="null"/>.</exception>
    ValueTask<SessionViewPromptReferenceResult> ResolveAsync(
        string prompt,
        string? projectRoot,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records usage for resolved prompt references.
    /// </summary>
    /// <param name="resolutions">Resolved prompt references.</param>
    /// <param name="accessedAt">The usage timestamp.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when usage recording has been attempted.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resolutions"/> is <see langword="null"/>.</exception>
    ValueTask RecordUsageAsync(
        IReadOnlyList<ProjectFileResolution> resolutions,
        DateTimeOffset accessedAt,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Project-file-backed implementation of <see cref="ISessionPromptReferenceService"/>.
/// </summary>
public sealed class SessionPromptReferenceService : ISessionPromptReferenceService
{
    private readonly IProjectFileSearchService _searchService;

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionPromptReferenceService"/> class.
    /// </summary>
    /// <param name="searchService">The project-file search service.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="searchService"/> is <see langword="null"/>.</exception>
    public SessionPromptReferenceService(IProjectFileSearchService searchService)
    {
        ArgumentNullException.ThrowIfNull(searchService);
        _searchService = searchService;
    }

    /// <inheritdoc />
    public async ValueTask<SessionViewPromptReferenceResult> ResolveAsync(
        string prompt,
        string? projectRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        var tokens = ProjectFilePromptReferenceParser.Parse(prompt);
        if (tokens.Count == 0)
        {
            return new SessionViewPromptReferenceResult(prompt, AgentInput.Text(prompt), []);
        }

        var builder = new StringBuilder(prompt.Length);
        var attachments = new List<AgentInputItem>();
        var resolvedReferences = new List<ProjectFileResolution>();
        var cursor = 0;
        foreach (var token in tokens)
        {
            builder.Append(prompt, cursor, token.StartIndex - cursor);

            if (token.Kind == ProjectFilePromptTokenKind.EscapedAt)
            {
                builder.Append('@');
                cursor = token.StartIndex + token.Length;
                continue;
            }

            if (string.IsNullOrWhiteSpace(projectRoot) ||
                token.IsMalformed ||
                string.IsNullOrWhiteSpace(token.LookupText))
            {
                builder.Append(token.RawText);
                cursor = token.StartIndex + token.Length;
                continue;
            }

            ProjectFileResolution resolution;
            try
            {
                resolution = await _searchService.ResolveAsync(
                        new ProjectFileResolveQuery(projectRoot, token.LookupText!, token.LineRange),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ArgumentException)
            {
                builder.Append(token.RawText);
                cursor = token.StartIndex + token.Length;
                continue;
            }

            if (!resolution.IsResolved || resolution.Item is null)
            {
                builder.Append(token.RawText);
                cursor = token.StartIndex + token.Length;
                continue;
            }

            builder.Append(ProjectFilePromptReferenceFormatter.BuildMarkdownLink(resolution, token.DisplayText));
            attachments.Add(CreateAttachment(resolution));
            resolvedReferences.Add(resolution);
            cursor = token.StartIndex + token.Length;
        }

        builder.Append(prompt, cursor, prompt.Length - cursor);
        var normalizedPrompt = builder.ToString();
        if (attachments.Count == 0)
        {
            return new SessionViewPromptReferenceResult(normalizedPrompt, AgentInput.Text(normalizedPrompt), []);
        }

        var items = new List<AgentInputItem>(attachments.Count + 1)
        {
            new AgentInputItem.Text(normalizedPrompt),
        };
        items.AddRange(attachments);
        return new SessionViewPromptReferenceResult(normalizedPrompt, new AgentInput(items), resolvedReferences);
    }

    /// <inheritdoc />
    public async ValueTask RecordUsageAsync(
        IReadOnlyList<ProjectFileResolution> resolutions,
        DateTimeOffset accessedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolutions);

        foreach (var resolution in resolutions)
        {
            var item = resolution.Item;
            if (item is null)
            {
                continue;
            }

            await _searchService.RecordUsageAsync(
                    new ProjectFileUsageEvent(
                        item.ProjectRoot,
                        item.RelativePath,
                        item.Kind,
                        accessedAt,
                        ProjectFileUsageAccessKind.PromptInserted),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static AgentInputItem CreateAttachment(ProjectFileResolution resolution)
    {
        var item = resolution.Item!;
        var lineRange = item.Kind == ProjectFileSearchItemKind.File && resolution.LineRange is not null
            ? new AgentLineRange(resolution.LineRange.StartLine, resolution.LineRange.EndLine)
            : null;

        return item.Kind switch
        {
            ProjectFileSearchItemKind.File => new AgentInputItem.File(item.FullPath, item.RelativePath, lineRange),
            ProjectFileSearchItemKind.Directory => new AgentInputItem.Directory(item.FullPath, item.RelativePath),
            _ => throw new ArgumentOutOfRangeException(nameof(item.Kind)),
        };
    }
}

/// <summary>
/// Describes prompt input after project-file references have been resolved.
/// </summary>
/// <param name="NormalizedPromptText">Prompt text with resolved references normalized to markdown links.</param>
/// <param name="Input">Agent input containing the normalized prompt and resolved file/directory attachments.</param>
/// <param name="ResolvedReferences">Resolved project-file references.</param>
public sealed record SessionViewPromptReferenceResult(
    string NormalizedPromptText,
    AgentInput Input,
    IReadOnlyList<ProjectFileResolution> ResolvedReferences);
