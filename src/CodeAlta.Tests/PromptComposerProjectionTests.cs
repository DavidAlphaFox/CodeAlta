using CodeAlta.Agent;
using CodeAlta.Catalog;
using CodeAlta.Models;
using CodeAlta.Presentation.Prompting;

namespace CodeAlta.Tests;

[TestClass]
public sealed class PromptComposerProjectionTests
{
    [TestMethod]
    public void Build_UsesUnavailableStateForConnectingThread()
    {
        var thread = CreateThread("Review startup");

        var projection = PromptComposerProjectionBuilder.Build(
            thread,
            selectedProject: null,
            globalScopeSelected: false,
            providerDisplayName: "Codex",
            availability: ModelProviderAvailability.Probing,
            anyBackendReady: false,
            draftTabOpen: false,
            openTabCount: 1,
            selectedThreadId: thread.ThreadId,
            selectedThreadHasQueuedPrompts: false,
            selectedThreadCanAlwaysEnqueue: true,
            selectedThreadCanCompact: false,
            selectedThreadCanAbort: true);

        Assert.AreEqual("Waiting for Codex to reconnect...", projection.Placeholder);
        Assert.IsFalse(projection.IsEnabled);
        Assert.IsFalse(projection.CanSend);
        Assert.IsFalse(projection.CanSteer);
        Assert.IsTrue(projection.CanAbort);
        Assert.IsFalse(projection.CanCompact);
        Assert.IsFalse(projection.CanClearQueue);
        Assert.IsTrue(projection.CanAlwaysEnqueue);
        Assert.IsTrue(projection.HasUnavailableStatus);
        Assert.AreEqual("Reconnecting session 'Review startup' to Codex. Prompt sending is temporarily unavailable.", projection.UnavailableStatusMessage);
        Assert.AreEqual(StatusTone.Info, projection.UnavailableStatusTone);
    }

    [TestMethod]
    public void Build_UsesReadyDraftStateForConnectedProjectScope()
    {
        var project = new ProjectDescriptor
        {
            Id = "project-1",
            DisplayName = "CodeAlta",
            ProjectPath = @"C:\code\CodeAlta",
            Slug = "codealta",
        };

        var projection = PromptComposerProjectionBuilder.Build(
            selectedThread: null,
            selectedProject: project,
            globalScopeSelected: false,
            providerDisplayName: "Codex",
            availability: ModelProviderAvailability.Ready,
            anyBackendReady: true,
            draftTabOpen: true,
            openTabCount: 1,
            selectedThreadId: null,
            selectedThreadHasQueuedPrompts: false,
            selectedThreadCanAlwaysEnqueue: false,
            selectedThreadCanCompact: false,
            selectedThreadCanAbort: false);

        Assert.AreEqual(
            "Start a session. [/] commands, [?] help, [@] to reference a project file, [ENTER] to send, [SHIFT+ENTER] for new line, [CTRL+ENTER] to steer.",
            projection.Placeholder);
        Assert.IsTrue(projection.IsEnabled);
        Assert.IsTrue(projection.CanSend);
        Assert.IsFalse(projection.CanSteer);
        Assert.IsFalse(projection.CanAbort);
        Assert.IsFalse(projection.CanCompact);
        Assert.IsFalse(projection.CanCloseTab);
        Assert.IsFalse(projection.CanClearQueue);
        Assert.IsFalse(projection.CanAlwaysEnqueue);
        Assert.IsFalse(projection.HasUnavailableStatus);
    }

    [TestMethod]
    public void Build_IncludesPluginPlaceholderContributionsInReadyPrompts()
    {
        var project = new ProjectDescriptor
        {
            Id = "project-1",
            DisplayName = "CodeAlta",
            ProjectPath = @"C:\code\CodeAlta",
            Slug = "codealta",
        };
        var placeholderContributions = new[] { "[#] to reference a GitHub issue" };

        var draftProjection = PromptComposerProjectionBuilder.Build(
            selectedThread: null,
            selectedProject: project,
            globalScopeSelected: false,
            providerDisplayName: "Codex",
            availability: ModelProviderAvailability.Ready,
            anyBackendReady: true,
            draftTabOpen: true,
            openTabCount: 1,
            selectedThreadId: null,
            selectedThreadHasQueuedPrompts: false,
            selectedThreadCanAlwaysEnqueue: false,
            selectedThreadCanCompact: false,
            selectedThreadCanAbort: false,
            promptPlaceholderContributions: placeholderContributions);
        var threadProjection = PromptComposerProjectionBuilder.Build(
            CreateThread("Review startup"),
            selectedProject: null,
            globalScopeSelected: false,
            providerDisplayName: "Codex",
            availability: ModelProviderAvailability.Ready,
            anyBackendReady: true,
            draftTabOpen: false,
            openTabCount: 1,
            selectedThreadId: "thread-1",
            selectedThreadHasQueuedPrompts: false,
            selectedThreadCanAlwaysEnqueue: true,
            selectedThreadCanCompact: true,
            selectedThreadCanAbort: false,
            promptPlaceholderContributions: placeholderContributions);

        Assert.AreEqual(
            "Start a session. [/] commands, [?] help, [@] to reference a project file, [#] to reference a GitHub issue, [ENTER] to send, [SHIFT+ENTER] for new line, [CTRL+ENTER] to steer.",
            draftProjection.Placeholder);
        Assert.AreEqual(
            "Continue the selected session. [/] commands, [?] help, [@] to reference a project file, [#] to reference a GitHub issue, [ENTER] to send, [SHIFT+ENTER] for new line, [CTRL+ENTER] to steer.",
            threadProjection.Placeholder);
    }

    [TestMethod]
    public void Build_UsesMissingProviderMessagingWhenNoProviderIsReady()
    {
        var projection = PromptComposerProjectionBuilder.Build(
            selectedThread: null,
            selectedProject: null,
            globalScopeSelected: true,
            providerDisplayName: "Codex",
            availability: ModelProviderAvailability.Unsupported,
            anyBackendReady: false,
            draftTabOpen: true,
            openTabCount: 1,
            selectedThreadId: null,
            selectedThreadHasQueuedPrompts: false,
            selectedThreadCanAlwaysEnqueue: false,
            selectedThreadCanCompact: false,
            selectedThreadCanAbort: false);

        Assert.AreEqual("Configure model providers (Ctrl+G Ctrl+R) to start a session...", projection.Placeholder);
        Assert.IsTrue(projection.HasUnavailableStatus);
        Assert.AreEqual("No model provider is ready. Open Model Providers (Ctrl+G Ctrl+R) to configure one.", projection.UnavailableStatusMessage);
        Assert.AreEqual(StatusTone.Warning, projection.UnavailableStatusTone);
    }

    [TestMethod]
    public void Build_EnablesClearQueueForQueuedSelectedThread()
    {
        var thread = CreateThread("Review startup");

        var projection = PromptComposerProjectionBuilder.Build(
            thread,
            selectedProject: null,
            globalScopeSelected: false,
            providerDisplayName: "Codex",
            availability: ModelProviderAvailability.Ready,
            anyBackendReady: true,
            draftTabOpen: false,
            openTabCount: 1,
            selectedThreadId: thread.ThreadId,
            selectedThreadHasQueuedPrompts: true,
            selectedThreadCanAlwaysEnqueue: true,
            selectedThreadCanCompact: true,
            selectedThreadCanAbort: false);

        Assert.AreEqual(
            "Continue the selected session. [/] commands, [?] help, [@] to reference a project file, [ENTER] to send, [SHIFT+ENTER] for new line, [CTRL+ENTER] to steer.",
            projection.Placeholder);
        Assert.IsTrue(projection.CanSend);
        Assert.IsTrue(projection.CanSteer);
        Assert.IsFalse(projection.CanAbort);
        Assert.IsTrue(projection.CanCompact);
        Assert.IsTrue(projection.CanClearQueue);
        Assert.IsTrue(projection.CanAlwaysEnqueue);
    }

    [TestMethod]
    public void Build_DisablesCompactWhenSelectedThreadCannotCompact()
    {
        var thread = CreateThread("Review startup");

        var projection = PromptComposerProjectionBuilder.Build(
            thread,
            selectedProject: null,
            globalScopeSelected: false,
            providerDisplayName: "Codex",
            availability: ModelProviderAvailability.Ready,
            anyBackendReady: true,
            draftTabOpen: false,
            openTabCount: 1,
            selectedThreadId: thread.ThreadId,
            selectedThreadHasQueuedPrompts: false,
            selectedThreadCanAlwaysEnqueue: true,
            selectedThreadCanCompact: false,
            selectedThreadCanAbort: false);

        Assert.IsFalse(projection.CanCompact);
    }

    [TestMethod]
    public void Build_EnablesAbortOnlyWhenSelectedThreadIsRunning()
    {
        var thread = CreateThread("Review startup");

        var idleProjection = PromptComposerProjectionBuilder.Build(
            thread,
            selectedProject: null,
            globalScopeSelected: false,
            providerDisplayName: "Codex",
            availability: ModelProviderAvailability.Ready,
            anyBackendReady: true,
            draftTabOpen: false,
            openTabCount: 1,
            selectedThreadId: thread.ThreadId,
            selectedThreadHasQueuedPrompts: false,
            selectedThreadCanAlwaysEnqueue: true,
            selectedThreadCanCompact: true,
            selectedThreadCanAbort: false);
        var runningProjection = PromptComposerProjectionBuilder.Build(
            thread,
            selectedProject: null,
            globalScopeSelected: false,
            providerDisplayName: "Codex",
            availability: ModelProviderAvailability.Ready,
            anyBackendReady: true,
            draftTabOpen: false,
            openTabCount: 1,
            selectedThreadId: thread.ThreadId,
            selectedThreadHasQueuedPrompts: false,
            selectedThreadCanAlwaysEnqueue: true,
            selectedThreadCanCompact: false,
            selectedThreadCanAbort: true);

        Assert.IsFalse(idleProjection.CanAbort);
        Assert.IsTrue(runningProjection.CanAbort);
    }

    [TestMethod]
    public void Build_EnablesDraftCloseWhenAnotherTabIsOpen()
    {
        var projection = PromptComposerProjectionBuilder.Build(
            selectedThread: null,
            selectedProject: null,
            globalScopeSelected: true,
            providerDisplayName: "Codex",
            availability: ModelProviderAvailability.Ready,
            anyBackendReady: true,
            draftTabOpen: true,
            openTabCount: 2,
            selectedThreadId: null,
            selectedThreadHasQueuedPrompts: false,
            selectedThreadCanAlwaysEnqueue: false,
            selectedThreadCanCompact: false,
            selectedThreadCanAbort: false);

        Assert.AreEqual(
            "Start a session. [/] commands, [?] help, [ENTER] to send, [SHIFT+ENTER] for new line, [CTRL+ENTER] to steer.",
            projection.Placeholder);
        Assert.IsTrue(projection.CanCloseTab);
    }

    private static SessionViewDescriptor CreateThread(string title)
    {
        return new SessionViewDescriptor
        {
            ThreadId = "thread-1",
            Kind = WorkThreadKind.ProjectThread,
            BackendId = AgentBackendIds.Codex.Value,
            ProjectRef = "project-1",
            WorkingDirectory = @"C:\code\CodeAlta",
            Title = title,
            Status = WorkThreadStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
        };
    }
}
