using CodeAlta.Agent;
using CodeAlta.App;
using CodeAlta.Catalog;
using CodeAlta.Models;

namespace CodeAlta.Tests;

[TestClass]
public sealed class ModelProviderPreferencePortTests
{
    [TestMethod]
    public void RememberThreadPreference_NormalizesBlankModelAndForwardsPersistFlag()
    {
        string? capturedThreadId = null;
        ModelProviderPreference? capturedPreference = null;
        bool? capturedPersist = null;
        var port = CreatePort(rememberThreadPreference: (threadId, preference, persist) =>
        {
            capturedThreadId = threadId;
            capturedPreference = preference;
            capturedPersist = persist;
        });

        port.RememberThreadPreference(
            "thread-1",
            new ModelProviderPreference(new ModelProviderId("provider-1"), "   ", AgentReasoningEffort.Medium),
            persistNow: true);

        Assert.AreEqual("thread-1", capturedThreadId);
        Assert.IsNotNull(capturedPreference);
        Assert.AreEqual("provider-1", capturedPreference.ModelProviderId.Value);
        Assert.IsNull(capturedPreference.ModelId);
        Assert.AreEqual(AgentReasoningEffort.Medium, capturedPreference.ReasoningEffort);
        Assert.IsTrue(capturedPersist);
    }

    [TestMethod]
    public void IsModelProviderReady_ForwardsValidatedModelProviderId()
    {
        var captured = default(ModelProviderId);
        var port = CreatePort(isModelProviderReady: modelProviderId =>
        {
            captured = modelProviderId;
            return true;
        });

        var isReady = port.IsModelProviderReady(new ModelProviderId("provider-1"));

        Assert.IsTrue(isReady);
        Assert.AreEqual("provider-1", captured.Value);
    }

    [TestMethod]
    public void GetPreferredModelProviderId_RejectsDefaultProjectId()
    {
        var port = CreatePort();

        Assert.ThrowsExactly<ArgumentException>(() => port.GetPreferredModelProviderId(default));
    }

    [TestMethod]
    public void ApplyDraftPreference_RequiresPreferenceModelProviderId()
    {
        var port = CreatePort();
        var binding = new PromptSessionBinding(
            new PromptSessionId("prompt-1"),
            ProjectId.NewVersion7(),
            new ShellThreadRef.Draft(new ThreadDraftId("draft-1")),
            new ModelProviderId("provider-1"));

        Assert.ThrowsExactly<ArgumentException>(() => port.ApplyDraftPreference(binding, new ModelProviderPreference(default)));
    }

    private static DelegatingModelProviderPreferencePort CreatePort(
        Func<ProjectId, ModelProviderId>? getPreferredModelProviderId = null,
        Func<ModelProviderId, bool>? isModelProviderReady = null,
        Action<PromptSessionBinding, ModelProviderPreference>? applyDraftPreference = null,
        Action<string, ModelProviderPreference>? applyThreadPreference = null,
        Action<ProjectId, ModelProviderPreference>? rememberProjectPreference = null,
        Action<string, ModelProviderPreference, bool>? rememberThreadPreference = null)
        => new(
            getPreferredModelProviderId ?? (_ => new ModelProviderId("provider-1")),
            isModelProviderReady ?? (_ => false),
            applyDraftPreference ?? ((_, _) => { }),
            applyThreadPreference ?? ((_, _) => { }),
            rememberProjectPreference ?? ((_, _) => { }),
            rememberThreadPreference ?? ((_, _, _) => { }));
}
