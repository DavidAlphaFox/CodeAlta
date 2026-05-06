using CodeAlta.Orchestration.Runtime;

namespace CodeAlta.Orchestration.Tests;

[TestClass]
public sealed class WorkThreadEventSequencerTests
{
    [TestMethod]
    public void Next_AssignsIndependentMonotonicSequencesPerThread()
    {
        var sequencer = new WorkThreadEventSequencer();

        Assert.AreEqual(1, sequencer.Next("thread-1"));
        Assert.AreEqual(2, sequencer.Next("thread-1"));
        Assert.AreEqual(1, sequencer.Next("thread-2"));
        Assert.AreEqual(3, sequencer.Next("THREAD-1"));
    }

    [TestMethod]
    public void Reset_ClearsThreadSequence()
    {
        var sequencer = new WorkThreadEventSequencer();
        _ = sequencer.Next("thread-1");

        sequencer.Reset("thread-1");

        Assert.AreEqual(1, sequencer.Next("thread-1"));
    }

    [TestMethod]
    public void Next_RejectsMissingThreadId()
    {
        var sequencer = new WorkThreadEventSequencer();

        Assert.ThrowsExactly<ArgumentException>(() => sequencer.Next(" "));
    }
}
