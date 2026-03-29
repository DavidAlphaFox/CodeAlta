namespace CodeAlta.Catalog.Bootstrap;

/// <summary>
/// Plans project checkout operations without network side effects.
/// </summary>
public sealed class ProjectBootstrapPlanner
{
    /// <summary>
    /// Creates checkout plans for a resolved project scope.
    /// </summary>
    /// <param name="resolution">The resolved project scope.</param>
    /// <returns>The planned operations.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="resolution"/> is <see langword="null"/>.</exception>
    public IReadOnlyList<ProjectCheckoutPlan> Plan(ProjectScopeResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);

        var plans = new List<ProjectCheckoutPlan>(resolution.Projects.Count);
        foreach (var project in resolution.Projects)
        {
            var action = Directory.Exists(project.CheckoutPath)
                ? CheckoutAction.Update
                : CheckoutAction.Clone;

            plans.Add(new ProjectCheckoutPlan
            {
                ProjectSlug = project.Project.Slug,
                ProjectPath = project.Project.ProjectPath,
                CheckoutPath = project.CheckoutPath,
                Action = action,
            });
        }

        return plans;
    }
}
