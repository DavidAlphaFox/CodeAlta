namespace CodeAlta.Catalog;

public static class ProjectFilePathUtilities
{
    public static string NormalizeProjectRoot(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            throw new ArgumentException("Project root is required.", nameof(projectRoot));
        }

        return Path.GetFullPath(projectRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public static string NormalizeLookupPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path is required.", nameof(path));
        }

        var trimmed = path.Trim().Trim('"').Trim();
        trimmed = trimmed.Replace('\\', '/');
        if (trimmed.StartsWith('@'))
        {
            trimmed = trimmed[1..];
        }

        var prefersDirectory = trimmed.EndsWith("/", StringComparison.Ordinal);
        var segments = trimmed
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();

        if (segments.Length == 0)
        {
            throw new ArgumentException("Path must contain at least one segment.", nameof(path));
        }

        if (segments.Any(static segment => segment is "." or ".."))
        {
            throw new ArgumentException("Relative parent segments are not supported.", nameof(path));
        }

        var normalized = string.Join('/', segments);
        return prefersDirectory ? $"{normalized}/" : normalized;
    }

    public static string NormalizeStoredRelativePath(string path)
    {
        var normalized = NormalizeLookupPath(path);
        return normalized.TrimEnd('/');
    }

    public static ProjectFileSearchFields CreateSearchFields(string relativePath, string basename, string extension)
    {
        var normalizedRelativePath = NormalizeStoredRelativePath(relativePath);
        return new ProjectFileSearchFields(
            basename.ToLowerInvariant(),
            normalizedRelativePath.ToLowerInvariant(),
            normalizedRelativePath.Split('/').Select(static segment => segment.ToLowerInvariant()).ToArray(),
            extension.ToLowerInvariant());
    }

    public static ProjectFileSearchItem CreateSeedItem(string projectRoot, ProjectFileUsageEntry usage)
    {
        var relativePath = NormalizeStoredRelativePath(usage.RelativePath);
        var basename = Path.GetFileName(relativePath);
        var parentPath = Path.GetDirectoryName(relativePath)?.Replace('\\', '/') ?? string.Empty;
        var fullPath = BuildFullPath(projectRoot, relativePath);
        var extension = usage.Kind == ProjectFileSearchItemKind.Directory ? string.Empty : Path.GetExtension(basename);

        return new ProjectFileSearchItem
        {
            Kind = usage.Kind,
            ProjectRoot = projectRoot,
            RelativePath = relativePath,
            FullPath = fullPath,
            Basename = basename,
            ParentPath = parentPath,
            Extension = extension,
            LastWriteTimeUtc = TryGetLastWriteTimeUtc(fullPath, usage.Kind),
            SearchFields = CreateSearchFields(relativePath, basename, extension),
            Usage = usage,
        };
    }

    public static string BuildFullPath(string projectRoot, string relativePath)
    {
        return Path.Combine(
            NormalizeProjectRoot(projectRoot),
            NormalizeStoredRelativePath(relativePath).Replace('/', Path.DirectorySeparatorChar));
    }

    public static DateTimeOffset? TryGetLastWriteTimeUtc(string fullPath, ProjectFileSearchItemKind kind)
    {
        try
        {
            return kind == ProjectFileSearchItemKind.Directory
                ? new DateTimeOffset(Directory.GetLastWriteTimeUtc(fullPath), TimeSpan.Zero)
                : new DateTimeOffset(File.GetLastWriteTimeUtc(fullPath), TimeSpan.Zero);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
