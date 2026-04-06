using System.Text.Json;

namespace CodeAlta.Acp;

/// <summary>
/// Downloads and caches ACP registry metadata.
/// </summary>
public sealed class AcpRegistryClient : IDisposable
{
    /// <summary>
    /// Gets the public ACP registry endpoint.
    /// </summary>
    public static Uri DefaultRegistryUri { get; } = new("https://cdn.agentclientprotocol.com/registry/v1/latest/registry.json");

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="AcpRegistryClient"/> class.
    /// </summary>
    /// <param name="httpClient">Optional HTTP client.</param>
    public AcpRegistryClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;
    }

    /// <summary>
    /// Downloads the latest ACP registry document.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The registry document.</returns>
    public Task<AcpRegistryDocument> DownloadLatestAsync(CancellationToken cancellationToken = default)
        => DownloadAsync(DefaultRegistryUri, cancellationToken);

    /// <summary>
    /// Downloads an ACP registry document from a URL.
    /// </summary>
    /// <param name="registryUri">Registry URL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The registry document.</returns>
    public async Task<AcpRegistryDocument> DownloadAsync(Uri registryUri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registryUri);

        using var response = await _httpClient.GetAsync(registryUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var document = await JsonSerializer.DeserializeAsync(
                stream,
                AcpRegistryJsonSerializerContext.Default.AcpRegistryDocument,
                cancellationToken)
            .ConfigureAwait(false);
        return document ?? throw new InvalidDataException($"Registry payload '{registryUri}' was empty.");
    }

    /// <summary>
    /// Loads a cached ACP registry document from disk.
    /// </summary>
    /// <param name="path">Cache path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The registry document.</returns>
    public async Task<AcpRegistryDocument> LoadFromFileAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using var stream = File.OpenRead(path);
        var document = await JsonSerializer.DeserializeAsync(
                stream,
                AcpRegistryJsonSerializerContext.Default.AcpRegistryDocument,
                cancellationToken)
            .ConfigureAwait(false);
        return document ?? throw new InvalidDataException($"Registry cache '{path}' was empty.");
    }

    /// <summary>
    /// Saves a registry document to disk.
    /// </summary>
    /// <param name="path">Target path.</param>
    /// <param name="document">Registry document.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task SaveToFileAsync(
        string path,
        AcpRegistryDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(document);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(
                stream,
                document,
                AcpRegistryJsonSerializerContext.Default.AcpRegistryDocument,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
