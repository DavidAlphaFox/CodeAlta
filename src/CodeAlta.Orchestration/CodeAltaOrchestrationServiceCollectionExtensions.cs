using CodeAlta.Orchestration.Context;
using CodeAlta.Orchestration.Roles;
using CodeAlta.Orchestration.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace CodeAlta.Orchestration;

/// <summary>
/// Extension methods for registering orchestration services.
/// </summary>
public static class CodeAltaOrchestrationServiceCollectionExtensions
{
    /// <summary>
    /// Registers core orchestration services.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configure">Optional options callback.</param>
    /// <returns><paramref name="services"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddCodeAltaOrchestration(
        this IServiceCollection services,
        Action<OrchestrationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new OrchestrationOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<RoleProfileStore>();
        services.AddSingleton<PlannerService>();
        services.AddSingleton<BuilderService>();
        services.AddSingleton<KnowledgeService>();
        services.AddSingleton<AgentHub>();
        services.AddSingleton<TaskContextProvider>();
        services.AddSingleton<SearchContextProvider>();
        services.AddSingleton<ContextPackBuilder>(sp =>
            new ContextPackBuilder(
            [
                sp.GetRequiredService<TaskContextProvider>(),
                sp.GetRequiredService<SearchContextProvider>(),
            ]));

        return services;
    }
}
