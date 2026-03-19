var cancellationTokenSource = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellationTokenSource.Cancel();
};

await using var app = await CodeAltaApp.CreateAsync(cancellationTokenSource.Token).ConfigureAwait(false);
await app.RunAsync(cancellationTokenSource.Token).ConfigureAwait(false);
