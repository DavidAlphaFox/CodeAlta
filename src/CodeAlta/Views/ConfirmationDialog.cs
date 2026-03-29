using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Commands;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal.UI.Styling;

namespace CodeAlta.Views;

internal sealed class ConfirmationDialog
{
    private readonly Dialog _dialog;
    private readonly Func<Task> _onConfirmAsync;
    private readonly Func<Visual?> _getFocusTarget;

    public ConfirmationDialog(
        string title,
        IReadOnlyList<string> bodyLines,
        string confirmText,
        ControlTone confirmTone,
        Func<Task> onConfirmAsync,
        Func<Rectangle?> getBounds,
        Func<Visual?> getFocusTarget,
        string cancelText = "Cancel",
        string? noteText = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(bodyLines);
        ArgumentException.ThrowIfNullOrWhiteSpace(confirmText);
        ArgumentNullException.ThrowIfNull(onConfirmAsync);
        ArgumentNullException.ThrowIfNull(getBounds);
        ArgumentNullException.ThrowIfNull(getFocusTarget);
        ArgumentException.ThrowIfNullOrWhiteSpace(cancelText);

        _onConfirmAsync = onConfirmAsync;
        _getFocusTarget = getFocusTarget;

        var closeButton = new Button(new TextBlock($"{NerdFont.MdClose} Close"))
        {
            HorizontalAlignment = Align.End,
            VerticalAlignment = Align.Start,
            Tone = ControlTone.Default,
        };
        closeButton.Click(Close);

        var cancelButton = new Button(cancelText)
        {
            Tone = ControlTone.Default,
        };
        cancelButton.Click(Close);

        var confirmButton = new Button(confirmText)
        {
            Tone = confirmTone,
        };
        confirmButton.Click(() => _ = ConfirmAsync());

        var body = new VStack(
            bodyLines
                .Where(static line => !string.IsNullOrWhiteSpace(line))
                .Select(static line => (Visual)new TextBlock(line).Wrap(true))
                .ToArray())
        {
            HorizontalAlignment = Align.Stretch,
        };

        var items = new List<Visual> { body };
        if (CreateNote(noteText) is { } note)
        {
            items.Add(note);
        }

        items.Add(new HStack(cancelButton, confirmButton)
        {
            HorizontalAlignment = Align.End,
            Spacing = 2,
        });

        var content = new VStack(items.ToArray())
        {
            HorizontalAlignment = Align.Stretch,
            VerticalAlignment = Align.Stretch,
            Spacing = 1,
        };

        _dialog = new Dialog()
            .Title(title)
            .TopRightText(closeButton)
            .IsModal(true)
            .Padding(1)
            .Content(content);
        ResponsiveDialogSize.Apply(_dialog, getBounds(), minWidth: 52, minHeight: 10, widthFactor: 0.65, heightFactor: 0.4);
        _dialog.AddCommand(new Command
        {
            Id = "CodeAlta.ConfirmationDialog.Close",
            LabelMarkup = cancelText,
            DescriptionMarkup = "Close the confirmation dialog.",
            Gesture = new KeyGesture(TerminalKey.Escape),
            Importance = CommandImportance.Primary,
            Execute = _ => Close(),
        });
    }

    public void Show()
        => _dialog.Show();

    private async Task ConfirmAsync()
    {
        Close();
        await _onConfirmAsync().ConfigureAwait(false);
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

    private static Visual? CreateNote(string? noteText)
    {
        if (string.IsNullOrWhiteSpace(noteText))
        {
            return null;
        }

        return new Markup($"[dim]{noteText}[/]").Wrap(true);
    }
}
