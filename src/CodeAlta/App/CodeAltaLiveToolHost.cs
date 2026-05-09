using CodeAlta.Agent;
using CodeAlta.Agent.Acp;
using CodeAlta.Agent.Codex;
using CodeAlta.Agent.Copilot;
using CodeAlta.Agent.ModelCatalog;
using CodeAlta.Catalog;
using CodeAlta.CodexSdk;
using CodeAlta.LiveTool;
using CodeAlta.Orchestration.Hosting;
using CodeAlta.Orchestration.Runtime;
using CodeAlta.Plugins;
using CodeAlta.Plugins.Abstractions;
using XenoAtom.Logging;

namespace CodeAlta.App;

internal sealed class CodeAltaLiveToolHost : IAsyncDisposable
{
    private readonly CodeAltaHost _host;
    private readonly ModelsDevCatalogService _modelsDevCatalogService;
    private readonly bool _ownsLogging;

    private CodeAltaLiveToolHost(
        CodeAltaHost host,
        ModelsDevCatalogService modelsDevCatalogService,
        AltaServiceCollection services,
        bool ownsLogging)
    {
        _host = host;
        _modelsDevCatalogService = modelsDevCatalogService;
        Services = services;
        _ownsLogging = ownsLogging;
    }

    public IServiceProvider Services { get; }

    public static async Task<CodeAltaLiveToolHost> CreateAsync(
        IReadOnlyList<string> args,
        string? currentDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);

        var homeRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".alta");
        Directory.CreateDirectory(homeRoot);
        var cacheRoot = Path.Combine(homeRoot, "cache");
        Directory.CreateDirectory(cacheRoot);
        var ownsLogging = CodeAltaLogging.Initialize(homeRoot);
        var bootstrapOptions = CodeAltaCliOptions.GetPluginBootstrapOptions(args);
        var catalogOptions = new CatalogOptions { GlobalRoot = homeRoot };
        var configStore = new CodeAltaConfigStore(catalogOptions);
        var installedBackendStore = new AcpInstalledBackendStore(catalogOptions);
        var modelsDevCatalogService = new ModelsDevCatalogService(
            new ModelsDevCatalogServiceOptions
            {
                CacheFilePath = Path.Combine(cacheRoot, "model-catalog", "models_dev_db.json"),
            });
        modelsDevCatalogService.StartBackgroundRefresh();
        var backendDescriptors = new List<AgentBackendDescriptor>();
        var providerDefinitions = configStore.LoadGlobalProviderDefinitions(includeDisabled: true)
            .ToDictionary(static definition => definition.ProviderKey, StringComparer.OrdinalIgnoreCase);
        CodeAltaHost host;
        try
        {
            host = await CodeAltaHost.CreateAsync(
                new CodeAltaHostOptions
                {
                    GlobalRoot = homeRoot,
                    CurrentProjectPath = currentDirectory ?? Environment.CurrentDirectory,
                    IsHeadless = true,
                    HasInteractiveUi = false,
                    PluginSafeMode = bootstrapOptions.PluginSafeMode,
                    RawArguments = args,
                    WaitForEnterAfterPluginLiveOutput = false,
                    PluginBuiltIns = CodeAltaBuiltInPlugins.All,
                    ConfigureAgentBackends = RegisterLiveToolBackends,
                },
                cancellationToken)
            .ConfigureAwait(false);
        }
        catch
        {
            await modelsDevCatalogService.DisposeAsync().ConfigureAwait(false);
            if (ownsLogging && LogManager.IsInitialized)
            {
                LogManager.Shutdown();
            }

            throw;
        }

        foreach (var backendId in host.AgentHub.ListRegisteredBackends().OrderBy(static id => id.Value, StringComparer.OrdinalIgnoreCase))
        {
            if (!backendDescriptors.Any(descriptor => descriptor.BackendId == backendId))
            {
                backendDescriptors.Add(new AgentBackendDescriptor(backendId, backendId.Value));
            }
        }

        var services = new AltaServiceCollection()
            .Add(host.CatalogOptions)
            .Add(host.ProjectCatalog)
            .Add(host.ThreadCatalog)
            .Add(host.SkillCatalog)
            .Add(host.AgentHub)
            .Add(host.RuntimeService)
            .Add(host.ProjectFileSearchService)
            .Add<IReadOnlyList<AgentBackendDescriptor>>(backendDescriptors)
            .Add<IAltaPluginCatalog>(new RuntimeAltaPluginCatalog(host.PluginRuntime));

        return new CodeAltaLiveToolHost(host, modelsDevCatalogService, services, ownsLogging);

        void RegisterLiveToolBackends(AgentBackendFactory backendFactory)
        {
            if (providerDefinitions.TryGetValue("codex", out var codexProvider) && codexProvider.Enabled != false)
            {
                var codexPath = CodeAltaOwnedServices.ResolveCodexExecutablePath(
                    Environment.GetEnvironmentVariable("CODEALTA_CODEX_PATH"));
                backendFactory.RegisterCodex(
                    new CodexAgentBackendOptions
                    {
                        ProcessOptions = new CodexProcessOptions
                        {
                            CodexPath = codexPath,
                            LocalRootPath = cacheRoot,
                            ReleaseTag = codexPath is null ? CodexClient.CompiledAgainstReleaseTag : null,
                        },
                    });
                backendDescriptors.Add(new AgentBackendDescriptor(AgentBackendIds.Codex, codexProvider.DisplayName ?? "Codex"));
            }

            if (providerDefinitions.TryGetValue("copilot", out var copilotProvider) && copilotProvider.Enabled != false)
            {
                backendFactory.RegisterCopilot(new CopilotAgentBackendOptions());
                backendDescriptors.Add(new AgentBackendDescriptor(AgentBackendIds.Copilot, copilotProvider.DisplayName ?? "GitHub Copilot"));
            }

            backendDescriptors.AddRange(
                RawApiBackendRegistrar.RegisterConfiguredBackends(
                    backendFactory,
                    configStore,
                    homeRoot,
                    modelsDevCatalogService));

            foreach (var definition in configStore.LoadEffectiveAcpBackendDefinitions(installedBackendStore.Load()))
            {
                if (!CodeAltaOwnedServices.TryCreateAcpBackendOptions(catalogOptions, definition, out var acpOptions))
                {
                    continue;
                }

                backendFactory.RegisterAcp(acpOptions);
                backendDescriptors.Add(new AgentBackendDescriptor(
                    AcpAgentBackendFactoryExtensions.CreateBackendId(acpOptions.AgentId),
                    acpOptions.DisplayName));
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _host.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            await _modelsDevCatalogService.DisposeAsync().ConfigureAwait(false);
            if (_ownsLogging && LogManager.IsInitialized)
            {
                LogManager.Shutdown();
            }
        }
    }

    private sealed class RuntimeAltaPluginCatalog(PluginRuntimeManager runtime) : IAltaPluginCatalog
    {
        public IReadOnlyList<AltaPluginSummary> ListPlugins()
            => runtime.ActivePlugins
                .Select(CreateSummary)
                .OrderBy(static plugin => plugin.RuntimeKey, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        public AltaPluginSummary? GetPlugin(string runtimeKey)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(runtimeKey);
            return ListPlugins().FirstOrDefault(plugin => string.Equals(plugin.RuntimeKey, runtimeKey, StringComparison.OrdinalIgnoreCase));
        }

        public IReadOnlyList<AltaCommandPolicy> ListCommandPolicies()
            => [];

        private AltaPluginSummary CreateSummary(ActivePluginInstance plugin)
        {
            var descriptor = plugin.Descriptor;
            var packageId = plugin.SourcePackage?.PackageId;
            var diagnostics = runtime.Diagnostics
                .Where(diagnostic => string.Equals(diagnostic.RuntimeKey, descriptor.RuntimeKey, StringComparison.OrdinalIgnoreCase) ||
                                     (!string.IsNullOrWhiteSpace(packageId) && string.Equals(diagnostic.PackageId, packageId, StringComparison.OrdinalIgnoreCase)))
                .Select(FormatDiagnostic)
                .ToArray();
            return new AltaPluginSummary
            {
                RuntimeKey = descriptor.RuntimeKey,
                DisplayName = descriptor.DisplayName ?? descriptor.TypeName,
                Version = descriptor.Version,
                Scope = plugin.SourcePackage?.Root.Scope.ToString().ToLowerInvariant() ?? "builtin",
                State = plugin.State.ToString().ToLowerInvariant(),
                Diagnostics = diagnostics,
            };
        }

        private static string FormatDiagnostic(PluginRuntimeDiagnostic diagnostic)
            => $"{diagnostic.Severity}/{diagnostic.Source}: {diagnostic.Message}";
    }
}
