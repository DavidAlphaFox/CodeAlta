using CodeAlta.Catalog;
using CodeAlta.ViewModels;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Commands;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal.UI.Styling;

namespace CodeAlta.Views;

internal sealed class NavigatorSettingsDialog
{
    private readonly NavigatorSettingsDialogViewModel _viewModel;
    private readonly Func<NavigatorSettings, Task> _onSaveAsync;
    private readonly Func<Visual?> _getFocusTarget;
    private readonly Dialog _dialog;

    public NavigatorSettingsDialog(
        NavigatorSettings settings,
        Func<NavigatorSettings, Task> onSaveAsync,
        Func<Rectangle?> getBounds,
        Func<Visual?> getFocusTarget)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(onSaveAsync);
        ArgumentNullException.ThrowIfNull(getBounds);
        ArgumentNullException.ThrowIfNull(getFocusTarget);

        _viewModel = new NavigatorSettingsDialogViewModel
        {
            SortMode = settings.SortMode,
            RecentThreadsPerProject = settings.RecentThreadsPerProject,
        };
        _onSaveAsync = onSaveAsync;
        _getFocusTarget = getFocusTarget;

        var closeButton = new Button(new TextBlock($"{NerdFont.MdClose} Close"))
        {
            HorizontalAlignment = Align.End,
            VerticalAlignment = Align.Start,
            Tone = ControlTone.Default,
        };
        closeButton.Click(Close);

        var sortModeSelect = new EnumSelect<NavigatorProjectSortMode>()
            .Value(_viewModel.Bind.SortMode)
            .MinWidth(18);

        var recentThreadsBox = new NumberBox<int>()
            .Value(_viewModel.Bind.RecentThreadsPerProject)
            .ValueValidator(static value => value is >= 1 and <= 50 ? null : "Use a value from 1 to 50.")
            .MinWidth(6)
            .MaxWidth(8);
        recentThreadsBox.ShowValidationMessage = false;
        recentThreadsBox.InvalidNumberMessage = "Enter a whole number.";
        var recentThreadsField = recentThreadsBox.Validate(
            _viewModel.Bind.RecentThreadsPerProject,
            static value => value is >= 1 and <= 50
                ? null
                : new ValidationMessage(ValidationSeverity.Error, "Use a value from 1 to 50."));

        var form = new Grid
            {
                HorizontalAlignment = Align.Stretch,
            }
            .Rows(
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto })
            .Columns(
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Star(1) });
        form.Cell(new TextBlock("Sort mode") { VerticalAlignment = Align.Center }, 0, 0);
        form.Cell(sortModeSelect, 0, 1);
        form.Cell(new TextBlock("Recent threads") { VerticalAlignment = Align.Center }, 1, 0);
        form.Cell(recentThreadsField, 1, 1);

        var cancelButton = new Button("Cancel")
        {
            Tone = ControlTone.Default,
        };
        cancelButton.Click(Close);

        var saveButton = new Button("Save")
        {
            Tone = ControlTone.Primary,
        };
        saveButton.Click(() => _ = SaveAsync());

        var content = new VStack(
            new TextBlock("Configure the navigator sort mode and how many recent threads each project shows.")
            {
                Wrap = true,
            },
            form,
            new HStack(cancelButton, saveButton)
            {
                HorizontalAlignment = Align.End,
                Spacing = 2,
            })
        {
            HorizontalAlignment = Align.Stretch,
            VerticalAlignment = Align.Stretch,
            Spacing = 1,
        };

        _dialog = new Dialog()
            .Title("Navigator Settings")
            .TopRightText(closeButton)
            .BottomRightText(new Markup("[dim]Esc Close[/]"))
            .IsModal(true)
            .Padding(1)
            .Content(content);
        ResponsiveDialogSize.Apply(_dialog, getBounds(), minWidth: 54, minHeight: 12, widthFactor: 0.65, heightFactor: 0.45);
        _dialog.AddCommand(new Command
        {
            Id = "CodeAlta.NavigatorSettings.Close",
            LabelMarkup = "Close",
            DescriptionMarkup = "Close the navigator settings dialog.",
            Gesture = new KeyGesture(TerminalKey.Escape),
            Importance = CommandImportance.Primary,
            Execute = _ => Close(),
        });
    }

    public void Show()
        => _dialog.Show();

    private async Task SaveAsync()
    {
        var settings = new NavigatorSettings
        {
            SortMode = _viewModel.SortMode,
            RecentThreadsPerProject = _viewModel.RecentThreadsPerProject,
        };

        try
        {
            settings.Validate();
        }
        catch (ArgumentOutOfRangeException)
        {
            return;
        }

        Close();
        await _onSaveAsync(settings).ConfigureAwait(false);
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
}
