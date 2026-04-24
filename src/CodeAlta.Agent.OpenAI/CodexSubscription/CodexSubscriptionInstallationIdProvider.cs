namespace CodeAlta.Agent.OpenAI.CodexSubscription;

internal sealed class CodexSubscriptionInstallationIdProvider
{
    private readonly string _stateRootPath;
    private readonly string? _codexHome;

    public CodexSubscriptionInstallationIdProvider(string stateRootPath, string? codexHome = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateRootPath);
        _stateRootPath = stateRootPath;
        _codexHome = codexHome;
    }

    public async ValueTask<string?> ResolveAsync(
        bool sendInstallationId,
        string installationIdSource,
        CancellationToken cancellationToken = default)
    {
        if (!sendInstallationId)
        {
            return null;
        }

        return installationIdSource switch
        {
            "codex_home_import" => await ImportCodexHomeIdAsync(cancellationToken).ConfigureAwait(false),
            "codex_home_readonly" => await ReadCodexHomeIdAsync(cancellationToken).ConfigureAwait(false)
                ?? await GetOrCreateCodeAltaIdAsync(cancellationToken).ConfigureAwait(false),
            _ => await GetOrCreateCodeAltaIdAsync(cancellationToken).ConfigureAwait(false),
        };
    }

    private async ValueTask<string> ImportCodexHomeIdAsync(CancellationToken cancellationToken)
    {
        var codexId = await ReadCodexHomeIdAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(codexId))
        {
            await WriteCodeAltaIdAsync(codexId, cancellationToken).ConfigureAwait(false);
            return codexId;
        }

        return await GetOrCreateCodeAltaIdAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<string> GetOrCreateCodeAltaIdAsync(CancellationToken cancellationToken)
    {
        var path = GetCodeAltaInstallationIdPath();
        if (File.Exists(path))
        {
            var existing = (await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)).Trim();
            if (Guid.TryParse(existing, out var parsed))
            {
                return parsed.ToString();
            }
        }

        var generated = Guid.NewGuid().ToString();
        await WriteCodeAltaIdAsync(generated, cancellationToken).ConfigureAwait(false);
        return generated;
    }

    private async ValueTask WriteCodeAltaIdAsync(string installationId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(installationId, out var parsed))
        {
            throw new ArgumentException("Installation id must be a UUID.", nameof(installationId));
        }

        var path = GetCodeAltaInstallationIdPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, parsed.ToString(), cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<string?> ReadCodexHomeIdAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_codexHome))
        {
            return null;
        }

        var path = Path.Combine(_codexHome, "installation_id");
        if (!File.Exists(path))
        {
            return null;
        }

        var text = (await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)).Trim();
        return Guid.TryParse(text, out var parsed) ? parsed.ToString() : null;
    }

    private string GetCodeAltaInstallationIdPath()
        => Path.Combine(_stateRootPath, "installation", "openai-codex-subscription", "installation_id");
}
