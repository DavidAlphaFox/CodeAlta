using System.Text.RegularExpressions;

namespace CodeAlta.Catalog;

/// <summary>
/// Validates catalog slugs.
/// </summary>
public static partial class CatalogSlugValidator
{
    /// <summary>
    /// Validates the slug format (<c>^[a-z0-9][a-z0-9\-_.]{1,63}$</c>).
    /// </summary>
    /// <param name="slug">The slug to validate.</param>
    /// <param name="paramName">The associated parameter name.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="slug"/> is invalid.</exception>
    public static void Validate(string? slug, string? paramName = null)
    {
        if (!IsValid(slug))
        {
            throw new ArgumentException(
                "Slugs must match ^[a-z0-9][a-z0-9\\-_.]{1,63}$.",
                paramName ?? nameof(slug));
        }
    }

    /// <summary>
    /// Determines whether a slug is valid.
    /// </summary>
    /// <param name="slug">The slug to validate.</param>
    /// <returns><see langword="true"/> when valid; otherwise <see langword="false"/>.</returns>
    public static bool IsValid(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return false;
        }

        return SlugRegex().IsMatch(slug);
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9\\-_.]{1,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugRegex();
}
