using CodeAlta.Orchestration.Hosting;

namespace CodeAlta.Tests;

[TestClass]
public sealed class CoordinatorAgentsBootstrapperTests
{
    [TestMethod]
    public void Ensure_CreatesMarkedCoordinatorAgentsFileOnFirstRun()
    {
        using var temp = TestTempDirectory.Create();

        var result = CoordinatorAgentsBootstrapper.Ensure(temp.Path);

        Assert.AreEqual(CoordinatorAgentsBootstrapAction.Created, result.Action);
        var content = File.ReadAllText(Path.Combine(temp.Path, "AGENTS.md"));
        StringAssert.Contains(content, "CodeAlta:coordinator-managed:begin");
        StringAssert.Contains(content, "checksum=");
        StringAssert.Contains(content, "CodeAlta Global Coordinator");
        StringAssert.Contains(content, "alta --help");
        StringAssert.Contains(content, "CodeAlta:local-instructions:begin");
    }

    [TestMethod]
    public void Ensure_MigratesUnmarkedFileWithBackupAndPreservesContentInLocalBlock()
    {
        using var temp = TestTempDirectory.Create();
        var agentsPath = Path.Combine(temp.Path, "AGENTS.md");
        File.WriteAllText(agentsPath, "my local coordinator notes\nsecond line");

        var result = CoordinatorAgentsBootstrapper.Ensure(temp.Path);

        Assert.AreEqual(CoordinatorAgentsBootstrapAction.Migrated, result.Action);
        Assert.IsNotNull(result.BackupPath);
        Assert.IsTrue(File.Exists(result.BackupPath));
        Assert.AreEqual("my local coordinator notes\nsecond line", File.ReadAllText(result.BackupPath));
        var content = File.ReadAllText(agentsPath);
        StringAssert.Contains(content, "CodeAlta:coordinator-managed:begin");
        StringAssert.Contains(content, "CodeAlta:local-instructions:begin");
        StringAssert.Contains(content, "my local coordinator notes\nsecond line");
    }

    [TestMethod]
    public void Ensure_UpdatesManagedBlockAndPreservesLocalBlockByteForByte()
    {
        using var temp = TestTempDirectory.Create();
        var agentsPath = Path.Combine(temp.Path, "AGENTS.md");
        _ = CoordinatorAgentsBootstrapper.Ensure(temp.Path);
        var current = File.ReadAllText(agentsPath);
        var localBlock = "<!-- CodeAlta:local-instructions:begin -->\n### Local instructions\n\nKeep this exact text.\n<!-- CodeAlta:local-instructions:end -->";
        var oldManaged = "<!-- CodeAlta:coordinator-managed:begin version=\"old\" checksum=\"old\" -->\nold managed text\n<!-- CodeAlta:coordinator-managed:end -->";
        File.WriteAllText(agentsPath, oldManaged + "\n\n" + localBlock + "\n");

        var result = CoordinatorAgentsBootstrapper.Ensure(temp.Path);

        Assert.AreEqual(CoordinatorAgentsBootstrapAction.Updated, result.Action);
        var updated = File.ReadAllText(agentsPath);
        StringAssert.Contains(updated, "CodeAlta Global Coordinator");
        StringAssert.Contains(updated, localBlock);
        Assert.IsFalse(updated.Contains("old managed text", StringComparison.Ordinal));
        Assert.IsFalse(updated.Contains(current + current, StringComparison.Ordinal));
    }

    [TestMethod]
    public void Ensure_DamagedMarkersLeaveFileUnchangedWithDiagnostic()
    {
        using var temp = TestTempDirectory.Create();
        var agentsPath = Path.Combine(temp.Path, "AGENTS.md");
        var damaged = "<!-- CodeAlta:coordinator-managed:begin version=\"bad\" checksum=\"bad\" -->\nmissing end";
        File.WriteAllText(agentsPath, damaged);

        var result = CoordinatorAgentsBootstrapper.Ensure(temp.Path);

        Assert.AreEqual(CoordinatorAgentsBootstrapAction.Unchanged, result.Action);
        Assert.AreEqual(damaged, File.ReadAllText(agentsPath));
        Assert.IsNotNull(result.Diagnostics);
        Assert.AreEqual(1, result.Diagnostics.Count);
        StringAssert.Contains(result.Diagnostics[0], "damaged or duplicated");
    }
}
