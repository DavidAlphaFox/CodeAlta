namespace CodeAlta.Workspaces;

/// <summary>
/// Loads workspace descriptors from the global repository layout.
/// </summary>
public sealed class WorkspaceCatalog
{
    private readonly WorkspaceCatalogOptions _options;
    private readonly WorkspaceYamlSerializer _serializer;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkspaceCatalog"/> class.
    /// </summary>
    /// <param name="options">Catalog options.</param>
    /// <param name="serializer">Optional YAML serializer.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <see cref="WorkspaceCatalogOptions.GlobalRepoRoot"/> is empty.</exception>
    public WorkspaceCatalog(WorkspaceCatalogOptions options, WorkspaceYamlSerializer? serializer = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.GlobalRepoRoot))
        {
            throw new ArgumentException("Global repository root is required.", nameof(options));
        }

        _options = options;
        _serializer = serializer ?? new WorkspaceYamlSerializer();
    }

    /// <summary>
    /// Loads all workspaces from disk.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All loaded workspace descriptors.</returns>
    public async Task<IReadOnlyList<WorkspaceDescriptor>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        var workspaceRoot = _options.WorkspacesRoot;
        if (!Directory.Exists(workspaceRoot))
        {
            return [];
        }

        var results = new List<WorkspaceDescriptor>();

        foreach (var workspaceDirectory in Directory.EnumerateDirectories(workspaceRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var workspaceYamlPath = Path.Combine(workspaceDirectory, "workspace.yaml");
            if (!File.Exists(workspaceYamlPath))
            {
                continue;
            }

            var yaml = await File.ReadAllTextAsync(workspaceYamlPath, cancellationToken).ConfigureAwait(false);
            var descriptor = _serializer.DeserializeWorkspace(yaml);
            descriptor.SourcePath = workspaceYamlPath;

            var expectedKey = Path.GetFileName(workspaceDirectory);
            if (!string.Equals(descriptor.Key, expectedKey, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Workspace key '{descriptor.Key}' does not match folder '{expectedKey}'.");
            }

            var projectDirectory = Path.Combine(workspaceDirectory, "projects");
            if (Directory.Exists(projectDirectory))
            {
                foreach (var projectPath in Directory.EnumerateFiles(projectDirectory, "*.yaml"))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var projectYaml = await File.ReadAllTextAsync(projectPath, cancellationToken).ConfigureAwait(false);
                    var projectDescriptor = _serializer.DeserializeProject(projectYaml);
                    descriptor.Projects.RemoveAll(x =>
                        string.Equals(x.Key, projectDescriptor.Key, StringComparison.OrdinalIgnoreCase));
                    descriptor.Projects.Add(projectDescriptor);
                }
            }

            descriptor.Validate();
            results.Add(descriptor);
        }

        return results
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Loads a single workspace by key.
    /// </summary>
    /// <param name="workspaceKey">The workspace key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching workspace descriptor, or <see langword="null"/> when not found.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="workspaceKey"/> is invalid.</exception>
    public async Task<WorkspaceDescriptor?> GetByKeyAsync(
        string workspaceKey,
        CancellationToken cancellationToken = default)
    {
        WorkspaceKeyValidator.Validate(workspaceKey, nameof(workspaceKey));

        var items = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return items.FirstOrDefault(x => string.Equals(x.Key, workspaceKey, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Loads a machine profile by machine id.
    /// </summary>
    /// <param name="machineId">The machine id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The machine profile when found; otherwise <see langword="null"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="machineId"/> is empty.</exception>
    public async Task<MachineProfile?> LoadMachineProfileAsync(
        string machineId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(machineId))
        {
            throw new ArgumentException("Machine id is required.", nameof(machineId));
        }

        var profilePath = Path.Combine(_options.GlobalRepoRoot, "machines", $"{machineId}.yaml");
        if (!File.Exists(profilePath))
        {
            return null;
        }

        var yaml = await File.ReadAllTextAsync(profilePath, cancellationToken).ConfigureAwait(false);
        var profile = _serializer.DeserializeMachineProfile(yaml);
        profile.Validate();
        return profile;
    }
}
