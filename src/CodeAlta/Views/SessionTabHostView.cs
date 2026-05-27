using System.Diagnostics.CodeAnalysis;
using CodeAlta.Presentation.Chat;
using CodeAlta.ViewModels;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;

namespace CodeAlta.Views;

internal sealed class SessionTabHostView
{
    private readonly Dictionary<string, TabPage> _tabPages = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, VSplitter> _sessionTabContentSplitters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SessionPromptPanel> _sessionPromptPanels = new(StringComparer.OrdinalIgnoreCase);
    private string? _activeSessionTabContentId;

    public SessionTabHostView(SessionTabHostController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        SessionTabControl = new TabControl();
        SessionTabControl.SelectionChanged((_, e) => controller.SelectTab(e.NewIndex));

        var sessionPaneLayout = new Grid
            {
                HorizontalAlignment = Align.Stretch,
                VerticalAlignment = Align.Stretch,
            }
            .Rows(
                new RowDefinition { Height = GridLength.Star(1) })
            .Columns(
                new ColumnDefinition { Width = GridLength.Star(1) });
        sessionPaneLayout.Cell(SessionTabControl.Stretch(), 0, 0);
        Root = sessionPaneLayout;
    }

    public Visual Root { get; }

    public TabControl SessionTabControl { get; }

    public SessionPromptPanel? ActivePromptPanel
        => !string.IsNullOrWhiteSpace(_activeSessionTabContentId) &&
           _sessionPromptPanels.TryGetValue(_activeSessionTabContentId, out var panel)
            ? panel
            : null;

    public bool TryGetTabPage(string tabId, [NotNullWhen(true)] out TabPage? page)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tabId);
        return _tabPages.TryGetValue(tabId, out page);
    }

    public bool TryGetPromptPanel(string tabId, [NotNullWhen(true)] out SessionPromptPanel? panel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tabId);
        return _sessionPromptPanels.TryGetValue(tabId, out panel);
    }

    public void RememberTabPage(string tabId, TabPage page)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tabId);
        ArgumentNullException.ThrowIfNull(page);
        _tabPages[tabId] = page;
    }

    public bool RemoveTabPage(string tabId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tabId);
        var removed = _tabPages.Remove(tabId, out var page);
        if (removed && page is not null)
        {
            SessionTabControl.TryCloseTab(page);
        }

        var splitter = _sessionTabContentSplitters.Remove(tabId, out var cachedSplitter)
            ? cachedSplitter
            : page?.Content as VSplitter;
        if (splitter is not null)
        {
            splitter.First = null;
            splitter.Second = null;
        }

        _sessionPromptPanels.Remove(tabId);
        if (string.Equals(_activeSessionTabContentId, tabId, StringComparison.OrdinalIgnoreCase))
        {
            _activeSessionTabContentId = null;
        }

        return removed;
    }

    public Visual CreateSessionTabContent(string tabId, Visual primaryContent, Func<SessionPromptPanel> promptPanelFactory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tabId);
        ArgumentNullException.ThrowIfNull(primaryContent);
        ArgumentNullException.ThrowIfNull(promptPanelFactory);

        if (_sessionTabContentSplitters.TryGetValue(tabId, out var existing))
        {
            return existing;
        }

        if (primaryContent.Parent is not null)
        {
            throw new InvalidOperationException($"Cannot create prompt tab content for '{tabId}' because the primary content is already attached to a visual tree.");
        }

        var promptPanel = promptPanelFactory();
        var splitter = new VSplitter
        {
            First = primaryContent,
            Second = promptPanel.Root,
            Ratio = 0.75,
            MinFirst = 6,
            MinSecond = 7,
        };
        _sessionPromptPanels[tabId] = promptPanel;
        _sessionTabContentSplitters[tabId] = splitter;
        return splitter;
    }

    public void ActivateSessionTabContent(string? tabId)
    {
        _activeSessionTabContentId = null;
        if (string.IsNullOrWhiteSpace(tabId))
        {
            return;
        }

        if (!_sessionTabContentSplitters.ContainsKey(tabId))
        {
            if (!_tabPages.TryGetValue(tabId, out var page) || page.Content is not VSplitter splitter)
            {
                return;
            }

            _sessionTabContentSplitters[tabId] = splitter;
        }

        if (!_sessionPromptPanels.ContainsKey(tabId))
        {
            return;
        }

        _activeSessionTabContentId = tabId;
    }
}

internal sealed class SessionPromptPanel
{
    public SessionPromptPanel(
        Visual root,
        ChatPromptEditor editor,
        Visual editorView,
        Visual sendPromptButton,
        Visual expandPromptButton,
        PromptComposerView composer,
        ModelProviderSelectorView modelProviderSelectorView,
        SessionPromptChromeState chromeState)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(editorView);
        ArgumentNullException.ThrowIfNull(sendPromptButton);
        ArgumentNullException.ThrowIfNull(expandPromptButton);
        ArgumentNullException.ThrowIfNull(composer);
        ArgumentNullException.ThrowIfNull(modelProviderSelectorView);
        ArgumentNullException.ThrowIfNull(chromeState);

        Root = root;
        Editor = editor;
        EditorView = editorView;
        SendPromptButton = sendPromptButton;
        ExpandPromptButton = expandPromptButton;
        Composer = composer;
        ModelProviderSelectorView = modelProviderSelectorView;
        ChromeState = chromeState;
    }

    public SessionPromptChromeState ChromeState { get; }

    public Visual Root { get; }

    public ChatPromptEditor Editor { get; }

    public Visual EditorView { get; }

    public Visual SendPromptButton { get; }

    public Visual ExpandPromptButton { get; }

    public PromptComposerView Composer { get; }

    public ModelProviderSelectorView ModelProviderSelectorView { get; }

    public CodeAltaShellViewModel ShellViewModel => ChromeState.ShellViewModel;

    public SessionWorkspaceViewModel WorkspaceViewModel => ChromeState.WorkspaceViewModel;

    public PromptComposerViewModel PromptComposerViewModel => ChromeState.PromptComposerViewModel;
}
