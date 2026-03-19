internal interface IKnownProjectImporter
{
    Task ImportAsync(CancellationToken cancellationToken);
}
