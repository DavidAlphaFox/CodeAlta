using CodeAlta.App;
using CodeAlta.App.Context;
using CodeAlta.Catalog;
using CodeAlta.Models;
using CodeAlta.Presentation.Shell;
using CodeAlta.Views;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace CodeAlta.Presentation.Tabs;

internal sealed class ThreadTabStripCoordinator
{
    private readonly ThreadSelectionContext _threadSelection;
    private readonly ThreadTabContext _threadTabs;
    private bool _syncingSelection;
    private bool _syncingPages;
    private int _lastObservedSelectedIndex = -1;
    private string? _pendingThreadSelectionThreadId;

    public ThreadTabStripCoordinator(
        ThreadSelectionContext threadSelection,
        ThreadTabContext threadTabs)
    {
        ArgumentNullException.ThrowIfNull(threadSelection);
        ArgumentNullException.ThrowIfNull(threadTabs);

        _threadSelection = threadSelection;
        _threadTabs = threadTabs;
    }

    public void SyncControl()
    {
        var tabControl = _threadTabs.GetTabControl();
        if (tabControl is null)
        {
            return;
        }

        _syncingPages = true;
        try
        {
            var projection = BuildProjection();
            var desiredPages = BuildDesiredPages(projection);

            tabControl.IsVisible = projection.Tabs.Count > 0;

            var existingPages = tabControl.Tabs;
            var matches = existingPages.Count == desiredPages.Count;
            if (matches)
            {
                for (var i = 0; i < desiredPages.Count; i++)
                {
                    if (!ReferenceEquals(existingPages[i], desiredPages[i]))
                    {
                        matches = false;
                        break;
                    }
                }
            }

            if (!matches)
            {
                for (var i = existingPages.Count - 1; i >= 0; i--)
                {
                    tabControl.TryCloseTab(existingPages[i]);
                }

                foreach (var page in desiredPages)
                {
                    tabControl.AddTab(page);
                }
            }

            SyncSelection(projection, tabControl);
        }
        finally
        {
            _syncingPages = false;
        }
    }

    public void OnSelectionChanged(int selectedIndex)
    {
        var tabControl = _threadTabs.GetTabControl();
        if (_syncingSelection || _syncingPages || tabControl is null)
        {
            return;
        }

        if (selectedIndex < 0 || selectedIndex >= tabControl.Tabs.Count)
        {
            return;
        }

        var selection = _threadSelection.Selection;
        if (string.Equals(tabControl.Tabs[selectedIndex].Data as string, CodeAltaApp.DraftTabId, StringComparison.Ordinal))
        {
            if (selection.Target is WorkspaceTarget.Draft)
            {
                return;
            }

            _threadTabs.ActivateDraftTab();
            return;
        }

        if (tabControl.Tabs[selectedIndex].Data is not string threadId ||
            string.Equals(threadId, selection.SelectedThreadId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.Equals(threadId, _pendingThreadSelectionThreadId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _pendingThreadSelectionThreadId = threadId;
        _threadTabs.GetUiDispatcher().Post(
            () =>
            {
                if (!string.Equals(threadId, _pendingThreadSelectionThreadId, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                _pendingThreadSelectionThreadId = null;
                _threadTabs.OpenThread(threadId);
            });
    }

    public void ObserveBoundSelection(int selectedIndex)
    {
        if (_lastObservedSelectedIndex == selectedIndex)
        {
            return;
        }

        _lastObservedSelectedIndex = selectedIndex;
        OnSelectionChanged(selectedIndex);
    }

    public void ResetPendingSelection()
    {
        _pendingThreadSelectionThreadId = null;
    }

    private ThreadTabStripProjection BuildProjection()
    {
        var selection = _threadSelection.Selection;
        var availableThreadIds = _threadSelection.OpenThreadIds
            .Select(_threadSelection.FindThread)
            .Where(static thread => thread is not null)
            .Select(thread =>
            {
                var current = thread!;
                _threadSelection.EnsureThreadTab(current);
                return current.ThreadId;
            })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return ThreadTabStripProjectionBuilder.Build(
            _threadSelection.OpenThreadIds,
            availableThreadIds,
            selection.Target is WorkspaceTarget.Draft,
            CodeAltaApp.DraftTabId,
            selection.SelectedThreadId);
    }

    private List<TabPage> BuildDesiredPages(ThreadTabStripProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);

        var pages = new List<TabPage>(projection.Tabs.Count);
        foreach (var tab in projection.Tabs)
        {
            pages.Add(tab.IsDraft
                ? EnsureDraftPage()
                : EnsureThreadPage(tab.TabId));
        }

        return pages;
    }

    private TabPage EnsureThreadPage(string threadId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        var workspaceView = _threadTabs.GetWorkspaceView() ?? throw new InvalidOperationException("Thread workspace view is not initialized.");

        if (workspaceView.TryGetTabPage(threadId, out var existingPage))
        {
            existingPage.Data = threadId;
            return existingPage;
        }

        var thread = _threadSelection.FindThread(threadId);
        if (thread is null)
        {
            throw new InvalidOperationException($"Thread '{threadId}' was not found when creating a tab page.");
        }

        var tab = _threadSelection.EnsureThreadTab(thread);
        var header = _threadTabs.CreateComputedVisual(
            () =>
            {
                return new HStack(
                [
                    ThreadTabVisualFactory.CreateIndicator(tab.ViewModel.StatusBusy, tab.HasPromptDraft, tab.ViewModel.StatusTone),
                    ThreadTabVisualFactory.CreateTitle(ThreadTabVisualFactory.CompactTitle(tab.ViewModel.Title)),
                ])
                {
                    Spacing = 1,
                };
            });

        var page = new TabPage(header, CodeAltaApp.CreateThreadTabPageContentPlaceholder())
        {
            Data = thread.ThreadId,
            ShowCloseButton = true,
        };
        page.RequestClosing += (_, e) =>
        {
            if (e.Reason != TabCloseReason.CloseButton || e.Page.Data is not string currentThreadId)
            {
                return;
            }

            e.Cancel = true;
            _threadTabs.CloseThread(currentThreadId);
        };

        workspaceView.RememberTabPage(thread.ThreadId, page);
        return page;
    }

    private TabPage EnsureDraftPage()
    {
        var workspaceView = _threadTabs.GetWorkspaceView() ?? throw new InvalidOperationException("Thread workspace view is not initialized.");
        if (workspaceView.TryGetTabPage(CodeAltaApp.DraftTabId, out var existingPage))
        {
            existingPage.Data = CodeAltaApp.DraftTabId;
            return existingPage;
        }

        var header = _threadTabs.CreateComputedVisual(
            () => new HStack(
            [
                ThreadTabVisualFactory.CreateIndicator(isBusy: false, StatusTone.Info),
                ThreadTabVisualFactory.CreateTitle(ShellTextFormatter.BuildDraftTabTitle(
                    _threadSelection.GetSelectedProject(),
                    _threadSelection.IsGlobalDraftSelected())),
            ])
            {
                Spacing = 1,
            });

        var page = new TabPage(header, CodeAltaApp.CreateThreadTabPageContentPlaceholder())
        {
            Data = CodeAltaApp.DraftTabId,
            ShowCloseButton = true,
        };
        page.RequestClosing += (_, e) =>
        {
            if (e.Reason != TabCloseReason.CloseButton || !string.Equals(e.Page.Data as string, CodeAltaApp.DraftTabId, StringComparison.Ordinal))
            {
                return;
            }

            e.Cancel = true;
            _threadTabs.CloseDraftTab();
        };

        workspaceView.RememberTabPage(CodeAltaApp.DraftTabId, page);
        return page;
    }

    private void SyncSelection(ThreadTabStripProjection projection, TabControl tabControl)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(tabControl);

        if (tabControl.Tabs.Count == 0 || string.IsNullOrWhiteSpace(projection.SelectedTabId))
        {
            return;
        }

        var selectedIndex = -1;
        for (var i = 0; i < tabControl.Tabs.Count; i++)
        {
            if (tabControl.Tabs[i].Data is string threadId &&
                string.Equals(threadId, projection.SelectedTabId, StringComparison.OrdinalIgnoreCase))
            {
                selectedIndex = i;
                break;
            }
        }

        if (selectedIndex < 0 || tabControl.SelectedIndex == selectedIndex)
        {
            return;
        }

        _syncingSelection = true;
        try
        {
            tabControl.SelectedIndex = selectedIndex;
        }
        finally
        {
            _syncingSelection = false;
        }
    }
}
