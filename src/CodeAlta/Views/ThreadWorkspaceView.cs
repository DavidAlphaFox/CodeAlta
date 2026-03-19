using CodeAlta.ViewModels;
using XenoAtom.Ansi;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Commands;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Layout;
using XenoAtom.Terminal.UI.Styling;

internal sealed class ThreadWorkspaceView
{
    private readonly Func<CodeAltaApp.StatusTone> _getStatusTone;
    private Markup? _statusIconVisual;

    public ThreadWorkspaceView(
        CodeAltaShellViewModel shellViewModel,
        ThreadWorkspaceViewModel workspaceViewModel,
        PromptComposerViewModel promptComposerViewModel,
        Spinner statusSpinner,
        Func<Visual> buildSessionUsageIndicatorVisual,
        Func<CodeAltaApp.StatusTone> getStatusTone,
        Func<ChatPromptEditor> createPromptEditor,
        Action sendPrompt,
        Action<int> onThreadTabSelectionChanged,
        Action<int> onChatBackendSelectionChanged,
        Action<int> onChatModelSelectionChanged,
        Action<int> onChatReasoningSelectionChanged,
        Action onAutoScrollChanged)
    {
        ArgumentNullException.ThrowIfNull(shellViewModel);
        ArgumentNullException.ThrowIfNull(workspaceViewModel);
        ArgumentNullException.ThrowIfNull(promptComposerViewModel);
        ArgumentNullException.ThrowIfNull(statusSpinner);
        ArgumentNullException.ThrowIfNull(buildSessionUsageIndicatorVisual);
        ArgumentNullException.ThrowIfNull(getStatusTone);
        ArgumentNullException.ThrowIfNull(createPromptEditor);
        ArgumentNullException.ThrowIfNull(sendPrompt);
        ArgumentNullException.ThrowIfNull(onThreadTabSelectionChanged);
        ArgumentNullException.ThrowIfNull(onChatBackendSelectionChanged);
        ArgumentNullException.ThrowIfNull(onChatModelSelectionChanged);
        ArgumentNullException.ThrowIfNull(onChatReasoningSelectionChanged);
        ArgumentNullException.ThrowIfNull(onAutoScrollChanged);

        _getStatusTone = getStatusTone;
        ThreadCommandBar = new CommandBar
        {
            HorizontalAlignment = Align.Stretch,
        };

        ThreadTabControl = new TabControl()
            .Style(TabControlStyle.NoBorder);
        ThreadTabControl.RegisterDynamicUpdate(_ => onThreadTabSelectionChanged(ThreadTabControl.SelectedIndex));

        ThreadInput = createPromptEditor();
        ThreadInput.RegisterDynamicUpdate(_ => ThreadInput.IsEnabled = promptComposerViewModel.IsEnabled);
        ThreadInputView = ThreadInput.Scrollable();

        SendPromptButton = new Button(new TextBlock($"{NerdFont.MdSend} Send"))
            .Click(sendPrompt);
        SendPromptButton.RegisterDynamicUpdate(_ => SendPromptButton.IsEnabled = promptComposerViewModel.CanSend);
        ChatBackendSelect = new Select<ChatBackendOption>()
            .SelectionChanged((_, e) => onChatBackendSelectionChanged(e.NewIndex))
            .MinWidth(14)
            .MaxWidth(22);
        ChatBackendSelect.RegisterDynamicUpdate(_ => ChatBackendSelect.IsEnabled = workspaceViewModel.CanSelectBackend);
        ChatModelSelect = new Select<ChatModelOption>()
            .SelectionChanged((_, e) => onChatModelSelectionChanged(e.NewIndex))
            .MinWidth(18)
            .MaxWidth(36);
        ChatModelSelect.RegisterDynamicUpdate(_ => ChatModelSelect.IsEnabled = workspaceViewModel.CanSelectModel);
        ChatReasoningSelect = new Select<ChatReasoningOption>()
            .SelectionChanged((_, e) => onChatReasoningSelectionChanged(e.NewIndex))
            .MinWidth(12)
            .MaxWidth(22);
        ChatReasoningSelect.RegisterDynamicUpdate(_ => ChatReasoningSelect.IsEnabled = workspaceViewModel.CanSelectReasoning);
        ChatAutoScrollCheckBox = new CheckBox("AutoScroll", isChecked: true);
        ChatAutoScrollCheckBox.RegisterDynamicUpdate(_ => onAutoScrollChanged());
        ChatAutoScrollCheckBox.RegisterDynamicUpdate(_ => ChatAutoScrollCheckBox.IsEnabled = workspaceViewModel.CanToggleAutoScroll);

        var usageIndicator = buildSessionUsageIndicatorVisual();
        var statusPrefix = new Center(
            new ComputedVisual(
                () => shellViewModel.StatusBusy
                    ? statusSpinner
                    : _statusIconVisual ??= new Markup(() => shellViewModel.StatusIconMarkup)
                    {
                        Wrap = false,
                    }))
        {
            MinWidth = 2,
            MaxWidth = 2,
        };

        var statusLine = new HStack(
            [
                statusPrefix,
                new TextBlock
                {
                    Wrap = true,
                    IsSelectable = false,
                }.Text(() => shellViewModel.StatusText)
                    .Style(() => CodeAltaApp.BuildStatusTextStyle(shellViewModel.StatusText, shellViewModel.StatusBusy, _getStatusTone())),
            ])
        {
            Spacing = 1,
            HorizontalAlignment = Align.Stretch,
        };

        var selectionControls = new HStack(
            [
                SendPromptButton,
                ChatBackendSelect,
                ChatModelSelect,
                ChatReasoningSelect,
                ChatAutoScrollCheckBox,
            ])
        {
            Spacing = 2,
        };

        var selectionRight = new HStack(
            [
                new Markup(() => workspaceViewModel.BackendStatusMarkup)
                {
                    Wrap = false,
                },
                usageIndicator,
            ])
        {
            Spacing = 2,
        };

        var selectionLine = new StatusBar()
            .LeftText(selectionControls)
            .RightText(selectionRight);

        ThreadBottomPanel = new DockLayout(
            top: statusLine,
            content: ThreadInputView,
            bottom: selectionLine)
        {
            HorizontalAlignment = Align.Stretch,
            VerticalAlignment = Align.Stretch,
        };

        ThreadBodySplitter = new VSplitter(new TextBlock("Open or create a thread to start working."), ThreadBottomPanel)
        {
            Ratio = 0.75,
            MinFirst = 6,
            MinSecond = 7,
        };

        var threadPaneLayout = new Grid
        {
            HorizontalAlignment = Align.Stretch,
            VerticalAlignment = Align.Stretch,
        }
        .Rows(
            new RowDefinition { Height = GridLength.Auto },
            new RowDefinition { Height = GridLength.Star(1) })
        .Columns(
            new ColumnDefinition { Width = GridLength.Star(1) });
        threadPaneLayout.Cell(ThreadTabControl.Stretch(), 0, 0);
        threadPaneLayout.Cell(ThreadBodySplitter, 1, 0);

        ThreadPaneLayout = threadPaneLayout;
        Root = ThreadPaneLayout;
    }

    public Visual Root { get; }

    public Visual ThreadPaneLayout { get; }

    public Visual ThreadBottomPanel { get; }

    public VSplitter ThreadBodySplitter { get; }

    public ChatPromptEditor ThreadInput { get; }

    public Visual ThreadInputView { get; }

    public Button SendPromptButton { get; }

    public CommandBar ThreadCommandBar { get; }

    public Select<ChatBackendOption> ChatBackendSelect { get; }

    public Select<ChatModelOption> ChatModelSelect { get; }

    public Select<ChatReasoningOption> ChatReasoningSelect { get; }

    public CheckBox ChatAutoScrollCheckBox { get; }

    public TabControl ThreadTabControl { get; }
}
