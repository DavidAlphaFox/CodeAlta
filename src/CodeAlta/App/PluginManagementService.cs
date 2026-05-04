using CodeAlta.Catalog;
using CodeAlta.Plugins;
using CodeAlta.Plugins.Abstractions;

namespace CodeAlta.App;

internal sealed class PluginManagementService
{
    private readonly CatalogOptions _catalogOptions;
    private readonly Func<ProjectDescriptor?> _getSelectedProject;
    private readonly PluginManagementModelBuilder _modelBuilder = new();
    private readonly SourcePluginDiscoveryService _sourceDiscovery = new();

    public PluginManagementService(CatalogOptions catalogOptions, Func<ProjectDescriptor?> getSelectedProject)
    {
        ArgumentNullException.ThrowIfNull(catalogOptions);
        ArgumentNullException.ThrowIfNull(getSelectedProject);
        _catalogOptions = catalogOptions;
        _getSelectedProject = getSelectedProject;
    }

    public PluginManagementSnapshot LoadSnapshot()
    {
        var selectedProject = _getSelectedProject();
        var globalConfig = new CodeAltaConfigStore(_catalogOptions).LoadGlobal();
        var projectConfig = new CodeAltaConfigStore(_catalogOptions).LoadProject(selectedProject?.ProjectPath);
        var safeMode = PluginRuntimeConfigResolver.IsSafeModeEnabled([]);
        var sourcePackages = DiscoverSourcePackages(selectedProject).ToArray();
        var entries = _modelBuilder.Build(
            builtIns: [],
            sourcePackages,
            globalConfig,
            projectConfig,
            pendingChanges: [],
            buildResults: [],
            diagnostics: [],
            contributions: [],
            safeMode);
        return new PluginManagementSnapshot(entries, safeMode, selectedProject?.ProjectPath);
    }

    private IEnumerable<SourcePluginPackage> DiscoverSourcePackages(ProjectDescriptor? selectedProject)
    {
        var globalRoot = Path.Combine(_catalogOptions.GlobalRoot, "plugins");
        if (Directory.Exists(globalRoot))
        {
            foreach (var package in _sourceDiscovery.Discover(new PluginRoot { RootPath = globalRoot, Scope = PluginScope.Global }))
            {
                yield return package;
            }
        }

        if (selectedProject is null)
        {
            yield break;
        }

        var projectRoot = Path.Combine(selectedProject.ProjectPath, ".alta", "plugins");
        if (!Directory.Exists(projectRoot))
        {
            yield break;
        }

        foreach (var package in _sourceDiscovery.Discover(new PluginRoot
        {
            RootPath = projectRoot,
            Scope = PluginScope.Project,
            ProjectId = selectedProject.Id,
            ProjectPath = selectedProject.ProjectPath,
        }))
        {
            yield return package;
        }
    }
}

internal sealed record PluginManagementSnapshot(
    IReadOnlyList<PluginManagementEntry> Entries,
    bool SafeMode,
    string? ProjectPath);
