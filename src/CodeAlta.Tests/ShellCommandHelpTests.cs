using CodeAlta.Frontend.Commands;
using CodeAlta.Frontend.Help;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI.Input;

namespace CodeAlta.Tests;

[TestClass]
public sealed class ShellCommandHelpTests
{
    [TestMethod]
    public void BuildSections_UsesShellCommandCatalogMetadata()
    {
        var helpCommand = ShellCommandCatalog.Get("CodeAlta.Shell.Help");
        var paletteCommand = ShellCommandCatalog.Get("CodeAlta.Shell.CommandPalette");
        var openFolderCommand = ShellCommandCatalog.Get("CodeAlta.Project.OpenFolder");
        var goToSidebarCommand = ShellCommandCatalog.Get("CodeAlta.Shell.FocusSidebar");
        var goToPromptCommand = ShellCommandCatalog.Get("CodeAlta.Shell.FocusPrompt");
        var fullPromptCommand = ShellCommandCatalog.Get("CodeAlta.Thread.ExpandPrompt");

        var sections = ShellHelpContentBuilder.BuildSections();
        var entry = sections
            .SelectMany(static section => section.Entries)
            .Single(candidate => string.Equals(candidate.Label, helpCommand.Label, StringComparison.Ordinal));
        var paletteEntry = sections
            .SelectMany(static section => section.Entries)
            .Single(candidate => string.Equals(candidate.Label, paletteCommand.Label, StringComparison.Ordinal));
        var openFolderEntry = sections
            .SelectMany(static section => section.Entries)
            .Single(candidate => string.Equals(candidate.Label, openFolderCommand.Label, StringComparison.Ordinal));
        var goToSidebarEntry = sections
            .SelectMany(static section => section.Entries)
            .Single(candidate => string.Equals(candidate.Label, goToSidebarCommand.Label, StringComparison.Ordinal));
        var goToPromptEntry = sections
            .SelectMany(static section => section.Entries)
            .Single(candidate => string.Equals(candidate.Label, goToPromptCommand.Label, StringComparison.Ordinal));
        var fullPromptEntry = sections
            .SelectMany(static section => section.Entries)
            .Single(candidate => string.Equals(candidate.Label, fullPromptCommand.Label, StringComparison.Ordinal));

        CollectionAssert.Contains(entry.Bindings.ToArray(), "/help");
        CollectionAssert.Contains(entry.Bindings.ToArray(), "?");
        CollectionAssert.Contains(paletteEntry.Bindings.ToArray(), "/");
        CollectionAssert.Contains(openFolderEntry.Bindings.ToArray(), "/open_folder");
        CollectionAssert.Contains(openFolderEntry.Bindings.ToArray(), "/open");
        CollectionAssert.Contains(goToSidebarEntry.Bindings.ToArray(), "/go_to_sidebar");
        CollectionAssert.Contains(goToSidebarEntry.Bindings.ToArray(), "/sidebar");
        CollectionAssert.Contains(goToPromptEntry.Bindings.ToArray(), "/go_to_prompt");
        CollectionAssert.Contains(goToPromptEntry.Bindings.ToArray(), "/prompt");
        CollectionAssert.Contains(fullPromptEntry.Bindings.ToArray(), "/full_prompt");
    }

    [TestMethod]
    public void BuildSections_FilterMatchesAliases()
    {
        var sections = ShellHelpContentBuilder.BuildSections("compact");
        var entries = sections.SelectMany(static section => section.Entries).ToArray();

        Assert.AreEqual(1, entries.Length);
        Assert.AreEqual("Compact", entries[0].Label);
    }

    [TestMethod]
    public void ShellCommandMetadata_UsesCommandStyleIdentifiers()
    {
        var fullPromptCommand = ShellCommandCatalog.Get("CodeAlta.Thread.ExpandPrompt");
        var closeTabCommand = ShellCommandCatalog.Get("CodeAlta.Thread.CloseTab");
        var openFolderCommand = ShellCommandCatalog.Get("CodeAlta.Project.OpenFolder");
        var tabLeftCommand = ShellCommandCatalog.Get("CodeAlta.Thread.TabLeft");
        var tabRightCommand = ShellCommandCatalog.Get("CodeAlta.Thread.TabRight");

        Assert.AreEqual("full_prompt", fullPromptCommand.CommandName);
        Assert.AreEqual("/full_prompt", fullPromptCommand.SlashCommandText);
        CollectionAssert.Contains(fullPromptCommand.Aliases.ToArray(), "full_prompt");
        StringAssert.Contains(fullPromptCommand.CommandSearchText, "/full_prompt");
        Assert.AreEqual("close_tab", closeTabCommand.CommandName);
        Assert.AreEqual("/close_tab", closeTabCommand.SlashCommandText);
        CollectionAssert.Contains(closeTabCommand.Aliases.ToArray(), "close_tab");
        CollectionAssert.Contains(closeTabCommand.Aliases.ToArray(), "close");
        StringAssert.Contains(closeTabCommand.CommandSearchText, "/close_tab");
        StringAssert.Contains(closeTabCommand.CommandSearchText, "/close");
        Assert.AreEqual(new KeyGesture(TerminalChar.CtrlW, TerminalModifiers.Ctrl), closeTabCommand.Gesture);
        Assert.AreEqual("open_folder", openFolderCommand.CommandName);
        CollectionAssert.Contains(openFolderCommand.Aliases.ToArray(), "open");
        StringAssert.Contains(openFolderCommand.CommandSearchText, "/open");
        Assert.AreEqual("tab_left", tabLeftCommand.CommandName);
        Assert.AreEqual("/tab_left", tabLeftCommand.SlashCommandText);
        CollectionAssert.Contains(tabLeftCommand.Aliases.ToArray(), "tab_left");
        StringAssert.Contains(tabLeftCommand.CommandSearchText, "/tab_left");
        Assert.AreEqual(new KeyGesture(TerminalKey.Left, TerminalModifiers.Alt), tabLeftCommand.Gesture);
        Assert.AreEqual("tab_right", tabRightCommand.CommandName);
        Assert.AreEqual("/tab_right", tabRightCommand.SlashCommandText);
        CollectionAssert.Contains(tabRightCommand.Aliases.ToArray(), "tab_right");
        StringAssert.Contains(tabRightCommand.CommandSearchText, "/tab_right");
        Assert.AreEqual(new KeyGesture(TerminalKey.Right, TerminalModifiers.Alt), tabRightCommand.Gesture);
    }
}
