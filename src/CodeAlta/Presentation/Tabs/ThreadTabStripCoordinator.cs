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

    public bool TrySelectRelativeTab(int delta)
    {
        var tabControl = _threadTabs.GetTabControl();
        if (tabControl is null || tabControl.Tabs.Count == 0)
        {
            return false;
        }

        var selectedIndex = ResolveSelectedIndex(tabControl);
        var targetIndex = GetAdjacentTabIndex(selectedIndex, tabControl.Tabs.Count, delta);
        if (targetIndex == selectedIndex)
        {
            return false;
        }

        tabControl.SelectedIndex = targetIndex;
        OnSelectionChanged(targetIndex);
        return true;
    }

    internal static int GetAdjacentTabIndex(int selectedIndex, int tabCount, int delta)
    {
        if (tabCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tabCount));
        }

        if (selectedIndex < 0 || selectedIndex >= tabCount)
        {
            throw new ArgumentOutOfRangeException(nameof(selectedIndex));
        }

        if (delta == 0 || tabCount == 1)
        {
            return selectedIndex;
        }

        var offset = delta % tabCount;
        var targetIndex = (selectedIndex + offset) % tabCount;
        return targetIndex < 0 ? targetIndex + tabCount : targetIndex;
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
                ? EnsureDraftPage(CanCloseTab(tab, projection.Tabs.Count))
                : EnsureThreadPage(tab.TabId, CanCloseTab(tab, projection.Tabs.Count)));
        }

        return pages;
    }

    internal static bool CanCloseTab(ThreadTabStripItemProjection tab, int totalTabCount)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(totalTabCount);
        return !tab.IsDraft || totalTabCount > 1;
    }

    private TabPage EnsureThreadPage(string threadId, bool canClose)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        var workspaceView = _threadTabs.GetWorkspaceView() ?? throw new InvalidOperationException("Thread workspace view is not initialized.");

        if (workspaceView.TryGetTabPage(threadId, out var existingPage))
        {
            existingPage.Data = threadId;
            existingPage.ShowCloseButton = canClose;
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
            ShowCloseButton = canClose,
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

    private TabPage EnsureDraftPage(bool canClose)
    {
        var workspaceView = _threadTabs.GetWorkspaceView() ?? throw new InvalidOperationException("Thread workspace view is not initialized.");
        if (workspaceView.TryGetTabPage(CodeAltaApp.DraftTabId, out var existingPage))
        {
            existingPage.Data = CodeAltaApp.DraftTabId;
            existingPage.ShowCloseButton = canClose;
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
            ShowCloseButton = canClose,
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

    private int ResolveSelectedIndex(TabControl tabControl)
    {
        ArgumentNullException.ThrowIfNull(tabControl);

        if (tabControl.SelectedIndex >= 0 && tabControl.SelectedIndex < tabControl.Tabs.Count)
        {
            return tabControl.SelectedIndex;
        }

        var selection = _threadSelection.Selection;
        var selectedTabId = selection.Target is WorkspaceTarget.Draft
            ? CodeAltaApp.DraftTabId
            : selection.SelectedThreadId;
        if (!string.IsNullOrWhiteSpace(selectedTabId))
        {
            for (var i = 0; i < tabControl.Tabs.Count; i++)
            {
                if (string.Equals(tabControl.Tabs[i].Data as string, selectedTabId, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
        }

        return 0;
    }
}
