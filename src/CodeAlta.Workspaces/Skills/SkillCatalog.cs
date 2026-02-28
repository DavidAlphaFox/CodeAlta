namespace CodeAlta.Workspaces.Skills;

/// <summary>
/// Discovers and reads skills stored on disk.
/// </summary>
public sealed class SkillCatalog
{
    /// <summary>
    /// Lists discovered skills under the provided root directories.
    /// </summary>
    /// <param name="roots">Root directories that contain skill folders (each with a <c>SKILL.md</c> file).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Discovered skills.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="roots"/> is <see langword="null"/>.</exception>
    public async Task<IReadOnlyList<SkillInfo>> ListAsync(
        IReadOnlyList<string> roots,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roots);

        var skills = new List<SkillInfo>();
        foreach (var root in roots.Where(static x => !string.IsNullOrWhiteSpace(x)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalized = Path.GetFullPath(root);
            if (!Directory.Exists(normalized))
            {
                continue;
            }

            var directSkillPath = Path.Combine(normalized, "SKILL.md");
            if (File.Exists(directSkillPath))
            {
                var info = await TryLoadAsync(normalized, cancellationToken).ConfigureAwait(false);
                if (info is not null)
                {
                    skills.Add(info);
                }

                continue;
            }

            foreach (var directory in Directory.EnumerateDirectories(normalized))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var info = await TryLoadAsync(directory, cancellationToken).ConfigureAwait(false);
                if (info is not null)
                {
                    skills.Add(info);
                }
            }
        }

        return skills
            .GroupBy(static x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static x => x.First())
            .OrderBy(static x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Reads the <c>SKILL.md</c> contents for a discovered skill.
    /// </summary>
    /// <param name="roots">Roots used for discovery.</param>
    /// <param name="skillName">Skill name (folder name).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The skill document when found; otherwise <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="roots"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="skillName"/> is empty.</exception>
    public async Task<SkillDocument?> GetAsync(
        IReadOnlyList<string> roots,
        string skillName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roots);
        if (string.IsNullOrWhiteSpace(skillName))
        {
            throw new ArgumentException("Skill name is required.", nameof(skillName));
        }

        var skills = await ListAsync(roots, cancellationToken).ConfigureAwait(false);
        var match = skills.FirstOrDefault(x => string.Equals(x.Name, skillName, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return null;
        }

        var skillPath = Path.Combine(match.Path, "SKILL.md");
        var content = await File.ReadAllTextAsync(skillPath, cancellationToken).ConfigureAwait(false);
        return new SkillDocument
        {
            Name = match.Name,
            Path = match.Path,
            Content = content,
        };
    }

    /// <summary>
    /// Reads a resource file under a discovered skill directory.
    /// </summary>
    /// <param name="roots">Roots used for discovery.</param>
    /// <param name="skillName">Skill name (folder name).</param>
    /// <param name="relativePath">Relative path under the skill directory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>File bytes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="roots"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when required arguments are empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the skill cannot be found.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the resource does not exist.</exception>
    public async Task<byte[]> GetResourceAsync(
        IReadOnlyList<string> roots,
        string skillName,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roots);
        if (string.IsNullOrWhiteSpace(skillName))
        {
            throw new ArgumentException("Skill name is required.", nameof(skillName));
        }

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Relative path is required.", nameof(relativePath));
        }

        if (relativePath.Contains("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException("Relative path must be a safe, non-rooted path.", nameof(relativePath));
        }

        var skills = await ListAsync(roots, cancellationToken).ConfigureAwait(false);
        var match = skills.FirstOrDefault(x => string.Equals(x.Name, skillName, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            throw new InvalidOperationException($"Skill '{skillName}' was not found.");
        }

        var resourcePath = Path.GetFullPath(Path.Combine(match.Path, relativePath));
        if (!resourcePath.StartsWith(match.Path, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Relative path must not escape the skill directory.", nameof(relativePath));
        }

        if (!File.Exists(resourcePath))
        {
            throw new FileNotFoundException("Skill resource was not found.", resourcePath);
        }

        return await File.ReadAllBytesAsync(resourcePath, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<SkillInfo?> TryLoadAsync(string directory, CancellationToken cancellationToken)
    {
        var normalized = Path.GetFullPath(directory);
        var skillFile = Path.Combine(normalized, "SKILL.md");
        if (!File.Exists(skillFile))
        {
            return null;
        }

        var content = await File.ReadAllTextAsync(skillFile, cancellationToken).ConfigureAwait(false);
        var (title, description) = ParseTitleAndDescription(content);
        var name = Path.GetFileName(normalized);

        return new SkillInfo
        {
            Name = name,
            Title = title ?? name,
            Description = description ?? $"{name} skill.",
            Path = normalized,
        };
    }

    private static (string? Title, string? Description) ParseTitleAndDescription(string markdown)
    {
        var normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n');

        string? title = null;
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("# ", StringComparison.Ordinal))
            {
                title = trimmed[2..].Trim();
                break;
            }
        }

        string? description = null;
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            description = trimmed;
            break;
        }

        return (title, description);
    }
}

