using CodeAlta.Agent;
using CodeAlta.Catalog;
using CodeAlta.Views;

namespace CodeAlta.Tests;

[TestClass]
public sealed class PromptReferenceProjectRootResolverTests
{
    [TestMethod]
    public void Resolve_GlobalDraftUsesGlobalRootWhenNoProjectIsSelected()
    {
        const string globalRoot = @"C:\Users\alex\.alta";

        var root = PromptReferenceProjectRootResolver.Resolve(
            selectedThread: null,
            getProjectById: static _ => null,
            getSelectedProject: static () => null,
            globalRoot);

        Assert.AreEqual(globalRoot, root);
    }

    [TestMethod]
    public void Resolve_GlobalThreadUsesThreadWorkingDirectoryInsteadOfSelectedProject()
    {
        const string globalRoot = @"C:\Users\alex\.alta";
        var project = new ProjectDescriptor
        {
            Id = "project-1",
            Slug = "azuredevops",
            Name = ".azuredevops",
            DisplayName = ".azuredevops",
            ProjectPath = @"C:\Users\alex\.azuredevops",
            DefaultBranch = "main",
        };
        var thread = new SessionViewDescriptor
        {
            ThreadId = "global-thread",
            Kind = WorkThreadKind.GlobalThread,
            ProviderId = ModelProviderIds.Codex.Value,
            WorkingDirectory = globalRoot,
            Title = "Global Thread",
            Status = WorkThreadStatus.Active,
            CreatedAt = DateTimeOffset.Parse("2026-03-29T12:00:00+00:00"),
            UpdatedAt = DateTimeOffset.Parse("2026-03-29T12:00:00+00:00"),
            LastActiveAt = DateTimeOffset.Parse("2026-03-29T12:00:00+00:00"),
        };

        var root = PromptReferenceProjectRootResolver.Resolve(
            thread,
            getProjectById: _ => project,
            getSelectedProject: () => project,
            globalRoot);

        Assert.AreEqual(globalRoot, root);
    }
}
