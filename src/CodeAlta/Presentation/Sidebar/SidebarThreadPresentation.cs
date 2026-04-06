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

        if (!string.IsNullOrWhiteSpace(backendId) &&
            backendId.StartsWith("acp:", StringComparison.OrdinalIgnoreCase))
        {
            return FormatBackendToken(backendId["acp:".Length..]);
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

    private static string FormatBackendToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return "ACP";
        }

        var normalized = token.Trim()
            .Replace('_', ' ')
            .Replace('-', ' ');
        return string.Join(
            ' ',
            normalized
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static part => char.ToUpperInvariant(part[0]) + part[1..]));
    }
}
