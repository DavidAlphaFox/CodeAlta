using CodeAlta.Models;
using CodeAlta.Presentation.Chat;
using CodeAlta.ViewModels;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Templating;

namespace CodeAlta.Views;

internal sealed class UserPromptSelectorView
{
    public UserPromptSelectorView(SessionWorkspaceViewModel workspaceViewModel, UserPromptSelectorController controller)
    {
        ArgumentNullException.ThrowIfNull(workspaceViewModel);
        ArgumentNullException.ThrowIfNull(controller);

        PromptSelect = new Select<UserPromptOption>()
            .SelectedIndex(workspaceViewModel.Bind.SelectedUserPromptIndex)
            .ItemTemplate(new DataTemplate<UserPromptOption>(
                static (DataTemplateValue<UserPromptOption> value, in DataTemplateContext _) =>
                    new Markup(UserPromptPresentation.BuildPromptOptionMarkup(value.GetValue()))
                    {
                        Wrap = false,
                    },
                null))
            .MinWidth(18)
            .MaxWidth(40)
            .IsEnabled(workspaceViewModel.Bind.CanSelectUserPrompt);
        PromptDialogButton = new Button(new TextBlock("Prompt->") { Wrap = false, IsSelectable = false })
            .Click(controller.OpenPrompts);
        var promptDialogButtonHost = PromptDialogButton.Tooltip(new TextBlock("Open the prompt dialog."));

        Root = new HStack(
        [
            promptDialogButtonHost,
            PromptSelect,
        ])
        {
            Spacing = 1,
        };
    }

    public Visual Root { get; }

    public Button PromptDialogButton { get; }

    public Select<UserPromptOption> PromptSelect { get; }

    public void SyncItems(SessionWorkspaceViewModel workspaceViewModel)
    {
        ArgumentNullException.ThrowIfNull(workspaceViewModel);
        using var _ = workspaceViewModel.SuppressSelectionChangedNotifications();
        UserPromptPresentation.ReplaceSelectItems(PromptSelect, workspaceViewModel.UserPromptOptions);
        PromptSelect.SelectedIndex = workspaceViewModel.SelectedUserPromptIndex;
    }
}
