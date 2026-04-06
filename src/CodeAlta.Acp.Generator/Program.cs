using CodeAlta.Acp.Generator;
using XenoAtom.Terminal;

using var session = Terminal.Open();
var app = GeneratorCliOptions.CreateCommandApp(ExecuteAsync);
return await app.RunAsync(args).ConfigureAwait(false);

async ValueTask<int> ExecuteAsync(GeneratorCliOptions options)
{
    ArgumentNullException.ThrowIfNull(options);

    var sourceInfo = await AcpSchemaSourceResolver.ResolveAsync(options, CancellationToken.None).ConfigureAwait(false);
    var outputDir = options.OutputDir is { Length: > 0 }
        ? Path.GetFullPath(options.OutputDir)
        : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "CodeAlta.Acp", "generated"));
    var methodMap = AcpMethodMap.Load(sourceInfo.MetaPath);

    Terminal.WriteLine($"Source:    {sourceInfo.SourceDisplayName}");
    Terminal.WriteLine($"Surface:   {options.Surface}");
    Terminal.WriteLine($"Schema:    {sourceInfo.SchemaPath}");
    Terminal.WriteLine($"Meta:      {sourceInfo.MetaPath}");
    Terminal.WriteLine($"Output:    {outputDir}");
    Terminal.WriteLine($"Namespace: {options.RootNamespace}");
    Terminal.WriteLine();

    var defs = await SchemaWalker.LoadDefinitionsAsync(sourceInfo.SchemaPath, options.RootNamespace).ConfigureAwait(false);
    var emitter = new CSharpEmitter(defs, options.RootNamespace);
    var filesByNamespace = emitter.EmitAll(defs);

    await OutputDirectoryCleaner.CleanAsync(outputDir).ConfigureAwait(false);
    Directory.CreateDirectory(outputDir);

    var totalFiles = 0;
    foreach (var (ns, files) in filesByNamespace)
    {
        var relPath = ns == options.RootNamespace
            ? string.Empty
            : ns[(options.RootNamespace.Length + 1)..].Replace('.', Path.DirectorySeparatorChar);
        var dir = Path.Combine(outputDir, relPath);
        Directory.CreateDirectory(dir);

        foreach (var (fileName, content) in files)
        {
            await File.WriteAllTextAsync(Path.Combine(dir, fileName), content).ConfigureAwait(false);
            totalFiles++;
        }
    }

    var contextCode = emitter.EmitSerializerContext("AcpJsonSerializerContext");
    await File.WriteAllTextAsync(Path.Combine(outputDir, "AcpJsonSerializerContext.gen.cs"), contextCode).ConfigureAwait(false);
    totalFiles++;

    var methodCode = AcpMethodEmitter.EmitClientMethods(defs, methodMap, options.RootNamespace);
    await File.WriteAllTextAsync(Path.Combine(outputDir, "AcpClient.Methods.gen.cs"), methodCode).ConfigureAwait(false);
    totalFiles++;

    var metadataCode = AcpMethodEmitter.EmitMetadataPartial(options.RootNamespace, sourceInfo, options.Surface, methodMap.Version);
    await File.WriteAllTextAsync(Path.Combine(outputDir, "AcpClient.Metadata.gen.cs"), metadataCode).ConfigureAwait(false);
    totalFiles++;

    Terminal.WriteLine($"Generated {totalFiles} files.");

    if (emitter.Warnings.Count > 0)
    {
        Terminal.WriteLine();
        Terminal.WriteLine($"Warnings ({emitter.Warnings.Count}):");
        foreach (var warning in emitter.Warnings)
        {
            Terminal.WriteLine($"  WARN: {warning}");
        }
    }

    return 0;
}
