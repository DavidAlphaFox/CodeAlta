using System.IO;
using System.Reflection;
using CodeAlta.Agent;
using CodeAlta.Catalog;
using CodeAlta.Catalog.Roles;
using CodeAlta.Orchestration.Runtime;
using CodeAlta.Persistence;
using XenoAtom.Terminal.UI.Controls;

namespace CodeAlta.Tests;

[TestClass]
public sealed class CodeAltaTerminalUiSidebarTests
{
    [TestMethod]
    public async Task RefreshView_RebuildsSidebarTreeWhenRecoveredThreadsChange()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"codealta-ui-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootPath);

        try
        {
            var catalogOptions = new CatalogOptions { GlobalRoot = rootPath };
            var projectCatalog = new ProjectCatalog(catalogOptions);
            var threadCatalog = new WorkThreadCatalog(catalogOptions);
            var db = await CreateTestDbAsync(rootPath);
            await using var hub = new AgentHub(new AgentBackendFactory(), new AgentRepository(db));
            await using var runtimeService = new WorkThreadRuntimeService(
                hub,
                projectCatalog,
                threadCatalog,
                new RoleProfileStore(),
                new AgentInstructionTemplateProvider(),
                catalogOptions);
            var project = new ProjectDescriptor
            {
                Id = ProjectId.NewVersion7().ToString(),
                Slug = "codealta",
                DisplayName = "CodeAlta",
                ProjectPath = Path.Combine(rootPath, "repo"),
            };
            Directory.CreateDirectory(project.ProjectPath);
            await projectCatalog.SaveAsync(project);

            var ui = new CodeAltaTerminalUi(projectCatalog, threadCatalog, runtimeService, catalogOptions, hub);
            var sidebarTree = new TreeView();
            SetPrivateField(ui, "_sidebarTree", sidebarTree);
            SetPrivateField(ui, "_projects", (IReadOnlyList<ProjectDescriptor>)[project]);
            SetPrivateField(ui, "_threads", Array.Empty<WorkThreadDescriptor>());
            SetPrivateField(ui, "_viewState", new WorkThreadViewState());
            SetPrivateField(ui, "_selectedProjectId", project.Id);
            SetPrivateField(ui, "_globalScopeSelected", false);

            InvokePrivate(ui, "RebuildSidebarTree");
            Assert.AreEqual(0, sidebarTree.Roots[1].Children[0].Children.Count);

            SetPrivateField(
                ui,
                "_threads",
                (IReadOnlyList<WorkThreadDescriptor>)
                [
                    new WorkThreadDescriptor
                    {
                        ThreadId = "thread-1",
                        Kind = WorkThreadKind.ProjectThread,
                        BackendId = AgentBackendIds.Codex.Value,
                        BackendSessionId = "session-1",
                        ProjectRef = project.Id,
                        WorkingDirectory = project.ProjectPath,
                        Title = "Recovered thread",
                        Status = WorkThreadStatus.Active,
                        CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
                        UpdatedAt = DateTimeOffset.UtcNow,
                        LastActiveAt = DateTimeOffset.UtcNow,
                    }
                ]);

            InvokePrivate(ui, "RefreshView");
            Assert.AreEqual(1, sidebarTree.Roots[1].Children[0].Children.Count);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    private static async Task<CodeAltaDb> CreateTestDbAsync(string rootPath)
    {
        var dbPath = Path.Combine(rootPath, "state", "db", "codealta.db");
        var db = new CodeAltaDb(new CodeAltaDbOptions { DatabasePath = dbPath });
        await db.InitializeAsync();
        return db;
    }

    private static void InvokePrivate(object instance, string methodName)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(method);
        method.Invoke(instance, null);
    }

    private static void SetPrivateField(object instance, string fieldName, object? value)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field);
        field.SetValue(instance, value);
    }
}
