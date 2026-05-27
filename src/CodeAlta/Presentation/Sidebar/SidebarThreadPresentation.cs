using CodeAlta.Agent;
using CodeAlta.Catalog;
using CodeAlta.Presentation.Styling;
using XenoAtom.Ansi;
using XenoAtom.Terminal.UI;

namespace CodeAlta.Presentation.Sidebar;

internal static class SidebarThreadPresentation
{
    public static SidebarAccent ResolveThreadAccent(string? providerKey, WorkThreadKind kind)
    {
        _ = providerKey;
        return kind switch
        {
            WorkThreadKind.GlobalThread => SidebarAccent.Global,
            WorkThreadKind.ProjectThread => SidebarAccent.ProjectThread,
            WorkThreadKind.InternalThread => SidebarAccent.InternalThread,
            _ => SidebarAccent.Fallback,
        };
    }

    public static string ResolveProviderDisplayName(string? providerKey, string? displayName = null)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            return displayName.Trim();
        }

        if (string.Equals(providerKey, ModelProviderIds.Copilot.Value, StringComparison.OrdinalIgnoreCase))
        {
            return "Copilot";
        }

        if (string.Equals(providerKey, ModelProviderIds.Codex.Value, StringComparison.OrdinalIgnoreCase))
        {
            return "Codex";
        }

        return string.IsNullOrWhiteSpace(providerKey)
            ? "Unknown"
            : providerKey.Trim();
    }

    public static string BuildProviderMarkup(string? providerKey, string? displayName, WorkThreadKind kind)
    {
        var accent = ResolveThreadAccent(providerKey, kind);
        return $"[{UiPalette.GetSidebarAccentMarkup(accent)}]{NerdFont.MdCircleSmall}[/] {AnsiMarkup.Escape(ResolveProviderDisplayName(providerKey, displayName))}";
    }

    public static string BuildEditedPromptIconMarkup(SidebarAccent accent)
        => $"[{UiPalette.GetSidebarAccentMarkup(accent)}]{NerdFont.MdSquareEditOutline}[/]";
}
