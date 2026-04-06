using CodeAlta.App;
using CodeAlta.Models;
using CodeAlta.ViewModels;
using XenoAtom.Ansi;
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

internal sealed class AcpManagementDialog
{
    private readonly AcpManagementService _service;
    private readonly Func<Task> _reloadAcpBackendsAsync;
    private readonly Func<string, Task> _probeAcpBackendAsync;
    private readonly Func<Visual?> _getFocusTarget;
    private readonly Dialog _dialog;
    private readonly EnumSelect<AcpManagementScope> _scopeSelect;
    private readonly Select<AcpManagementFilterOption> _filterSelect;
    private readonly DataGridListDocument<AcpManagementRowViewModel> _document;
    private readonly DataGridControl _grid;
    private readonly Markup _summaryMarkup;
    private readonly TextBlock _detailText;
    private AcpManagementSnapshot _snapshot = new(null, null, null, []);
    private IReadOnlyList<AcpManagementRowViewModel> _visibleRows = [];
    private string? _selectedAgentId;
    private bool _rebuilding;
    private int _documentRowCount;
    private string _summaryText = "Loading ACP registry and installed agents...";

    public AcpManagementDialog(
        AcpManagementService service,
        Func<Task> reloadAcpBackendsAsync,
        Func<string, Task> probeAcpBackendAsync,
        Func<Rectangle?> getBounds,
        Func<Visual?> getFocusTarget)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(reloadAcpBackendsAsync);
        ArgumentNullException.ThrowIfNull(probeAcpBackendAsync);
        ArgumentNullException.ThrowIfNull(getBounds);
        ArgumentNullException.ThrowIfNull(getFocusTarget);

        _service = service;
        _reloadAcpBackendsAsync = reloadAcpBackendsAsync;
        _probeAcpBackendAsync = probeAcpBackendAsync;
        _getFocusTarget = getFocusTarget;

        var closeButton = new Button(new TextBlock($"{NerdFont.MdClose} Close"))
        {
            HorizontalAlignment = Align.End,
            VerticalAlignment = Align.Start,
            Tone = ControlTone.Default,
        };
        closeButton.Click(Close);

        _scopeSelect = new EnumSelect<AcpManagementScope>()
            .Value(AcpManagementScope.Catalog)
            .MinWidth(16);
        _scopeSelect.SelectionChanged((_, _) => RebuildVisibleItems());

        _filterSelect = new Select<AcpManagementFilterOption>()
            .HorizontalAlignment(Align.Stretch)
            .MinWidth(22);
        _filterSelect.SelectionChanged((_, _) => RebuildVisibleItems());

        _document = new DataGridListDocument<AcpManagementRowViewModel>();
        using (_document.BeginUpdate())
        {
            _document
                .AddColumn(new DataGridColumnInfo<string>("state", "State", true, AcpManagementRowViewModel.Accessor.StateMarkup))
                .AddColumn(new DataGridColumnInfo<string>("installed", "Inst", true, AcpManagementRowViewModel.Accessor.InstalledText))
                .AddColumn(new DataGridColumnInfo<string>("enabled", "On", true, AcpManagementRowViewModel.Accessor.EnabledText))
                .AddColumn(new DataGridColumnInfo<string>("name", "Agent", true, AcpManagementRowViewModel.Accessor.Name))
                .AddColumn(new DataGridColumnInfo<string>("id", "Id", true, AcpManagementRowViewModel.Accessor.AgentId))
                .AddColumn(new DataGridColumnInfo<string>("version", "Version", true, AcpManagementRowViewModel.Accessor.Version))
                .AddColumn(new DataGridColumnInfo<string>("dist", "Dist", true, AcpManagementRowViewModel.Accessor.Distribution))
                .AddColumn(new DataGridColumnInfo<string>("runtime", "Runtime", true, AcpManagementRowViewModel.Accessor.RuntimeMarkup));
        }

        var view = new DataGridDocumentView(_document);
        _grid = new DataGridControl { View = view }
            .SelectionMode(DataGridSelectionMode.Row)
            .EditMode(DataGridEditMode.OnEnter)
            .ReadOnly(true)
            .ShowHeader(true)
            .ShowRowAnchor(false)
            .HorizontalAlignment(Align.Stretch)
            .VerticalAlignment(Align.Stretch);
        ConfigureColumns(_grid);

        _summaryMarkup = new Markup(() => _summaryText)
        {
            Wrap = true,
        };

        _detailText = new TextBlock(BuildDetailText)
        {
            Wrap = true,
            HorizontalAlignment = Align.Stretch,
            VerticalAlignment = Align.Stretch,
        };

        var refreshButton = new Button("Refresh Registry")
            .Tone(ControlTone.Primary)
            .Click(() => _ = ReloadSnapshotAsync(refreshRegistry: true));
        var installButton = new Button("Install")
            .Click(() => _ = InstallSelectedAsync());
        var configureButton = new Button("Configure")
            .Click(EditSelectedItem);
        var resetButton = new Button("Reset Config")
            .Click(() => _ = ResetSelectedAsync());
        var probeButton = new Button("Probe")
            .Click(() => _ = ProbeSelectedAsync());
        var removeButton = new Button("Remove")
            .Tone(ControlTone.Error)
            .Click(() => _ = RemoveSelectedAsync());
        var manualButton = new Button("New Manual")
            .Click(CreateManualAgent);

        var toolbar = new Grid
            {
                HorizontalAlignment = Align.Stretch,
            }
            .Rows(new RowDefinition { Height = GridLength.Auto })
            .Columns(
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Star(1) });
        toolbar.Cell(new TextBlock("View") { VerticalAlignment = Align.Center }, 0, 0);
        toolbar.Cell(_scopeSelect, 0, 1);
        toolbar.Cell(_filterSelect, 0, 2);
        toolbar.Cell(
            new HStack(manualButton, installButton, configureButton, resetButton, probeButton, removeButton, refreshButton)
            {
                HorizontalAlignment = Align.End,
                Spacing = 1,
            },
            0,
            3);

        var introText = new Markup("[dim]Browse the official ACP registry and the ACP backends currently known to CodeAlta. Use the grid to inspect installation state, runtime state, and agent identity.[/]")
        {
            Wrap = true,
        };

        var contentGrid = new Grid
            {
                HorizontalAlignment = Align.Stretch,
                VerticalAlignment = Align.Stretch,
            }
            .Rows(
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Star(1) })
            .Columns(
                new ColumnDefinition { Width = GridLength.Star(3) },
                new ColumnDefinition { Width = GridLength.Star(2) });

        contentGrid.Cell(toolbar, 0, 0, columnSpan: 2);
        contentGrid.Cell(introText, 1, 0, columnSpan: 2);
        contentGrid.Cell(_summaryMarkup, 2, 0, columnSpan: 2);
        contentGrid.Cell(
            new Border(new ScrollViewer(_grid.Stretch()).Stretch())
                .Style(BorderStyle.Rounded)
                .Padding(new Thickness(1, 0, 1, 0))
                .HorizontalAlignment(Align.Stretch)
                .VerticalAlignment(Align.Stretch),
            3,
            0);
        contentGrid.Cell(
            new Border(new ScrollViewer(_detailText).Stretch())
                .Style(BorderStyle.Rounded)
                .Padding(new Thickness(1, 0, 1, 0))
                .HorizontalAlignment(Align.Stretch)
                .VerticalAlignment(Align.Stretch),
            3,
            1);

        _dialog = new Dialog()
            .Title("ACP Agents")
            .TopRightText(closeButton)
            .BottomRightText(new Markup("[dim]Esc Close[/]"))
            .IsModal(true)
            .Padding(1)
            .Content(contentGrid);
        ResponsiveDialogSize.Apply(_dialog, getBounds(), minWidth: 112, minHeight: 26, widthFactor: 0.9, heightFactor: 0.82);
        _dialog.AddCommand(new Command
        {
            Id = "CodeAlta.Acp.Manage.Close",
            LabelMarkup = "Close",
            DescriptionMarkup = "Close the ACP manager.",
            Gesture = new KeyGesture(TerminalKey.Escape),
            Importance = CommandImportance.Primary,
            Execute = _ => Close(),
        });
    }

    public void Show()
    {
        PopulateFilterOptions();
        _dialog.Show();
        _ = ReloadSnapshotAsync(refreshRegistry: false);
    }

    private async Task ReloadSnapshotAsync(bool refreshRegistry)
    {
        _summaryText = "[primary]Loading ACP registry and installed agents...[/]";
        _snapshot = await _service.LoadSnapshotAsync(refreshRegistry);
        RebuildVisibleItems();
    }

    private void PopulateFilterOptions()
    {
        var options = GetFilterOptions(_scopeSelect.Value);
        _filterSelect.Items.Clear();
        foreach (var option in options)
        {
            _filterSelect.Items.Add(option);
        }

        if (_filterSelect.Items.Count > 0)
        {
            _filterSelect.SelectedIndex = 0;
        }
    }

    private void RebuildVisibleItems()
    {
        if (_rebuilding)
        {
            return;
        }

        _rebuilding = true;
        try
        {
            PopulateFilterOptionsIfNeeded();
            var previousSelectedAgentId = GetSelectedItem()?.AgentId ?? _selectedAgentId;
            var scope = _scopeSelect.Value;
            var filter = _filterSelect.SelectedIndex >= 0 && _filterSelect.SelectedIndex < _filterSelect.Items.Count
                ? _filterSelect.Items[_filterSelect.SelectedIndex]
                : GetFilterOptions(scope)[0];

            _visibleRows = _snapshot.Items
                .Where(item => scope == AcpManagementScope.Catalog ? item.IsInRegistry : item.IsInstalled || item.HasConfiguration || item.IsManual)
                .Where(item => MatchesFilter(item, filter.Kind))
                .Select(BuildRow)
                .ToArray();

            RebuildDocumentRows();

            if (_visibleRows.Count == 0)
            {
                _selectedAgentId = null;
                _grid.SelectedRow = -1;
                _grid.CurrentCell = DataGridCell.None;
                _summaryText = BuildSummaryMarkup();
                return;
            }

            var newIndex = previousSelectedAgentId is null
                ? 0
                : Array.FindIndex(
                    _visibleRows.ToArray(),
                    row => string.Equals(row.Item.AgentId, previousSelectedAgentId, StringComparison.OrdinalIgnoreCase));
            if (newIndex < 0 || newIndex >= _visibleRows.Count)
            {
                newIndex = 0;
            }

            _grid.SelectedRow = newIndex;
            _grid.CurrentCell = new DataGridCell(newIndex, 0);
            _selectedAgentId = _visibleRows[newIndex].Item.AgentId;
            _summaryText = BuildSummaryMarkup();
        }
        finally
        {
            _rebuilding = false;
        }
    }

    private void PopulateFilterOptionsIfNeeded()
    {
        var expectedKinds = GetFilterOptions(_scopeSelect.Value).Select(static option => option.Kind).ToArray();
        var currentKinds = _filterSelect.Items.Select(static option => option.Kind).ToArray();
        if (expectedKinds.SequenceEqual(currentKinds))
        {
            return;
        }

        PopulateFilterOptions();
    }

    private AcpAgentSummaryItem? GetSelectedItem()
    {
        var rowIndex = _grid.SelectedRow >= 0 ? _grid.SelectedRow : _grid.CurrentCell.Row;
        if ((uint)rowIndex < (uint)_visibleRows.Count)
        {
            return _visibleRows[rowIndex].Item;
        }

        return _selectedAgentId is null
            ? null
            : _visibleRows.FirstOrDefault(row => string.Equals(row.Item.AgentId, _selectedAgentId, StringComparison.OrdinalIgnoreCase))?.Item;
    }

    private async Task InstallSelectedAsync()
    {
        var item = GetSelectedItem();
        if (item is null || !item.IsInRegistry)
        {
            return;
        }

        new ConfirmationDialog(
            "Install ACP Agent",
            [
                $"Install '{item.DisplayName}'?",
                $"Version: {item.RegistryVersion ?? "unknown"}",
                $"Source: {item.Repository ?? item.Website ?? "Unavailable"}",
                $"Distribution: {(item.DistributionKinds.Count == 0 ? "unknown" : string.Join(", ", item.DistributionKinds))}",
                $"Command preview: {item.CommandSummary ?? "Unavailable"}",
                item.InstallabilityMessage,
            ],
            "Install",
            ControlTone.Primary,
            async () =>
            {
                await _service.InstallAgentAsync(item.AgentId);
                await _reloadAcpBackendsAsync();
                await ReloadSnapshotAsync(refreshRegistry: false);
            },
            () => _dialog.GetAbsoluteBounds(),
            () => _dialog)
            .Show();
    }

    private void EditSelectedItem()
    {
        var item = GetSelectedItem();
        if (item is null)
        {
            return;
        }

        var definition = _service.CreateEditableDefinition(item.AgentId);
        var existingAgentId = definition.AgentId;
        new AcpAgentSettingsDialog(
            $"ACP Settings · {item.DisplayName}",
            definition,
            canEditAgentId: false,
            candidateAgentId => ValidateAgentId(candidateAgentId, existingAgentId),
            async savedDefinition =>
            {
                _service.SaveConfiguration(savedDefinition);
                await _reloadAcpBackendsAsync();
                await ReloadSnapshotAsync(refreshRegistry: false);
            },
            () => _dialog.GetAbsoluteBounds(),
            () => _dialog)
            .Show();
    }

    private void CreateManualAgent()
    {
        var definition = _service.CreateNewManualDefinition();
        new AcpAgentSettingsDialog(
            "Create Manual ACP Agent",
            definition,
            canEditAgentId: true,
            candidateAgentId => ValidateAgentId(candidateAgentId, exceptAgentId: null),
            async savedDefinition =>
            {
                _service.SaveConfiguration(savedDefinition);
                await _reloadAcpBackendsAsync();
                await ReloadSnapshotAsync(refreshRegistry: false);
            },
            () => _dialog.GetAbsoluteBounds(),
            () => _dialog)
            .Show();
    }

    private async Task ResetSelectedAsync()
    {
        var item = GetSelectedItem();
        if (item is null || !item.HasConfiguration)
        {
            return;
        }

        new ConfirmationDialog(
            "Reset ACP Configuration",
            [$"Reset CodeAlta overrides for '{item.DisplayName}' and return to installed defaults?"],
            "Reset",
            ControlTone.Default,
            async () =>
            {
                _service.ResetConfiguration(item.AgentId);
                await _reloadAcpBackendsAsync();
                await ReloadSnapshotAsync(refreshRegistry: false);
            },
            () => _dialog.GetAbsoluteBounds(),
            () => _dialog)
            .Show();
        await Task.CompletedTask;
    }

    private async Task RemoveSelectedAsync()
    {
        var item = GetSelectedItem();
        if (item is null)
        {
            return;
        }

        new ConfirmationDialog(
            "Remove ACP Agent",
            [
                $"Remove '{item.DisplayName}' from CodeAlta?",
                item.IsInstalled
                    ? "This removes the installed manifest, CodeAlta config override, and local ACP artifacts."
                    : "This removes the CodeAlta ACP configuration.",
            ],
            "Remove",
            ControlTone.Error,
            async () =>
            {
                _service.RemoveAgent(item.AgentId, removeArtifacts: true);
                await _reloadAcpBackendsAsync();
                await ReloadSnapshotAsync(refreshRegistry: false);
            },
            () => _dialog.GetAbsoluteBounds(),
            () => _dialog)
            .Show();
        await Task.CompletedTask;
    }

    private async Task ProbeSelectedAsync()
    {
        var item = GetSelectedItem();
        if (item is null)
        {
            return;
        }

        await _probeAcpBackendAsync(item.AgentId);
        await ReloadSnapshotAsync(refreshRegistry: false);
    }

    private string? ValidateAgentId(string? candidateAgentId, string? exceptAgentId)
    {
        if (string.IsNullOrWhiteSpace(candidateAgentId))
        {
            return "Agent id is required.";
        }

        var normalized = candidateAgentId.Trim().ToLowerInvariant();
        if (normalized.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            normalized.Contains(Path.DirectorySeparatorChar) ||
            normalized.Contains(Path.AltDirectorySeparatorChar))
        {
            return "Agent id must be a simple identifier.";
        }

        if (_service.AgentIdExists(normalized, exceptAgentId))
        {
            return $"An ACP agent with id '{normalized}' already exists.";
        }

        return null;
    }

    private string BuildSummaryMarkup()
    {
        var installedCount = _snapshot.Items.Count(static item => item.IsInstalled);
        var configuredCount = _snapshot.Items.Count(static item => item.HasConfiguration);
        var enabledCount = _snapshot.Items.Count(static item => item.IsEnabled);
        var brokenCount = _snapshot.Items.Count(static item => item.IsBroken);
        var registryVersion = string.IsNullOrWhiteSpace(_snapshot.RegistryVersion)
            ? "registry unavailable"
            : $"registry v{AnsiMarkup.Escape(_snapshot.RegistryVersion)}";
        var fetchedAt = _snapshot.RegistryFetchedAtUtc is { } fetchedAtUtc
            ? $" · cached {AnsiMarkup.Escape(fetchedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"))}"
            : string.Empty;
        var error = string.IsNullOrWhiteSpace(_snapshot.RegistryError)
            ? string.Empty
            : $" · [warning]{AnsiMarkup.Escape(_snapshot.RegistryError)}[/]";

        return
            $"[bold]{registryVersion}[/]{fetchedAt}{error}\n" +
            $"[success]{installedCount} installed[/]   [primary]{configuredCount} configured[/]   " +
            $"[accent]{enabledCount} enabled[/]   [warning]{brokenCount} broken[/]";
    }

    private string BuildDetailText()
    {
        var selected = GetSelectedItem();
        if (selected is null)
        {
            return "Choose an ACP agent from the grid to inspect its registry metadata, install state, and runtime state.";
        }

        var authors = selected.Authors.Count == 0 ? "Unknown" : string.Join(", ", selected.Authors);
        var distributions = selected.DistributionKinds.Count == 0 ? "Unknown" : string.Join(", ", selected.DistributionKinds);
        var runtimeModels = selected.RuntimeModels.Count == 0 ? "No runtime-discovered models." : string.Join(", ", selected.RuntimeModels);
        var runtimeState = selected.RuntimeAvailability switch
        {
            ChatBackendAvailability.Ready => "Ready",
            ChatBackendAvailability.Connecting => "Loading",
            ChatBackendAvailability.Unsupported => "Unsupported",
            ChatBackendAvailability.Failed => "Failed",
            ChatBackendAvailability.Unknown or null => "Unknown",
            _ => "Unknown",
        };

        return
            $"Agent\n" +
            $"{selected.DisplayName}\n" +
            $"{selected.Description ?? "No description."}\n\n" +
            $"Identity\n" +
            $"Agent Id: {selected.AgentId}\n" +
            $"Registry Id: {selected.RegistryId ?? "None"}\n" +
            $"Version: {selected.RegistryVersion ?? "Unknown"}\n" +
            $"Authors: {authors}\n" +
            $"License: {selected.License ?? "Unknown"}\n\n" +
            $"Install\n" +
            $"In Registry: {(selected.IsInRegistry ? "Yes" : "No")}\n" +
            $"Installed: {(selected.IsInstalled ? "Yes" : "No")}\n" +
            $"Configured: {(selected.HasConfiguration ? "Yes" : "No")}\n" +
            $"Enabled: {(selected.IsEnabled ? "Yes" : "No")}\n" +
            $"Manual: {(selected.IsManual ? "Yes" : "No")}\n" +
            $"Installability: {selected.InstallabilityMessage}\n" +
            $"Distribution: {distributions}\n" +
            $"Command: {selected.CommandSummary ?? "Unavailable"}\n" +
            $"Working Directory: {selected.WorkingDirectory ?? "Default"}\n\n" +
            $"Runtime\n" +
            $"Status: {runtimeState}\n" +
            $"Runtime Detail: {selected.RuntimeStatus ?? "No runtime probe yet."}\n" +
            $"Models ({selected.RuntimeModelCount ?? 0}, runtime-discovered): {runtimeModels}\n\n" +
            $"Links\n" +
            $"Repository: {selected.Repository ?? "Unavailable"}\n" +
            $"Website: {selected.Website ?? "Unavailable"}";
    }

    private void RebuildDocumentRows()
    {
        using (_document.BeginUpdate())
        {
            if (_documentRowCount > 0)
            {
                _document.RemoveRows(0, _documentRowCount);
            }

            foreach (var row in _visibleRows)
            {
                _document.AddRow(row);
            }
        }

        _documentRowCount = _visibleRows.Count;
    }

    private static void ConfigureColumns(DataGridControl grid)
    {
        ArgumentNullException.ThrowIfNull(grid);

        static Visual BuildStateCell(DataTemplateValue<string> value, in DataTemplateContext _)
            => new Markup(value.GetValue()).Wrap(false);

        static Visual BuildRuntimeCell(DataTemplateValue<string> value, in DataTemplateContext _)
            => new Markup(value.GetValue()).Wrap(false);

        static Visual BuildNameCell(DataTemplateValue<string> value, in DataTemplateContext _)
        {
            var row = (AcpManagementRowViewModel)value.GetBinding().Owner;
            return new TextBlock(value.GetValue())
                .Tooltip(new TextBlock(() => string.IsNullOrWhiteSpace(row.Item.Description)
                    ? $"{row.Item.AgentId}"
                    : $"{row.Item.AgentId}\n{row.Item.Description}").Wrap(true));
        }

        static Visual BuildIdCell(DataTemplateValue<string> value, in DataTemplateContext _)
        {
            var row = (AcpManagementRowViewModel)value.GetBinding().Owner;
            return new TextBlock(value.GetValue())
                .Tooltip(new TextBlock(() => row.Item.RegistryId is null || string.Equals(row.Item.RegistryId, row.Item.AgentId, StringComparison.OrdinalIgnoreCase)
                    ? row.Item.AgentId
                    : $"Agent Id: {row.Item.AgentId}\nRegistry Id: {row.Item.RegistryId}").Wrap(true));
        }

        grid.Columns.Add(new DataGridColumn<string>
        {
            Key = "state",
            Header = new TextBlock("State"),
            TypedValueAccessor = AcpManagementRowViewModel.Accessor.StateMarkup,
            Width = GridLength.Auto,
            Sortable = true,
            CellTemplate = new DataTemplate<string>(BuildStateCell, null),
        });
        grid.Columns.Add(new DataGridColumn<string>
        {
            Key = "installed",
            Header = new TextBlock("Inst"),
            TypedValueAccessor = AcpManagementRowViewModel.Accessor.InstalledText,
            Width = GridLength.Auto,
            Sortable = true,
        });
        grid.Columns.Add(new DataGridColumn<string>
        {
            Key = "enabled",
            Header = new TextBlock("On"),
            TypedValueAccessor = AcpManagementRowViewModel.Accessor.EnabledText,
            Width = GridLength.Auto,
            Sortable = true,
        });
        grid.Columns.Add(new DataGridColumn<string>
        {
            Key = "name",
            Header = new TextBlock("Agent"),
            TypedValueAccessor = AcpManagementRowViewModel.Accessor.Name,
            Width = GridLength.Star(2),
            Sortable = true,
            CellTemplate = new DataTemplate<string>(BuildNameCell, null),
        });
        grid.Columns.Add(new DataGridColumn<string>
        {
            Key = "id",
            Header = new TextBlock("Id"),
            TypedValueAccessor = AcpManagementRowViewModel.Accessor.AgentId,
            Width = GridLength.Star(2),
            Sortable = true,
            CellTemplate = new DataTemplate<string>(BuildIdCell, null),
        });
        grid.Columns.Add(new DataGridColumn<string>
        {
            Key = "version",
            Header = new TextBlock("Version"),
            TypedValueAccessor = AcpManagementRowViewModel.Accessor.Version,
            Width = GridLength.Auto,
            Sortable = true,
        });
        grid.Columns.Add(new DataGridColumn<string>
        {
            Key = "dist",
            Header = new TextBlock("Dist"),
            TypedValueAccessor = AcpManagementRowViewModel.Accessor.Distribution,
            Width = GridLength.Auto,
            Sortable = true,
        });
        grid.Columns.Add(new DataGridColumn<string>
        {
            Key = "runtime",
            Header = new TextBlock("Runtime"),
            TypedValueAccessor = AcpManagementRowViewModel.Accessor.RuntimeMarkup,
            Width = GridLength.Auto,
            Sortable = true,
            CellTemplate = new DataTemplate<string>(BuildRuntimeCell, null),
        });
    }

    private static AcpManagementRowViewModel BuildRow(AcpAgentSummaryItem item)
    {
        var stateMarkup = item switch
        {
            { IsBroken: true } => "[warning]Broken[/]",
            { IsInstalled: true } => "[success]Installed[/]",
            { IsManual: true } => "[accent]Manual[/]",
            { Installability: AcpInstallabilityState.Unavailable } => "[dim]Unavailable[/]",
            _ => "[primary]Catalog[/]",
        };
        var runtimeMarkup = item.RuntimeAvailability switch
        {
            ChatBackendAvailability.Ready => "[success]Ready[/]",
            ChatBackendAvailability.Connecting => "[primary]Loading[/]",
            ChatBackendAvailability.Unsupported => "[warning]Unsupported[/]",
            ChatBackendAvailability.Failed => "[warning]Failed[/]",
            _ => "[dim]-[/]",
        };

        return new AcpManagementRowViewModel
        {
            Item = item,
            StateMarkup = stateMarkup,
            InstalledText = item.IsInstalled ? "Yes" : "No",
            EnabledText = item.IsInstalled || item.HasConfiguration ? (item.IsEnabled ? "Yes" : "No") : "-",
            Name = item.DisplayName,
            AgentId = item.AgentId,
            Version = item.RegistryVersion ?? "-",
            Distribution = item.DistributionKinds.Count == 0 ? "-" : string.Join("/", item.DistributionKinds),
            RuntimeMarkup = runtimeMarkup,
        };
    }

    private static bool MatchesFilter(AcpAgentSummaryItem item, AcpManagementFilterKind filterKind)
    {
        return filterKind switch
        {
            AcpManagementFilterKind.All => true,
            AcpManagementFilterKind.Installed => item.IsInstalled,
            AcpManagementFilterKind.NotInstalled => !item.IsInstalled,
            AcpManagementFilterKind.PlatformReady => item.Installability == AcpInstallabilityState.Installable,
            AcpManagementFilterKind.PlatformUnavailable => item.Installability == AcpInstallabilityState.Unavailable,
            AcpManagementFilterKind.Enabled => item.IsEnabled,
            AcpManagementFilterKind.Disabled => !item.IsEnabled,
            AcpManagementFilterKind.Manual => item.IsManual,
            AcpManagementFilterKind.Broken => item.IsBroken,
            _ => true,
        };
    }

    private static IReadOnlyList<AcpManagementFilterOption> GetFilterOptions(AcpManagementScope scope)
    {
        return scope == AcpManagementScope.Catalog
        ?
        [
            new(AcpManagementFilterKind.All, "All Registry"),
            new(AcpManagementFilterKind.Installed, "Installed"),
            new(AcpManagementFilterKind.NotInstalled, "Not Installed"),
            new(AcpManagementFilterKind.PlatformReady, "Platform Ready"),
            new(AcpManagementFilterKind.PlatformUnavailable, "Platform Unavailable"),
        ]
        :
        [
            new(AcpManagementFilterKind.All, "All Installed"),
            new(AcpManagementFilterKind.Enabled, "Enabled"),
            new(AcpManagementFilterKind.Disabled, "Disabled"),
            new(AcpManagementFilterKind.Manual, "Manual"),
            new(AcpManagementFilterKind.Broken, "Broken"),
        ];
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

internal enum AcpManagementScope
{
    Catalog,
    Installed,
}

internal enum AcpManagementFilterKind
{
    All,
    Installed,
    NotInstalled,
    PlatformReady,
    PlatformUnavailable,
    Enabled,
    Disabled,
    Manual,
    Broken,
}

internal sealed record AcpManagementFilterOption(AcpManagementFilterKind Kind, string Label)
{
    public override string ToString() => Label;
}
