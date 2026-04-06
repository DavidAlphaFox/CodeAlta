using CodeAlta.Agent;
using CodeAlta.Agent.Acp;
using CodeAlta.Agent.Codex;
using CodeAlta.Agent.Copilot;
using CodeAlta.Acp;
using CodeAlta.Catalog;
using CodeAlta.Catalog.Roles;
using CodeAlta.Orchestration.Runtime;
using CodeAlta.Persistence;
using CodeAlta.Search;
using XenoAtom.Logging;

namespace CodeAlta.App;

internal sealed class CodeAltaOwnedServices : IAsyncDisposable
{
    private readonly bool _ownsLogging;
    private readonly CodeAltaDb _db;

    private CodeAltaOwnedServices(
        bool ownsLogging,
        CodeAltaDb db,
        CatalogOptions catalogOptions,
        IReadOnlyList<AgentBackendDescriptor> backendDescriptors,
        AcpAgentRegistryService acpAgentRegistryService,
        ProjectCatalog projectCatalog,
        WorkThreadCatalog threadCatalog,
        AgentHub agentHub,
        WorkThreadRuntimeService runtimeService,
        IProjectFileSearchService projectFileSearchService)
    {
        _ownsLogging = ownsLogging;
        _db = db;
        CatalogOptions = catalogOptions;
        BackendDescriptors = backendDescriptors;
        AcpAgentRegistryService = acpAgentRegistryService;
        ProjectCatalog = projectCatalog;
        ThreadCatalog = threadCatalog;
        AgentHub = agentHub;
        RuntimeService = runtimeService;
        ProjectFileSearchService = projectFileSearchService;
    }

    public CatalogOptions CatalogOptions { get; }

    public IReadOnlyList<AgentBackendDescriptor> BackendDescriptors { get; }

    public AcpAgentRegistryService AcpAgentRegistryService { get; }

    public ProjectCatalog ProjectCatalog { get; }

    public WorkThreadCatalog ThreadCatalog { get; }

    public AgentHub AgentHub { get; }

    public WorkThreadRuntimeService RuntimeService { get; }

    public IProjectFileSearchService ProjectFileSearchService { get; }

    public static async Task<CodeAltaOwnedServices> CreateAsync(CancellationToken cancellationToken)
    {
        var homeRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codealta");
        Directory.CreateDirectory(homeRoot);
        var ownsLogging = CodeAltaLogging.Initialize(homeRoot);

        var machineRoot = Path.Combine(homeRoot, "machine");
        Directory.CreateDirectory(machineRoot);

        var db = new CodeAltaDb(
            new CodeAltaDbOptions
            {
                DatabasePath = Path.Combine(machineRoot, "codealta.db"),
            });
        await db.InitializeAsync(cancellationToken).ConfigureAwait(false);

        var agentRepository = new AgentRepository(db);
        var catalogOptions = new CatalogOptions
        {
            GlobalRoot = homeRoot,
        };
        var projectCatalog = new ProjectCatalog(catalogOptions);
        await projectCatalog.UpsertFromPathAsync(Environment.CurrentDirectory, cancellationToken).ConfigureAwait(false);

        var threadCatalog = new WorkThreadCatalog(catalogOptions);
        var roleProfileStore = new RoleProfileStore();
        var instructionTemplateProvider = new AgentInstructionTemplateProvider();
        var configStore = new CodeAltaConfigStore(catalogOptions);
        var installedBackendStore = new AcpInstalledBackendStore(catalogOptions);
        var acpAgentRegistryService = new AcpAgentRegistryService(catalogOptions, installedBackendStore);

        var backendFactory = new AgentBackendFactory();
        backendFactory.RegisterCodex(new CodexAgentBackendOptions());
        backendFactory.RegisterCopilot(new CopilotAgentBackendOptions());
        var backendDescriptors = new List<AgentBackendDescriptor>(CreateBuiltInBackendDescriptors());
        foreach (var definition in configStore.LoadEffectiveAcpBackendDefinitions(installedBackendStore.Load()))
        {
            if (TryCreateAcpBackendOptions(catalogOptions, definition, out var acpOptions))
            {
                backendFactory.RegisterAcp(acpOptions);
                backendDescriptors.Add(new AgentBackendDescriptor(
                    AcpAgentBackendFactoryExtensions.CreateBackendId(acpOptions.AgentId),
                    acpOptions.DisplayName));
            }
        }

        var agentHub = new AgentHub(backendFactory, agentRepository);
        var runtimeService = new WorkThreadRuntimeService(
            agentHub,
            projectCatalog,
            threadCatalog,
            roleProfileStore,
            instructionTemplateProvider,
            catalogOptions);
        var projectFileSearchService = new ProjectFileSearchService(
            new ProjectFileSnapshotCache(),
            new PersistentProjectFileUsageStore(new ProjectFileUsageRepository(db)));

        return new CodeAltaOwnedServices(
            ownsLogging,
            db,
            catalogOptions,
            backendDescriptors,
            acpAgentRegistryService,
            projectCatalog,
            threadCatalog,
            agentHub,
            runtimeService,
            projectFileSearchService);
    }

    public async ValueTask DisposeAsync()
    {
        await RuntimeService.DisposeAsync().ConfigureAwait(false);
        await AgentHub.DisposeAsync().ConfigureAwait(false);
        AcpAgentRegistryService.Dispose();

        GC.KeepAlive(_db);

        if (_ownsLogging)
        {
            LogManager.Shutdown();
        }
    }

    internal static IReadOnlyList<AgentBackendDescriptor> CreateBuiltInBackendDescriptors()
    {
        return
        [
            new AgentBackendDescriptor(AgentBackendIds.Codex, "Codex"),
            new AgentBackendDescriptor(AgentBackendIds.Copilot, "Copilot"),
        ];
    }

    private static bool TryCreateAcpBackendOptions(
        CatalogOptions catalogOptions,
        AcpBackendDefinition definition,
        out AcpAgentBackendOptions options)
    {
        ArgumentNullException.ThrowIfNull(catalogOptions);
        ArgumentNullException.ThrowIfNull(definition);

        if (string.IsNullOrWhiteSpace(definition.AgentId) ||
            string.IsNullOrWhiteSpace(definition.Command))
        {
            options = null!;
            return false;
        }

        var normalizedAgentId = definition.AgentId.Trim().ToLowerInvariant();
        options = new AcpAgentBackendOptions
        {
            AgentId = normalizedAgentId,
            DisplayName = string.IsNullOrWhiteSpace(definition.DisplayName)
                ? normalizedAgentId
                : definition.DisplayName.Trim(),
            RegistryId = string.IsNullOrWhiteSpace(definition.RegistryId)
                ? null
                : definition.RegistryId.Trim(),
            ProcessOptions = new AcpProcessOptions
            {
                FileName = definition.Command.Trim(),
                Arguments = definition.Arguments,
                WorkingDirectory = definition.WorkingDirectory,
                EnvironmentVariables = definition.EnvironmentVariables,
            },
            StateRootPath = Path.Combine(catalogOptions.AcpStateRoot, normalizedAgentId),
            EnableFilesystem = definition.EnableFilesystem,
            EnableTerminal = definition.EnableTerminal,
            EnableElicitation = definition.EnableElicitation,
            UseUnstableFeatures = definition.UseUnstable,
            UnstableFeatures = new AcpUnstableFeatureOptions
            {
                UseSessionResume = definition.UseUnstable,
                UseSessionClose = definition.UseUnstable,
                UseSessionDelete = definition.UseUnstable,
                UseElicitation = definition.UseUnstable && definition.EnableElicitation,
                UseSetModel = definition.UseUnstable,
            },
        };
        return true;
    }
}
