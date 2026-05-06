namespace CodeAlta.Catalog;

public sealed record ProjectFilePromptActiveReference(
    int StartIndex,
    int Length,
    string QueryText);
