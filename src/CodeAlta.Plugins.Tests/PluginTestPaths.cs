namespace CodeAlta.Plugins.Tests;

internal static class PluginTestPaths
{
    public static string SourceRoot { get; } = FindSourceRoot();

    public static string DirectoryPackagesPropsPath
        => Path.Combine(SourceRoot, "Directory.Packages.props");

    public static string PluginRuntimeSampleRoot
        => Path.Combine(SourceRoot, "CodeAlta.Catalog", "BuiltinSkills", "codealta-plugin-runtime", "samples");

    private static string FindSourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidates = new[]
            {
                directory.FullName,
                Path.Combine(directory.FullName, "src"),
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(Path.Combine(candidate, "Directory.Packages.props")) &&
                    Directory.Exists(Path.Combine(candidate, "CodeAlta.Catalog")))
                {
                    return candidate;
                }
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the CodeAlta source root from the test output path.");
    }
}
