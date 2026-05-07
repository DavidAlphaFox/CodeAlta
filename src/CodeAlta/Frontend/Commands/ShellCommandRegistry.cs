namespace CodeAlta.Frontend.Commands;

internal sealed class ShellCommandRegistry
{
    private readonly Dictionary<Type, Func<ShellCommand, CancellationToken, ValueTask>> _handlers = new();
    private readonly Dictionary<string, Func<ShellCommand>> _factories = new(StringComparer.Ordinal);

    public void Register<TCommand>(Func<TCommand, CancellationToken, ValueTask> handler)
        where TCommand : ShellCommand
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handlers[typeof(TCommand)] = (command, cancellationToken) => handler((TCommand)command, cancellationToken);
    }

    public bool TryGetHandler(ShellCommand command, out Func<ShellCommand, CancellationToken, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(command);
        return _handlers.TryGetValue(command.GetType(), out handler!);
    }

    public void RegisterFactory(string commandId, Func<ShellCommand> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ArgumentNullException.ThrowIfNull(factory);

        _factories[commandId] = factory;
    }

    public bool TryCreateCommand(string commandId, out ShellCommand command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        if (!_factories.TryGetValue(commandId, out var factory))
        {
            command = null!;
            return false;
        }

        command = factory();
        return true;
    }
}
