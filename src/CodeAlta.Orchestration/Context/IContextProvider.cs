namespace CodeAlta.Orchestration.Context;

/// <summary>
/// Provides contextual items used to build bounded context packs.
/// </summary>
public interface IContextProvider
{
    /// <summary>
    /// Produces context items for a request.
    /// </summary>
    /// <param name="request">Provider request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Context items ordered by provider preference.</returns>
    Task<IReadOnlyList<ContextItem>> ProvideAsync(
        ContextProviderRequest request,
        CancellationToken cancellationToken = default);
}
