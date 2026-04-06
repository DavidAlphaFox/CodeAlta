using CodeAlta.Acp;
using CodeAlta.Catalog;

namespace CodeAlta.App;

internal sealed class AcpAgentRegistryService : IDisposable
{
    private readonly CatalogOptions _catalogOptions;
    private readonly AcpInstalledBackendStore _installedBackendStore;
    private readonly AcpRegistryClient _registryClient;
    private readonly AcpInstallResolver _installResolver;
    private readonly AcpInstaller _installer;
    private bool _disposed;

    public AcpAgentRegistryService(
        CatalogOptions catalogOptions,
        AcpInstalledBackendStore installedBackendStore,
        AcpRegistryClient? registryClient = null,
        AcpInstallResolver? installResolver = null,
        AcpInstaller? installer = null)
    {
        ArgumentNullException.ThrowIfNull(catalogOptions);
        ArgumentNullException.ThrowIfNull(installedBackendStore);

        _catalogOptions = catalogOptions;
        _installedBackendStore = installedBackendStore;
        _registryClient = registryClient ?? new AcpRegistryClient();
        _installResolver = installResolver ?? new AcpInstallResolver();
        _installer = installer ?? new AcpInstaller();
    }

    public string RegistryCachePath => Path.Combine(_catalogOptions.AcpRegistryRoot, "latest", "registry.json");

    public IReadOnlyList<AcpBackendDefinition> LoadInstalledDefinitions()
        => _installedBackendStore.Load();

    public async Task<AcpRegistryDocument> RefreshRegistryAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var document = await _registryClient.DownloadLatestAsync(cancellationToken).ConfigureAwait(false);
        await _registryClient.SaveToFileAsync(RegistryCachePath, document, cancellationToken).ConfigureAwait(false);
        return document;
    }

    public async Task<AcpRegistryDocument?> LoadCachedRegistryAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!File.Exists(RegistryCachePath))
        {
            return null;
        }

        return await _registryClient.LoadFromFileAsync(RegistryCachePath, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AcpBackendDefinition> InstallAgentAsync(
        string agentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ThrowIfDisposed();

        var registry = await LoadCachedRegistryAsync(cancellationToken).ConfigureAwait(false)
            ?? await RefreshRegistryAsync(cancellationToken).ConfigureAwait(false);
        var manifest = registry.Agents.FirstOrDefault(
                agent => string.Equals(agent.Id, agentId.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"ACP registry agent '{agentId}' was not found.");
        var plan = _installResolver.Resolve(manifest);
        var resolvedInstall = await _installer
            .InstallAsync(plan, _catalogOptions.AcpDownloadsRoot, _catalogOptions.AcpInstallsRoot, cancellationToken)
            .ConfigureAwait(false);
        var definition = ToBackendDefinition(resolvedInstall);
        _installedBackendStore.Save(definition);
        return definition;
    }

    public bool RemoveInstalledAgent(string agentId)
    {
        ThrowIfDisposed();
        return _installedBackendStore.Delete(agentId);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _registryClient.Dispose();
        _installer.Dispose();
    }

    private static AcpBackendDefinition ToBackendDefinition(AcpResolvedInstall install)
    {
        ArgumentNullException.ThrowIfNull(install);

        return new AcpBackendDefinition
        {
            AgentId = install.Manifest.Id,
            DisplayName = install.Manifest.Name,
            Enabled = true,
            RegistryId = install.Manifest.Id,
            Command = install.Command,
            Arguments = install.Arguments is null ? null : [.. install.Arguments],
            WorkingDirectory = install.WorkingDirectory,
            EnvironmentVariables = install.EnvironmentVariables is null
                ? null
                : new Dictionary<string, string>(install.EnvironmentVariables, StringComparer.OrdinalIgnoreCase),
            UseUnstable = true,
            EnableFilesystem = true,
            EnableTerminal = true,
            EnableElicitation = false,
        };
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
