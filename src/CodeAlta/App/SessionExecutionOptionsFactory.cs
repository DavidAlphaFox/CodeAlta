using CodeAlta.Agent;
using CodeAlta.App.Context;
using CodeAlta.App.State;
using CodeAlta.Catalog;
using CodeAlta.LiveTool;
using CodeAlta.Models;
using CodeAlta.Orchestration.Runtime;
using CodeAlta.Threading;

namespace CodeAlta.App;

internal sealed class SessionExecutionOptionsFactory
{
    private readonly CatalogOptions _catalogOptions;
    private readonly Dictionary<string, ModelProviderState> _modelProviderStates;
    private readonly SessionSelectionContext _sessionSelection;
    private readonly SessionPermissionRequestCoordinator _permissionRequests;
    private readonly SessionUserInputRequestCoordinator _userInputRequests;
    private readonly IServiceProvider? _altaServices;
    private readonly IReadOnlySet<string> _altaToolProviderIds;

    public SessionExecutionOptionsFactory(
        CatalogOptions catalogOptions,
        Dictionary<string, ModelProviderState> modelProviderStates,
        SessionSelectionContext sessionSelection,
        SessionPermissionRequestCoordinator permissionRequests,
        SessionUserInputRequestCoordinator userInputRequests,
        IServiceProvider? altaServices = null,
        IReadOnlySet<string>? altaToolProviderIds = null)
    {
        ArgumentNullException.ThrowIfNull(catalogOptions);
        ArgumentNullException.ThrowIfNull(modelProviderStates);
        ArgumentNullException.ThrowIfNull(sessionSelection);
        ArgumentNullException.ThrowIfNull(permissionRequests);
        ArgumentNullException.ThrowIfNull(userInputRequests);

        _catalogOptions = catalogOptions;
        _modelProviderStates = modelProviderStates;
        _sessionSelection = sessionSelection;
        _permissionRequests = permissionRequests;
        _userInputRequests = userInputRequests;
        _altaServices = altaServices;
        _altaToolProviderIds = altaToolProviderIds ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public SessionExecutionOptions BuildPreferredExecutionOptions(
        ModelProviderId providerId,
        string workingDirectory,
        IReadOnlyList<string> projectRoots,
        Func<string?>? sourceSessionIdProvider = null)
    {
        ArgumentNullException.ThrowIfNull(projectRoots);

        _modelProviderStates.TryGetValue(providerId.Value, out var backendState);
        var model = backendState?.SelectedModelId;
        var reasoning = backendState?.SelectedReasoningEffort;

        var sourceProjectId = projectRoots.Count == 0
            ? null
            : _sessionSelection.GetSelectedProjectId();
        return new SessionExecutionOptions
        {
            ProviderId = providerId,
            ProviderKey = providerId.Value,
            WorkingDirectory = workingDirectory,
            ProjectRoots = projectRoots,
            Model = model,
            ReasoningEffort = reasoning,
            Tools = CreateAltaTools(
                providerId,
                sourceSessionIdProvider: sourceSessionIdProvider,
                sourceProjectIdProvider: () => sourceProjectId,
                workingDirectoryProvider: () => workingDirectory),
            OnPermissionRequest = static (_, _) => Task.FromResult(new AgentPermissionDecision(AgentPermissionDecisionKind.AllowOnce)),
            OnUserInputRequest = (request, cancellationToken) => _userInputRequests.HandleAsync(CreateTransientSessionKey(providerId, workingDirectory), request, cancellationToken),
        };
    }

    public SessionExecutionOptions BuildExecutionOptions(SessionViewDescriptor session, OpenSessionState tab)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(tab);

        var workingDirectory = ResolveWorkingDirectory(session);
        var projectRoots = ResolveProjectRoots(session);
        var providerId = new ModelProviderId(session.ResolvedProviderKey);
        return new SessionExecutionOptions
        {
            ProviderId = providerId,
            ProviderKey = session.ResolvedProviderKey,
            WorkingDirectory = workingDirectory,
            ProjectRoots = projectRoots,
            Model = tab.ModelId,
            ReasoningEffort = tab.ReasoningEffort,
            Tools = CreateAltaTools(
                providerId,
                sourceSessionIdProvider: () => session.SessionId,
                sourceProjectIdProvider: () => session.ProjectRef,
                workingDirectoryProvider: () => ResolveWorkingDirectory(session)),
            OnPermissionRequest = CreatePermissionHandler(providerId, session.SessionId),
            OnUserInputRequest = (request, cancellationToken) => _userInputRequests.HandleAsync(session.SessionId, request, cancellationToken),
        };
    }

    public static string CreateTransientSessionKey(ModelProviderId providerId, string workingDirectory)
        => $"{providerId.Value}:{workingDirectory}";

    private AgentPermissionRequestHandler CreatePermissionHandler(ModelProviderId providerId, string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        return string.Equals(providerId.Value, ModelProviderIds.Codex.Value, StringComparison.OrdinalIgnoreCase)
            ? static (_, _) => Task.FromResult(new AgentPermissionDecision(AgentPermissionDecisionKind.AllowOnce))
            : (request, cancellationToken) => _permissionRequests.HandleAsync(sessionId, request, cancellationToken);
    }

    private IReadOnlyList<AgentToolDefinition>? CreateAltaTools(
        ModelProviderId providerId,
        Func<string?>? sourceSessionIdProvider,
        Func<string?>? sourceProjectIdProvider,
        Func<string?>? workingDirectoryProvider)
    {
        if (_altaServices is null || !_altaToolProviderIds.Contains(providerId.Value))
        {
            return null;
        }

        var dispatcher = _altaServices.GetService(typeof(AltaCommandDispatcher)) as AltaCommandDispatcher
            ?? new AltaCommandDispatcher(new AltaCommandRegistry(), _altaServices);
        return
        [
            AltaSessionToolFactory.Create(
                dispatcher,
                new AltaSessionToolOptions
                {
                    SourceSessionIdProvider = sourceSessionIdProvider,
                    SourceProjectIdProvider = sourceProjectIdProvider,
                    WorkingDirectoryProvider = workingDirectoryProvider,
                    DefaultMaxOutputRecords = 200,
                    DefaultMaxOutputBytes = 64 * 1024,
                    DefaultTimeout = TimeSpan.FromSeconds(120),
                }),
        ];
    }

    private string ResolveWorkingDirectory(SessionViewDescriptor session)
    {
        return session.Kind switch
        {
            SessionViewKind.GlobalSession => _catalogOptions.GlobalRoot,
            SessionViewKind.ProjectSession when _sessionSelection.GetProjectById(session.ProjectRef) is { } project => project.ProjectPath,
            _ => session.WorkingDirectory,
        };
    }

    private IReadOnlyList<string> ResolveProjectRoots(SessionViewDescriptor session)
    {
        if (_sessionSelection.GetProjectById(session.ProjectRef) is { } project)
        {
            return [project.ProjectPath];
        }

        return [];
    }
}
