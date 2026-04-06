using CodeAlta.App;
using CodeAlta.Models;
using XenoAtom.Ansi;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Commands;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal.UI.Styling;

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
    private readonly Select<AcpDialogListItem> _itemSelect;
    private readonly Markup _summaryMarkup;
    private readonly TextBlock _detailText;
    private AcpManagementSnapshot _snapshot = new(null, null, null, []);
    private IReadOnlyList<AcpDialogListItem> _visibleItems = [];
    private string? _selectedAgentId;
    private bool _rebuilding;
    private string _summaryText = "Loading ACP registry and installed agents...";
    private string _detailValue = "Select an ACP agent to inspect its registry, install, and runtime details.";

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

        var refreshButton = new Button("Refresh Registry")
            .Tone(ControlTone.Primary)
            .Click(() => _ = ReloadSnapshotAsync(refreshRegistry: true));
        var installButton = new Button("Install")
            .Click(() => _ = InstallSelectedAsync());
        var configureButton = new Button("Configure")
            .Click(() => EditSelectedItem());
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

        _summaryMarkup = new Markup(() => _summaryText)
        {
            Wrap = true,
        };

        _itemSelect = new Select<AcpDialogListItem>()
            .HorizontalAlignment(Align.Stretch)
            .VerticalAlignment(Align.Stretch);
        _itemSelect.SelectionChanged((_, e) => OnSelectionChanged(e.NewIndex));

        _detailText = new TextBlock(() => _detailValue)
        {
            Wrap = true,
            HorizontalAlignment = Align.Stretch,
            VerticalAlignment = Align.Stretch,
        };

        var contentGrid = new Grid
            {
                HorizontalAlignment = Align.Stretch,
                VerticalAlignment = Align.Stretch,
            }
            .Rows(
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Star(1) })
            .Columns(
                new ColumnDefinition { Width = GridLength.Star(1) },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Star(2) });

        contentGrid.Cell(toolbar, 0, 0, columnSpan: 3);
        contentGrid.Cell(_summaryMarkup, 1, 0, columnSpan: 3);
        contentGrid.Cell(new ScrollViewer(_itemSelect) { HorizontalAlignment = Align.Stretch, VerticalAlignment = Align.Stretch }, 2, 0);
        contentGrid.Cell(new ScrollViewer(_detailText) { HorizontalAlignment = Align.Stretch, VerticalAlignment = Align.Stretch }, 2, 2);

        _dialog = new Dialog()
            .Title("ACP Agents")
            .TopRightText(closeButton)
            .BottomRightText(new Markup("[dim]Esc Close[/]"))
            .IsModal(true)
            .Padding(1)
            .Content(contentGrid);
        ResponsiveDialogSize.Apply(_dialog, getBounds(), minWidth: 96, minHeight: 22, widthFactor: 0.88, heightFactor: 0.82);
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
            var scope = _scopeSelect.Value;
            var filter = _filterSelect.SelectedIndex >= 0 && _filterSelect.SelectedIndex < _filterSelect.Items.Count
                ? _filterSelect.Items[_filterSelect.SelectedIndex]
                : GetFilterOptions(scope)[0];

            var filteredItems = _snapshot.Items
                .Where(item => scope == AcpManagementScope.Catalog ? item.IsInRegistry : item.IsInstalled || item.HasConfiguration || item.IsManual)
                .Where(item => MatchesFilter(item, filter.Kind))
                .Select(item => new AcpDialogListItem(item, scope == AcpManagementScope.Catalog ? item.CatalogLabel : item.InstalledLabel))
                .ToArray();

            _visibleItems = filteredItems;
            var selectedAgentId = _selectedAgentId;
            _itemSelect.Items.Clear();
            foreach (var item in filteredItems)
            {
                _itemSelect.Items.Add(item);
            }

            if (_itemSelect.Items.Count == 0)
            {
                _itemSelect.SelectedIndex = -1;
                _selectedAgentId = null;
                _detailValue = "No ACP agents match the current view.";
                _summaryText = BuildSummaryMarkup();
                return;
            }

            var newIndex = selectedAgentId is null
                ? 0
                : Array.FindIndex(filteredItems, item => string.Equals(item.Item.AgentId, selectedAgentId, StringComparison.OrdinalIgnoreCase));
            _itemSelect.SelectedIndex = newIndex >= 0 ? newIndex : 0;
            OnSelectionChanged(_itemSelect.SelectedIndex);
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

    private void OnSelectionChanged(int newIndex)
    {
        if ((uint)newIndex >= (uint)_visibleItems.Count)
        {
            _selectedAgentId = null;
            _detailValue = "Select an ACP agent to inspect its registry, install, and runtime details.";
            return;
        }

        var item = _visibleItems[newIndex].Item;
        _selectedAgentId = item.AgentId;
        _detailValue = BuildDetailText();
    }

    private AcpAgentSummaryItem? GetSelectedItem()
    {
        return _visibleItems.FirstOrDefault(entry => string.Equals(entry.Item.AgentId, _selectedAgentId, StringComparison.OrdinalIgnoreCase))?.Item;
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
        var selected = _visibleItems.FirstOrDefault(entry => string.Equals(entry.Item.AgentId, _selectedAgentId, StringComparison.OrdinalIgnoreCase))?.Item;
        if (selected is null)
        {
            return "Select an ACP agent to inspect its registry, install, and runtime details.";
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
            $"Name: {selected.DisplayName}\n" +
            $"Agent Id: {selected.AgentId}\n" +
            $"Registry Id: {selected.RegistryId ?? "None"}\n" +
            $"Registry Version: {selected.RegistryVersion ?? "Unknown"}\n" +
            $"In Registry: {(selected.IsInRegistry ? "Yes" : "No")}\n" +
            $"Installed: {(selected.IsInstalled ? "Yes" : "No")}\n" +
            $"Configured: {(selected.HasConfiguration ? "Yes" : "No")}\n" +
            $"Enabled: {(selected.IsEnabled ? "Yes" : "No")}\n" +
            $"Manual: {(selected.IsManual ? "Yes" : "No")}\n" +
            $"Broken: {(selected.IsBroken ? "Yes" : "No")}\n\n" +
            $"Description:\n{selected.Description ?? "No description."}\n\n" +
            $"Authors: {authors}\n" +
            $"License: {selected.License ?? "Unknown"}\n" +
            $"Repository: {selected.Repository ?? "Unavailable"}\n" +
            $"Website: {selected.Website ?? "Unavailable"}\n\n" +
            $"Distribution: {distributions}\n" +
            $"Installability: {selected.InstallabilityMessage}\n" +
            $"Command: {selected.CommandSummary ?? "Unavailable"}\n" +
            $"Working Directory: {selected.WorkingDirectory ?? "Default"}\n\n" +
            $"Runtime: {runtimeState}\n" +
            $"Runtime Status: {selected.RuntimeStatus ?? "No runtime probe yet."}\n" +
            $"Models ({selected.RuntimeModelCount ?? 0}, runtime-discovered): {runtimeModels}";
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

internal sealed record AcpDialogListItem(AcpAgentSummaryItem Item, string Label)
{
    public override string ToString() => Label;
}
