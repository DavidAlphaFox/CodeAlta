using System.Diagnostics.CodeAnalysis;
using XenoAtom.CommandLine;
using XenoAtom.CommandLine.Terminal;

namespace CodeAlta.CodexSdk.Generator;

internal sealed class GeneratorCliOptions
{
    private const string DefaultRootNamespace = "CodeAlta.CodexSdk";

    public GeneratorCliOptions(
        string? schemaFile,
        string rootNamespace,
        SchemaBundle schemaBundle,
        bool includeExperimentalApi)
    {
        SchemaFile = schemaFile;
        RootNamespace = rootNamespace;
        SchemaBundle = schemaBundle;
        IncludeExperimentalApi = includeExperimentalApi;
    }

    public string? SchemaFile { get; }

    public string RootNamespace { get; }

    public SchemaBundle SchemaBundle { get; }

    public bool IncludeExperimentalApi { get; }

    public static CommandApp CreateCommandApp(Func<GeneratorCliOptions, ValueTask<int>> execute)
    {
        ArgumentNullException.ThrowIfNull(execute);

        return CreateCommandAppCore(
            new ParseState(),
            execute);
    }

    private static CommandApp CreateCommandAppCore(
        ParseState state,
        Func<GeneratorCliOptions, ValueTask<int>> execute)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(execute);

        const string _ = "";

        return new CommandApp(
            "CodeAlta.CodexSdk.Generator",
            config: new CommandConfig
            {
                OutputFactory = static _ => new TerminalVisualCommandOutput(new TerminalVisualOutputOptions
                {
                    UseTableForOptions = true,
                    SectionGroupMinWidth = 70,
                    ErrorGroupMinWidth = 70,
                }),
            })
        {
            new CommandUsage(),
            _,
            "Options:",
            { "s|schema=", "Existing schema {FILE} to use instead of generating one", value => state.SchemaFile = value },
            { "n|namespace=", "Root {NAMESPACE} for generated types", value => state.RootNamespace = value },
            { "b|bundle=", "Schema {BUNDLE}: auto, mixed, or v2", value => state.SchemaBundle = value },
            { "experimental", "Include the experimental Codex API surface", value => state.IncludeExperimentalApi = value is not null },
            new HelpOption(),
            (_, _) =>
            {
                if (!TryCreateOptions(state, out var options, out var error))
                {
                    throw new CommandException(error!);
                }

                return execute(options!);
            },
        };
    }

    private static bool TryCreateOptions(
        ParseState state,
        [NotNullWhen(true)] out GeneratorCliOptions? options,
        [NotNullWhen(false)] out string? error)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!TryParseSchemaBundle(state.SchemaBundle, out var schemaBundle, out error))
        {
            options = null;
            return false;
        }

        options = new GeneratorCliOptions(
            state.SchemaFile,
            string.IsNullOrWhiteSpace(state.RootNamespace) ? DefaultRootNamespace : state.RootNamespace,
            schemaBundle,
            state.IncludeExperimentalApi);
        error = null;
        return true;
    }

    private static bool TryParseSchemaBundle(
        string? value,
        out SchemaBundle schemaBundle,
        [NotNullWhen(false)] out string? error)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            schemaBundle = SchemaBundle.V2;
            error = null;
            return true;
        }

        switch (normalized.ToLowerInvariant())
        {
            case "auto":
                schemaBundle = SchemaBundle.Auto;
                error = null;
                return true;
            case "mixed":
            case "schemas":
                schemaBundle = SchemaBundle.Mixed;
                error = null;
                return true;
            case "v2":
            case "flatv2":
            case "v2flat":
                schemaBundle = SchemaBundle.V2;
                error = null;
                return true;
            default:
                schemaBundle = default;
                error = $"Unknown --bundle value: '{value}'. Expected: auto | mixed | v2.";
                return false;
        }
    }

    private sealed class ParseState
    {
        public string? SchemaFile { get; set; }

        public string? RootNamespace { get; set; }

        public string? SchemaBundle { get; set; }

        public bool IncludeExperimentalApi { get; set; }
    }
}
