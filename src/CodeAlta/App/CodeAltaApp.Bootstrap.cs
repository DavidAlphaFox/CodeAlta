using CodeAlta.Agent;
using CodeAlta.Catalog;
using CodeAlta.Orchestration.Runtime;

internal sealed partial class CodeAltaApp
{
    public static async Task<CodeAltaApp> CreateAsync(CancellationToken cancellationToken)
    {
        var ownedServices = await CodeAltaOwnedServices.CreateAsync(cancellationToken).ConfigureAwait(false);
        return new CodeAltaApp(
            ownedServices.ProjectCatalog,
            ownedServices.ThreadCatalog,
            ownedServices.RuntimeService,
            ownedServices.CatalogOptions,
            ownedServices.AgentHub,
            new KnownProjectImporter(ownedServices.AgentHub, ownedServices.ProjectCatalog),
            ownedServices);
    }
}
