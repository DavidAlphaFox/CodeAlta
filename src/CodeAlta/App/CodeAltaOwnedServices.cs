using CodeAlta.Agent;
using CodeAlta.Agent.Anthropic;
using CodeAlta.Agent.Copilot;
using CodeAlta.Agent.GoogleGenAI;
using CodeAlta.Agent.ModelCatalog;
using CodeAlta.Agent.OpenAI;
using CodeAlta.Catalog;
using CodeAlta.Catalog.Skills;
using CodeAlta.Orchestration.Hosting;
using CodeAlta.Orchestration.Runtime;
using CodeAlta.Plugins;
using XenoAtom.Logging;

namespace CodeAlta.App;

internal sealed class CodeAltaOwnedServices : IAsyncDisposable
{
    private readonly bool _ownsLogging;
    private readonly AgentBackendFactory _backendFactory;
    private readonly ModelProviderRegistry _modelProviderRegistry;
    private readonly IModelProviderInitializationService _modelProviderInitializationService;
    private readonly CodeAltaConfigStore _configStore;
    private readonly List<ModelProviderDescriptor> _backendDescriptors;
    private readonly ModelsDevCatalogService _modelsDevCatalogService;

    private CodeAltaOwnedServices(
        bool ownsLogging,
        AgentBackendFactory backendFactory,
        ModelProviderRegistry modelProviderRegistry,
        IModelProviderInitializationService modelProviderInitializationService,
        CodeAltaConfigStore configStore,
        ModelsDevCatalogService modelsDevCatalogService,
        PluginRuntimeManager pluginRuntime,
        PluginHostBridge pluginHostBridge,
        CatalogOptions catalogOptions,
        List<ModelProviderDescriptor> backendDescriptors,
        IAgentSessionCatalog sessionCatalog,
        ProjectCatalog projectCatalog,
        WorkThreadCatalog threadCatalog,
        SkillCatalog skillCatalog,
        AgentHub agentHub,
        SessionRuntimeService runtimeService,
        IProjectFileSearchService projectFileSearchService)
    {
        _ownsLogging = ownsLogging;
        _backendFactory = backendFactory;
        _modelProviderRegistry = modelProviderRegistry;
        _modelProviderInitializationService = modelProviderInitializationService;
        _configStore = configStore;
        _modelsDevCatalogService = modelsDevCatalogService;
        PluginRuntime = pluginRuntime;
        PluginHostBridge = pluginHostBridge;
        _backendDescriptors = backendDescriptors;
        SessionCatalog = sessionCatalog;
        CatalogOptions = catalogOptions;
        ProjectCatalog = projectCatalog;
        ThreadCatalog = threadCatalog;
        SkillCatalog = skillCatalog;
        AgentHub = agentHub;
        RuntimeService = runtimeService;
        ProjectFileSearchService = projectFileSearchService;
    }

    public CatalogOptions CatalogOptions { get; }

    public IReadOnlyList<ModelProviderDescriptor> BackendDescriptors => _backendDescriptors;

    public IModelProviderRegistry ModelProviderRegistry => _modelProviderRegistry;

    public IModelProviderInitializationService ModelProviderInitializationService => _modelProviderInitializationService;

    internal IModelProviderInitializationService ProviderInit => _modelProviderInitializationService;

    internal ModelsDevCatalogService ModelsDevCatalogService => _modelsDevCatalogService;

    public ProjectCatalog ProjectCatalog { get; }

    public WorkThreadCatalog ThreadCatalog { get; }

    public SkillCatalog SkillCatalog { get; }

    public AgentHub AgentHub { get; }

    public SessionRuntimeService RuntimeService { get; }

    public IAgentSessionCatalog SessionCatalog { get; }

    public IProjectFileSearchService ProjectFileSearchService { get; }

    public PluginRuntimeManager PluginRuntime { get; }

    public PluginHostBridge PluginHostBridge { get; }

    public static async Task<CodeAltaOwnedServices> CreateAsync(
        CancellationToken cancellationToken,
        PluginRuntimeManager? prestartedPluginRuntime = null)
    {
        var homeRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".alta");
        Directory.CreateDirectory(homeRoot);
        var cacheRoot = Path.Combine(homeRoot, "cache");
        var ownsLogging = CodeAltaLogging.Initialize(homeRoot);

        Directory.CreateDirectory(cacheRoot);
        var rawArguments = Environment.GetCommandLineArgs();
        var pluginBootstrapOptions = CodeAltaCliOptions.GetPluginBootstrapOptions(rawArguments);
        var catalogOptions = new CatalogOptions { GlobalRoot = homeRoot };
        var configStore = new CodeAltaConfigStore(catalogOptions);
        var modelsDevCatalogService = new ModelsDevCatalogService(
            new ModelsDevCatalogServiceOptions
            {
                CacheFilePath = Path.Combine(cacheRoot, "model-catalog", "models_dev_db.json"),
            });
        modelsDevCatalogService.StartBackgroundRefresh();

        var backendDescriptors = new List<ModelProviderDescriptor>();
        var pluginAltaServiceBridge = new PluginAltaServiceBridge();
        var sharedHost = await CodeAltaHost.CreateAsync(
                new CodeAltaHostOptions
                {
                    GlobalRoot = homeRoot,
                    CurrentProjectPath = Environment.CurrentDirectory,
                    IsHeadless = false,
                    HasInteractiveUi = true,
                    PluginSafeMode = pluginBootstrapOptions.PluginSafeMode,
                    RawArguments = rawArguments,
                    WaitForEnterAfterPluginLiveOutput = pluginBootstrapOptions.WaitForEnterAfterPluginLiveOutput,
                    PrestartedPluginRuntime = prestartedPluginRuntime,
                    PluginBuiltIns = CodeAltaBuiltInPlugins.All,
                    PluginServices = new CodeAltaPluginServices(pluginAltaServiceBridge),
                    ConfigureModelProviders = RegisterFrontendModelProviders,
                },
                cancellationToken)
            .ConfigureAwait(false);
        var backendFactory = sharedHost.BackendFactory;
        var pluginRuntime = sharedHost.PluginRuntime;
        var pluginHostBridge = new PluginHostBridge(pluginRuntime, () => sharedHost.CurrentProject, pluginAltaServiceBridge);
        backendDescriptors.AddRange(
            pluginRuntime.Adapter.GetAgentBackends(new PluginAdapterOperationOptions { HasInteractiveUi = true })
                .Select(static pluginBackend => new ModelProviderDescriptor(
                    new ModelProviderId(pluginBackend.Name),
                    pluginBackend.DisplayName ?? pluginBackend.Name)));

        return new CodeAltaOwnedServices(
            ownsLogging,
            backendFactory,
            sharedHost.ModelProviderRegistry,
            sharedHost.ModelProviderInitializationService,
            configStore,
            modelsDevCatalogService,
            pluginRuntime,
            pluginHostBridge,
            sharedHost.CatalogOptions,
            backendDescriptors,
            sharedHost.SessionCatalog,
            sharedHost.ProjectCatalog,
            sharedHost.ThreadCatalog,
            sharedHost.SkillCatalog,
            sharedHost.AgentHub,
            sharedHost.RuntimeService,
            sharedHost.ProjectFileSearchService);

        void RegisterFrontendModelProviders(ModelProviderRegistry modelProviderRegistry, AgentBackendFactory backendFactory)
        {
            backendDescriptors.AddRange(
                ConfiguredModelProviderRegistryBuilder.RegisterConfiguredProviders(
                    backendFactory,
                    modelProviderRegistry,
                    configStore,
                    homeRoot,
                    modelsDevCatalogService));

        }
    }

    public async ValueTask DisposeAsync()
    {
        await RuntimeService.DisposeAsync().ConfigureAwait(false);
        await AgentHub.DisposeAsync().ConfigureAwait(false);
        await _modelProviderRegistry.DisposeAsync().ConfigureAwait(false);
        await PluginRuntime.DisposeAsync().ConfigureAwait(false);
        await _modelsDevCatalogService.DisposeAsync().ConfigureAwait(false);

        if (_ownsLogging)
        {
            LogManager.Shutdown();
        }
    }

    internal static IReadOnlyList<ModelProviderDescriptor> CreateBuiltInBackendDescriptors()
    {
        return
        [
            new ModelProviderDescriptor(ModelProviderIds.Codex, "Codex"),
            new ModelProviderDescriptor(ModelProviderIds.Copilot, "Copilot"),
        ];
    }

    public async Task<IReadOnlyList<ModelProviderDescriptor>> RefreshProviderBackendsAsync(
        CancellationToken cancellationToken = default)
    {
        var providerDefinitions = _configStore.LoadGlobalProviderDefinitions(includeDisabled: true)
            .ToDictionary(static definition => definition.ProviderKey, StringComparer.OrdinalIgnoreCase);
        var expectedBackendIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var providerDescriptors = new List<ModelProviderDescriptor>();

        providerDescriptors.AddRange(
            ConfiguredModelProviderRegistryBuilder.RegisterOrReplaceConfiguredProviders(
                _backendFactory,
                _modelProviderRegistry,
                providerDefinitions.Values.Where(static definition => definition.Enabled != false),
                CatalogOptions.GlobalRoot,
                _modelsDevCatalogService));
        foreach (var descriptor in providerDescriptors)
        {
            expectedBackendIds.Add(descriptor.BackendId.Value);
        }

        foreach (var descriptor in _backendDescriptors.ToArray())
        {
            if (expectedBackendIds.Contains(descriptor.BackendId.Value))
            {
                continue;
            }

            _backendFactory.Unregister(descriptor.BackendId);
            _modelProviderRegistry.Unregister(descriptor.ProviderId);
        }

        _backendDescriptors.Clear();
        _backendDescriptors.InsertRange(
            0,
            providerDescriptors.OrderBy(static descriptor => descriptor.DisplayName, StringComparer.OrdinalIgnoreCase));

        return _backendDescriptors;
    }
}
