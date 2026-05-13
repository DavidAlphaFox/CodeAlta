namespace CodeAlta.App;

internal sealed record DeleteThreadResult(
    IReadOnlyList<string> DeletedThreadIds,
    bool DeletedByBackend);
