using CodeAlta.Plugins.Abstractions;

[Plugin("skill-root", DisplayName = "Skill Root", Description = "Exposes a plugin-owned skill root.")]
public sealed class SkillRootPlugin : PluginBase
{
    public override IEnumerable<PluginResourceContribution> GetResources()
    {
        yield return Resources.SkillRoot("skills");
    }
}
