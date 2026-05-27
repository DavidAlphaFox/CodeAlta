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
    private readonly ModelProviderRegistry _modelProviderRegistry;
    private readonly IModelProviderInitializationService _modelProviderInitializationService;
    private readonly CodeAltaConfigStore _configStore;
    private readonly List<ModelProviderDescriptor> _providerDescriptors;
    private readonly ModelsDevCatalogService _modelsDevCatalogService;

    private CodeAltaOwnedServices(
        bool ownsLogging,
        ModelProviderRegistry modelProviderRegistry,
        IModelProviderInitializationService modelProviderInitializationService,
        CodeAltaConfigStore configStore,
        ModelsDevCatalogService modelsDevCatalogService,
        PluginRuntimeManager pluginRuntime,
        PluginHostBridge pluginHostBridge,
        CatalogOptions catalogOptions,
        List<ModelProviderDescriptor> providerDescriptors,
        IAgentSessionCatalog sessionCatalog,
        ProjectCatalog projectCatalog,
        WorkThreadCatalog threadCatalog,
        SkillCatalog skillCatalog,
        AgentHub agentHub,
        SessionRuntimeService runtimeService,
        IProjectFileSearchService projectFileSearchService)
    {
        _ownsLogging = ownsLogging;
        _modelProviderRegistry = modelProviderRegistry;
        _modelProviderInitializationService = modelProviderInitializationService;
        _configStore = configStore;
        _modelsDevCatalogService = modelsDevCatalogService;
        PluginRuntime = pluginRuntime;
        PluginHostBridge = pluginHostBridge;
        _providerDescriptors = providerDescriptors;
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

    public IReadOnlyList<ModelProviderDescriptor> ProviderDescriptors => _providerDescriptors;

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

        var providerDescriptors = new List<ModelProviderDescriptor>();
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
        var pluginRuntime = sharedHost.PluginRuntime;
        var pluginHostBridge = new PluginHostBridge(pluginRuntime, () => sharedHost.CurrentProject, pluginAltaServiceBridge);

        return new CodeAltaOwnedServices(
            ownsLogging,
            sharedHost.ModelProviderRegistry,
            sharedHost.ModelProviderInitializationService,
            configStore,
            modelsDevCatalogService,
            pluginRuntime,
            pluginHostBridge,
            sharedHost.CatalogOptions,
            providerDescriptors,
            sharedHost.SessionCatalog,
            sharedHost.ProjectCatalog,
            sharedHost.ThreadCatalog,
            sharedHost.SkillCatalog,
            sharedHost.AgentHub,
            sharedHost.RuntimeService,
            sharedHost.ProjectFileSearchService);

        void RegisterFrontendModelProviders(ModelProviderRegistry modelProviderRegistry)
        {
            providerDescriptors.AddRange(
                ConfiguredModelProviderRegistryBuilder.RegisterConfiguredProviders(
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

    internal static IReadOnlyList<ModelProviderDescriptor> CreateBuiltInProviderDescriptors()
    {
        return
        [
            new ModelProviderDescriptor(ModelProviderIds.Codex, "Codex"),
            new ModelProviderDescriptor(ModelProviderIds.Copilot, "Copilot"),
        ];
    }

    public async Task<IReadOnlyList<ModelProviderDescriptor>> RefreshModelProvidersAsync(
        CancellationToken cancellationToken = default)
    {
        var providerDefinitions = _configStore.LoadGlobalProviderDefinitions(includeDisabled: true)
            .ToDictionary(static definition => definition.ProviderKey, StringComparer.OrdinalIgnoreCase);
        var expectedProviderIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var providerDescriptors = new List<ModelProviderDescriptor>();

        providerDescriptors.AddRange(
            ConfiguredModelProviderRegistryBuilder.RegisterOrReplaceConfiguredProviders(
                _modelProviderRegistry,
                providerDefinitions.Values.Where(static definition => definition.Enabled != false),
                CatalogOptions.GlobalRoot,
                _modelsDevCatalogService));
        foreach (var descriptor in providerDescriptors)
        {
            expectedProviderIds.Add(descriptor.ProviderId.Value);
        }

        foreach (var descriptor in _providerDescriptors.ToArray())
        {
            if (expectedProviderIds.Contains(descriptor.ProviderId.Value))
            {
                continue;
            }

            _modelProviderRegistry.Unregister(descriptor.ProviderId);
        }

        _providerDescriptors.Clear();
        _providerDescriptors.InsertRange(
            0,
            providerDescriptors.OrderBy(static descriptor => descriptor.DisplayName, StringComparer.OrdinalIgnoreCase));

        return _providerDescriptors;
    }
}
