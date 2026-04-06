using CodeAlta.Catalog;
using CodeAlta.Presentation.Prompting;
using CodeAlta.Presentation.Sidebar;
using CodeAlta.Presentation.Styling;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Commands;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal.UI.Styling;
using XenoAtom.Terminal.UI.Templating;
using XenoAtom.Terminal.UI.Text;

namespace CodeAlta.Views;

internal sealed class DirectoryPathDialog
{
    private const int ProjectPathMaxLength = 72;
    private readonly Func<string, bool, Task> _onSubmitAsync;
    private readonly Func<Visual?> _getFocusTarget;
    private readonly Func<Visual?> _getSubmitFocusTarget;
    private readonly State<string?> _validationText = new(null);
    private readonly DirectoryPathCompletionProvider _suggestionProvider;
    private readonly TextBox _editor;
    private readonly OptionList<OpenProjectSuggestion> _suggestions;
    private readonly Dialog _dialog;
    private readonly List<OpenProjectSuggestion> _items = [];
    private bool _includeHidden;
    private bool _suppressTextChanged;

    public DirectoryPathDialog(
        string title,
        string description,
        string submitText,
        Func<string, bool, Task> onSubmitAsync,
        Func<Rectangle?> getBounds,
        Func<Visual?> getFocusTarget,
        Func<Visual?>? getSubmitFocusTarget = null,
        Func<IEnumerable<ProjectDescriptor>>? getProjects = null,
        string? initialPath = null,
        string? placeholder = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(submitText);
        ArgumentNullException.ThrowIfNull(onSubmitAsync);
        ArgumentNullException.ThrowIfNull(getBounds);
        ArgumentNullException.ThrowIfNull(getFocusTarget);

        _onSubmitAsync = onSubmitAsync;
        _getFocusTarget = getFocusTarget;
        _getSubmitFocusTarget = getSubmitFocusTarget ?? getFocusTarget;
        _suggestionProvider = new DirectoryPathCompletionProvider(
            includeHidden: () => _includeHidden,
            projects: getProjects);

        _editor = new TextBox()
            .Placeholder(placeholder ?? "C:\\code\\SomeFolder or CodeAlta")
            .HorizontalAlignment(Align.Stretch)
            .Style(TextBoxStyle.Default with
            {
                Placeholder = UiPalette.PromptPlaceholderColor,
            });
        _editor.TextDocument.Changed += OnEditorTextChanged;
        _editor.KeyDown((_, e) => HandleEditorKeyDown(e));
        _editor.Text = initialPath ?? string.Empty;

        _suggestions = new OptionList<OpenProjectSuggestion>()
            .ActivateOnClick(true)
            .HorizontalAlignment(Align.Stretch)
            .VerticalAlignment(Align.Stretch)
            .ItemActivated((_, _) => ApplySelectedSuggestion());
        _suggestions.ItemTemplate = new DataTemplate<OpenProjectSuggestion>(
            static (DataTemplateValue<OpenProjectSuggestion> value, in DataTemplateContext _)
                => BuildSuggestionRow(value.GetValue()),
            null);
        _suggestions.KeyDown((_, e) => HandleSuggestionKeyDown(e));

        var resultsHost = new ScrollViewer(_suggestions, focusable: false)
            .HorizontalScrollEnabled(false)
            .VerticalScrollEnabled(true)
            .HorizontalAlignment(Align.Stretch)
            .VerticalAlignment(Align.Stretch)
            .MinHeight(5)
            .MaxHeight(8);

        var closeButton = new Button(new TextBlock($"{NerdFont.MdClose} Close"))
        {
            HorizontalAlignment = Align.End,
            VerticalAlignment = Align.Start,
            Tone = ControlTone.Default,
        };
        closeButton.Click(Close);

        var cancelButton = new Button("Cancel")
        {
            Tone = ControlTone.Default,
        };
        cancelButton.Click(Close);

        var submitButton = new Button(submitText)
        {
            Tone = ControlTone.Primary,
        };
        submitButton.Click(() => _ = SubmitAsync());

        var includeHiddenCheckBox = new CheckBox("Include hidden", _includeHidden);
        includeHiddenCheckBox.KeyDown((_, e) => RefreshSuggestionsAfterToggle(e));
        includeHiddenCheckBox.PointerPressed((_, e) => RefreshSuggestionsAfterPointerToggle(e));

        var validation = new ComputedVisual(
            () =>
            {
                if (string.IsNullOrWhiteSpace(_validationText.Value))
                {
                    return new Markup("[dim]Type a project name or rooted folder path · Tab applies the selected suggestion · Enter opens the current input[/]");
                }

                return new TextBlock(() => _validationText.Value ?? string.Empty)
                    .Style(TextBlockStyle.Default with { Foreground = Colors.OrangeRed })
                    .Wrap(true);
            });

        var content = new VStack(
            new TextBlock(description)
            {
                Wrap = true,
            },
            _editor,
            resultsHost,
            includeHiddenCheckBox,
            validation,
            new HStack(cancelButton, submitButton)
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
            .Title(title)
            .TopRightText(closeButton)
            .BottomRightText(new Markup("[dim]Arrows select · Tab complete[/]"))
            .IsModal(true)
            .Padding(1)
            .Content(content);
        ResponsiveDialogSize.Apply(_dialog, getBounds(), minWidth: 72, minHeight: 16, widthFactor: 0.56, heightFactor: 0.28);
        _dialog.AddCommand(new Command
        {
            Id = "CodeAlta.DirectoryPathDialog.Close",
            LabelMarkup = "Close",
            DescriptionMarkup = "Close the directory input dialog.",
            Gesture = new KeyGesture(TerminalKey.Escape),
            Importance = CommandImportance.Primary,
            Execute = _ => Close(),
        });

        RefreshSuggestions();
    }

    public void Show()
    {
        _dialog.Show();
        _dialog.App?.Focus(_editor);
    }

    private void OnEditorTextChanged(object? sender, TextDocumentChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_suppressTextChanged)
        {
            return;
        }

        _validationText.Value = null;
        RefreshSuggestions();
    }

    private void HandleEditorKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var handled = e.Key switch
        {
            TerminalKey.Up => TryMoveSelection(-1),
            TerminalKey.Down => TryMoveSelection(1),
            TerminalKey.PageUp => TryMoveSelection(-8),
            TerminalKey.PageDown => TryMoveSelection(8),
            TerminalKey.Home => TryMoveSelectionToBoundary(first: true),
            TerminalKey.End => TryMoveSelectionToBoundary(first: false),
            TerminalKey.Tab => ApplySelectedSuggestion(),
            TerminalKey.Enter => RaiseSubmit(),
            _ => false,
        };

        if (handled)
        {
            e.Handled = true;
        }
    }

    private void HandleSuggestionKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var handled = e.Key switch
        {
            TerminalKey.Tab => ApplySelectedSuggestion(),
            TerminalKey.Enter => RaiseSubmit(selectedSuggestionPreferred: true),
            TerminalKey.Escape => RaiseClose(),
            _ => false,
        };

        if (handled)
        {
            e.Handled = true;
        }
    }

    private void RefreshSuggestionsAfterToggle(KeyEventArgs e)
    {
        if (e.Key is not (TerminalKey.Space or TerminalKey.Enter))
        {
            return;
        }

        _dialog.Dispatcher.Post(() =>
        {
            _includeHidden = !_includeHidden;
            RefreshSuggestions();
        });
    }

    private void RefreshSuggestionsAfterPointerToggle(PointerEventArgs e)
    {
        if (e.Button != TerminalMouseButton.Left)
        {
            return;
        }

        _dialog.Dispatcher.Post(() =>
        {
            _includeHidden = !_includeHidden;
            RefreshSuggestions();
        });
    }

    private void RefreshSuggestions()
    {
        _items.Clear();
        _items.AddRange(_suggestionProvider.GetSuggestions(_editor.Text));

        _suggestions.Items.Clear();
        foreach (var item in _items)
        {
            _suggestions.Items.Add(item);
        }

        _suggestions.SelectedIndex = _items.Count == 0 ? -1 : 0;
    }

    private bool TryMoveSelection(int delta)
    {
        if (_items.Count == 0)
        {
            return false;
        }

        _suggestions.SelectedIndex = Math.Clamp(Math.Max(_suggestions.SelectedIndex, 0) + delta, 0, _items.Count - 1);
        return true;
    }

    private bool TryMoveSelectionToBoundary(bool first)
    {
        if (_items.Count == 0)
        {
            return false;
        }

        _suggestions.SelectedIndex = first ? 0 : _items.Count - 1;
        return true;
    }

    private bool ApplySelectedSuggestion()
    {
        if (_suggestions.SelectedIndex < 0 || _suggestions.SelectedIndex >= _items.Count)
        {
            return false;
        }

        SetEditorText(_items[_suggestions.SelectedIndex].ReplaceText);
        _dialog.App?.Focus(_editor);
        return true;
    }

    private bool RaiseSubmit(bool selectedSuggestionPreferred = false)
    {
        _ = SubmitAsync(selectedSuggestionPreferred);
        return true;
    }

    private bool RaiseClose()
    {
        Close();
        return true;
    }

    private void SetEditorText(string value)
    {
        _suppressTextChanged = true;
        try
        {
            _editor.Text = value;
            _editor.CaretIndex = _editor.TextDocument.CurrentSnapshot.Length;
        }
        finally
        {
            _suppressTextChanged = false;
        }

        RefreshSuggestions();
    }

    private async Task SubmitAsync(bool selectedSuggestionPreferred = false)
    {
        var input = ResolveSubmissionText(selectedSuggestionPreferred);
        if (string.IsNullOrWhiteSpace(input))
        {
            _validationText.Value = "A project name or rooted path is required.";
            return;
        }

        try
        {
            await _onSubmitAsync(input.Trim(), _includeHidden);
            Close(_getSubmitFocusTarget);
        }
        catch (Exception ex)
        {
            _validationText.Value = ex.Message;
        }
    }

    private string ResolveSubmissionText(bool selectedSuggestionPreferred)
    {
        if (selectedSuggestionPreferred &&
            _suggestions.SelectedIndex >= 0 &&
            _suggestions.SelectedIndex < _items.Count)
        {
            return _items[_suggestions.SelectedIndex].ReplaceText;
        }

        return _editor.Text ?? string.Empty;
    }

    private void Close()
        => Close(_getFocusTarget);

    private void Close(Func<Visual?> getFocusTarget)
    {
        var app = _dialog.App;
        _dialog.Close();
        if (getFocusTarget() is { } focusTarget)
        {
            app?.Focus(focusTarget);
        }
    }

    private static Visual BuildSuggestionRow(OpenProjectSuggestion suggestion)
    {
        var icon = suggestion.Kind switch
        {
            OpenProjectSuggestionKind.Project => new TextBlock(NerdFont.MdFolderOutline.ToString())
                .Style(TextBlockStyle.Default with
                {
                    Foreground = UiPalette.GetSidebarAccentColor(SidebarAccent.Projects),
                }),
            _ => new TextBlock(ProjectFileAppearanceRegistry.Default.GetDirectoryAppearance().Icon)
                .Style(TextBlockStyle.Default with
                {
                    Foreground = ProjectFileAppearanceRegistry.Default.GetDirectoryAppearance().IconForeground,
                }),
        };

        var label = new HStack(
        [
            icon,
            new TextBlock(suggestion.PrimaryText)
            {
                Wrap = false,
                IsSelectable = false,
            },
        ])
        {
            Spacing = 1,
        };

        Visual? shortcut = null;
        if (!string.IsNullOrWhiteSpace(suggestion.SecondaryText))
        {
            shortcut = new TextBlock(ShortenProjectPath(suggestion.SecondaryText))
            {
                Wrap = false,
                IsSelectable = false,
            }.Style(TextBlockStyle.Default with
            {
                Foreground = UiPalette.PromptPlaceholderColor,
            });
        }

        return new OptionListItem(label, shortcut);
    }

    private static string ShortenProjectPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (path.Length <= ProjectPathMaxLength)
        {
            return path;
        }

        return "..." + path[^Math.Max(0, ProjectPathMaxLength - 3)..];
    }
}
