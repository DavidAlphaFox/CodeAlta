using CodeAlta.Agent.ModelCatalog;

var repositoryRoot = FindRepositoryRoot(Environment.CurrentDirectory)
    ?? FindRepositoryRoot(AppContext.BaseDirectory)
    ?? throw new InvalidOperationException("Failed to locate the repository root containing CodeAlta.slnx.");

var defaultOutputPath = Path.Combine(repositoryRoot, "CodeAlta.Agent", "Data", "models_dev_db.json");
var outputPath = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0])
    ? Path.GetFullPath(args[0])
    : defaultOutputPath;
var sourceUri = args.Length > 1 && Uri.TryCreate(args[1], UriKind.Absolute, out var resolvedUri)
    ? resolvedUri
    : new Uri("https://models.dev/api.json", UriKind.Absolute);

Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

using var httpClient = new HttpClient();
using var stream = await httpClient.GetStreamAsync(sourceUri).ConfigureAwait(false);
using var memory = new MemoryStream();
await stream.CopyToAsync(memory).ConfigureAwait(false);
memory.Position = 0;

var database = ModelsDevDatabaseJson.Deserialize(memory);
var bytes = ModelsDevDatabaseJson.SerializeUtf8(database);
await File.WriteAllBytesAsync(outputPath, bytes).ConfigureAwait(false);

Console.WriteLine($"Downloaded models.dev snapshot from {sourceUri}");
Console.WriteLine($"Providers: {database.Providers.Count}");
Console.WriteLine($"Output:    {outputPath}");

static string? FindRepositoryRoot(string? startPath)
{
    if (string.IsNullOrWhiteSpace(startPath))
    {
        return null;
    }

    var current = Directory.Exists(startPath)
        ? new DirectoryInfo(startPath)
        : new DirectoryInfo(Path.GetDirectoryName(startPath)!);
    while (current is not null)
    {
        if (File.Exists(Path.Combine(current.FullName, "CodeAlta.slnx")))
        {
            return current.FullName;
        }

        current = current.Parent;
    }

    return null;
}
