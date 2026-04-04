using CodeAlta.Search;

namespace CodeAlta.Presentation.Prompting;

internal sealed record ProjectFilePromptToken(
    ProjectFilePromptTokenKind Kind,
    int StartIndex,
    int Length,
    string RawText,
    string? LookupText = null,
    ProjectFileLineRange? LineRange = null,
    bool IsMalformed = false);
