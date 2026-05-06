using CodeAlta.Orchestration.Runtime.Plugins;
using CodeAlta.Plugins.Abstractions;

namespace CodeAlta.Orchestration.Tests;

[TestClass]
public sealed class PluginPromptContributionScopeTests
{
    [TestMethod]
    public void Take_ReturnsOnlyMatchingThreadRunScope()
    {
        var scope = new PluginPromptContributionScope();
        var first = CreateContribution("first");
        var second = CreateContribution("second");
        scope.Add(new PluginPromptContributionScopeKey("thread-1", "run-1"), [first]);
        scope.Add(new PluginPromptContributionScopeKey("thread-1", "run-2"), [second]);

        var taken = scope.Take(new PluginPromptContributionScopeKey("thread-1", "run-1"));

        CollectionAssert.AreEqual(new[] { first }, taken.ToArray());
        CollectionAssert.AreEqual(new[] { second }, scope.Take(new PluginPromptContributionScopeKey("thread-1", "run-2")).ToArray());
    }

    [TestMethod]
    public void Take_RemovesPendingContributions()
    {
        var scope = new PluginPromptContributionScope();
        scope.Add(new PluginPromptContributionScopeKey("thread-1"), [CreateContribution("once")]);

        Assert.AreEqual(1, scope.Take(new PluginPromptContributionScopeKey("thread-1")).Count);
        Assert.AreEqual(0, scope.Take(new PluginPromptContributionScopeKey("thread-1")).Count);
    }

    private static PluginSystemPromptContribution CreateContribution(string title)
        => new()
        {
            Title = title,
            Channel = PluginPromptChannel.System,
            Content = (_, _) => ValueTask.FromResult<string?>($"content:{title}"),
        };
}
