using CodeAlta.Frontend.Commands;
using CodeAlta.Threading;

namespace CodeAlta.Tests;

[TestClass]
public sealed class ShellCommandDispatcherTests
{
    [TestMethod]
    public async Task DispatchAsync_InvokesRegisteredHandler()
    {
        var handled = false;
        var registry = new ShellCommandRegistry();
        registry.Register<OpenHelpCommand>((command, _) =>
        {
            handled = command.FilterText == "sessions";
            return ValueTask.CompletedTask;
        });
        var dispatcher = new ShellCommandDispatcher(registry);

        await dispatcher.DispatchAsync(new OpenHelpCommand("sessions"));

        Assert.IsTrue(handled);
    }

    [TestMethod]
    public async Task DispatchAsync_UsesUiDispatcherWhenCurrentSessionDoesNotOwnUi()
    {
        var uiDispatcher = new CapturingUiDispatcher(hasAccess: false);
        var handlerRanInsideDispatcher = false;
        var registry = new ShellCommandRegistry();
        registry.Register<FocusPromptCommand>((_, _) =>
        {
            handlerRanInsideDispatcher = uiDispatcher.IsInvoking;
            return ValueTask.CompletedTask;
        });
        var dispatcher = new ShellCommandDispatcher(registry, uiDispatcher);

        await dispatcher.DispatchAsync(new FocusPromptCommand());

        Assert.IsTrue(uiDispatcher.InvokeAsyncCalled);
        Assert.IsTrue(handlerRanInsideDispatcher);
    }

    [TestMethod]
    public async Task DispatchAsync_RejectsUnregisteredCommand()
    {
        var dispatcher = new ShellCommandDispatcher(new ShellCommandRegistry());

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () => await dispatcher.DispatchAsync(new OpenHelpCommand()).AsTask());
    }

    [TestMethod]
    public void TryCreateCommand_UsesRegisteredCommandFactory()
    {
        var registry = new ShellCommandRegistry();
        registry.RegisterFactory("CodeAlta.Shell.FocusPrompt", static () => new FocusPromptCommand());

        var created = registry.TryCreateCommand("CodeAlta.Shell.FocusPrompt", out var command);

        Assert.IsTrue(created);
        Assert.IsInstanceOfType<FocusPromptCommand>(command);
    }

    [TestMethod]
    public void RegisterFactory_RejectsCommandIdWithoutCatalogMetadata()
    {
        var registry = new ShellCommandRegistry();

        Assert.ThrowsExactly<ArgumentException>(() => registry.RegisterFactory("CodeAlta.Test.Missing", static () => new FocusPromptCommand()));
    }

    private sealed class CapturingUiDispatcher : IUiDispatcher
    {
        private readonly bool _hasAccess;

        public CapturingUiDispatcher(bool hasAccess)
            => _hasAccess = hasAccess;

        public bool InvokeAsyncCalled { get; private set; }

        public bool IsInvoking { get; private set; }

        public bool CheckAccess() => _hasAccess;

        public void Post(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            action();
        }

        public Task InvokeAsync(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            InvokeAsyncCalled = true;
            IsInvoking = true;
            try
            {
                action();
            }
            finally
            {
                IsInvoking = false;
            }

            return Task.CompletedTask;
        }

        public Task<T> InvokeAsync<T>(Func<T> action)
        {
            ArgumentNullException.ThrowIfNull(action);
            InvokeAsyncCalled = true;
            IsInvoking = true;
            try
            {
                return Task.FromResult(action());
            }
            finally
            {
                IsInvoking = false;
            }
        }
    }
}
