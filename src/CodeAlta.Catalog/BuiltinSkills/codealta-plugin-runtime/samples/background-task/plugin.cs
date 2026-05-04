using CodeAlta.Plugins.Abstractions;

[Plugin("background-task", DisplayName = "Background Task", Description = "Starts tracked background work.")]
public sealed class BackgroundTaskPlugin : PluginBase
{
    public override ValueTask OnActivatedAsync(CancellationToken cancellationToken = default)
    {
        _ = Tasks.Run("sample-background-loop", async token =>
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), token).ConfigureAwait(false);
            }
        });
        return ValueTask.CompletedTask;
    }
}
