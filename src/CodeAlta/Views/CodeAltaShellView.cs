using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace CodeAlta.Views;

internal sealed class CodeAltaShellView
{
    public CodeAltaShellView(
        Visual sidebar,
        Visual threadWorkspace,
        Visual threadCommandBar,
        Action<TerminalApp> configureApp)
    {
        ArgumentNullException.ThrowIfNull(sidebar);
        ArgumentNullException.ThrowIfNull(threadWorkspace);
        ArgumentNullException.ThrowIfNull(threadCommandBar);
        ArgumentNullException.ThrowIfNull(configureApp);

        var mainLayout = new Grid
            {
                HorizontalAlignment = Align.Stretch,
                VerticalAlignment = Align.Stretch,
            }
            .Rows(
                new RowDefinition { Height = GridLength.Star(1) },
                new RowDefinition { Height = GridLength.Auto })
            .Columns(
                new ColumnDefinition { Width = GridLength.Star(1) });

        mainLayout.Cell(
            new HSplitter(sidebar, threadWorkspace)
            {
                Ratio = 0.26,
                MinFirst = 24,
                MinSecond = 40,
            },
            0,
            0);
        mainLayout.Cell(threadCommandBar, 1, 0);

        Root = new CodeAltaRootView(mainLayout, configureApp)
        {
            HorizontalAlignment = Align.Stretch,
            VerticalAlignment = Align.Stretch,
        };
    }

    public Visual Root { get; }
}
