using CodeAlta.Catalog;

namespace CodeAlta.Catalog;

public sealed record ProjectFilePromptToken(
    ProjectFilePromptTokenKind Kind,
    int StartIndex,
    int Length,
    string RawText,
    string? LookupText = null,
    ProjectFileLineRange? LineRange = null,
    bool IsMalformed = false,
    string? DisplayText = null);
