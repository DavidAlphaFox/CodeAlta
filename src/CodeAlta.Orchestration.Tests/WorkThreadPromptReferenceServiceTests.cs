using CodeAlta.Agent;
using CodeAlta.Catalog;
using CodeAlta.Orchestration.Runtime.Prompts;

namespace CodeAlta.Orchestration.Tests;

[TestClass]
public sealed class WorkThreadPromptReferenceServiceTests
{
    [TestMethod]
    public async Task ResolveAsync_NormalizesReferencesAndCreatesAttachments()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"CodeAlta.Orchestration.ProjectFiles.{Guid.NewGuid():N}");
        var searchService = new FakeProjectFileSearchService(
            [
                CreateItem(projectRoot, "src/My File.cs"),
                CreateItem(projectRoot, "docs", ProjectFileSearchItemKind.Directory),
            ]);
        var service = new WorkThreadPromptReferenceService(searchService);

        var result = await service.ResolveAsync(
            "Review @@literal @\"src/My File.cs\":10-12 and @docs",
            projectRoot);

        Assert.AreEqual("Review @literal [My File.cs](src/My File.cs:10-12) and [docs](docs)", result.NormalizedPromptText);
        Assert.AreEqual(2, result.ResolvedReferences.Count);
        Assert.AreEqual(3, result.Input.Items.Count);
        Assert.AreEqual(result.NormalizedPromptText, ((AgentInputItem.Text)result.Input.Items[0]).Value);
        var file = (AgentInputItem.File)result.Input.Items[1];
        Assert.AreEqual(Path.Combine(projectRoot, "src", "My File.cs"), file.Path);
        Assert.AreEqual("src/My File.cs", file.DisplayName);
        Assert.AreEqual(new AgentLineRange(10, 12), file.LineRange);
        var directory = (AgentInputItem.Directory)result.Input.Items[2];
        Assert.AreEqual(Path.Combine(projectRoot, "docs"), directory.Path);
        Assert.AreEqual("docs", directory.DisplayName);
    }

    [TestMethod]
    public async Task ResolveAsync_LeavesUnresolvedReferencesUntouched()
    {
        var service = new WorkThreadPromptReferenceService(new FakeProjectFileSearchService([]));

        var result = await service.ResolveAsync("Review @missing and @@literal", projectRoot: "C:/project");

        Assert.AreEqual("Review @missing and @literal", result.NormalizedPromptText);
        Assert.AreEqual(0, result.ResolvedReferences.Count);
        Assert.AreEqual(1, result.Input.Items.Count);
        Assert.AreEqual("Review @missing and @literal", ((AgentInputItem.Text)result.Input.Items[0]).Value);
    }

    [TestMethod]
    public async Task RecordUsageAsync_RecordsResolvedReferences()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"CodeAlta.Orchestration.ProjectFiles.{Guid.NewGuid():N}");
        var item = CreateItem(projectRoot, "src/app.cs");
        var searchService = new FakeProjectFileSearchService([item]);
        var service = new WorkThreadPromptReferenceService(searchService);
        var result = await service.ResolveAsync("Use @src/app.cs", projectRoot);
        var accessedAt = DateTimeOffset.Parse("2026-05-05T12:00:00Z");

        await service.RecordUsageAsync(result.ResolvedReferences, accessedAt);

        Assert.AreEqual(1, searchService.UsageEvents.Count);
        Assert.AreEqual(projectRoot, searchService.UsageEvents[0].ProjectRoot);
        Assert.AreEqual("src/app.cs", searchService.UsageEvents[0].RelativePath);
        Assert.AreEqual(ProjectFileUsageAccessKind.PromptInserted, searchService.UsageEvents[0].AccessKind);
        Assert.AreEqual(accessedAt, searchService.UsageEvents[0].AccessedAt);
    }

    private static ProjectFileSearchItem CreateItem(
        string projectRoot,
        string relativePath,
        ProjectFileSearchItemKind kind = ProjectFileSearchItemKind.File)
    {
        var basename = Path.GetFileName(relativePath);
        var extension = kind == ProjectFileSearchItemKind.Directory ? string.Empty : Path.GetExtension(basename);
        return new ProjectFileSearchItem
        {
            Kind = kind,
            ProjectRoot = projectRoot,
            RelativePath = relativePath.Replace('\\', '/'),
            FullPath = Path.Combine(projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)),
            Basename = basename,
            ParentPath = Path.GetDirectoryName(relativePath)?.Replace('\\', '/') ?? string.Empty,
            Extension = extension,
            LastWriteTimeUtc = DateTimeOffset.UtcNow,
            SearchFields = new ProjectFileSearchFields(
                basename.ToLowerInvariant(),
                relativePath.ToLowerInvariant(),
                relativePath.Split('/').Select(static segment => segment.ToLowerInvariant()).ToArray(),
                extension.ToLowerInvariant()),
            Usage = null,
        };
    }

    private sealed class FakeProjectFileSearchService(IReadOnlyList<ProjectFileSearchItem> items) : IProjectFileSearchService
    {
        public List<ProjectFileUsageEvent> UsageEvents { get; } = [];

        public ValueTask<IProjectFileSearchSession> CreateSessionAsync(
            ProjectFileSearchSessionOptions options,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<ProjectFileResolution> ResolveAsync(
            ProjectFileResolveQuery query,
            CancellationToken cancellationToken = default)
        {
            var normalized = query.ReferenceText.Replace('\\', '/').Trim().Trim('"');
            var item = items.FirstOrDefault(candidate => string.Equals(candidate.RelativePath, normalized, StringComparison.OrdinalIgnoreCase));
            return ValueTask.FromResult(
                item is null
                    ? new ProjectFileResolution(false, normalized, Item: null, query.LineRange)
                    : new ProjectFileResolution(true, normalized, item, query.LineRange));
        }

        public ValueTask RecordUsageAsync(ProjectFileUsageEvent usageEvent, CancellationToken cancellationToken = default)
        {
            UsageEvents.Add(usageEvent);
            return ValueTask.CompletedTask;
        }

        public ValueTask InvalidateAsync(string projectRoot, ProjectFileInvalidationReason reason, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }
}
