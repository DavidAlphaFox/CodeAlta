using CodeAlta.Catalog;

namespace CodeAlta.App;

internal interface ISessionDeleter
{
    Task<bool> DeleteSessionAsync(SessionViewDescriptor session, CancellationToken cancellationToken);
}
