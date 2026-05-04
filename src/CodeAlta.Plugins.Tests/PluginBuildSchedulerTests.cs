namespace CodeAlta.Plugins.Tests;

[TestClass]
public sealed class PluginBuildSchedulerTests
{
    [TestMethod]
    public async Task BuildAsyncPreservesOrderBoundsParallelismAndReportsProgress()
    {
        using var temp = new TestTempDirectory();
        var requests = Enumerable.Range(0, 5)
            .Select(index => new PluginBuildRequest { Package = CreatePackage(temp.Path, $"plugin{index}") })
            .ToArray();
        FakeBuildService? service = null;
        service = new FakeBuildService(async (request, token) =>
        {
            service!.Enter();
            await Task.Delay(40, token);
            service.Exit();
            return new PluginBuildResult { Package = request.Package, Succeeded = true };
        });
        var scheduler = new PluginBuildScheduler(service, new PluginBuildSchedulerOptions { MaxDegreeOfParallelism = 2 });
        var progress = new List<PluginBuildProgress>();
        scheduler.ProgressChanged += (_, item) =>
        {
            lock (progress)
            {
                progress.Add(item);
            }
        };

        var results = await scheduler.BuildAsync(requests);

        CollectionAssert.AreEqual(requests.Select(static request => request.Package.PackageId).ToArray(), results.Select(static result => result.Package.PackageId).ToArray());
        Assert.IsTrue(service.MaxActive <= 2);
        Assert.AreEqual(5, progress.Count(item => item.State == PluginBuildProgressState.Queued));
        Assert.AreEqual(5, progress.Count(item => item.State == PluginBuildProgressState.Running));
        Assert.AreEqual(5, progress.Count(item => item.State == PluginBuildProgressState.Succeeded));
    }

    [TestMethod]
    public async Task BuildAsyncIsolatesFailures()
    {
        using var temp = new TestTempDirectory();
        var first = new PluginBuildRequest { Package = CreatePackage(temp.Path, "first") };
        var second = new PluginBuildRequest { Package = CreatePackage(temp.Path, "second") };
        var service = new FakeBuildService((request, _) => request.Package.PackageId == "first"
            ? throw new InvalidOperationException("fail")
            : ValueTask.FromResult(new PluginBuildResult { Package = request.Package, Succeeded = true }));
        var scheduler = new PluginBuildScheduler(service, new PluginBuildSchedulerOptions { MaxDegreeOfParallelism = 1 });

        var results = await scheduler.BuildAsync([first, second]);

        Assert.IsFalse(results[0].Succeeded);
        Assert.IsTrue(results[0].RuntimeDiagnostics.Any(diagnostic => diagnostic.Exception?.Message == "fail"));
        Assert.IsTrue(results[1].Succeeded);
    }

    private static SourcePluginPackage CreatePackage(string rootPath, string id)
    {
        var directory = Path.Combine(rootPath, id);
        Directory.CreateDirectory(directory);
        var entry = Path.Combine(directory, "plugin.cs");
        File.WriteAllText(entry, "// plugin");
        return new SourcePluginPackage
        {
            PackageId = id,
            PackageDirectory = directory,
            EntryFilePath = entry,
            Root = new PluginRoot { RootPath = rootPath, Scope = CodeAlta.Plugins.Abstractions.PluginScope.Global },
        };
    }

    private sealed class FakeBuildService : IPluginBuildService
    {
        private readonly Func<PluginBuildRequest, CancellationToken, ValueTask<PluginBuildResult>> _handler;
        private int _active;

        public FakeBuildService(Func<PluginBuildRequest, CancellationToken, ValueTask<PluginBuildResult>> handler)
            => _handler = handler;

        public int MaxActive { get; private set; }

        public ValueTask<PluginBuildResult> BuildAsync(PluginBuildRequest request, CancellationToken cancellationToken = default)
            => _handler(request, cancellationToken);

        public void Enter()
        {
            var active = Interlocked.Increment(ref _active);
            MaxActive = Math.Max(MaxActive, active);
        }

        public void Exit()
            => Interlocked.Decrement(ref _active);
    }
}
