using CodeAlta.Agent;
using CodeAlta.Plugins.Abstractions;

[Plugin("backend-provider", DisplayName = "Backend Provider", Description = "Declares a sample backend factory.")]
public sealed class BackendProviderPlugin : PluginBase
{
    public override IEnumerable<PluginAgentBackendContribution> GetAgentBackends()
    {
        yield return PluginBackend.FromFactory("sample-backend", static (_, _) => throw new NotSupportedException("Sample backend only."), "Sample Backend");
    }
}
