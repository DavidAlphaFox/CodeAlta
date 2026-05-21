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
    private readonly Func<Rectangle?> _getBounds;
    private readonly Func<Visual?> _getFocusTarget;
    private readonly Func<CodeAltaUpdateCheckSnapshot> _getUpdateSnapshot;
    private readonly State<float> _logoAnimationPhase01;
    private readonly State<int>? _updateStatusVersion;
    private readonly Dialog _dialog;

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

        ResponsiveDialogSize.Apply(_dialog, _getBounds(), minWidth: 86, minHeight: 18, widthFactor: 0.56, heightFactor: 0.42);
        _dialog.Show();
    }

    private Dialog BuildDialog()
    {
        var version = CodeAltaApplicationInfo.GetVersionInfo();
        var updateNote = new Markup(BuildUpdateStatusMarkup)
        {
            HorizontalAlignment = Align.Center,
            Wrap = true,
        };

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
        return dialog;
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

    private string BuildUpdateStatusMarkup()
    {
        _ = _updateStatusVersion?.Value;
        var snapshot = _getUpdateSnapshot();
        return snapshot.Status switch
        {
            CodeAltaUpdateCheckStatus.Checking => $"[info]{NerdFont.MdCloudSearchOutline} Checking for updates...[/]",
            CodeAltaUpdateCheckStatus.Latest => $"[success]{NerdFont.MdCheckCircleOutline} You are running the latest {CodeAltaApplicationInfo.ProductName} version.[/]",
            CodeAltaUpdateCheckStatus.UpdateAvailable => $"[warning]{NerdFont.MdUpdate} Version {AnsiMarkup.Escape(snapshot.LatestVersionText ?? "?")} is available.[/] [dim]{AnsiMarkup.Escape(snapshot.UpdateCommand)}[/]",
            CodeAltaUpdateCheckStatus.PackageNotFound => $"[dim]{NerdFont.MdPackageVariantClosed} No published {AnsiMarkup.Escape(snapshot.PackageId)} package was found yet.[/]",
            CodeAltaUpdateCheckStatus.Failed => $"[warning]{NerdFont.MdAlertCircleOutline} Update check failed: {AnsiMarkup.Escape(snapshot.ErrorMessage ?? "unknown error")}[/]",
            _ => $"[info]{NerdFont.MdInformationOutline} Update status has not been checked yet.[/]",
        };
    }
}
