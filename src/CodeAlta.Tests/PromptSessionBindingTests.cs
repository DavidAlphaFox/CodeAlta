using CodeAlta.Agent;
using CodeAlta.Catalog;
using CodeAlta.Models;

namespace CodeAlta.Tests;

[TestClass]
public sealed class PromptSessionBindingTests
{
    [TestMethod]
    public void Constructor_CapturesProjectThreadAndModelProviderContext()
    {
        var projectId = ProjectId.NewVersion7();
        var binding = new PromptSessionBinding(
            new PromptSessionId("prompt-1"),
            projectId,
            new ShellThreadRef.Draft(new ThreadDraftId("draft-1")),
            new ModelProviderId("provider-1"),
            "model-1",
            AgentReasoningEffort.High);

        Assert.AreEqual("prompt-1", binding.PromptSessionId.Value);
        Assert.AreEqual(projectId, binding.ProjectId);
        Assert.IsInstanceOfType<ShellThreadRef.Draft>(binding.Thread);
        Assert.AreEqual("provider-1", binding.ModelProviderId.Value);
        Assert.AreEqual("model-1", binding.ModelId);
        Assert.AreEqual(AgentReasoningEffort.High, binding.ReasoningEffort);
    }

    [TestMethod]
    public void Constructor_TrimsEmptyModelToNull()
    {
        var binding = new PromptSessionBinding(
            new PromptSessionId("prompt-1"),
            ProjectId.NewVersion7(),
            new ShellThreadRef.Running("thread-1"),
            new ModelProviderId("provider-1"),
            "   ");

        Assert.IsNull(binding.ModelId);
    }

    [TestMethod]
    public void Constructor_RejectsMissingProjectScope()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new PromptSessionBinding(
            new PromptSessionId("prompt-1"),
            default,
            new ShellThreadRef.Draft(new ThreadDraftId("draft-1")),
            new ModelProviderId("provider-1")));
    }

    [TestMethod]
    public void ThreadRefs_RejectEmptyIds()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new ShellThreadRef.Draft(default));
        Assert.ThrowsExactly<ArgumentException>(() => new ShellThreadRef.Running(string.Empty));
    }

    [TestMethod]
    public void Ids_RejectEmptyValues()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new PromptSessionId(string.Empty));
        Assert.ThrowsExactly<ArgumentException>(() => new ThreadDraftId("   "));
        Assert.ThrowsExactly<ArgumentException>(() => new ModelProviderId(string.Empty));
    }
}
