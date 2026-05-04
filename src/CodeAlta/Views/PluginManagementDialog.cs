using System.Text;
using CodeAlta.App;
using CodeAlta.Plugins;
using XenoAtom.Ansi;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Commands;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal.UI.Styling;

namespace CodeAlta.Views;

internal sealed class PluginManagementDialog
{
    private readonly PluginManagementService _service;
    private readonly Func<Rectangle?> _getBounds;
    private readonly Func<Visual?> _getFocusTarget;
    private readonly Dialog _dialog;
    private readonly Markup _content;
    private string _markup = string.Empty;

    public PluginManagementDialog(
        PluginManagementService service,
        Func<Rectangle?> getBounds,
        Func<Visual?> getFocusTarget)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(getBounds);
        ArgumentNullException.ThrowIfNull(getFocusTarget);
        _service = service;
        _getBounds = getBounds;
        _getFocusTarget = getFocusTarget;
        _content = new Markup(() => _markup) { Wrap = true };

        var closeButton = new Button(new TextBlock($"{NerdFont.MdClose} Close"))
        {
            HorizontalAlignment = Align.End,
            VerticalAlignment = Align.Start,
        };
        closeButton.Click(Close);

        var refreshButton = new Button("Refresh").Click(Reload);
        var body = new VStack(
            new Markup("[dim]Source plugins are trusted code. Build/load operations can execute plugin and package build logic. Use --no-plugins, --plugin-safe-mode, or CODEALTA_DISABLE_PLUGINS=1 if a plugin breaks startup.[/]"),
            refreshButton,
            new Border(new ScrollViewer(_content).Stretch())
                .Style(BorderStyle.Rounded)
                .Padding(new Thickness(1, 0, 1, 0))
                .HorizontalAlignment(Align.Stretch)
                .VerticalAlignment(Align.Stretch))
        {
            HorizontalAlignment = Align.Stretch,
            VerticalAlignment = Align.Stretch,
            Spacing = 1,
        };

        _dialog = new Dialog()
            .Title("Plugins")
            .TopRightText(closeButton)
            .BottomRightText(new Markup("[dim]Esc Close[/]"))
            .IsModal(true)
            .Padding(1)
            .Content(body);
        ResponsiveDialogSize.Apply(_dialog, getBounds(), minWidth: 96, minHeight: 22, widthFactor: 0.82, heightFactor: 0.72);
        _dialog.AddCommand(new Command
        {
            Id = "CodeAlta.Plugins.Manage.Close",
            LabelMarkup = "Close",
            DescriptionMarkup = "Close the plugins dialog.",
            Gesture = new KeyGesture(TerminalKey.Escape),
            Importance = CommandImportance.Primary,
            Execute = _ => Close(),
        });
    }

    public void Show()
    {
        _dialog.Show();
        Reload();
    }

    private void Reload()
    {
        try
        {
            _markup = BuildMarkup(_service.LoadSnapshot());
        }
        catch (Exception ex)
        {
            _markup = $"[error]Failed to load plugin management data:[/] {AnsiMarkup.Escape(ex.Message)}";
        }
    }

    private void Close()
        => _dialog.Close();

    private static string BuildMarkup(PluginManagementSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.Append("[bold]Status:[/] ");
        builder.Append(snapshot.SafeMode ? "[warning]safe mode enabled[/]" : "[success]plugins enabled by policy[/]");
        builder.AppendLine();
        if (!string.IsNullOrWhiteSpace(snapshot.ProjectPath))
        {
            builder.Append("[bold]Project root:[/] ").Append(AnsiMarkup.Escape(snapshot.ProjectPath)).AppendLine();
        }

        builder.AppendLine();
        if (snapshot.Entries.Count == 0)
        {
            builder.AppendLine("[dim]No built-in or source plugins were discovered for the current scope.[/]");
            return builder.ToString();
        }

        foreach (var entry in snapshot.Entries)
        {
            builder.Append("[bold]").Append(AnsiMarkup.Escape(entry.DisplayName)).Append("[/] ");
            builder.Append('(').Append(entry.LoadUnitKind).Append('/').Append(entry.Scope).Append(") ");
            builder.Append(FormatState(entry.State)).AppendLine();
            builder.Append("  Key: ").Append(AnsiMarkup.Escape(entry.Key)).AppendLine();
            if (!string.IsNullOrWhiteSpace(entry.SourcePath))
            {
                builder.Append("  Source: ").Append(AnsiMarkup.Escape(entry.SourcePath)).AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(entry.ReadmePath))
            {
                builder.Append("  README: ").Append(AnsiMarkup.Escape(entry.ReadmePath)).AppendLine();
            }

            if (entry.LastBuildSummary is not null)
            {
                builder.Append("  Build: ").Append(AnsiMarkup.Escape(entry.LastBuildSummary.ToString() ?? string.Empty)).AppendLine();
            }

            if (entry.Contributions.Count > 0)
            {
                builder.Append("  Contributions: ").Append(entry.Contributions.Count).AppendLine();
            }

            if (entry.Diagnostics.Count > 0)
            {
                builder.Append("  Diagnostics:").AppendLine();
                foreach (var diagnostic in entry.Diagnostics.Take(4))
                {
                    builder.Append("    - ").Append(diagnostic.Severity).Append(": ").Append(AnsiMarkup.Escape(diagnostic.Message)).AppendLine();
                }
            }

            if (entry.Actions.Count > 0)
            {
                builder.Append("  Actions: ").Append(string.Join(", ", entry.Actions)).AppendLine();
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string FormatState(PluginManagementState state)
        => state switch
        {
            PluginManagementState.Active => "[success]active[/]",
            PluginManagementState.Enabled => "[primary]enabled[/]",
            PluginManagementState.Disabled => "[dim]disabled[/]",
            PluginManagementState.Failed => "[error]failed[/]",
            PluginManagementState.Changed => "[warning]changed[/]",
            PluginManagementState.UnknownConfig => "[warning]unknown config[/]",
            _ => AnsiMarkup.Escape(state.ToString()),
        };
}
