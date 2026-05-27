using CodeAlta.App;
using CodeAlta.Catalog;
using CodeAlta.Presentation.Sidebar;
using CodeAlta.ViewModels;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Commands;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.DataGrid;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal.UI.Styling;
using XenoAtom.Terminal.UI.Templating;

namespace CodeAlta.Views;

internal sealed class ProjectThreadsDialog
{
    private readonly ProjectThreadsDialogState _state;
    private readonly ProjectThreadsDialogViewModel _viewModel = new();
    private readonly Func<IReadOnlyList<string>, Task> _deleteThreadsAsync;
    private readonly Func<string, Task> _openThreadAsync;
    private readonly Func<string?, string> _resolveProviderDisplayName;
    private readonly Func<Visual?> _getFocusTarget;
    private readonly DataGridListDocument<ProjectThreadsDialogRowViewModel> _document;
    private readonly Dialog _dialog;
    private int _documentRowCount;

    public ProjectThreadsDialog(
        ProjectDescriptor project,
        IReadOnlyList<SessionViewDescriptor> threads,
        Func<string?, string> resolveProviderDisplayName,
        Func<IReadOnlyList<string>, Task> deleteThreadsAsync,
        Func<string, Task> openThreadAsync,
        Func<Rectangle?> getBounds,
        Func<Visual?> getFocusTarget)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(threads);
        ArgumentNullException.ThrowIfNull(resolveProviderDisplayName);
        ArgumentNullException.ThrowIfNull(deleteThreadsAsync);
        ArgumentNullException.ThrowIfNull(openThreadAsync);
        ArgumentNullException.ThrowIfNull(getBounds);
        ArgumentNullException.ThrowIfNull(getFocusTarget);

        _resolveProviderDisplayName = resolveProviderDisplayName;
        _deleteThreadsAsync = deleteThreadsAsync;
        _openThreadAsync = openThreadAsync;
        _getFocusTarget = getFocusTarget;

        var nowUtc = DateTimeOffset.UtcNow;
        _state = new ProjectThreadsDialogState(
            threads
                .Where(static thread => thread.Status != WorkThreadStatus.Archived)
                .OrderByDescending(static thread => thread.LastActiveAt)
                .ThenBy(static thread => thread.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static thread => thread.ThreadId, StringComparer.OrdinalIgnoreCase)
                .Select(thread => CreateRow(thread, nowUtc, _resolveProviderDisplayName))
                .ToArray());

        _document = new DataGridListDocument<ProjectThreadsDialogRowViewModel>();
        using (_document.BeginUpdate())
        {
            _document
                .AddColumn(new DataGridColumnInfo<bool>("select", "✅", false, ProjectThreadsDialogRowViewModel.Accessor.IsSelected))
                .AddColumn(new DataGridColumnInfo<string>("backend", "🤖 Provider", true, ProjectThreadsDialogRowViewModel.Accessor.BackendDisplayName))
                .AddColumn(new DataGridColumnInfo<string>("title", "🧵 Session", false, ProjectThreadsDialogRowViewModel.Accessor.Title))
                .AddColumn(new DataGridColumnInfo<DateTimeOffset?>("updated", "🕒 Updated", true, ProjectThreadsDialogRowViewModel.Accessor.LastUpdatedAt))
                .AddColumn(new DataGridColumnInfo<int?>("messages", "💬 Messages", true, ProjectThreadsDialogRowViewModel.Accessor.MessageCount))
                .AddColumn(new DataGridColumnInfo<string>("open", "🚀 Open", false, ProjectThreadsDialogRowViewModel.Accessor.ThreadId));

            foreach (var row in _state.Rows)
            {
                _document.AddRow(row);
            }
        }

        _documentRowCount = _state.Rows.Count;
        var view = new DataGridDocumentView(_document);
        var grid = new DataGridControl { View = view }
            .FilterRowVisible(_viewModel.Bind.FilterRowVisible)
            .SelectionMode(DataGridSelectionMode.Cell)
            .EditMode(DataGridEditMode.OnEnter)
            .ShowHeader(true)
            .ShowRowAnchor(false);

        static Visual BuildLastUpdatedCell(DataTemplateValue<DateTimeOffset?> value, in DataTemplateContext _)
        {
            var row = (ProjectThreadsDialogRowViewModel)value.GetBinding().Owner;
            return new TextBlock(() => row.LastUpdatedRelative)
                .Tooltip(new TextBlock(() => row.LastUpdatedExact));
        }

        static Visual BuildMessageCountCell(DataTemplateValue<int?> value, in DataTemplateContext _)
            => new TextBlock(value.GetValue()?.ToString() ?? "—");

        static Visual BuildBackendCell(DataTemplateValue<string> value, in DataTemplateContext _)
        {
            var row = (ProjectThreadsDialogRowViewModel)value.GetBinding().Owner;
            return new Markup(() => SidebarThreadPresentation.BuildProviderMarkup(row.BackendId, row.BackendDisplayName, row.ThreadKind))
                .Wrap(false)
                .Tooltip(new TextBlock(() => string.IsNullOrWhiteSpace(row.BackendId)
                    ? row.BackendDisplayName
                    : $"{row.BackendDisplayName} ({row.BackendId})"));
        }

        static Visual BuildOpenButtonDisplay(DataTemplateValue<string> value, in DataTemplateContext context)
        {
            _ = value;
            _ = context;
            return new Button("Open")
            {
                IsHitTestVisible = false,
            }
                .Tone(ControlTone.Primary);
        }

        Visual BuildOpenButtonEditor(Binding<string> binding, in DataTemplateContext context)
        {
            _ = context;
            return new Button("Open")
                .Tone(ControlTone.Primary)
                .Click(() => _ = OpenThreadAsync(binding.GetValue()));
        }

        grid.Columns.Add(new DataGridColumn<bool>
        {
            Key = "select",
            Header = new TextBlock("✅"),
            TypedValueAccessor = ProjectThreadsDialogRowViewModel.Accessor.IsSelected,
            Width = GridLength.Auto,
            //CellTemplate = new DataTemplate<ProjectThreadsDialogRowViewModel>(BuildSelectionCell, null),
        });
        grid.Columns.Add(new DataGridColumn<string>
        {
            Key = "title",
            Header = new TextBlock("🧵 Session"),
            TypedValueAccessor = ProjectThreadsDialogRowViewModel.Accessor.Title,
            Width = GridLength.Star(2),
            Sortable = true,
        });
        grid.Columns.Add(new DataGridColumn<string>
        {
            Key = "backend",
            Header = new TextBlock("🤖 Provider"),
            TypedValueAccessor = ProjectThreadsDialogRowViewModel.Accessor.BackendDisplayName,
            Width = GridLength.Auto,
            Sortable = true,
            CellTemplate = new DataTemplate<string>(BuildBackendCell, null),
        });
        grid.Columns.Add(new DataGridColumn<DateTimeOffset?>
        {
            Key = "updated",
            Header = new TextBlock("🕒 Updated"),
            TypedValueAccessor = ProjectThreadsDialogRowViewModel.Accessor.LastUpdatedAt,
            Width = GridLength.Auto,
            Sortable = true,
            SortComparer = Comparer<DateTimeOffset?>.Create(static (left, right) => Nullable.Compare(left, right)),
            CellTemplate = new DataTemplate<DateTimeOffset?>(BuildLastUpdatedCell, null),
        });
        grid.Columns.Add(new DataGridColumn<int?>
        {
            Key = "messages",
            Header = new TextBlock("💬 Messages"),
            TypedValueAccessor = ProjectThreadsDialogRowViewModel.Accessor.MessageCount,
            Width = GridLength.Auto,
            CellAlignment = TextAlignment.Right,
            Sortable = true,
            SortComparer = Comparer<int?>.Create(static (left, right) =>
            {
                if (left == right)
                {
                    return 0;
                }

                if (left is null)
                {
                    return 1;
                }

                if (right is null)
                {
                    return -1;
                }

                return left.Value.CompareTo(right.Value);
            }),
            CellTemplate = new DataTemplate<int?>(BuildMessageCountCell, null),
        });
        grid.Columns.Add(new DataGridColumn<string>
        {
            Key = "open",
            Header = new TextBlock("🚀 Open"),
            TypedValueAccessor = ProjectThreadsDialogRowViewModel.Accessor.ThreadId,
            Width = GridLength.Auto,
            CellActivationMode = DataGridCellActivationMode.DirectActivate,
            CellTemplate = new DataTemplate<string>(BuildOpenButtonDisplay, null),
            CellEditorTemplate = new DataTemplate<string>(null, BuildOpenButtonEditor),
        });

        var closeButton = new Button(new TextBlock($"{NerdFont.MdClose} Close"))
        {
            HorizontalAlignment = Align.End,
            VerticalAlignment = Align.Start,
            Tone = ControlTone.Default,
        };
        closeButton.Click(Close);

        var toolbar = new HStack(
            new Button("Select None").Click(_state.SelectNone),
            new Button("Select All").Click(_state.SelectAll),
            new Button("Invert").Click(_state.InvertSelection),
            new Button("Delete Selected")
                .Tone(ControlTone.Error)
                .Click(() => _ = ConfirmDeleteSelectedAsync()),
            new CheckBox("Filter row").IsChecked(_viewModel.Bind.FilterRowVisible),
            new TextBlock(() => $"{_state.SelectedCount} selected"))
        {
            HorizontalAlignment = Align.Stretch,
            Spacing = 2,
        }.Pad(new(0, 0, 0, 1));

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Star(1) });

        content.Cells.Add(new GridCell() { Column = 0, Row = 0, Content = toolbar });
        content.Cells.Add(new GridCell() { Column = 0, Row = 1, Content = new ScrollViewer(grid.Stretch()).Stretch() });

        _dialog = new Dialog()
            .Title($"Sessions · {project.DisplayName}")
            .TopRightText(closeButton)
            .BottomRightText(new Markup("[dim]Esc Close[/]"))
            .IsModal(true)
            .Padding(1)
            .Content(content);
        ResponsiveDialogSize.Apply(_dialog, getBounds(), minWidth: 80, minHeight: 16);
        _dialog.AddCommand(new Command
        {
            Id = "CodeAlta.ProjectThreads.Close",
            LabelMarkup = "Close",
            DescriptionMarkup = "Close the project session list dialog.",
            Gesture = new KeyGesture(TerminalKey.Escape),
            Importance = CommandImportance.Primary,
            Execute = _ => Close(),
        });
    }

    public void Show()
        => _dialog.Show();

    private async Task ConfirmDeleteSelectedAsync()
    {
        var selectedThreadIds = _state.GetSelectedThreadIds();
        if (selectedThreadIds.Count == 0)
        {
            return;
        }

        new ConfirmationDialog(
            "Delete Selected Sessions",
            [$"Delete {selectedThreadIds.Count} selected session(s)?"],
            "Delete",
            ControlTone.Error,
            async () =>
            {
                await _deleteThreadsAsync(selectedThreadIds);
                _state.RemoveThreads(selectedThreadIds);
                RebuildDocumentRows();
            },
            () => _dialog.GetAbsoluteBounds(),
            () => _dialog)
            .Show();
    }

    private async Task OpenThreadAsync(string threadId)
    {
        Close();
        await _openThreadAsync(threadId);
    }

    private void RebuildDocumentRows()
    {
        using (_document.BeginUpdate())
        {
            if (_documentRowCount > 0)
            {
                _document.RemoveRows(0, _documentRowCount);
            }

            foreach (var row in _state.Rows)
            {
                _document.AddRow(row);
            }
        }

        _documentRowCount = _state.Rows.Count;
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

    private static ProjectThreadsDialogRowViewModel CreateRow(
        SessionViewDescriptor thread,
        DateTimeOffset nowUtc,
        Func<string?, string> resolveProviderDisplayName)
    {
        ArgumentNullException.ThrowIfNull(resolveProviderDisplayName);

        var display = SidebarRelativeTimeFormatter.Format(thread.LastActiveAt, nowUtc);
        return new ProjectThreadsDialogRowViewModel
        {
            ThreadId = thread.ThreadId,
            Title = thread.Title,
            BackendId = thread.BackendId,
            BackendDisplayName = resolveProviderDisplayName(thread.BackendId),
            ThreadKind = thread.Kind,
            LastUpdatedAt = thread.LastActiveAt,
            LastUpdatedRelative = display.RelativeText,
            LastUpdatedExact = display.ExactText,
            MessageCount = thread.MessageCount,
            ProjectId = thread.ProjectRef,
        };
    }
}
