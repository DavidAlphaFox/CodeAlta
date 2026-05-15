using CodeAlta.Catalog;
using CodeAlta.Models;

namespace CodeAlta.Presentation.Prompting
{
    internal readonly record struct PromptComposerProjection(
        string Placeholder,
        bool IsEnabled,
        bool CanSend,
        bool CanSteer,
        bool CanAbort,
        bool CanCompact,
        bool CanCloseTab,
        bool CanClearQueue,
        bool CanAlwaysEnqueue,
        string? UnavailableStatusMessage,
        StatusTone UnavailableStatusTone)
    {
        public bool HasUnavailableStatus
            => !string.IsNullOrWhiteSpace(UnavailableStatusMessage);
    }

    internal static class PromptComposerProjectionBuilder
    {
        internal static string BuildDefaultPromptPlaceholder()
            => BuildReadyPromptPlaceholder(isContinuation: false, hasProjectContext: false);

        public static PromptComposerProjection Build(
            WorkThreadDescriptor? selectedThread,
            ProjectDescriptor? selectedProject,
            bool globalScopeSelected,
            string providerDisplayName,
            ChatBackendAvailability availability,
            bool anyBackendReady,
            bool draftTabOpen,
            int openTabCount,
            string? selectedThreadId,
            bool selectedThreadHasQueuedPrompts,
            bool selectedThreadCanAlwaysEnqueue,
            bool selectedThreadCanCompact,
            bool selectedThreadCanAbort)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(providerDisplayName);

            var isUnavailable = availability != ChatBackendAvailability.Ready;
            var placeholder = isUnavailable
                ? BuildPromptUnavailablePlaceholder(selectedThread, providerDisplayName, availability, anyBackendReady)
                : BuildPromptPlaceholder(selectedThread, selectedProject, globalScopeSelected);
            var unavailableStatusMessage = isUnavailable
                ? BuildPromptUnavailableStatusText(selectedThread, providerDisplayName, availability, anyBackendReady)
                : null;
            var unavailableStatusTone = availability == ChatBackendAvailability.Connecting
                ? StatusTone.Info
                : StatusTone.Warning;
            var hasThread = selectedThread is not null;

            return new PromptComposerProjection(
                Placeholder: placeholder,
                IsEnabled: !isUnavailable,
                CanSend: !isUnavailable,
                CanSteer: hasThread && !isUnavailable,
                CanAbort: hasThread && selectedThreadCanAbort,
                CanCompact: hasThread && selectedThreadCanCompact && !isUnavailable,
                CanCloseTab: hasThread || (draftTabOpen && string.IsNullOrWhiteSpace(selectedThreadId) && openTabCount > 1),
                CanClearQueue: hasThread && selectedThreadHasQueuedPrompts,
                CanAlwaysEnqueue: hasThread && selectedThreadCanAlwaysEnqueue,
                UnavailableStatusMessage: unavailableStatusMessage,
                UnavailableStatusTone: unavailableStatusTone);
        }

        internal static string BuildPromptPlaceholder(
            WorkThreadDescriptor? thread,
            ProjectDescriptor? selectedProject,
            bool globalScopeSelected)
        {
            var hasProjectContext =
                thread?.Kind == WorkThreadKind.ProjectThread ||
                (!globalScopeSelected && selectedProject is not null);

            return BuildReadyPromptPlaceholder(thread is not null, hasProjectContext);
        }

        internal static string BuildPromptUnavailablePlaceholder(
            WorkThreadDescriptor? thread,
            string providerDisplayName,
            ChatBackendAvailability availability,
            bool anyBackendReady)
        {
            if (thread is not null)
            {
                return availability == ChatBackendAvailability.Connecting
                    ? $"Waiting for {providerDisplayName} to reconnect..."
                    : $"'{thread.Title}' is unavailable until {providerDisplayName} is connected.";
            }

            if (availability == ChatBackendAvailability.Connecting)
            {
                return $"Connecting to {providerDisplayName}...";
            }

            return anyBackendReady
                ? "Select a connected provider to start a thread..."
                : "Configure model providers (Ctrl+G Ctrl+R) to start a thread...";
        }

        internal static string BuildPromptUnavailableStatusText(
            WorkThreadDescriptor? thread,
            string providerDisplayName,
            ChatBackendAvailability availability,
            bool anyBackendReady)
        {
            if (thread is not null)
            {
                return availability == ChatBackendAvailability.Connecting
                    ? $"Reconnecting '{thread.Title}' to {providerDisplayName}. Prompt sending is temporarily unavailable."
                    : $"'{thread.Title}' is unavailable because {providerDisplayName} is not connected.";
            }

            if (availability == ChatBackendAvailability.Connecting)
            {
                return $"Connecting to {providerDisplayName}. Prompt sending will be available once the provider is ready.";
            }

            return anyBackendReady
                ? "Select a connected provider to send prompts."
                : "No model provider is ready. Open Model Providers (Ctrl+G Ctrl+R) to configure one.";
        }

        private static string BuildReadyPromptPlaceholder(bool isContinuation, bool hasProjectContext)
        {
            var action = isContinuation
                ? "Continue the selected thread."
                : "Start a thread.";

            return $"{action} {BuildReadyPromptGuidance(hasProjectContext)}";
        }

        private static string BuildReadyPromptGuidance(bool hasProjectContext)
        {
            var segments = new List<string>(6)
            {
                "[/] commands",
                "[?] help",
            };
            if (hasProjectContext)
            {
                segments.Add("[@] to reference a project file");
            }

            segments.Add("[ENTER] for new line");
            segments.Add($"[{GetPromptSendShortcutLabel().ToUpperInvariant()}] to send");
            segments.Add("[F5] to steer");
            return string.Join(", ", segments) + ".";
        }

        private static string GetPromptSendShortcutLabel()
            => OperatingSystem.IsWindows() ? "Ctrl+Enter" : "Ctrl+J";
    }
}
