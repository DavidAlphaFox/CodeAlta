using System.Reflection;
using System.Text.Json;
using NuGet.Versioning;

namespace CodeAlta.Views;

internal sealed record CodeAltaNuGetUpdateCheckResult(
    string PackageId,
    NuGetVersion CurrentVersion,
    NuGetVersion? LatestVersion,
    bool PackageFound,
    bool HasNewerVersion,
    bool IncludePrerelease)
{
    public string CurrentVersionText => CurrentVersion.ToNormalizedString();

    public string? LatestVersionText => LatestVersion?.ToNormalizedString();
}

internal static class CodeAltaUpdateChecker
{
    public const string PackageId = "CodeAlta";

    private static readonly Uri NuGetRegistrationBaseUri = new("https://api.nuget.org/v3/registration5-gz-semver2/");

    public static async Task<CodeAltaNuGetUpdateCheckResult> CheckCurrentAssemblyAsync(
        Assembly? assembly = null,
        bool? includePrerelease = null,
        CancellationToken cancellationToken = default)
    {
        var currentVersion = GetCurrentAssemblyNuGetVersion(assembly);
        var effectiveIncludePrerelease = includePrerelease ?? currentVersion.IsPrerelease;
        return await CheckNuGetOrgAsync(PackageId, currentVersion, effectiveIncludePrerelease, cancellationToken);
    }

    public static async Task<CodeAltaNuGetUpdateCheckResult> CheckNuGetOrgAsync(
        string packageId,
        NuGetVersion currentVersion,
        bool includePrerelease = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(currentVersion);

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var listedVersions = await GetListedVersionsAsync(httpClient, packageId, cancellationToken);
        if (listedVersions.Length == 0)
        {
            return new CodeAltaNuGetUpdateCheckResult(packageId, currentVersion, null, PackageFound: false, HasNewerVersion: false, includePrerelease);
        }

        var latestVersion = listedVersions
            .Where(version => includePrerelease || !version.IsPrerelease)
            .OrderByDescending(static version => version, VersionComparer.VersionRelease)
            .FirstOrDefault();
        if (latestVersion is null)
        {
            return new CodeAltaNuGetUpdateCheckResult(packageId, currentVersion, null, PackageFound: true, HasNewerVersion: false, includePrerelease);
        }

        var hasNewerVersion = VersionComparer.VersionRelease.Compare(latestVersion, currentVersion) > 0;
        return new CodeAltaNuGetUpdateCheckResult(packageId, currentVersion, latestVersion, PackageFound: true, hasNewerVersion, includePrerelease);
    }

    public static NuGetVersion GetCurrentAssemblyNuGetVersion(Assembly? assembly = null)
    {
        var versionInfo = CodeAltaApplicationInfo.GetVersionInfo(assembly);
        if (NuGetVersion.TryParse(versionInfo.InformationalVersion, out var parsed))
        {
            return parsed;
        }

        if (NuGetVersion.TryParse(versionInfo.PackageVersion, out parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException($"Unable to determine the current CodeAlta NuGet version from '{versionInfo.InformationalVersion}'.");
    }

    private static async Task<NuGetVersion[]> GetListedVersionsAsync(HttpClient httpClient, string packageId, CancellationToken cancellationToken)
    {
        var registrationUri = new Uri(NuGetRegistrationBaseUri, $"{packageId.ToLowerInvariant()}/index.json");
        using var response = await httpClient.GetAsync(registrationUri, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return [];
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var versions = new List<NuGetVersion>();
        await AddListedVersionsAsync(httpClient, document.RootElement, versions, cancellationToken);
        return versions.ToArray();
    }

    private static async Task AddListedVersionsAsync(HttpClient httpClient, JsonElement registrationPage, List<NuGetVersion> versions, CancellationToken cancellationToken)
    {
        if (!registrationPage.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in items.EnumerateArray())
        {
            if (item.TryGetProperty("items", out var inlineItems) && inlineItems.ValueKind == JsonValueKind.Array)
            {
                AddListedVersions(inlineItems, versions);
                continue;
            }

            if (!item.TryGetProperty("@id", out var pageUriElement) || pageUriElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var pageUri = pageUriElement.GetString();
            if (string.IsNullOrWhiteSpace(pageUri))
            {
                continue;
            }

            using var response = await httpClient.GetAsync(pageUri, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var pageDocument = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (pageDocument.RootElement.TryGetProperty("items", out var pageItems) && pageItems.ValueKind == JsonValueKind.Array)
            {
                AddListedVersions(pageItems, versions);
            }
        }
    }

    private static void AddListedVersions(JsonElement items, List<NuGetVersion> versions)
    {
        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("catalogEntry", out var catalogEntry) || catalogEntry.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var listed = !catalogEntry.TryGetProperty("listed", out var listedElement) || listedElement.ValueKind != JsonValueKind.False;
            if (!listed || !catalogEntry.TryGetProperty("version", out var versionElement) || versionElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var versionText = versionElement.GetString();
            if (!string.IsNullOrWhiteSpace(versionText) && NuGetVersion.TryParse(versionText, out var version))
            {
                versions.Add(version);
            }
        }
    }
}
