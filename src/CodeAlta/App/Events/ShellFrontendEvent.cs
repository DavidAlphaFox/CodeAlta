using CodeAlta.App.State;

namespace CodeAlta.App.Events;

internal abstract record ShellFrontendEvent;

internal sealed record CatalogChangedEvent : ShellFrontendEvent;

internal sealed record SelectionChangedEvent(ShellFrontendStateSnapshot? Snapshot = null) : ShellFrontendEvent;

internal sealed record HeaderChangedEvent : ShellFrontendEvent;

internal sealed record ShellChromeChangedEvent : ShellFrontendEvent;

internal sealed record ThreadStatusChangedEvent(string ThreadId) : ShellFrontendEvent;

internal sealed record PromptDraftChangedEvent(string PromptSessionId) : ShellFrontendEvent;

internal sealed record PromptImagesChangedEvent(string PromptSessionId) : ShellFrontendEvent;

internal sealed record PromptAvailabilityChangedEvent : ShellFrontendEvent;

internal sealed record QueuedPromptListChangedEvent(string ThreadId) : ShellFrontendEvent;

internal sealed record SessionUsageChangedEvent(string ThreadId) : ShellFrontendEvent;

internal sealed record ModelProviderStateChangedEvent(string ModelProviderId) : ShellFrontendEvent;

internal sealed record ModelProviderCatalogChangedEvent : ShellFrontendEvent;

internal sealed record RuntimeTimelineChangedEvent(string ThreadId) : ShellFrontendEvent;
