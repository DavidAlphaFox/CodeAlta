namespace CodeAlta.Models;

internal readonly record struct SessionVisualState(
    bool IsRunning,
    bool HasPromptDraft);
