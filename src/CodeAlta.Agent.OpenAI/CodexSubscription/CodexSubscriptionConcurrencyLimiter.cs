using System.Collections.Concurrent;

namespace CodeAlta.Agent.OpenAI.CodexSubscription;

internal static class CodexSubscriptionConcurrencyLimiter
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SessionSemaphores = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> AccountSemaphores = new(StringComparer.Ordinal);

    public static async ValueTask<IAsyncDisposable> AcquireAsync(
        string providerKey,
        string sessionId,
        string? accountId,
        int maxConcurrentRequests,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var sessionSemaphore = SessionSemaphores.GetOrAdd(
            providerKey + "\n" + sessionId,
            static _ => new SemaphoreSlim(1, 1));
        var accountSemaphore = AccountSemaphores.GetOrAdd(
            providerKey + "\n" + (string.IsNullOrWhiteSpace(accountId) ? "<default>" : accountId.Trim()),
            _ => new SemaphoreSlim(Math.Max(1, maxConcurrentRequests), Math.Max(1, maxConcurrentRequests)));

        await sessionSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await accountSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new Releaser(sessionSemaphore, accountSemaphore);
        }
        catch
        {
            sessionSemaphore.Release();
            throw;
        }
    }

    private sealed class Releaser(
        SemaphoreSlim sessionSemaphore,
        SemaphoreSlim accountSemaphore) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            accountSemaphore.Release();
            sessionSemaphore.Release();
            return ValueTask.CompletedTask;
        }
    }
}
