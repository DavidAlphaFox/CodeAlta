using System.Diagnostics;
using System.IO.Compression;

namespace CodeAlta.Acp;

/// <summary>
/// Materializes resolved ACP install plans into runnable local definitions.
/// </summary>
public sealed class AcpInstaller : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="AcpInstaller"/> class.
    /// </summary>
    /// <param name="httpClient">Optional HTTP client.</param>
    public AcpInstaller(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;
    }

    /// <summary>
    /// Installs or resolves an ACP agent.
    /// </summary>
    /// <param name="plan">Install plan.</param>
    /// <param name="downloadsRoot">Download cache root.</param>
    /// <param name="installsRoot">Install root.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resolved local install definition.</returns>
    public async Task<AcpResolvedInstall> InstallAsync(
        AcpInstallPlan plan,
        string downloadsRoot,
        string installsRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(downloadsRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(installsRoot);

        return plan.Kind switch
        {
            AcpInstallKind.Binary => await InstallBinaryAsync(plan, downloadsRoot, installsRoot, cancellationToken).ConfigureAwait(false),
            AcpInstallKind.Npx or AcpInstallKind.Uvx => ResolvePackageInstall(plan),
            _ => throw new NotSupportedException($"Unsupported ACP install kind '{plan.Kind}'."),
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private static AcpResolvedInstall ResolvePackageInstall(AcpInstallPlan plan)
    {
        return new AcpResolvedInstall
        {
            Manifest = plan.Manifest,
            Kind = plan.Kind,
            Command = plan.Command,
            Arguments = plan.Arguments,
            EnvironmentVariables = plan.EnvironmentVariables,
        };
    }

    private async Task<AcpResolvedInstall> InstallBinaryAsync(
        AcpInstallPlan plan,
        string downloadsRoot,
        string installsRoot,
        CancellationToken cancellationToken)
    {
        if (plan.ArchiveUri is null)
        {
            throw new InvalidOperationException("Binary install plans require an archive URL.");
        }

        var installRoot = Path.Combine(installsRoot, plan.Manifest.Id, plan.Manifest.Version);
        var archiveFileName = Path.GetFileName(plan.ArchiveUri.LocalPath);
        var downloadPath = Path.Combine(downloadsRoot, plan.Manifest.Id, plan.Manifest.Version, archiveFileName);

        Directory.CreateDirectory(Path.GetDirectoryName(downloadPath)!);
        Directory.CreateDirectory(installRoot);

        await DownloadFileAsync(plan.ArchiveUri, downloadPath, cancellationToken).ConfigureAwait(false);
        ExtractArchive(downloadPath, installRoot);

        var commandPath = ResolveInstalledCommandPath(installRoot, plan.RelativeCommandPath ?? plan.Command);
        return new AcpResolvedInstall
        {
            Manifest = plan.Manifest,
            Kind = plan.Kind,
            Command = commandPath,
            Arguments = plan.Arguments,
            EnvironmentVariables = plan.EnvironmentVariables,
            WorkingDirectory = installRoot,
            InstallRoot = installRoot,
        };
    }

    private async Task DownloadFileAsync(Uri uri, string path, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var destination = File.Create(path);
        await responseStream.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
    }

    private static string ResolveInstalledCommandPath(string installRoot, string relativeCommandPath)
    {
        if (Path.IsPathRooted(relativeCommandPath))
        {
            return relativeCommandPath;
        }

        var normalized = relativeCommandPath.Trim().Replace('/', Path.DirectorySeparatorChar);
        while (normalized.StartsWith($".{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            normalized = normalized[(2)..];
        }

        return Path.GetFullPath(Path.Combine(installRoot, normalized));
    }

    private static void ExtractArchive(string archivePath, string destinationDirectory)
    {
        if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(archivePath, destinationDirectory, overwriteFiles: true);
            return;
        }

        var tarCommand = AcpCommandLocator.FindCommandPath("tar")
            ?? throw new InvalidOperationException("Extracting ACP binary archives requires 'tar' to be available on PATH.");
        var startInfo = new ProcessStartInfo
        {
            FileName = tarCommand,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-xf");
        startInfo.ArgumentList.Add(archivePath);
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(destinationDirectory);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start archive extractor '{tarCommand}'.");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd().Trim();
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(error)
                    ? $"Failed to extract archive '{archivePath}'."
                    : $"Failed to extract archive '{archivePath}': {error}");
        }
    }
}
