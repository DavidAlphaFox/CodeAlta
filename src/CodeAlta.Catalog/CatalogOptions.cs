namespace CodeAlta.Catalog;

/// <summary>
/// Options describing the global CodeAlta catalog layout.
/// </summary>
public sealed class CatalogOptions
{
    /// <summary>
    /// Gets or sets the path to the portable CodeAlta root.
    /// </summary>
    public string GlobalRoot { get; set; } = string.Empty;

    /// <summary>
    /// Gets the default checkout root path under the global catalog.
    /// </summary>
    public string CheckoutsRoot => Path.Combine(GlobalRoot, "checkouts");

    /// <summary>
    /// Gets the projects root path under the global catalog.
    /// </summary>
    public string ProjectsRoot => Path.Combine(GlobalRoot, "projects");

    /// <summary>
    /// Gets the machine configuration root path under the global catalog.
    /// </summary>
    public string MachinesRoot => Path.Combine(GlobalRoot, "machines");

    /// <summary>
    /// Gets the agents root path under the global catalog.
    /// </summary>
    public string AgentsRoot => Path.Combine(GlobalRoot, "agents");

    /// <summary>
    /// Gets the global user configuration path.
    /// </summary>
    public string ConfigPath => Path.Combine(GlobalRoot, "config.toml");

    /// <summary>
    /// Gets the machine-local runtime root path under the global catalog.
    /// </summary>
    public string MachineRoot => Path.Combine(GlobalRoot, "machine");

    /// <summary>
    /// Gets the internal thread linkage root path under the global catalog.
    /// </summary>
    public string InternalThreadsRoot => Path.Combine(GlobalRoot, "threads", "internal");
}
