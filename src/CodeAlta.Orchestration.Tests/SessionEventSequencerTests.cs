using CodeAlta.Orchestration.Runtime;

namespace CodeAlta.Orchestration.Tests;

[TestClass]
public sealed class SessionEventSequencerTests
{
    [TestMethod]
    public void Next_AssignsIndependentMonotonicSequencesPerSession()
    {
        var sequencer = new SessionEventSequencer();

        Assert.AreEqual(1, sequencer.Next("session-1"));
        Assert.AreEqual(2, sequencer.Next("session-1"));
        Assert.AreEqual(1, sequencer.Next("session-2"));
        Assert.AreEqual(3, sequencer.Next("SESSION-1"));
    }

    [TestMethod]
    public void Reset_ClearsSessionSequence()
    {
        var sequencer = new SessionEventSequencer();
        _ = sequencer.Next("session-1");

        sequencer.Reset("session-1");

        Assert.AreEqual(1, sequencer.Next("session-1"));
    }

    [TestMethod]
    public void Next_RejectsMissingSessionId()
    {
        var sequencer = new SessionEventSequencer();

        Assert.ThrowsExactly<ArgumentException>(() => sequencer.Next(" "));
    }
}
