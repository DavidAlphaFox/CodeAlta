using System.Diagnostics;
using CodeAlta.Presentation.Shell;
using XenoAtom.Ansi;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Commands;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal.UI.Styling;

namespace CodeAlta.Views;

internal sealed class AboutDialog
{
    internal const string GitHubProjectUri = "https://github.com/CodeAlta/CodeAlta";
    internal const string WebsiteUri = "https://codealta.github.io/";

    private readonly Func<Rectangle?> _getBounds;
    private readonly Func<Visual?> _getFocusTarget;
    private readonly Func<CodeAltaUpdateCheckSnapshot> _getUpdateSnapshot;
    private readonly State<float> _logoAnimationPhase01;
    private readonly State<int>? _updateStatusVersion;
    private readonly Dialog _dialog;
    private CodeAltaUpdateCheckSnapshot? _lastUpdateStatusSnapshot;
    private Visual? _lastUpdateStatusVisual;

    public AboutDialog(
        Func<Rectangle?> getBounds,
        Func<Visual?> getFocusTarget,
        State<float> logoAnimationPhase01,
        CodeAltaUpdateService? updateService = null)
    {
        ArgumentNullException.ThrowIfNull(getBounds);
        ArgumentNullException.ThrowIfNull(getFocusTarget);
        ArgumentNullException.ThrowIfNull(logoAnimationPhase01);

        _getBounds = getBounds;
        _getFocusTarget = getFocusTarget;
        _getUpdateSnapshot = () => updateService?.Snapshot ?? CodeAltaUpdateCheckSnapshot.CreateNotStarted();
        _logoAnimationPhase01 = logoAnimationPhase01;
        _updateStatusVersion = updateService?.UiRefreshVersion;
        _dialog = BuildDialog();
    }

    public void Show()
    {
        if (_dialog.App is not null)
        {
            return;
        }

        ResponsiveDialogSize.Apply(_dialog, _getBounds(), minWidth: 86, minHeight: 24, widthFactor: 0.56, heightFactor: 0.52);
        _dialog.Show();
    }

    private Dialog BuildDialog()
    {
        var version = CodeAltaApplicationInfo.GetVersionInfo();
        var closeButton = new Button(new TextBlock($"{NerdFont.MdClose} Close"))
        {
            HorizontalAlignment = Align.End,
            VerticalAlignment = Align.Start,
            Tone = ControlTone.Default,
        };
        closeButton.Click(Close);

        var updateNote = new ComputedVisual(BuildUpdateStatusVisual);

        var versionLines = new List<Visual>
        {
            new Markup($"[bold]Version[/] {AnsiMarkup.Escape(version.PackageVersion)}")
            {
                HorizontalAlignment = Align.Center,
                Wrap = true,
            },
        };
        if (!string.IsNullOrWhiteSpace(version.BuildMetadata))
        {
            versionLines.Add(new Markup($"[dim]Build {AnsiMarkup.Escape(ShortenBuildMetadata(version.BuildMetadata))}[/]")
            {
                HorizontalAlignment = Align.Center,
                Wrap = true,
            });
        }

        var content = new Center(new VStack(
            [
                WelcomePaneFactory.BuildWelcomeLogo(_logoAnimationPhase01),
                new Markup($"[bold]{CodeAltaApplicationInfo.ProductName}[/]")
                {
                    HorizontalAlignment = Align.Center,
                },
                .. versionLines,
                new Markup(AnsiMarkup.Escape(CodeAltaApplicationInfo.GetCopyrightText()))
                {
                    HorizontalAlignment = Align.Center,
                    Wrap = true,
                },
                BuildLinksRow(),
                updateNote,
            ])
        {
            Spacing = 1,
            HorizontalAlignment = Align.Center,
            VerticalAlignment = Align.Center,
            MinWidth = 80,
        });

        var dialog = new Dialog()
            .Title("About CodeAlta")
            .TopRightText(closeButton)
            .BottomRightText(new Markup("[dim]Esc Close[/]"))
            .IsModal(true)
            .Padding(1)
            .Content(content)
            .Style(DialogStyle.Rounded);

        dialog.AddCommand(new Command
        {
            Id = "CodeAlta.Shell.About.Close",
            LabelMarkup = "Close",
            DescriptionMarkup = "Close the about dialog.",
            Gesture = new KeyGesture(TerminalKey.Escape),
            Importance = CommandImportance.Primary,
            Execute = _ => Close(),
        });
        dialog.KeyDown((_, e) =>
        {
            if (e.Key != TerminalKey.Escape)
            {
                return;
            }

            Close();
            e.Handled = true;
        });
        return dialog;
    }

    private static Visual BuildLinksRow()
    {
        return new HStack(
            CreateExternalLink(GitHubProjectUri, $"{NerdFont.FaGithub} GitHub"),
            CreateExternalLink(WebsiteUri, $"{NerdFont.MdWeb} Website"))
        {
            HorizontalAlignment = Align.Center,
            Spacing = 4,
        };
    }

    private static Visual CreateExternalLink(string uri, string text)
    {
        return new Link(uri, text)
            .Opened((_, e) =>
            {
                TryOpenBrowser(e.Uri);
                e.Handled = true;
            })
            .Tooltip(new TextBlock($"Open {uri}"));
    }

    private void Close()
    {
        var app = _dialog.App;
        _dialog.Close();
        if (_getFocusTarget() is { } focusTarget)
        {
            app?.Focus(focusTarget);
        }
    }

    private static string ShortenBuildMetadata(string buildMetadata)
        => buildMetadata.Length <= 12 ? buildMetadata : buildMetadata[..12];

    private static void TryOpenBrowser(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uri,
                UseShellExecute = true,
            });
        }
        catch (Exception)
        {
            // The link still renders as an OSC 8 terminal hyperlink when shell launch is unavailable.
        }
    }

    private Visual BuildUpdateStatusVisual()
    {
        _ = _updateStatusVersion?.Value;
        var snapshot = _getUpdateSnapshot();
        if (_lastUpdateStatusVisual is not null && snapshot == _lastUpdateStatusSnapshot)
        {
            return _lastUpdateStatusVisual;
        }

        _lastUpdateStatusSnapshot = snapshot;
        _lastUpdateStatusVisual = CodeAltaUpdateVisualFactory.CreateAboutUpdateStatus(snapshot, CopyUpdateCommand);
        return _lastUpdateStatusVisual;
    }

    private void CopyUpdateCommand(string command)
        => _dialog.App?.Terminal.Clipboard.TrySetText(command);
}
