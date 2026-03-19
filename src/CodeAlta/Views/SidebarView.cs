using CodeAlta.ViewModels;
using XenoAtom.Ansi;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Layout;

internal sealed class SidebarView
{
    public SidebarView(
        SidebarViewModel viewModel,
        Action refreshCatalog)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(refreshCatalog);

        Tree = new TreeView
        {
            HorizontalAlignment = Align.Stretch,
            VerticalAlignment = Align.Stretch,
        };

        var treeHost = new ScrollViewer(Tree)
            .HorizontalScrollEnabled(false)
            .VerticalScrollEnabled(true);

        var footer = new VStack(
            [
                new TextBlock("Thread Title (optional)"),
                new TextBox().Text(viewModel.Bind.DraftThreadTitle),
                new Button(new TextBlock("Refresh Catalog")).Click(refreshCatalog),
            ])
        {
            Spacing = 1,
        };

        var contentGrid = new Grid
        {
            HorizontalAlignment = Align.Stretch,
            VerticalAlignment = Align.Stretch,
        }
        .Rows(
            new RowDefinition { Height = GridLength.Star(1) },
            new RowDefinition { Height = GridLength.Auto })
        .Columns(
            new ColumnDefinition { Width = GridLength.Star(1) });
        contentGrid.Cell(treeHost, 0, 0);
        contentGrid.Cell(footer, 1, 0);

        Root = new Group(
            new Markup($"[bold]{AnsiMarkup.Escape($"{NerdFont.FaFolderTree} Navigator")}[/]"),
            contentGrid)
        {
            HorizontalAlignment = Align.Stretch,
            VerticalAlignment = Align.Stretch,
        };
    }

    public Visual Root { get; }

    public TreeView Tree { get; }
}
