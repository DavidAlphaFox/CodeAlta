using CodeAlta.Agent;
using CodeAlta.App.Context;
using CodeAlta.App.State;
using CodeAlta.Catalog;
using CodeAlta.Models;
using CodeAlta.Orchestration.Runtime;
using CodeAlta.Presentation.Chat;
using CodeAlta.Threading;

namespace CodeAlta.App;

internal sealed class ThreadExecutionOptionsFactory
{
    private readonly CatalogOptions _catalogOptions;
    private readonly IReadOnlyList<AgentBackendDescriptor> _backendDescriptors;
    private readonly Dictionary<string, ChatBackendState> _chatBackendStates;
    private readonly ThreadSelectionContext _threadSelection;
    private readonly ChatSelectorStateContext _selectorState;
    private readonly ThreadPermissionRequestCoordinator _permissionRequests;
    private readonly ThreadUserInputRequestCoordinator _userInputRequests;

    public ThreadExecutionOptionsFactory(
        CatalogOptions catalogOptions,
        IReadOnlyList<AgentBackendDescriptor> backendDescriptors,
        Dictionary<string, ChatBackendState> chatBackendStates,
        ThreadSelectionContext threadSelection,
        ChatSelectorStateContext selectorState,
        ThreadPermissionRequestCoordinator permissionRequests,
        ThreadUserInputRequestCoordinator userInputRequests)
    {
        ArgumentNullException.ThrowIfNull(catalogOptions);
        ArgumentNullException.ThrowIfNull(backendDescriptors);
        ArgumentNullException.ThrowIfNull(chatBackendStates);
        ArgumentNullException.ThrowIfNull(threadSelection);
        ArgumentNullException.ThrowIfNull(selectorState);
        ArgumentNullException.ThrowIfNull(permissionRequests);
        ArgumentNullException.ThrowIfNull(userInputRequests);

        _catalogOptions = catalogOptions;
        _backendDescriptors = backendDescriptors;
        _chatBackendStates = chatBackendStates;
        _threadSelection = threadSelection;
        _selectorState = selectorState;
        _permissionRequests = permissionRequests;
        _userInputRequests = userInputRequests;
    }

    public WorkThreadExecutionOptions BuildPreferredExecutionOptions(
        AgentBackendId backendId,
        string workingDirectory,
        IReadOnlyList<string> projectRoots)
    {
        ArgumentNullException.ThrowIfNull(projectRoots);

        var backendState = _chatBackendStates[backendId.Value];
        var model = UiDispatch.Invoke(
            _selectorState.GetUiDispatcher(),
            () =>
            {
                if (_selectorState.GetSelectedBackendIndex() is not { } backendIndex || _selectorState.GetSelectedModelIndex() is not { } modelIndex)
                {
                    return backendState.SelectedModelId;
                }

                var backendOptions = ChatBackendPresentation.BuildBackendOptions(_backendDescriptors);
                if ((uint)backendIndex < (uint)backendOptions.Count &&
                    string.Equals(backendOptions[backendIndex].BackendId.Value, backendId.Value, StringComparison.OrdinalIgnoreCase))
                {
                    var modelOptions = ChatBackendPresentation.BuildModelOptions(backendState);
                    if ((uint)modelIndex < (uint)modelOptions.Count)
                    {
                        return modelOptions[modelIndex].ModelId;
                    }
                }

                return backendState.SelectedModelId;
            });

        var reasoning = UiDispatch.Invoke(
            _selectorState.GetUiDispatcher(),
            () =>
            {
                if (_selectorState.GetSelectedBackendIndex() is not { } backendIndex || _selectorState.GetSelectedReasoningIndex() is not { } reasoningIndex)
                {
                    return backendState.SelectedReasoningEffort;
                }

                var backendOptions = ChatBackendPresentation.BuildBackendOptions(_backendDescriptors);
                if ((uint)backendIndex < (uint)backendOptions.Count &&
                    string.Equals(backendOptions[backendIndex].BackendId.Value, backendId.Value, StringComparison.OrdinalIgnoreCase))
                {
                    var selectedModel = backendState.Models.FirstOrDefault(candidate => string.Equals(candidate.Id, model, StringComparison.Ordinal));
                    var reasoningOptions = ChatBackendPresentation.BuildReasoningOptions(selectedModel);
                    if ((uint)reasoningIndex < (uint)reasoningOptions.Count)
                    {
                        return reasoningOptions[reasoningIndex].Effort;
                    }
                }

                return backendState.SelectedReasoningEffort;
            });

        return new WorkThreadExecutionOptions
        {
            BackendId = backendId,
            WorkingDirectory = workingDirectory,
            ProjectRoots = projectRoots,
            Model = model,
            ReasoningEffort = reasoning,
            OnPermissionRequest = static (_, _) => Task.FromResult(new AgentPermissionDecision(AgentPermissionDecisionKind.AllowOnce)),
            OnUserInputRequest = (request, cancellationToken) => _userInputRequests.HandleAsync(CreateTransientThreadKey(backendId, workingDirectory), request, cancellationToken),
        };
    }

    public WorkThreadExecutionOptions BuildExecutionOptions(WorkThreadDescriptor thread, OpenThreadState tab)
    {
        ArgumentNullException.ThrowIfNull(thread);
        ArgumentNullException.ThrowIfNull(tab);

        var workingDirectory = ResolveWorkingDirectory(thread);
        var projectRoots = ResolveProjectRoots(thread);
        return new WorkThreadExecutionOptions
        {
            BackendId = new AgentBackendId(thread.BackendId),
            WorkingDirectory = workingDirectory,
            ProjectRoots = projectRoots,
            Model = tab.ModelId,
            ReasoningEffort = tab.ReasoningEffort,
            OnPermissionRequest = (request, cancellationToken) => _permissionRequests.HandleAsync(thread.ThreadId, request, cancellationToken),
            OnUserInputRequest = (request, cancellationToken) => _userInputRequests.HandleAsync(thread.ThreadId, request, cancellationToken),
        };
    }

    public WorkThreadExecutionOptions BuildDelegationExecutionOptions(
        string threadId,
        OpenThreadState tab,
        string workingDirectory,
        IReadOnlyList<string> projectRoots)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(projectRoots);

        return new WorkThreadExecutionOptions
        {
            BackendId = tab.BackendId,
            WorkingDirectory = workingDirectory,
            ProjectRoots = projectRoots,
            Model = tab.ModelId,
            ReasoningEffort = tab.ReasoningEffort,
            OnPermissionRequest = (request, cancellationToken) => _permissionRequests.HandleAsync(threadId, request, cancellationToken),
            OnUserInputRequest = (request, cancellationToken) => _userInputRequests.HandleAsync(threadId, request, cancellationToken),
        };
    }

    public static string CreateTransientThreadKey(AgentBackendId backendId, string workingDirectory)
        => $"{backendId.Value}:{workingDirectory}";

    private string ResolveWorkingDirectory(WorkThreadDescriptor thread)
    {
        return thread.Kind switch
        {
            WorkThreadKind.GlobalThread => _catalogOptions.GlobalRoot,
            WorkThreadKind.ProjectThread or WorkThreadKind.InternalThread when _threadSelection.GetProjectById(thread.ProjectRef) is { } project => project.ProjectPath,
            _ => thread.WorkingDirectory,
        };
    }

    private IReadOnlyList<string> ResolveProjectRoots(WorkThreadDescriptor thread)
    {
        if (_threadSelection.GetProjectById(thread.ProjectRef) is { } project)
        {
            return [project.ProjectPath];
        }

        return [];
    }
}
