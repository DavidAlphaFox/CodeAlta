using CodeAlta.Models;
using CodeAlta.Orchestration.Runtime.SystemPrompts;
using XenoAtom.Ansi;
using XenoAtom.Terminal.UI.Controls;

namespace CodeAlta.Presentation.Chat;

internal static class UserPromptPresentation
{
    public static IReadOnlyList<UserPromptOption> BuildPromptOptions(IReadOnlyList<UserPromptDescriptor> prompts)
    {
        ArgumentNullException.ThrowIfNull(prompts);
        return prompts
            .OrderBy(static prompt => prompt.Precedence)
            .ThenBy(static prompt => prompt.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static prompt => prompt.PromptName, StringComparer.OrdinalIgnoreCase)
            .Select(static prompt => new UserPromptOption(
                prompt.PromptName,
                BuildPromptLabel(prompt),
                ToSourceLabel(prompt.SourceKind),
                prompt.SystemPromptName,
                prompt.Description,
                prompt.IsBuiltIn))
            .ToArray();
    }

    public static string BuildPromptLabel(UserPromptDescriptor prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        return string.Equals(prompt.DisplayName, prompt.PromptName, StringComparison.OrdinalIgnoreCase)
            ? prompt.DisplayName
            : $"{prompt.DisplayName} ({prompt.PromptName})";
    }

    public static string BuildPromptOptionMarkup(UserPromptOption? option)
    {
        if (option is null)
        {
            return "[gray]No prompts[/]";
        }

        var color = option.IsBuiltIn ? "gray" : option.SourceLabel == "project" ? "lime" : "cyan";
        return $"{AnsiMarkup.Escape(option.Label)} [dim][{color}]{AnsiMarkup.Escape(option.SourceLabel)}[/][/]";
    }

    public static void ReplaceSelectItems<T>(Select<T> select, IReadOnlyList<T> items)
    {
        ArgumentNullException.ThrowIfNull(select);
        ArgumentNullException.ThrowIfNull(items);
        select.Items.Clear();
        foreach (var item in items)
        {
            select.Items.Add(item);
        }
    }

    public static string ToSourceLabel(UserPromptSourceKind sourceKind)
        => sourceKind switch
        {
            UserPromptSourceKind.BuiltIn => "built-in",
            UserPromptSourceKind.UserGlobal => "global",
            UserPromptSourceKind.Project => "project",
            _ => "unknown",
        };
}
