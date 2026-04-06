using XenoAtom.Terminal.UI.Commands;

namespace CodeAlta.Frontend.Commands;

internal static class ShellCommandViewFactory
{
    public static Command Create(
        ShellCommandMetadata metadata,
        Action execute,
        CommandPresentation? presentation = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(execute);

        return new Command
        {
            Id = metadata.Id,
            LabelMarkup = metadata.DisplayLabelMarkup,
            Name = metadata.CommandName,
            SearchText = metadata.CommandSearchText,
            DescriptionMarkup = metadata.DescriptionMarkup,
            Gesture = metadata.Gesture,
            Sequence = metadata.Sequence,
            Presentation = presentation ?? ResolvePresentation(metadata),
            Execute = _ => execute(),
        };
    }

    private static CommandPresentation ResolvePresentation(ShellCommandMetadata metadata)
    {
        var presentation = CommandPresentation.None;
        if (metadata.ShowInCommandBar)
        {
            presentation |= CommandPresentation.CommandBar;
        }

        if (metadata.ShowInCommandPalette)
        {
            presentation |= CommandPresentation.CommandPalette;
        }

        return presentation;
    }
}
