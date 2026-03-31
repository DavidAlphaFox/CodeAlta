using CodeAlta.Agent;
using CodeAlta.Catalog;
using CodeAlta.Presentation.Styling;
using XenoAtom.Ansi;
using XenoAtom.Terminal.UI;

namespace CodeAlta.Presentation.Sidebar;

internal static class SidebarThreadPresentation
{
    public static SidebarAccent ResolveThreadAccent(string? backendId, WorkThreadKind kind)
    {
        if (string.Equals(backendId, AgentBackendIds.Copilot.Value, StringComparison.OrdinalIgnoreCase))
        {
            return SidebarAccent.CopilotThread;
        }

        return kind switch
        {
            WorkThreadKind.GlobalThread => SidebarAccent.Global,
            WorkThreadKind.ProjectThread => SidebarAccent.ProjectThread,
            WorkThreadKind.InternalThread => SidebarAccent.InternalThread,
            _ => SidebarAccent.Fallback,
        };
    }

    public static string CompactThreadTitle(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        const int maxLength = 34;
        var normalized = title.Trim();
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..Math.Max(1, maxLength - 1)].TrimEnd() + "…";
    }

    public static string ResolveBackendDisplayName(string? backendId)
    {
        if (string.Equals(backendId, AgentBackendIds.Copilot.Value, StringComparison.OrdinalIgnoreCase))
        {
            return "Copilot";
        }

        if (string.Equals(backendId, AgentBackendIds.Codex.Value, StringComparison.OrdinalIgnoreCase))
        {
            return "Codex";
        }

        return string.IsNullOrWhiteSpace(backendId)
            ? "Unknown"
            : backendId.Trim();
    }

    public static string BuildBackendMarkup(string? backendId, WorkThreadKind kind)
    {
        var accent = ResolveThreadAccent(backendId, kind);
        return $"[{UiPalette.GetSidebarAccentMarkup(accent)}]{NerdFont.MdCircleSmall}[/] {AnsiMarkup.Escape(ResolveBackendDisplayName(backendId))}";
    }

    public static string BuildEditedPromptIconMarkup(SidebarAccent accent)
        => $"[{UiPalette.GetSidebarAccentMarkup(accent)}]{NerdFont.MdSquareEditOutline}[/]";
}
