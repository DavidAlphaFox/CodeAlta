using SharpYaml.Serialization;

namespace CodeAlta.Workspaces;

/// <summary>
/// Defines checkout behavior for a project.
/// </summary>
public sealed class CheckoutRule
{
    /// <summary>
    /// Gets or sets the path template.
    /// </summary>
    [YamlMember("path_template")]
    public string PathTemplate { get; set; } = "{workspaceKey}\\{projectKey}";

    /// <summary>
    /// Gets or sets the optional clone depth.
    /// </summary>
    [YamlMember("depth")]
    public int? Depth { get; set; }

    /// <summary>
    /// Gets or sets whether submodules are enabled.
    /// </summary>
    [YamlMember("submodules")]
    public bool? Submodules { get; set; }
}
