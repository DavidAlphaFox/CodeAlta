using CodeAlta.Orchestration.Runtime;

namespace CodeAlta.Orchestration.Tests;

[TestClass]
public sealed class BoundedRuntimeEventStreamTests
{
    [TestMethod]
    public async Task TryPublish_DropsNewestEventWhenCapacityIsFull()
    {
        var stream = new BoundedRuntimeEventStream<int>(capacity: 1);

        Assert.IsTrue(stream.TryPublish(1));
        Assert.IsFalse(stream.TryPublish(2));
        Assert.AreEqual(1, stream.DroppedCount);
        stream.Complete();

        var events = new List<int>();
        await foreach (var item in stream.ReadAllAsync())
        {
            events.Add(item);
        }

        CollectionAssert.AreEqual(new[] { 1 }, events);
    }

    [TestMethod]
    public void TryPublish_ReturnsFalseAfterCompletion()
    {
        var stream = new BoundedRuntimeEventStream<string>(capacity: 1);
        stream.Complete();

        Assert.IsFalse(stream.TryPublish("event"));
        Assert.AreEqual(1, stream.DroppedCount);
    }

    [TestMethod]
    public void Constructor_RejectsInvalidCapacity()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new BoundedRuntimeEventStream<object>(0));
    }
}
