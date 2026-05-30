namespace CodeAlta.Views;

internal sealed record UserPromptSelectorController(Action<int> SelectPrompt, Action OpenPrompts)
{
    public static UserPromptSelectorController Create(Action<int> selectPrompt)
        => Create(selectPrompt, static () => { });

    public static UserPromptSelectorController Create(Action<int> selectPrompt, Action openPrompts)
    {
        ArgumentNullException.ThrowIfNull(selectPrompt);
        ArgumentNullException.ThrowIfNull(openPrompts);
        return new UserPromptSelectorController(selectPrompt, openPrompts);
    }
}
