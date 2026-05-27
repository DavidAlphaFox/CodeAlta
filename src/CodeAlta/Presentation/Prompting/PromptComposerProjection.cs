using CodeAlta.Agent;
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
            SessionViewDescriptor? selectedThread,
            ProjectDescriptor? selectedProject,
            bool globalScopeSelected,
            string providerDisplayName,
            ModelProviderAvailability availability,
            bool anyBackendReady,
            bool draftTabOpen,
            int openTabCount,
            string? selectedThreadId,
            bool selectedThreadHasQueuedPrompts,
            bool selectedThreadCanAlwaysEnqueue,
            bool selectedThreadCanCompact,
            bool selectedThreadCanAbort,
            IReadOnlyList<string>? promptPlaceholderContributions = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(providerDisplayName);

            var isUnavailable = availability != ModelProviderAvailability.Ready;
            var placeholder = isUnavailable
                ? BuildPromptUnavailablePlaceholder(selectedThread, providerDisplayName, availability, anyBackendReady)
                : BuildPromptPlaceholder(selectedThread, selectedProject, globalScopeSelected, promptPlaceholderContributions);
            var unavailableStatusMessage = isUnavailable
                ? BuildPromptUnavailableStatusText(selectedThread, providerDisplayName, availability, anyBackendReady)
                : null;
            var unavailableStatusTone = availability == ModelProviderAvailability.Probing
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
            SessionViewDescriptor? thread,
            ProjectDescriptor? selectedProject,
            bool globalScopeSelected,
            IReadOnlyList<string>? promptPlaceholderContributions = null)
        {
            var hasProjectContext =
                thread?.Kind == WorkThreadKind.ProjectThread ||
                (!globalScopeSelected && selectedProject is not null);

            return BuildReadyPromptPlaceholder(thread is not null, hasProjectContext, promptPlaceholderContributions);
        }

        internal static string BuildPromptUnavailablePlaceholder(
            SessionViewDescriptor? thread,
            string providerDisplayName,
            ModelProviderAvailability availability,
            bool anyBackendReady)
        {
            if (thread is not null)
            {
                return availability == ModelProviderAvailability.Probing
                    ? $"Waiting for {providerDisplayName} to reconnect..."
                    : $"'{thread.Title}' is unavailable until {providerDisplayName} is connected.";
            }

            if (availability == ModelProviderAvailability.Probing)
            {
                return $"Connecting to {providerDisplayName}...";
            }

            return anyBackendReady
                ? "Select a connected provider to start a session..."
                : "Configure model providers (Ctrl+G Ctrl+R) to start a session...";
        }

        internal static string BuildPromptUnavailableStatusText(
            SessionViewDescriptor? thread,
            string providerDisplayName,
            ModelProviderAvailability availability,
            bool anyBackendReady)
        {
            if (thread is not null)
            {
                return availability == ModelProviderAvailability.Probing
                    ? $"Reconnecting session '{thread.Title}' to {providerDisplayName}. Prompt sending is temporarily unavailable."
                    : $"'{thread.Title}' is unavailable because {providerDisplayName} is not connected.";
            }

            if (availability == ModelProviderAvailability.Probing)
            {
                return $"Connecting to {providerDisplayName}. Prompt sending will be available once the provider is ready.";
            }

            return anyBackendReady
                ? "Select a connected provider to send prompts."
                : "No model provider is ready. Open Model Providers (Ctrl+G Ctrl+R) to configure one.";
        }

        private static string BuildReadyPromptPlaceholder(
            bool isContinuation,
            bool hasProjectContext,
            IReadOnlyList<string>? promptPlaceholderContributions = null)
        {
            var action = isContinuation
                ? "Continue the selected session."
                : "Start a session.";

            return $"{action} {BuildReadyPromptGuidance(hasProjectContext, promptPlaceholderContributions)}";
        }

        private static string BuildReadyPromptGuidance(bool hasProjectContext, IReadOnlyList<string>? promptPlaceholderContributions)
        {
            var contributionSegments = promptPlaceholderContributions?
                .Where(static segment => !string.IsNullOrWhiteSpace(segment))
                .Select(static segment => segment.Trim())
                .ToArray() ?? [];
            var segments = new List<string>(6 + contributionSegments.Length)
            {
                "[/] commands",
                "[?] help",
            };
            if (hasProjectContext)
            {
                segments.Add("[@] to reference a project file");
            }

            segments.AddRange(contributionSegments);

            segments.Add("[ENTER] to send");
            segments.Add("[SHIFT+ENTER] for new line");
            segments.Add("[CTRL+ENTER] to steer");
            return string.Join(", ", segments) + ".";
        }
    }
}
