using CodeAlta.Plugins.Abstractions;

namespace CodeAlta.Plugins.Tests;

[TestClass]
public sealed class PluginRuntimeLifecycleTests
{
    [TestMethod]
    public async Task DeactivateCancelsAndWaitsForTrackedPluginTasks()
    {
        var registry = new PluginContributionRegistry();
        var activator = new PluginRuntimeActivator(registry);
        TrackingPlugin.Reset();
        var discovered = new DiscoveredPluginType
        {
            Type = typeof(TrackingPlugin),
            Descriptor = PluginDescriptorFactory.FromType(typeof(TrackingPlugin)),
        };

        var result = await activator.ActivateAsync(discovered, null, null, new PluginActivationOptions { HostInfo = CreateHostInfo() });

        Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        Assert.IsNotNull(result.ActivePlugin);
        await TrackingPlugin.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsTrue(result.ActivePlugin.RuntimeContext.Services.Tasks.HasRunningTasks);

        var diagnostics = await result.ActivePlugin.DeactivateAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(0, diagnostics.Count, string.Join(Environment.NewLine, diagnostics.Select(static diagnostic => diagnostic.Message)));
        await TrackingPlugin.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsFalse(result.ActivePlugin.RuntimeContext.IsValid);
        Assert.IsFalse(result.ActivePlugin.RuntimeContext.Services.Tasks.HasRunningTasks);
        Assert.IsNull(result.ActivePlugin.Instance);
    }

    [TestMethod]
    public async Task ActivationFailureRollsBackContributionsAndReportsDiagnostic()
    {
        var registry = new PluginContributionRegistry();
        var activator = new PluginRuntimeActivator(registry);
        var discovered = new DiscoveredPluginType
        {
            Type = typeof(FailingContributionPlugin),
            Descriptor = PluginDescriptorFactory.FromType(typeof(FailingContributionPlugin)),
        };

        var result = await activator.ActivateAsync(discovered, null, null, new PluginActivationOptions { HostInfo = CreateHostInfo() });

        Assert.IsFalse(result.Succeeded);
        Assert.IsNull(result.ActivePlugin);
        Assert.AreEqual(0, registry.GetSnapshot().Count);
        Assert.IsTrue(result.Diagnostics.Any(diagnostic => diagnostic.Source == PluginRuntimeDiagnosticSource.Activation));
    }

    [TestMethod]
    public async Task InitializationFailureReportsDiagnostic()
    {
        var activator = new PluginRuntimeActivator(new PluginContributionRegistry());
        var discovered = new DiscoveredPluginType
        {
            Type = typeof(FailingInitializePlugin),
            Descriptor = PluginDescriptorFactory.FromType(typeof(FailingInitializePlugin)),
        };

        var result = await activator.ActivateAsync(discovered, null, null, new PluginActivationOptions { HostInfo = CreateHostInfo() });

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Diagnostics.Any(diagnostic => diagnostic.Message.Contains("Plugin activation failed", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task DeactivateReportsFailedUnloadDiagnosticsWhenLoadContextIsStillReferenced()
    {
        var registry = new PluginContributionRegistry();
        var activator = new PluginRuntimeActivator(registry);
        var discovered = new DiscoveredPluginType
        {
            Type = typeof(EmptyPlugin),
            Descriptor = PluginDescriptorFactory.FromType(typeof(EmptyPlugin)),
        };
        var heldLoadContext = new PluginAssemblyLoadContext(typeof(PluginRuntimeLifecycleTests).Assembly.Location);
        var result = await activator.ActivateAsync(discovered, null, heldLoadContext, new PluginActivationOptions { HostInfo = CreateHostInfo() });
        Assert.IsNotNull(result.ActivePlugin);

        var diagnostics = await result.ActivePlugin.DeactivateAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(PluginRuntimeState.Failed, result.ActivePlugin.State);
        Assert.IsTrue(diagnostics.Any(diagnostic => diagnostic.Source == PluginRuntimeDiagnosticSource.Unload));
        GC.KeepAlive(heldLoadContext);
    }

    private static PluginHostInfo CreateHostInfo()
        => new()
        {
            ApplicationName = "CodeAlta.Tests",
            Version = "1.0.0",
            HostApiVersion = "1.0.0",
            UserDataDirectory = Path.GetTempPath(),
            IsHeadless = true,
        };

    public sealed class TrackingPlugin : PluginBase
    {
        public static TaskCompletionSource Started { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static TaskCompletionSource Cancelled { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static void Reset()
        {
            Started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public override ValueTask InitializeAsync(CancellationToken cancellationToken = default)
        {
            Tasks.Run("wait", async token =>
            {
                Started.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    Cancelled.TrySetResult();
                }
            });
            return ValueTask.CompletedTask;
        }
    }

    public sealed class FailingContributionPlugin : PluginBase
    {
        public override IEnumerable<PluginCommandContribution> GetCommands()
        {
            yield return new PluginCommandContribution { Name = "before-failure", Handler = static (_, _) => ValueTask.FromResult(PluginCommandResult.Handled) };
            throw new InvalidOperationException("Contribution failure.");
        }
    }

    public sealed class FailingInitializePlugin : PluginBase
    {
        public override ValueTask InitializeAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Initialization failure.");
    }

    public sealed class EmptyPlugin : PluginBase
    {
    }
}
