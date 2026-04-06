namespace CodeAlta.Acp;

/// <summary>
/// Options used to start an ACP agent process.
/// </summary>
public sealed class AcpProcessOptions
{
    /// <summary>
    /// Gets or sets the executable or command to start.
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// Gets or sets the command-line arguments.
    /// </summary>
    public IReadOnlyList<string>? Arguments { get; init; }

    /// <summary>
    /// Gets or sets the working directory.
    /// </summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// Gets or sets additional environment variables.
    /// </summary>
    public IReadOnlyDictionary<string, string>? EnvironmentVariables { get; init; }
}
