using System.Text;
using CodeAlta.LiveTool;
using XenoAtom.Ansi;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Commands;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Extensions.CodeEditor.TextMateSharp;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal.UI.Styling;
using XenoAtom.Terminal.UI.Text;

namespace CodeAlta.Views;

internal sealed class AskFileReviewView
{
    private readonly Dictionary<int, AskFileCommentEntry> _comments = new();
    private readonly State<int> _commentVersion = new(0);
    private readonly State<int> _fileStateVersion = new(0);
    private readonly TextDocument? _document;
    private readonly string _fullPath;
    private readonly string _displayPath;
    private ITextSnapshot? _lastFileSnapshot;
    private string _savedText;
    private string? _saveError;
    private int _lastEditorLine = 1;
    private bool _hasSavedUserChanges;

    private AskFileReviewView(string fullPath, string displayPath, string? text, string? loadError)
    {
        _fullPath = fullPath;
        _displayPath = displayPath;
        _savedText = text ?? string.Empty;

        if (loadError is null)
        {
            Editor = CreateEditor(_savedText, fullPath);
            _document = (TextDocument)Editor.TextDocument;
            _lastFileSnapshot = _document.CurrentSnapshot;
            _document.Changed += OnDocumentChanged;
            Editor.LeftMargins.Insert(0, CodeEditor.CreateDiffIndicatorMargin(GetCommentMarker, GetCommentMarkerStyle));
            Editor.AddCommand(new Command
            {
                Id = "CodeAlta.Ask.FileComment.Insert",
                LabelMarkup = "Add line comment",
                DescriptionMarkup = "Insert a user comment below the current file line without changing the file text.",
                Gesture = new KeyGesture(TerminalChar.CtrlK, TerminalModifiers.Ctrl),
                Importance = CommandImportance.Primary,
                Execute = _ => InsertCommentAtCurrentLine(),
            });
            Editor.AddCommand(CreateSaveCommand());
            Body = new ScrollViewer(Editor.Stretch(), focusable: false)
                .IsTabStop(false)
                .HorizontalAlignment(Align.Stretch)
                .VerticalAlignment(Align.Stretch);
        }
        else
        {
            Body = CreateUnavailableContent(loadError);
        }

        var clearButton = new Button("Clear comments");
        clearButton.Click(ClearComments);
        var header = new Footer()
            .Left(new Markup(BuildHeaderMarkup))
            .Right(new HStack(new Markup(BuildCommentCountMarkup), clearButton)
            {
                Spacing = 1,
                HorizontalAlignment = Align.End,
            });

        Root = new DockLayout()
            .Top(header)
            .Content(new Border(Body.Stretch())
            {
                HorizontalAlignment = Align.Stretch,
                VerticalAlignment = Align.Stretch,
            })
            .Bottom(new Markup(BuildFooterMarkup)
            {
                Wrap = true,
                Margin = new Thickness(1, 0, 1, 0),
            })
            .HorizontalAlignment(Align.Stretch)
            .VerticalAlignment(Align.Stretch);
        Root.AddCommand(CreateSaveCommand());
        Root.AddCommand(CreateClearCommentsCommand());
        Root.AddCommand(CreateFocusEditorCommand());
    }

    public Visual Root { get; }

    public Visual Body { get; }

    public CodeEditor? Editor { get; }

    public bool HasUnsavedChanges => Editor is not null && !string.Equals(GetEditorText(), _savedText, StringComparison.Ordinal);

    public static AskFileReviewView? Create(AltaAskFile? file, IReadOnlyList<string> rootCandidates)
    {
        if (string.IsNullOrWhiteSpace(file?.Path))
        {
            return null;
        }

        var resolution = ResolveFilePath(file.Path!, rootCandidates);
        return TryReadText(resolution.FullPath, out var text, out var error)
            ? new AskFileReviewView(resolution.FullPath, resolution.DisplayPath, text, loadError: null)
            : new AskFileReviewView(resolution.FullPath, resolution.DisplayPath, text: null, error);
    }

    public void AddQuestionFocusCommand(AskQuestionFormView form)
    {
        ArgumentNullException.ThrowIfNull(form);
        Root.AddCommand(new Command
        {
            Id = "CodeAlta.Ask.FocusQuestions",
            LabelMarkup = "Focus ask questions",
            DescriptionMarkup = "Move focus from the attached file editor back to the ask questions.",
            Sequence = new KeySequence(
                new KeyGesture(TerminalChar.CtrlG, TerminalModifiers.Ctrl),
                new KeyGesture(TerminalChar.CtrlN, TerminalModifiers.Ctrl)),
            Presentation = CommandPresentation.None,
            Execute = _ => form.FocusCurrentQuestionInput(),
        });
    }

    public void FocusEditor()
    {
        if (Editor is null)
        {
            return;
        }

        Editor.GoToLine(_lastEditorLine);
        Editor.App?.Focus(Editor);
    }

    public void ClearComments()
    {
        if (Editor is null || _comments.Count == 0)
        {
            return;
        }

        foreach (var lineIndex in _comments.Keys.ToArray())
        {
            Editor.RemoveLineVisual(lineIndex);
        }

        _comments.Clear();
        TouchComments();
    }

    public bool TrySave(out string error)
    {
        error = string.Empty;
        if (Editor is null)
        {
            error = "The attached file is not available for editing.";
            return false;
        }

        try
        {
            var text = GetEditorText();
            File.WriteAllText(_fullPath, text);
            _savedText = text;
            _hasSavedUserChanges = true;
            _saveError = null;
            TouchFileState();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException)
        {
            error = ex.Message;
            _saveError = error;
            TouchFileState();
            return false;
        }
    }

    public AltaAskFileReview CreateReviewSnapshot()
        => new()
        {
            FileModifiedAndSaved = _hasSavedUserChanges,
            Comments = _comments.Values
                .Where(static comment => comment.IsSubmitted && !string.IsNullOrWhiteSpace(comment.SubmittedText))
                .OrderBy(static comment => comment.LineIndex)
                .Select(static comment => new AltaAskFileComment
                {
                    Line = comment.LineIndex + 1,
                    Text = comment.SubmittedText,
                })
                .ToArray(),
        };

    public Command CreateClearCommentsCommand()
        => new()
        {
            Id = "CodeAlta.Ask.FileComment.Clear",
            LabelMarkup = "Clear file comments",
            DescriptionMarkup = "Clear all comments attached to the ask file.",
            Sequence = new KeySequence(
                new KeyGesture(TerminalChar.CtrlL, TerminalModifiers.Ctrl),
                new KeyGesture(TerminalChar.CtrlK, TerminalModifiers.Ctrl)),
            Presentation = CommandPresentation.None,
            CanExecute = _ => _comments.Count > 0,
            Execute = _ => ClearComments(),
        };

    public Command CreateFocusEditorCommand()
        => new()
        {
            Id = "CodeAlta.Ask.FocusFileEditor",
            LabelMarkup = "Focus file editor",
            DescriptionMarkup = "Move focus to the attached ask file editor.",
            Sequence = new KeySequence(
                new KeyGesture(TerminalChar.CtrlG, TerminalModifiers.Ctrl),
                new KeyGesture(TerminalChar.CtrlE, TerminalModifiers.Ctrl)),
            Presentation = CommandPresentation.None,
            CanExecute = _ => Editor is not null,
            Execute = _ => FocusEditor(),
        };

    private CodeEditor CreateEditor(string text, string fullPath)
    {
        var editor = new CodeEditor(new CodeEditorConfig { GoToLine = CodeEditorGoToLineConfig.Disabled })
            .AutoFocus(false)
            .WordWrap(false)
            .ShowLineNumbers(true)
            .HighlightCurrentLine(true)
            .MinHeight(8);
        editor.TextDocument = new TextDocument(text);
        editor.SyntaxHighlighter = CreateSyntaxHighlighter(fullPath);
        return editor;
    }

    public Command CreateSaveCommand()
        => new()
        {
            Id = "CodeAlta.Ask.File.Save",
            LabelMarkup = "Save ask file",
            DescriptionMarkup = "Save user edits to the attached ask file.",
            Gesture = new KeyGesture(TerminalChar.CtrlS, TerminalModifiers.Ctrl),
            Presentation = CommandPresentation.None,
            CanExecute = _ => HasUnsavedChanges,
            Execute = target =>
            {
                _ = TrySave(out var ignored);
            },
        };

    private void InsertCommentAtCurrentLine()
    {
        if (Editor is null)
        {
            return;
        }

        var lineIndex = Math.Max(0, Editor.Line - 1);
        _lastEditorLine = lineIndex + 1;
        if (_comments.TryGetValue(lineIndex, out var existing))
        {
            existing.TextArea.App?.Focus(existing.TextArea);
            return;
        }

        var entry = CreateCommentEntry(lineIndex);
        _comments.Add(lineIndex, entry);
        Editor.SetLineVisual(lineIndex, entry.Group);
        TouchComments();
        entry.TextArea.App?.Focus(entry.TextArea);
    }

    private AskFileCommentEntry CreateCommentEntry(int lineIndex)
    {
        var commentDocument = new TextDocument(string.Empty);
        var textArea = new TextArea()
            .Placeholder("Enter a comment... Shift+Enter newline · Ctrl+Enter validate · Esc discard")
            .AutoSizeMode(TextEditorAutoSizeMode.Height)
            .MinHeight(1)
            .MaxHeight(8)
            .HorizontalAlignment(Align.Stretch);
        textArea.TextDocument = commentDocument;

        var entry = new AskFileCommentEntry(lineIndex, textArea);
        commentDocument.Changed += (_, _) =>
        {
            if (entry.IsSubmitted && !string.Equals(GetText(entry.TextArea.TextDocument).Trim(), entry.SubmittedText, StringComparison.Ordinal))
            {
                entry.IsSubmitted = false;
                entry.Group.BottomRightText = null;
                TouchComments();
            }

            Editor?.NotifyLineVisualChanged(lineIndex);
        };
        var deleteButton = new Button("Delete") { Tone = ControlTone.Error };
        deleteButton.Click(() => DeleteComment(lineIndex));

        var group = new Group(new Markup("[bold primary]Comment[/]"))
        {
            TopRightText = deleteButton,
            BottomLeftText = new Markup("[dim]Esc discard · Shift+Enter newline · Ctrl+Enter validate[/]"),
            Padding = new Thickness(1, 0, 1, 0),
            HorizontalAlignment = Align.Stretch,
            Content = textArea,
        };
        entry.Group = group;

        textArea.AddCommand(new Command
        {
            Id = $"CodeAlta.Ask.FileComment.Submit.{lineIndex}",
            LabelMarkup = "Submit comment",
            DescriptionMarkup = "Validate this line comment.",
            Gesture = new KeyGesture(TerminalKey.Enter, TerminalModifiers.Ctrl),
            Presentation = CommandPresentation.None,
            Execute = _ => SubmitComment(entry),
        });
        textArea.AddCommand(new Command
        {
            Id = $"CodeAlta.Ask.FileComment.Discard.{lineIndex}",
            LabelMarkup = "Discard comment",
            DescriptionMarkup = "Discard this line comment.",
            Gesture = new KeyGesture(TerminalKey.Escape),
            Presentation = CommandPresentation.None,
            Execute = _ => DeleteComment(lineIndex),
        });
        textArea.AddCommand(new Command
        {
            Id = $"CodeAlta.Ask.FileComment.NewLine.{lineIndex}",
            LabelMarkup = "Insert comment newline",
            DescriptionMarkup = "Insert a new line in this line comment.",
            Gesture = new KeyGesture(TerminalKey.Enter, TerminalModifiers.Shift),
            Presentation = CommandPresentation.None,
            Execute = _ => InsertCommentNewLine(textArea),
        });

        return entry;
    }

    private void SubmitComment(AskFileCommentEntry entry)
    {
        var text = GetText(entry.TextArea.TextDocument).Trim();
        if (text.Length == 0)
        {
            DeleteComment(entry.LineIndex);
            return;
        }

        entry.IsSubmitted = true;
        entry.SubmittedText = text;
        entry.Group.BottomRightText = new Markup("[success]✅[/]");
        Editor?.NotifyLineVisualChanged(entry.LineIndex);
        TouchComments();
        if (Editor is not null)
        {
            _lastEditorLine = Math.Min(entry.LineIndex + 2, Math.Max(1, Editor.TextDocument.CurrentSnapshot.LineCount));
            Editor.GoToLine(_lastEditorLine);
            Editor.App?.Focus(Editor);
        }
    }

    private void DeleteComment(int lineIndex)
    {
        if (Editor is null || !_comments.Remove(lineIndex))
        {
            return;
        }

        Editor.RemoveLineVisual(lineIndex);
        _lastEditorLine = lineIndex + 1;
        Editor.GoToLine(_lastEditorLine);
        Editor.App?.Focus(Editor);
        TouchComments();
    }

    private void InsertCommentNewLine(TextArea textArea)
    {
        var position = Math.Clamp(textArea.CaretIndex, 0, textArea.TextDocument.CurrentSnapshot.Length);
        textArea.TextDocument.Insert(position, "\n");
        textArea.CaretIndex = position + 1;
    }

    private void OnDocumentChanged(object? sender, TextDocumentChangedEventArgs e)
    {
        MoveCommentsForDocumentChange(e);
        if (Editor is not null)
        {
            _lastEditorLine = Editor.Line;
        }

        _lastFileSnapshot = _document?.CurrentSnapshot;
        TouchFileState();
    }

    private void MoveCommentsForDocumentChange(TextDocumentChangedEventArgs e)
    {
        if (Editor is null || _lastFileSnapshot is null || _comments.Count == 0 || e.NewLineCount == e.OldLineCount)
        {
            return;
        }

        var lineDelta = e.NewLineCount - e.OldLineCount;
        var changeLine = _lastFileSnapshot.GetLineIndexFromPosition(Math.Clamp(e.Position, 0, _lastFileSnapshot.Length));
        var removedEndLine = _lastFileSnapshot.GetLineIndexFromPosition(Math.Clamp(e.Position + e.RemovedLength, 0, _lastFileSnapshot.Length));
        var maxLineIndex = Math.Max(0, e.NewLineCount - 1);
        var moved = new Dictionary<int, AskFileCommentEntry>();
        var changed = false;

        foreach (var (lineIndex, entry) in _comments.OrderBy(static pair => pair.Key))
        {
            var newLineIndex = lineIndex;
            if (lineIndex > removedEndLine)
            {
                newLineIndex = Math.Clamp(lineIndex + lineDelta, 0, maxLineIndex);
            }
            else if (lineIndex >= changeLine)
            {
                newLineIndex = Math.Clamp(changeLine, 0, maxLineIndex);
            }

            newLineIndex = FindAvailableLineIndex(newLineIndex, maxLineIndex, moved);

            entry.LineIndex = newLineIndex;
            moved[newLineIndex] = entry;
            changed |= newLineIndex != lineIndex;
        }

        if (!changed)
        {
            return;
        }

        Editor.ClearLineVisuals();
        _comments.Clear();
        foreach (var (lineIndex, entry) in moved.OrderBy(static pair => pair.Key))
        {
            _comments[lineIndex] = entry;
            Editor.SetLineVisual(lineIndex, entry.Group);
        }

        TouchComments();
    }

    private static int FindAvailableLineIndex(int preferredLineIndex, int maxLineIndex, Dictionary<int, AskFileCommentEntry> comments)
    {
        if (!comments.ContainsKey(preferredLineIndex))
        {
            return preferredLineIndex;
        }

        for (var candidate = preferredLineIndex + 1; candidate <= maxLineIndex; candidate++)
        {
            if (!comments.ContainsKey(candidate))
            {
                return candidate;
            }
        }

        for (var candidate = preferredLineIndex - 1; candidate >= 0; candidate--)
        {
            if (!comments.ContainsKey(candidate))
            {
                return candidate;
            }
        }

        return preferredLineIndex;
    }

    private string BuildHeaderMarkup()
    {
        _ = _fileStateVersion.Value;
        var modified = HasUnsavedChanges ? " [warning]*[/]" : string.Empty;
        return $"[bold]File context:[/] {AnsiMarkup.Escape(_displayPath)}{modified}";
    }

    private string BuildCommentCountMarkup()
    {
        _ = _commentVersion.Value;
        var count = _comments.Count;
        return count == 1 ? "[dim]1 comment[/]" : $"[dim]{count} comments[/]";
    }

    private string BuildFooterMarkup()
    {
        _ = _fileStateVersion.Value;
        var status = _saveError is null ? string.Empty : $" · [error]Save failed: {AnsiMarkup.Escape(_saveError)}[/]";
        return $"[dim]Ctrl+K comment · Ctrl+S save · Ctrl+G Ctrl+N questions · Ctrl+L Ctrl+K clear comments · Ctrl+F find[/]{status}";
    }

    private Rune? GetCommentMarker(int lineIndex)
        => _comments.ContainsKey(lineIndex) ? new Rune('■') : null;

    private Style GetCommentMarkerStyle(int lineIndex)
        => _comments.ContainsKey(lineIndex)
            ? Style.None.WithForeground(Colors.DeepSkyBlue) | TextStyle.Bold
            : Style.None;

    private string GetEditorText()
        => Editor is null ? string.Empty : GetText(Editor.TextDocument);

    private static string GetText(ITextDocument document)
    {
        var snapshot = document.CurrentSnapshot;
        if (snapshot.Length == 0)
        {
            return string.Empty;
        }

        return string.Create(snapshot.Length, snapshot, static (span, currentSnapshot) => currentSnapshot.CopyTo(0, span));
    }

    private void TouchComments()
        => _commentVersion.Value++;

    private void TouchFileState()
        => _fileStateVersion.Value++;

    private static Visual CreateUnavailableContent(string message)
        => new ScrollViewer(new TextBlock(message)
        {
            Wrap = true,
            Margin = new Thickness(1),
            HorizontalAlignment = Align.Stretch,
            VerticalAlignment = Align.Stretch,
        }, focusable: false)
            .HorizontalScrollEnabled(false)
            .VerticalScrollEnabled(true)
            .Stretch();

    private static bool TryReadText(string fullPath, out string text, out string error)
    {
        try
        {
            if (!File.Exists(fullPath))
            {
                text = string.Empty;
                error = $"Attached ask file was not found: {fullPath}";
                return false;
            }

            text = File.ReadAllText(fullPath);
            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException)
        {
            text = string.Empty;
            error = $"Attached ask file could not be loaded: {ex.Message}";
            return false;
        }
    }

    private static AskFileResolution ResolveFilePath(string path, IReadOnlyList<string> rootCandidates)
    {
        var normalizedPath = path.Trim();
        if (Path.IsPathFullyQualified(normalizedPath))
        {
            var fullPath = Path.GetFullPath(normalizedPath);
            return new AskFileResolution(fullPath, fullPath);
        }

        foreach (var root in rootCandidates)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            var candidate = Path.GetFullPath(Path.Combine(root, normalizedPath));
            if (File.Exists(candidate))
            {
                return new AskFileResolution(candidate, path.Replace('\\', '/'));
            }
        }

        var fallbackRoot = rootCandidates.FirstOrDefault(static root => !string.IsNullOrWhiteSpace(root)) ?? Environment.CurrentDirectory;
        var fallback = Path.GetFullPath(Path.Combine(fallbackRoot, normalizedPath));
        return new AskFileResolution(fallback, path.Replace('\\', '/'));
    }

    private static CodeEditorSyntaxHighlighter? CreateSyntaxHighlighter(string fullPath)
    {
        try
        {
            return new TextMateCodeEditorSyntaxHighlighter(
                new TextMateCodeEditorOptions
                {
                    FileName = fullPath,
                });
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private sealed record AskFileResolution(string FullPath, string DisplayPath);

    private sealed class AskFileCommentEntry
    {
        public AskFileCommentEntry(int lineIndex, TextArea textArea)
        {
            LineIndex = lineIndex;
            TextArea = textArea;
        }

        public int LineIndex { get; set; }

        public TextArea TextArea { get; }

        public Group Group { get; set; } = null!;

        public bool IsSubmitted { get; set; }

        public string SubmittedText { get; set; } = string.Empty;
    }
}
