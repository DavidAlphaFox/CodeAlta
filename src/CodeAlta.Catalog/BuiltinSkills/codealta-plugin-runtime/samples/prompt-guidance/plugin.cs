using CodeAlta.Plugins.Abstractions;

[Plugin("prompt-guidance", DisplayName = "Prompt Guidance", Description = "Adds developer prompt guidance.")]
public sealed class PromptGuidancePlugin : PluginBase
{
    public override IEnumerable<PluginSystemPromptContribution> GetSystemPromptContributions()
    {
        yield return Prompt.Developer("Prefer concise, actionable answers when this sample plugin is enabled.", "Sample guidance");
    }
}
