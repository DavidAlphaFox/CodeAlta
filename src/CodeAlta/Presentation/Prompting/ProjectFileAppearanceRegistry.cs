using CodeAlta.Presentation.Styling;
using CodeAlta.Search;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Styling;

namespace CodeAlta.Presentation.Prompting;

internal sealed class ProjectFileAppearanceRegistry : IProjectFileAppearanceRegistry
{
    private static readonly ProjectFileAppearanceDescriptor DefaultDirectory = new(
        $"{NerdFont.MdFolderOutline}",
        Color.FromOklch(0.82f, 0.09f, 85f),
        "directory");

    private static readonly ProjectFileAppearanceDescriptor DefaultFile = new(
        $"{NerdFont.MdFileDocumentOutline}",
        Color.FromOklch(0.77f, 0.02f, 255f),
        "file");

    private static readonly IReadOnlyDictionary<string, ProjectFileAppearanceDescriptor> BuiltInExtensions =
        new Dictionary<string, ProjectFileAppearanceDescriptor>(StringComparer.OrdinalIgnoreCase)
        {
            [".cs"] = new($"{NerdFont.MdLanguageCsharp}", Color.FromOklch(0.79f, 0.09f, 245f), "csharp"),
            [".csproj"] = new($"{NerdFont.MdLanguageCsharp}", Color.FromOklch(0.74f, 0.10f, 220f), "dotnet"),
            [".sln"] = new($"{NerdFont.MdCodeBracesBox}", Color.FromOklch(0.70f, 0.16f, 300f), "dotnet"),
            [".json"] = new($"{NerdFont.MdCodeJson}", Color.FromOklch(0.82f, 0.11f, 85f), "json"),
            [".yml"] = new($"{NerdFont.MdFileCodeOutline}", Color.FromOklch(0.78f, 0.07f, 165f), "yaml"),
            [".yaml"] = new($"{NerdFont.MdFileCodeOutline}", Color.FromOklch(0.78f, 0.07f, 165f), "yaml"),
            [".md"] = new($"{NerdFont.MdLanguageMarkdown}", Color.FromOklch(0.83f, 0.04f, 225f), "markdown"),
            [".ts"] = new($"{NerdFont.MdLanguageTypescript}", Color.FromOklch(0.77f, 0.09f, 245f), "typescript"),
            [".tsx"] = new($"{NerdFont.MdLanguageTypescript}", Color.FromOklch(0.73f, 0.10f, 205f), "typescript"),
            [".js"] = new($"{NerdFont.MdLanguageJavascript}", Color.FromOklch(0.87f, 0.12f, 100f), "javascript"),
            [".jsx"] = new($"{NerdFont.MdLanguageJavascript}", Color.FromOklch(0.79f, 0.10f, 205f), "javascript"),
            [".py"] = new($"{NerdFont.MdLanguagePython}", Color.FromOklch(0.79f, 0.08f, 235f), "python"),
            [".go"] = new($"{NerdFont.MdLanguageGo}", Color.FromOklch(0.81f, 0.08f, 220f), "go"),
            [".rs"] = new($"{NerdFont.MdLanguageRust}", Color.FromOklch(0.73f, 0.13f, 45f), "rust"),
            [".java"] = new($"{NerdFont.MdLanguageJava}", Color.FromOklch(0.72f, 0.13f, 35f), "java"),
            [".kt"] = new($"{NerdFont.MdLanguageKotlin}", Color.FromOklch(0.74f, 0.16f, 310f), "kotlin"),
            [".cpp"] = new($"{NerdFont.MdLanguageCpp}", Color.FromOklch(0.76f, 0.10f, 235f), "cpp"),
            [".h"] = new($"{NerdFont.MdLanguageCpp}", Color.FromOklch(0.70f, 0.05f, 235f), "cpp"),
            [".html"] = new($"{NerdFont.MdLanguageHtml5}", Color.FromOklch(0.76f, 0.13f, 35f), "html"),
            [".css"] = new($"{NerdFont.MdLanguageCss3}", Color.FromOklch(0.76f, 0.09f, 245f), "css"),
            [".scss"] = new($"{NerdFont.MdLanguageCss3}", Color.FromOklch(0.78f, 0.10f, 350f), "scss"),
            [".toml"] = new($"{NerdFont.MdCodeBraces}", Color.FromOklch(0.80f, 0.05f, 210f), "toml"),
            [".xml"] = new($"{NerdFont.MdXml}", Color.FromOklch(0.79f, 0.05f, 210f), "xml"),
            [".sh"] = new($"{NerdFont.MdBash}", Color.FromOklch(0.78f, 0.07f, 145f), "shell"),
            [".ps1"] = new($"{NerdFont.MdPowershell}", Color.FromOklch(0.75f, 0.07f, 235f), "powershell"),
            [".sql"] = new($"{NerdFont.MdDatabase}", Color.FromOklch(0.77f, 0.07f, 170f), "sql"),
        };

    private static readonly IReadOnlyDictionary<string, ProjectFileAppearanceDescriptor> BuiltInFiles =
        new Dictionary<string, ProjectFileAppearanceDescriptor>(StringComparer.OrdinalIgnoreCase)
        {
            ["dockerfile"] = new($"{NerdFont.MdDocker}", Color.FromOklch(0.77f, 0.09f, 225f), "docker"),
        };

    public static ProjectFileAppearanceRegistry Default { get; } = new();

    private readonly IReadOnlyList<IProjectFileAppearanceContribution> _contributions;
    private readonly ProjectFileAppearanceConfig? _userOverrides;

    public ProjectFileAppearanceRegistry(
        IEnumerable<IProjectFileAppearanceContribution>? contributions = null,
        ProjectFileAppearanceConfig? userOverrides = null)
    {
        _contributions = contributions?.ToArray() ?? [];
        _userOverrides = userOverrides;
    }

    public ProjectFileAppearance GetAppearance(ProjectFileSearchItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var descriptor = ResolveDescriptor(item) ?? DefaultFile;
        return new ProjectFileAppearance(
            descriptor.Icon,
            descriptor.Foreground,
            Style.None,
            descriptor.Category);
    }

    public ProjectFileAppearance GetDirectoryAppearance()
    {
        var descriptor = ResolveDirectoryDescriptor();
        return new ProjectFileAppearance(
            descriptor.Icon,
            descriptor.Foreground,
            Style.None,
            descriptor.Category);
    }

    private ProjectFileAppearanceDescriptor? ResolveDescriptor(ProjectFileSearchItem item)
    {
        if (item.Kind == ProjectFileSearchItemKind.Directory)
        {
            return ResolveDirectoryDescriptor();
        }

        var descriptor = ResolveFromMaps(BuiltInFiles, BuiltInExtensions, item.Basename, item.Extension) ?? DefaultFile;
        foreach (var contribution in _contributions)
        {
            descriptor = ResolveFromMaps(contribution.Files, contribution.Extensions, item.Basename, item.Extension) ?? descriptor;
        }

        return ResolveFromMaps(_userOverrides?.Files, _userOverrides?.Extensions, item.Basename, item.Extension) ?? descriptor;
    }

    private ProjectFileAppearanceDescriptor ResolveDirectoryDescriptor()
    {
        var descriptor = DefaultDirectory;
        foreach (var contribution in _contributions)
        {
            descriptor = contribution.Directory ?? descriptor;
        }

        return _userOverrides?.Directory ?? descriptor;
    }

    private static ProjectFileAppearanceDescriptor? ResolveFromMaps(
        IReadOnlyDictionary<string, ProjectFileAppearanceDescriptor>? fileMap,
        IReadOnlyDictionary<string, ProjectFileAppearanceDescriptor>? extensionMap,
        string basename,
        string extension)
    {
        if (fileMap is not null && fileMap.TryGetValue(basename, out var fileDescriptor))
        {
            return fileDescriptor;
        }

        if (extensionMap is not null &&
            !string.IsNullOrWhiteSpace(extension) &&
            extensionMap.TryGetValue(extension, out var extensionDescriptor))
        {
            return extensionDescriptor;
        }

        return null;
    }
}
