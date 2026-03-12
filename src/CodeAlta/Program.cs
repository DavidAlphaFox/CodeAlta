var cancellationTokenSource = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellationTokenSource.Cancel();
};

await using var host = await TerminalHost.CreateAsync(cancellationTokenSource.Token).ConfigureAwait(false);
await host.RunAsync(cancellationTokenSource.Token).ConfigureAwait(false);

